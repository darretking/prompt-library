using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

public sealed class TeamLibraryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-team-svc-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private readonly AppSettings _settings = new();
    private int _settingsSaves;
    private DateTimeOffset _now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    public TeamLibraryServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Cache => Path.Combine(_root, "cache", "team-cache.json");

    private TeamLibraryService NewService(HttpClient? http = null, Func<string, TeamSource?>? parse = null,
        Func<Library>? personal = null)
        => new(http ?? _http, "1.0.0", Cache, _settings, personal ?? (() => new Library()), () => { _settingsSaves++; return true; },
            () => _now, _ => { }, TimeSpan.FromMilliseconds(50), parse);

    private string MakeTeamFolder(string name, params string[] promptTitles)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        var cat = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        var team = new TeamLibrary { Name = name, UpdatedAt = _now };
        team.Content.Categories.Add(cat);
        foreach (var t in promptTitles)
            team.Content.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = t, Text = t, CategoryId = cat.Id });
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), TeamFile.Serialize(team));
        return folder;
    }

    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.Result; }

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) Sta.DoEvents();
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until) { Thread.Sleep(20); Sta.DoEvents(); }
    }

    [Fact]
    public void Join_InvalidFile_DoesNotSaveSource() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "bad");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), "not json");
        using var svc = NewService();

        var result = Wait(svc.JoinAsync(folder));

        Assert.False(result.Success);
        Assert.Null(_settings.TeamSource);
        Assert.Null(svc.Source);
        Assert.Equal(TeamStatus.NotConnected, svc.Status);
        Assert.False(File.Exists(Cache));
    });

    [Fact]
    public void Join_NotAFolderFileOrLink_Fails() => Sta.Run(() =>
    {
        using var svc = NewService();
        var result = Wait(svc.JoinAsync("http://example.com/x.json"));
        Assert.False(result.Success);
        Assert.Equal("That isn't a folder, file or https link.", result.Message);
    });

    [Fact]
    public void Join_Valid_SavesSourceTakesBaselineRaisesChanged() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One", "Two");
        using var svc = NewService();
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        var result = Wait(svc.JoinAsync(folder));

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(folder, TeamFile.FileName), _settings.TeamSource);
        Assert.True(_settings.TeamBaselineTaken);
        Assert.Equal(2, _settings.SeenTeamPrompts.Count);
        Assert.Equal(TeamStatus.Connected, svc.Status);
        Assert.Equal("acme", svc.Team!.Name);
        Assert.True(File.Exists(Cache));
        Assert.True(svc.CanEdit);
        Sta.DoEvents();
        Assert.True(raised >= 1);
        Assert.True(_settingsSaves >= 1);
    });

    [Fact]
    public void Refresh_SourceGone_KeepsTeamStatusOffline() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        File.Delete(Path.Combine(folder, TeamFile.FileName));

        Wait(svc.RefreshAsync());

        Assert.Equal(TeamStatus.Offline, svc.Status);
        Assert.NotNull(svc.Team);
        Assert.Single(svc.Team!.Content.Prompts);
        Assert.False(svc.CanEdit);
        Assert.NotNull(svc.StatusMessage);
    });

    [Fact]
    public void Start_LoadsCacheAsOfflineThenRefreshes() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using (var first = NewService()) Wait(first.JoinAsync(folder));
        using var svc = NewService();

        svc.Start();
        Assert.Equal(TeamStatus.Offline, svc.Status);
        Assert.Equal("acme", svc.Team!.Name);

        PumpUntil(() => svc.Status == TeamStatus.Connected);
        Assert.Equal(TeamStatus.Connected, svc.Status);
    });

    [Fact]
    public void HoldRefresh_DefersUntilReleased() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        var path = Path.Combine(folder, TeamFile.FileName);
        var edited = TeamFile.Parse(File.ReadAllText(path));
        edited.Content.Prompts[0].Title = "Changed elsewhere";
        File.WriteAllText(path, TeamFile.Serialize(edited));

        svc.HoldRefresh(true);
        Wait(svc.RequestRefreshAsync());
        Assert.Equal("One", svc.Team!.Content.Prompts[0].Title);

        svc.HoldRefresh(false);
        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    [Fact]
    public void Save_Saved_UpdatesHashAndOwnChangesNeverMark() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        var oldHash = svc.CurrentHash;
        var edited = svc.Team!;
        edited.Content.Prompts[0].Text = "New text";
        edited.Content.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Added", CategoryId = edited.Content.Categories[0].Id });

        var result = Wait(svc.SaveAsync(edited, oldHash));

        Assert.Equal(TeamSaveStatus.Saved, result.Status);
        Assert.NotEqual(oldHash, svc.CurrentHash);
        Assert.Equal(result.NewHash, svc.CurrentHash);
        Assert.All(edited.Content.Prompts, p => Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(_settings, p)));
    });

    [Fact]
    public void Save_ChangedByOther_Reloads() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        var baseHash = svc.CurrentHash;
        var path = Path.Combine(folder, TeamFile.FileName);
        var theirs = TeamFile.Parse(File.ReadAllText(path));
        theirs.Content.Prompts[0].Title = "Theirs";
        File.WriteAllText(path, TeamFile.Serialize(theirs));
        var raised = 0;
        svc.Changed += (_, _) => raised++;
        var mine = TeamFile.Parse(File.ReadAllText(path));
        mine.Content.Prompts[0].Title = "Mine";
        svc.HoldRefresh(true);

        var result = Wait(svc.SaveAsync(mine, baseHash));

        Assert.Equal(TeamSaveStatus.ChangedByOther, result.Status);
        Assert.Equal("Theirs", svc.Team!.Content.Prompts[0].Title);
        Sta.DoEvents();
        Assert.True(raised >= 1);
    });

    [Fact]
    public void Leave_ClearsCacheAndTeamState() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        TeamUserState.Pin(_settings, svc.Team!.Content.Prompts[0].Id);
        TeamUserState.Hide(_settings, Guid.NewGuid());

        svc.Leave();

        Assert.Null(_settings.TeamSource);
        Assert.False(File.Exists(Cache));
        Assert.Null(svc.Team);
        Assert.Null(svc.Source);
        Assert.Equal(TeamStatus.NotConnected, svc.Status);
        Assert.Empty(_settings.PinnedPromptIds);
        Assert.Empty(_settings.HiddenTeamPromptIds);
        Assert.Empty(_settings.SeenTeamPrompts);
        Assert.False(_settings.TeamBaselineTaken);
    });

    [Fact]
    public void Join_DifferentSource_ClearsOldTeamState() => Sta.Run(() =>
    {
        var a = MakeTeamFolder("a", "A1");
        var b = MakeTeamFolder("b", "B1");
        using var svc = NewService();
        Wait(svc.JoinAsync(a));
        TeamUserState.Pin(_settings, svc.Team!.Content.Prompts[0].Id);

        Wait(svc.JoinAsync(b));

        Assert.Equal("b", svc.Team!.Name);
        Assert.Empty(_settings.PinnedPromptIds);
        Assert.Single(_settings.SeenTeamPrompts);
        Assert.True(_settings.TeamBaselineTaken);
    });

    [Fact]
    public void Start_CreatesAndJoins_OrReportsExisting() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "new");
        Directory.CreateDirectory(folder);
        using var svc = NewService();

        var created = Wait(svc.StartAsync(folder, "Fresh", []));
        Assert.True(created.Created);
        Assert.Equal("Fresh", svc.Team!.Name);

        svc.Leave();
        var again = Wait(svc.StartAsync(folder, "Other", []));
        Assert.False(again.Created);
        Assert.Equal("Fresh", again.ExistingName);
        Assert.Null(svc.Source);
    });

    private static void WriteTitle(string folder, string title)
    {
        var path = Path.Combine(folder, TeamFile.FileName);
        var team = TeamFile.Parse(File.ReadAllText(path));
        team.Content.Prompts[0].Title = title;
        File.WriteAllText(path, TeamFile.Serialize(team));
    }

    [Fact]
    public void Refresh_InFlightWhenHeld_AppliesNothingUntilReleased() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        var hash = svc.CurrentHash;
        WriteTitle(folder, "Changed elsewhere");

        var inFlight = svc.RequestRefreshAsync(); // read started, not yet applied
        svc.HoldRefresh(true);
        Wait(inFlight);

        Assert.Equal("One", svc.Team!.Content.Prompts[0].Title);
        Assert.Equal(hash, svc.CurrentHash);

        svc.HoldRefresh(false);
        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    [Fact]
    public void SaveAsync_OldBaseHashAfterRefreshLanded_IsChangedByOther() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        var baseHash = svc.CurrentHash; // captured when editing started
        WriteTitle(folder, "Theirs");
        Wait(svc.RefreshAsync()); // CurrentHash has moved on meanwhile
        var mine = TeamFile.Parse(File.ReadAllText(Path.Combine(folder, TeamFile.FileName)));
        mine.Content.Prompts[0].Title = "Mine";

        var result = Wait(svc.SaveAsync(mine, baseHash));

        Assert.Equal(TeamSaveStatus.ChangedByOther, result.Status);
        Assert.Equal("Theirs", TeamFile.Parse(File.ReadAllText(Path.Combine(folder, TeamFile.FileName))).Content.Prompts[0].Title);
    });

    [Fact]
    public void Refresh_OlderSlowReadFinishingLast_DoesNotRollBack() => Sta.Run(() =>
    {
        static string Json(string title)
        {
            var team = new TeamLibrary { Name = "Web", UpdatedAt = DateTimeOffset.UnixEpoch };
            var cat = new Category { Id = Guid.NewGuid(), Name = "C" };
            team.Content.Categories.Add(cat);
            team.Content.Prompts.Add(new Prompt { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Title = title, CategoryId = cat.Id });
            return TeamFile.Serialize(team);
        }

        var slow = new TaskCompletionSource<bool>();
        var requests = 0;
        using var http = new HttpClient(new Handler(async () =>
        {
            var n = Interlocked.Increment(ref requests);
            if (n == 2) await slow.Task;
            return Json(n == 2 ? "Old" : n == 1 ? "Join" : "New");
        }));
        using var svc = NewService(http);
        Wait(svc.JoinAsync("https://example.com/team.json"));

        var first = svc.RefreshAsync();
        PumpUntil(() => Volatile.Read(ref requests) >= 2);
        var second = svc.RefreshAsync();
        Wait(second);
        Assert.Equal("New", svc.Team!.Content.Prompts[0].Title);
        var hash = svc.CurrentHash;

        slow.SetResult(true);
        Wait(first);

        Assert.Equal("New", svc.Team!.Content.Prompts[0].Title);
        Assert.Equal(hash, svc.CurrentHash);
    });

    [Fact]
    public void Watcher_FileOverwritten_RefreshesWithoutCallingRefresh() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using (var first = NewService()) Wait(first.JoinAsync(folder));
        using var svc = NewService();
        svc.Start();
        PumpUntil(() => svc.Status == TeamStatus.Connected);

        WriteTitle(folder, "Changed elsewhere");

        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    [Fact]
    public void Watcher_WhileHeld_ChangesNothingUntilReleased() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        svc.HoldRefresh(true);

        WriteTitle(folder, "Changed elsewhere");
        var until = DateTime.UtcNow.AddSeconds(1.5);
        while (DateTime.UtcNow < until) { Thread.Sleep(20); Sta.DoEvents(); }
        Assert.Equal("One", svc.Team!.Content.Prompts[0].Title);

        svc.HoldRefresh(false);
        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    [Fact]
    public void Watcher_FolderMissingAtStart_StartsAfterFirstSuccessfulRead() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "later");
        _settings.TeamSource = Path.Combine(folder, TeamFile.FileName);
        using var svc = NewService();
        svc.Start();
        PumpUntil(() => svc.StatusMessage is not null);
        Assert.Equal(TeamStatus.Offline, svc.Status);

        var made = MakeTeamFolder("later", "One");
        Wait(svc.RefreshAsync());
        Assert.Equal(TeamStatus.Connected, svc.Status);

        WriteTitle(made, "Changed elsewhere");
        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    private sealed class Handler(Func<Task<string>> body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(await body(), System.Text.Encoding.UTF8, "application/json"),
            };
    }

    [Fact]
    public void Start_SavedSource_DoesNotTouchTheFileSystem() => Sta.Run(() =>
    {
        var calls = 0;
        _settings.TeamSource = @"\unreachable-nas\team\team-prompts.json";
        using var svc = NewService(parse: _ => { calls++; return null; });

        svc.Start();

        Assert.Equal(0, calls);
        Assert.Equal(new TeamSource(TeamSourceKind.File, @"\unreachable-nas\team\team-prompts.json"), svc.Source);
        Assert.Equal(TeamStatus.Offline, svc.Status);
    });

    [Fact]
    public void Start_SavedLink_IsAUrlSource() => Sta.Run(() =>
    {
        _settings.TeamSource = "https://example.com/team.json";
        using var svc = NewService(parse: _ => throw new InvalidOperationException());
        svc.Start();
        Assert.Equal(TeamSourceKind.Url, svc.Source!.Kind);
    });

    [Fact]
    public void Join_ParsesTheInputOffTheUiThread() => Sta.Run(() =>
    {
        var uiThread = Environment.CurrentManagedThreadId;
        int? parsedOn = null;
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService(parse: input => { parsedOn = Environment.CurrentManagedThreadId; return TeamSource.FromInput(input); });

        Assert.True(Wait(svc.JoinAsync(folder)).Success);

        Assert.NotNull(parsedOn);
        Assert.NotEqual(uiThread, parsedOn);
    });

    // ---- Final review fixes ----------------------------------------------------------------------

    [Fact]
    public void Start_FolderWithUnreadableTeamFile_SaysSoPlainly() => Sta.Run(() =>
    {
        var folder = Path.Combine(_root, "unreadable");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), "not a team file");
        using var svc = NewService();

        var result = Wait(svc.StartAsync(folder, "Fresh", []));

        Assert.False(result.Created);
        Assert.Null(result.ExistingName);
        Assert.Equal("This folder already has a team file that can't be read.", result.Message);
        Assert.Equal("not a team file", File.ReadAllText(Path.Combine(folder, TeamFile.FileName)));
        Assert.Null(svc.Source);
    });

    [Fact]
    public void Refresh_RecordsWhyTheReadFailed() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        var path = Path.Combine(folder, TeamFile.FileName);
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        Assert.Equal(TeamReadStatus.Ok, svc.LastReadStatus);

        File.WriteAllText(path, """{"kind":"prompt-library-team","schemaVersion":2,"name":"x","prompts":[{"id":1}]}""");
        Wait(svc.RefreshAsync());
        Assert.Equal(TeamStatus.Offline, svc.Status);
        Assert.Equal(TeamReadStatus.TooNew, svc.LastReadStatus);

        File.WriteAllText(path, "not json");
        Wait(svc.RefreshAsync());
        Assert.Equal(TeamReadStatus.NotATeamFile, svc.LastReadStatus);

        File.Delete(path);
        Wait(svc.RefreshAsync());
        Assert.Equal(TeamReadStatus.Unreachable, svc.LastReadStatus);
        Assert.Equal("acme", svc.Team!.Name); // the last good copy stays throughout
    });

    [Fact]
    public void Watcher_Error_DropsItAndTheNextReadMakesANewOne() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One");
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        Assert.True(svc.IsWatching);

        svc.SimulateWatcherError();
        PumpUntil(() => !svc.IsWatching);
        Assert.False(svc.IsWatching);

        Wait(svc.RefreshAsync());
        Assert.True(svc.IsWatching);
        WriteTitle(folder, "Changed elsewhere");
        PumpUntil(() => svc.Team!.Content.Prompts[0].Title == "Changed elsewhere");
        Assert.Equal("Changed elsewhere", svc.Team!.Content.Prompts[0].Title);
    });

    [Fact]
    public void Save_FileWithRepeatedPromptId_IsSavedNotFailed() => Sta.Run(() =>
    {
        var folder = MakeTeamFolder("acme", "One", "Two");
        var path = Path.Combine(folder, TeamFile.FileName);
        var text = File.ReadAllText(path);
        var file = TeamFile.Parse(text);
        text = text.Replace(file.Content.Prompts[1].Id.ToString(), file.Content.Prompts[0].Id.ToString()); // hand-edited
        File.WriteAllText(path, text);
        using var svc = NewService();
        Wait(svc.JoinAsync(folder));
        Assert.Equal(2, svc.Team!.Content.Prompts.Select(p => p.Id).Distinct().Count());
        var edited = svc.Team!;
        edited.Content.Prompts[1].Text = "Changed";

        var result = Wait(svc.SaveAsync(edited, svc.CurrentHash));

        Assert.Equal(TeamSaveStatus.Saved, result.Status);
        Assert.Equal(TeamStatus.Connected, svc.Status);
        var written = TeamFile.Parse(File.ReadAllText(path));
        Assert.Equal("Changed", written.Content.Prompts[1].Text);
        Assert.Equal(2, written.Content.Prompts.Select(p => p.Id).Distinct().Count());
    });

    [Fact]
    public void Join_TeamIdsSharedWithPersonalPrompts_GetNewIds_LeaveKeepsPersonalPins() => Sta.Run(() =>
    {
        // A team library started by hand from someone's library.json: same prompt and category ids as their own.
        var folder = MakeTeamFolder("acme", "One");
        var file = TeamFile.Parse(File.ReadAllText(Path.Combine(folder, TeamFile.FileName)));
        var personal = new Library();
        personal.Categories.Add(new Category { Id = file.Content.Categories[0].Id, Name = "Sales" });
        personal.Prompts.Add(new Prompt { Id = file.Content.Prompts[0].Id, Title = "One", Text = "One", CategoryId = file.Content.Categories[0].Id });
        using var svc = NewService(personal: () => personal);

        Wait(svc.JoinAsync(folder));

        var team = svc.Team!.Content;
        Assert.NotEqual(personal.Prompts[0].Id, team.Prompts[0].Id);
        Assert.NotEqual(personal.Categories[0].Id, team.Categories[0].Id);
        Assert.Equal(team.Categories[0].Id, team.Prompts[0].CategoryId);
        var teamId = team.Prompts[0].Id;
        Wait(svc.RefreshAsync());
        Assert.Equal(teamId, svc.Team!.Content.Prompts[0].Id); // the same new id on every read

        TeamUserState.Pin(_settings, personal.Prompts[0].Id);
        svc.Leave();
        Assert.Equal([personal.Prompts[0].Id], _settings.PinnedPromptIds);
    });
}
