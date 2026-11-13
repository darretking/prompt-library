using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Team;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Tests.Team;

public sealed class TeamUserStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public TeamUserStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Prompt P(string title = "T", string text = "body", Guid? category = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = "d",
        Text = text,
        CategoryId = category ?? Guid.NewGuid(),
    };

    private static Library Lib(params Prompt[] prompts) => new() { Prompts = [.. prompts] };

    [Fact]
    public void FirstJoin_NothingMarked()
    {
        var s = new AppSettings();
        var a = P();
        var team = Lib(a);

        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));
        Assert.False(TeamUserState.HasUnseen(s, team));

        TeamUserState.TakeBaseline(s, team);

        Assert.True(s.TeamBaselineTaken);
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));
        Assert.False(TeamUserState.HasUnseen(s, team));
    }

    [Fact]
    public void NewPrompt_IsNew_EditedText_IsUpdated_CategoryRename_None()
    {
        var s = new AppSettings();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Old" };
        var a = P(category: cat.Id);
        TeamUserState.TakeBaseline(s, Lib(a));

        var b = P();
        Assert.Equal(TeamMarker.New, TeamUserState.MarkerFor(s, b));

        cat.Name = "Renamed";
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));

        a.Text = "changed";
        Assert.Equal(TeamMarker.Updated, TeamUserState.MarkerFor(s, a));
        Assert.True(TeamUserState.HasUnseen(s, Lib(a, b)));
    }

    [Fact]
    public void Fingerprint_IgnoresLineEndingStyle_ButSeesCategoryMove()
    {
        var a = P(text: "x\ny");
        var f = TeamUserState.Fingerprint(a);
        a.Text = "x\r\ny";
        Assert.Equal(f, TeamUserState.Fingerprint(a));
        a.CategoryId = Guid.NewGuid();
        Assert.NotEqual(f, TeamUserState.Fingerprint(a));
    }

    [Fact]
    public void MarkSeen_Clears()
    {
        var s = new AppSettings();
        TeamUserState.TakeBaseline(s, Lib());
        var a = P();
        Assert.Equal(TeamMarker.New, TeamUserState.MarkerFor(s, a));

        TeamUserState.MarkSeen(s, a);
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));

        a.Title = "edited";
        Assert.Equal(TeamMarker.Updated, TeamUserState.MarkerFor(s, a));
        TeamUserState.MarkSeen(s, a);
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));
    }

    [Fact]
    public void MarkAllSeen_ClearsAll()
    {
        var s = new AppSettings();
        TeamUserState.TakeBaseline(s, Lib());
        var a = P();
        var b = P();
        var team = Lib(a, b);
        Assert.True(TeamUserState.HasUnseen(s, team));

        TeamUserState.MarkAllSeen(s, team);

        Assert.False(TeamUserState.HasUnseen(s, team));
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, a));
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(s, b));
    }

    [Fact]
    public void Hide_Unpins()
    {
        var s = new AppSettings();
        var id = Guid.NewGuid();
        Assert.True(TeamUserState.Pin(s, id));

        TeamUserState.Hide(s, id);

        Assert.DoesNotContain(id, s.PinnedPromptIds);
        Assert.Contains(id, s.HiddenTeamPromptIds);

        TeamUserState.Hide(s, id);
        Assert.Single(s.HiddenTeamPromptIds);

        TeamUserState.Unhide(s, id);
        Assert.Empty(s.HiddenTeamPromptIds);
    }

    [Fact]
    public void Pin_KeepsOrder()
    {
        var s = new AppSettings();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids) TeamUserState.Pin(s, id);

        Assert.True(TeamUserState.Pin(s, ids[0]));
        Assert.Equal(ids, s.PinnedPromptIds);

        TeamUserState.Unpin(s, ids[1]);
        Assert.Equal([ids[0], ids[2]], s.PinnedPromptIds);
    }

    [Fact]
    public void Pin_HiddenPrompt_Unhides()
    {
        var s = new AppSettings();
        var id = Guid.NewGuid();
        TeamUserState.Hide(s, id);

        Assert.True(TeamUserState.Pin(s, id));

        Assert.Contains(id, s.PinnedPromptIds);
        Assert.DoesNotContain(id, s.HiddenTeamPromptIds);
    }

    [Fact]
    public void Pin_AtCap_ReturnsFalse_AndChangesNothing()
    {
        var s = new AppSettings();
        for (var i = 0; i < TeamUserState.MaxPins; i++) Assert.True(TeamUserState.Pin(s, Guid.NewGuid()));
        Assert.Equal(10, TeamUserState.MaxPins);

        var extra = Guid.NewGuid();
        TeamUserState.Hide(s, extra);
        var before = s.PinnedPromptIds.ToList();

        Assert.False(TeamUserState.Pin(s, extra));

        Assert.Equal(before, s.PinnedPromptIds);
        Assert.Contains(extra, s.HiddenTeamPromptIds);
        Assert.True(TeamUserState.Pin(s, before[0]));
    }

    [Fact]
    public void Prune_DropsDanglingPinsHidesAndSeen()
    {
        var s = new AppSettings();
        var personal = P();
        var team = P();
        var gone = Guid.NewGuid();
        TeamUserState.TakeBaseline(s, Lib(team));
        s.SeenTeamPrompts[gone] = "x";
        s.PinnedPromptIds.AddRange([personal.Id, team.Id, gone]);
        s.HiddenTeamPromptIds.AddRange([team.Id, gone]);

        var changed = TeamUserState.Prune(s, Lib(personal), Lib(team));

        Assert.True(changed);
        Assert.Equal([personal.Id, team.Id], s.PinnedPromptIds);
        Assert.Equal([team.Id], s.HiddenTeamPromptIds);
        Assert.Equal([team.Id], s.SeenTeamPrompts.Keys);
        Assert.False(TeamUserState.Prune(s, Lib(personal), Lib(team)));
    }

    [Fact]
    public void Prune_DropsUsageOfGonePrompts_KeepsPersonalAndTeam()
    {
        var s = new AppSettings();
        var personal = P();
        var team = P();
        var gone = Guid.NewGuid();
        foreach (var id in new[] { personal.Id, team.Id, gone }) UsageTracker.Record(s, id, DateTimeOffset.Now);

        Assert.True(TeamUserState.Prune(s, Lib(personal), Lib(team)));

        Assert.Equivalent(new[] { personal.Id, team.Id }, s.Usage.Keys);
    }

    [Fact]
    public void Prune_TeamJoinedButNotLoaded_KeepsTeamUsage()
    {
        var s = new AppSettings();
        var personal = P();
        var teamId = Guid.NewGuid();
        UsageTracker.Record(s, personal.Id, DateTimeOffset.Now);
        UsageTracker.Record(s, teamId, DateTimeOffset.Now);

        Assert.False(TeamUserState.Prune(s, Lib(personal), TeamUserState.TeamForPrune(joined: true, loaded: null)));

        Assert.Contains(teamId, s.Usage.Keys);
    }

    [Fact]
    public void Prune_NoTeamJoined_DropsTeamUsage()
    {
        var s = new AppSettings();
        var personal = P();
        var teamId = Guid.NewGuid();
        UsageTracker.Record(s, personal.Id, DateTimeOffset.Now);
        UsageTracker.Record(s, teamId, DateTimeOffset.Now);

        Assert.True(TeamUserState.Prune(s, Lib(personal), TeamUserState.TeamForPrune(joined: false, loaded: null)));

        Assert.Equal([personal.Id], s.Usage.Keys);
    }

    [Fact]
    public void ClearTeam_KeepsPersonalPins()
    {
        var s = new AppSettings { TeamSource = @"\\x\y" };
        var personal = P();
        var team = P();
        var teamLib = Lib(team);
        TeamUserState.TakeBaseline(s, teamLib);
        s.PinnedPromptIds.AddRange([team.Id, personal.Id]);
        s.HiddenTeamPromptIds.Add(Guid.NewGuid());

        TeamUserState.ClearTeam(s, teamLib);

        Assert.Equal([personal.Id], s.PinnedPromptIds);
        Assert.Empty(s.HiddenTeamPromptIds);
        Assert.Empty(s.SeenTeamPrompts);
        Assert.False(s.TeamBaselineTaken);
    }

    [Fact]
    public void Settings_RoundTrip_NewFields()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = new AppSettings { TeamSource = @"C:\team", TeamBaselineTaken = true };
        var pin = Guid.NewGuid();
        var hide = Guid.NewGuid();
        s.PinnedPromptIds.Add(pin);
        s.HiddenTeamPromptIds.Add(hide);
        s.SeenTeamPrompts[hide] = "abc";

        JsonStore.SaveSettings(path, s);
        var back = JsonStore.LoadSettings(path).Value!;

        Assert.Equal(@"C:\team", back.TeamSource);
        Assert.True(back.TeamBaselineTaken);
        Assert.Equal([pin], back.PinnedPromptIds);
        Assert.Equal([hide], back.HiddenTeamPromptIds);
        Assert.Equal("abc", back.SeenTeamPrompts[hide]);
        Assert.Contains("\"pinnedPromptIds\"", File.ReadAllText(path));
    }

    [Fact]
    public void Settings_OldFileWithoutFields_StillLoads()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, "{ \"hotkey\": \"Ctrl+Alt+Q\", \"backupsToKeep\": 5 }");

        var result = JsonStore.LoadSettings(path);

        Assert.Equal(LoadStatus.Ok, result.Status);
        var back = result.Value!;
        Assert.Equal("Ctrl+Alt+Q", back.Hotkey);
        Assert.Null(back.TeamSource);
        Assert.Empty(back.PinnedPromptIds);
        Assert.Empty(back.HiddenTeamPromptIds);
        Assert.Empty(back.SeenTeamPrompts);
        Assert.False(back.TeamBaselineTaken);
    }

    [Fact]
    public void TeamForPrune_EmptyWhenNotJoined_LoadedCopyWhenJoined_NullWhenJoinedButNotLoaded()
    {
        var loaded = new Library();
        Assert.NotNull(TeamUserState.TeamForPrune(joined: false, loaded: null));
        Assert.Empty(TeamUserState.TeamForPrune(joined: false, loaded: null)!.Prompts);
        Assert.Same(loaded, TeamUserState.TeamForPrune(joined: true, loaded: loaded));
        Assert.Null(TeamUserState.TeamForPrune(joined: true, loaded: null));

        // Solo: a pin whose prompt is gone is dropped, so it doesn't use up one of the 10.
        var s = new AppSettings();
        var kept = Guid.NewGuid();
        var personal = new Library();
        personal.Prompts.Add(new Prompt { Id = kept, Title = "t", Text = "x" });
        s.PinnedPromptIds.AddRange([kept, Guid.NewGuid()]);
        Assert.True(TeamUserState.Prune(s, personal, TeamUserState.TeamForPrune(false, null)));
        Assert.Equal([kept], s.PinnedPromptIds);
    }
}
