using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

/// <summary>The main window's Team side, against a stand-in host and a real team library in a temp folder.</summary>
public sealed class MainViewModelTeamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-main-team-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    public MainViewModelTeamTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ---- Tests ---------------------------------------------------------------------------------

    [Fact]
    public void TeamSide_EditorIsReadOnly() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var vm = new MainViewModel(host, new FakeDialogs());

        Assert.True(vm.ShowSwitch);
        vm.ShowTeamCommand.Execute(null);

        Assert.Equal(LibrarySide.Team, vm.Side);
        Assert.True(vm.CurrentDocument.IsTeam);
        Assert.True(vm.CurrentDocument.IsReadOnly);
        Assert.NotNull(vm.Editor);
        Assert.True(vm.Editor!.IsReadOnly);
        Assert.Equal("Team prompt · Acme best practice · read only", vm.LockLineText);
        Assert.True(vm.ShowTeamButtons);
        Assert.False(vm.ShowEditButtons);

        // Delete and New prompt do nothing on the read-only side.
        var before = host.Team!.Team!.Content.Prompts.Count;
        vm.DeleteCommand.Execute(null);
        vm.NewPromptCommand.Execute(null);
        Assert.Equal(before, host.Team.Team!.Content.Prompts.Count);
        Assert.False(vm.Editor!.IsNew);
    });

    [Fact]
    public void CopyToYours_UsesSameNamedCategory() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var mine = AddCategory(host.Library, "Sales");
        AddPrompt(host.Library, mine, "Thank-you note");
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");
        var teamPrompt = TeamPrompt(host, "Follow-up email");

        vm.CopyToYoursCommand.Execute(null);

        Assert.Equal(LibrarySide.Yours, vm.Side);
        Assert.Single(host.Library.Categories);
        var copy = Assert.Single(host.Library.Prompts, p => p.Title == "Follow-up email");
        Assert.NotEqual(teamPrompt.Id, copy.Id);
        Assert.Equal(mine.Id, copy.CategoryId);
        Assert.Equal(teamPrompt.Text, copy.Text);
        Assert.Equal(teamPrompt.Description, copy.Description);
        Assert.Equal(copy.Id, vm.Editor!.PromptId);
        Assert.False(vm.Editor.IsReadOnly);
        Assert.Equal("Copied to your prompts · Sales", vm.StatusText);
        Assert.True(host.LibrarySaves > 0);
        Assert.Contains(host.Team!.Team!.Content.Prompts, p => p.Id == teamPrompt.Id); // the team prompt stays
    });

    [Fact]
    public void CopyToYours_CreatesCategoryWhenMissing() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");

        vm.CopyToYoursCommand.Execute(null);

        var sales = Assert.Single(host.Library.Categories, c => c.Name == "Sales");
        Assert.NotEqual(TeamCategory(host, "Sales").Id, sales.Id);
        var copy = Assert.Single(host.Library.Prompts, p => p.Title == "Follow-up email");
        Assert.Equal(sales.Id, copy.CategoryId);
        Assert.Equal(sales.Id, vm.SelectedCategory?.Id);
        Assert.Equal("Copied to your prompts · Sales", vm.StatusText);
    });

    [Fact]
    public void Hide_RemovesFromListAndUnpins() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");
        var id = vm.Editor!.PromptId!.Value;
        vm.PinCommand.Execute(null);
        Assert.Contains(id, host.Settings.PinnedPromptIds);
        var saves = host.SettingsSaves;
        var menus = host.MenuRefreshes;

        vm.HideCommand.Execute(null);

        Assert.Contains(id, host.Settings.HiddenTeamPromptIds);
        Assert.DoesNotContain(id, host.Settings.PinnedPromptIds);
        Assert.DoesNotContain(vm.Prompts, r => r.Id == id);
        Assert.Equal(1, vm.HiddenCount);
        Assert.True(vm.ShowHiddenToggle);
        Assert.Equal("Hidden from the menu", vm.StatusText);
        Assert.Equal("Unhide", vm.HideLabel);
        Assert.True(host.SettingsSaves > saves);
        Assert.True(host.MenuRefreshes > menus);
    });

    [Fact]
    public void ShowHidden_ListsGreyedWithUnhide() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var hiddenId = TeamPrompt(host, "Discovery questions").Id;
        host.Settings.HiddenTeamPromptIds.Add(hiddenId);
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        Assert.DoesNotContain(vm.Prompts, r => r.Id == hiddenId);
        Assert.Equal("Show hidden (1)", vm.ShowHiddenText);

        vm.ShowHiddenTeamPrompts = true;

        var row = Assert.Single(vm.Prompts, r => r.Id == hiddenId);
        Assert.True(row.IsHidden);
        Assert.Equal("Unhide", row.HideLabel);
        Assert.All(vm.Prompts.Where(r => r.Id != hiddenId), r => Assert.False(r.IsHidden));

        vm.HideCommand.Execute(row);

        Assert.DoesNotContain(hiddenId, host.Settings.HiddenTeamPromptIds);
        Assert.Equal("Unhidden", vm.StatusText);
        Assert.Equal(0, vm.HiddenCount);
        Assert.False(vm.ShowHiddenToggle);
        Assert.Contains(vm.Prompts, r => r.Id == hiddenId && !r.IsHidden);
    });

    [Fact]
    public void Search_FindsTeamPrompt_OpensOnTeamSide() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var writing = AddCategory(host.Library, "Writing");
        AddPrompt(host.Library, writing, "Thank-you email");
        AddPrompt(host.Library, AddCategory(host.Library, "Other"), "Unrelated");
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.SelectedCategory = vm.Categories.First(c => c.Name == "Other");

        vm.SearchText = "email";

        Assert.True(vm.SelectedCategory!.IsAll); // the rail shows All while searching both libraries
        Assert.Contains(vm.Prompts, r => !r.IsTeam && r.Title == "Thank-you email");
        var hit = Assert.Single(vm.Prompts, r => r.IsTeam && r.Title == "Follow-up email");
        Assert.True(hit.ShowTeamIcon);
        Assert.Equal(LibrarySide.Yours, vm.Side);

        vm.SelectedPrompt = hit;

        Assert.Equal(LibrarySide.Team, vm.Side);
        Assert.Equal(hit.Id, vm.Editor!.PromptId);
        Assert.True(vm.Editor.IsReadOnly);
        Assert.Equal("email", vm.SearchText);
        Assert.Contains(vm.Prompts, r => !r.IsTeam); // still both libraries

        vm.SearchText = "";

        Assert.Equal(LibrarySide.Team, vm.Side); // clearing goes back to the side's own list, editor kept
        Assert.Equal(hit.Id, vm.Editor!.PromptId);
        Assert.All(vm.Prompts, r => Assert.True(r.IsTeam));
    });

    [Fact]
    public void Open_ClearsMarker() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var prompt = TeamPrompt(host, "Discovery questions");
        host.Settings.SeenTeamPrompts.Remove(prompt.Id); // arrived since the last look: New
        var vm = new MainViewModel(host, new FakeDialogs());
        Assert.True(vm.TeamHasUnseen);
        vm.ShowTeamCommand.Execute(null);
        Assert.True(vm.ShowMarkAllSeen);
        var row = vm.Prompts.First(r => r.Id == prompt.Id);
        Assert.Equal("New", row.MarkerText);
        var saves = host.SettingsSaves;

        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == prompt.Id);

        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(host.Settings, prompt));
        Assert.Null(vm.Prompts.First(r => r.Id == prompt.Id).MarkerText);
        Assert.False(vm.TeamHasUnseen);
        Assert.False(vm.ShowMarkAllSeen);
        Assert.True(host.SettingsSaves > saves);
    });

    [Fact]
    public void NoTeam_NoSwitch() => Sta.Run(() =>
    {
        // No service at all, and a service with nothing joined.
        foreach (var withService in new[] { false, true })
        {
            var host = NewHost(withTeam: false);
            if (withService) host.Team = NewService(host);
            var other = AddCategory(host.Library, "Other");
            AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Thank-you email");
            AddPrompt(host.Library, other, "Reply email");
            var vm = new MainViewModel(host, new FakeDialogs());

            Assert.False(vm.ShowSwitch);
            vm.ShowTeamCommand.Execute(null);
            Assert.Equal(LibrarySide.Yours, vm.Side);
            Assert.Null(vm.TeamHeaderText);
            Assert.False(vm.TeamHasUnseen);

            // Search stays as it was: within the chosen category, your prompts only.
            vm.SelectedCategory = vm.Categories.First(c => c.Name == "Other");
            vm.SearchText = "email";
            Assert.Equal("Other", vm.SelectedCategory!.Name);
            var row = Assert.Single(vm.Prompts);
            Assert.Equal("Reply email", row.Title);
            Assert.False(row.ShowTeamIcon);
        }
    });

    [Fact]
    public void Pin_AtCap_ShowsMessage() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var mine = AddCategory(host.Library, "Mine");
        for (var i = 0; i < TeamUserState.MaxPins; i++) host.Settings.PinnedPromptIds.Add(AddPrompt(host.Library, mine, $"P{i}").Id);
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");
        var id = vm.Editor!.PromptId!.Value;

        vm.PinCommand.Execute(null);

        Assert.Equal("You can pin up to 10 prompts.", vm.StatusText);
        Assert.DoesNotContain(id, host.Settings.PinnedPromptIds);
        Assert.Equal("Pin", vm.PinLabel);

        host.Settings.PinnedPromptIds.RemoveAt(0);
        vm.PinCommand.Execute(null);
        Assert.Equal("Pinned", vm.StatusText);
        Assert.Equal("Unpin", vm.PinLabel);
        Assert.True(vm.Prompts.First(r => r.Id == id).IsPinned);
    });

    [Fact]
    public void SelectionRememberedPerSide() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var writing = AddCategory(host.Library, "Writing");
        AddPrompt(host.Library, writing, "Alpha");
        var beta = AddPrompt(host.Library, writing, "Beta");
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == beta.Id);

        vm.ShowTeamCommand.Execute(null);
        vm.SelectedCategory = vm.Categories.First(c => c.Name == "Sales");
        var teamPick = vm.Prompts.Last(); // not the one a side opens on by itself
        Assert.NotEqual(teamPick.Id, vm.Editor!.PromptId);
        vm.SelectedPrompt = teamPick;

        vm.ShowYoursCommand.Execute(null);
        Assert.Equal(LibrarySide.Yours, vm.Side);
        Assert.Equal(beta.Id, vm.Editor!.PromptId);

        vm.ShowTeamCommand.Execute(null);
        Assert.Equal(teamPick.Id, vm.Editor!.PromptId);
        Assert.Equal("Sales", vm.SelectedCategory!.Name);
    });

    [Fact]
    public void TeamSide_LoadingThenOffline() => Sta.Run(() =>
    {
        // Connected, but no offline copy yet and the first read not back: the switch shows, the Team side says it's
        // loading. The source can't be read, so it then says so, with Retry.
        var host = NewHost(withTeam: false);
        AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
        host.Settings.TeamSource = Path.Combine(_root, "missing", TeamFile.FileName);
        var service = NewService(host);
        host.Team = service;
        service.Start();
        var vm = new MainViewModel(host, new FakeDialogs());

        Assert.True(vm.ShowSwitch);
        vm.ShowTeamCommand.Execute(null);
        Assert.Equal(LibrarySide.Team, vm.Side);
        Assert.True(vm.IsTeamLoading);
        Assert.False(vm.ShowNoTeamPrompts);
        Assert.False(vm.IsTeamOffline);
        Assert.Null(vm.Editor);

        var until = DateTime.UtcNow.AddSeconds(10);
        while (service.StatusMessage is null && DateTime.UtcNow < until) Sta.DoEvents();

        Assert.False(vm.IsTeamLoading);
        Assert.True(vm.IsTeamOffline);
        Assert.Equal("Can't reach the team library", vm.TeamHeaderText);
        Assert.True(vm.ShowTeamHeader);
        Assert.Equal(LibrarySide.Team, vm.Side);
    });

    [Fact]
    public void HiddenRow_CantBePinned() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var hiddenId = TeamPrompt(host, "Discovery questions").Id;
        host.Settings.HiddenTeamPromptIds.Add(hiddenId);
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        vm.ShowHiddenTeamPrompts = true;

        var row = vm.Prompts.First(r => r.Id == hiddenId);
        Assert.False(row.CanPin);
        Assert.True(vm.Prompts.First(r => r.Id != hiddenId).CanPin);

        vm.PinCommand.Execute(row);
        Assert.DoesNotContain(hiddenId, host.Settings.PinnedPromptIds);
    });

    [Fact]
    public void AutoOpen_KeepsMarker_UserOpenClearsIt() => Sta.Run(() =>
    {
        // "Complaint reply" sorts first, so switching to the Team side opens it by itself: that isn't the user
        // opening it, so it stays New (and the switch keeps its dot) until they choose it.
        var host = NewHost(withTeam: true);
        var first = TeamPrompt(host, "Complaint reply");
        host.Settings.SeenTeamPrompts.Remove(first.Id);
        var vm = new MainViewModel(host, new FakeDialogs());

        vm.ShowTeamCommand.Execute(null);

        Assert.Equal(first.Id, vm.Editor!.PromptId);
        Assert.Equal(TeamMarker.New, TeamUserState.MarkerFor(host.Settings, first));
        Assert.Equal("New", vm.Prompts.First(r => r.Id == first.Id).MarkerText);
        Assert.True(vm.TeamHasUnseen);

        // Choosing a category opens its first prompt by itself too.
        vm.SelectedCategory = vm.Categories.First(c => c.Name == "Support");
        Assert.Equal(TeamMarker.New, TeamUserState.MarkerFor(host.Settings, first));

        vm.SelectedCategory = vm.Categories.First(c => c.IsAll);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id != first.Id);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == first.Id);

        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(host.Settings, first));
        Assert.False(vm.TeamHasUnseen);
    });

    [Fact]
    public void CachedCopy_BeforeFirstRead_ShowsOnlineHeader() => Sta.Run(() =>
    {
        // Start loads the offline copy (status Offline until a read works), but nothing has failed yet: the header
        // reads as usual, with no "can't reach" and no Retry.
        var joined = NewHost(withTeam: true);
        joined.Team!.Dispose();
        var host = new FakeHost();
        host.Settings.TeamSource = joined.Settings.TeamSource;
        host.Settings.TeamBaselineTaken = true;
        var service = NewService(host);
        host.Team = service;
        service.Start();
        Assert.NotNull(service.Team);
        Assert.Null(service.StatusMessage);
        var vm = new MainViewModel(host, new FakeDialogs());

        vm.ShowTeamCommand.Execute(null);

        Assert.False(vm.IsTeamOffline);
        Assert.StartsWith("Acme best practice · updated ", vm.TeamHeaderText);
        Assert.Null(vm.TeamHeaderDetail);
    });

    // ---- Final review fixes ----------------------------------------------------------------------

    [Fact]
    public void Pin_DanglingPinsDontCountTowardsTheCap_Solo() => Sta.Run(() =>
    {
        // Solo (no team service, and a service with nothing joined): pins of prompts that are gone are dropped first.
        foreach (var withService in new[] { false, true })
        {
            var host = NewHost(withTeam: false);
            if (withService) host.Team = NewService(host);
            var prompt = AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
            for (var i = 0; i < TeamUserState.MaxPins; i++) host.Settings.PinnedPromptIds.Add(Guid.NewGuid());
            var vm = new MainViewModel(host, new FakeDialogs());
            Assert.Equal(prompt.Id, vm.Editor!.PromptId);

            vm.PinCommand.Execute(null);

            Assert.Equal("Pinned", vm.StatusText);
            Assert.Equal([prompt.Id], host.Settings.PinnedPromptIds);
        }
    });

    [Fact]
    public void Pin_DanglingPinsDontCountTowardsTheCap_Team() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var kept = TeamPrompt(host, "Complaint reply").Id;
        host.Settings.PinnedPromptIds.Add(kept);
        for (var i = 1; i < TeamUserState.MaxPins; i++) host.Settings.PinnedPromptIds.Add(Guid.NewGuid());
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");
        var id = vm.Editor!.PromptId!.Value;

        vm.PinCommand.Execute(null);

        Assert.Equal("Pinned", vm.StatusText);
        Assert.Equal([kept, id], host.Settings.PinnedPromptIds);
    });

    [Fact]
    public void Pin_TeamJoinedButNotLoaded_KeepsItsPins() => Sta.Run(() =>
    {
        // Joined, but the source can't be read and there's no offline copy: team pins can't be told from dangling
        // ones, so none are dropped.
        var host = NewHost(withTeam: false);
        var prompt = AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
        host.Settings.TeamSource = Path.Combine(_root, "missing", TeamFile.FileName);
        var teamPin = Guid.NewGuid();
        host.Settings.PinnedPromptIds.Add(teamPin);
        var service = NewService(host);
        host.Team = service;
        service.Start();
        var vm = new MainViewModel(host, new FakeDialogs());

        vm.PinCommand.Execute(null);

        Assert.Equal([teamPin, prompt.Id], host.Settings.PinnedPromptIds);
    });

    [Fact]
    public void Delete_PinnedPersonalPrompt_Unpins() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: false);
        var prompt = AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
        host.Settings.PinnedPromptIds.Add(prompt.Id);
        var vm = new MainViewModel(host, new FakeDialogs());
        Assert.Equal(prompt.Id, vm.Editor!.PromptId);
        var saves = host.SettingsSaves;

        vm.DeleteCommand.Execute(null);

        Assert.Empty(host.Library.Prompts);
        Assert.Empty(host.Settings.PinnedPromptIds);
        Assert.True(host.SettingsSaves > saves);
    });

    [Fact]
    public void ReloadAfterRestore_DropsPinsOfPromptsTheRestoreRemoved() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var mine = AddPrompt(host.Library, AddCategory(host.Library, "Writing"), "Essay plan");
        var team = TeamPrompt(host, "Complaint reply").Id;
        var gone = Guid.NewGuid(); // a prompt the restored library doesn't have
        host.Settings.PinnedPromptIds.AddRange([mine.Id, gone, team]);
        var vm = new MainViewModel(host, new FakeDialogs());

        vm.ReloadAfterRestore("Library restored from today.");

        Assert.Equal([mine.Id, team], host.Settings.PinnedPromptIds);
    });

    [Theory]
    [InlineData("""{"kind":"prompt-library-team","schemaVersion":2,"name":"x","prompts":[{"id":1}]}""", " · update Prompt Library to read it")]
    [InlineData("not json", " · can't read the team library")]
    [InlineData(null, " · can't reach the team library")]
    public void OfflineHeader_SaysWhyTheReadFailed(string? fileText, string ending) => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        var path = host.Settings.TeamSource!;
        if (fileText is null) File.Delete(path);
        else File.WriteAllText(path, fileText);

        var refresh = host.Team!.RefreshAsync();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!refresh.IsCompleted && DateTime.UtcNow < until) Sta.DoEvents();
        Sta.DoEvents();

        Assert.True(vm.IsTeamOffline);
        Assert.StartsWith("Last updated ", vm.TeamHeaderText);
        Assert.EndsWith(ending, vm.TeamHeaderText);
        Assert.Equal(LibrarySide.Team, vm.Side); // the last good copy still shows
    });

    // ---- Helpers -------------------------------------------------------------------------------

    private FakeHost NewHost(bool withTeam)
    {
        var host = new FakeHost();
        if (!withTeam) return host;

        var folder = Path.Combine(_root, "team");
        Directory.CreateDirectory(folder);
        var team = new TeamLibrary { Name = "Acme best practice", UpdatedAt = DateTimeOffset.Now };
        var sales = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        var support = new Category { Id = Guid.NewGuid(), Name = "Support" };
        team.Content.Categories.AddRange([sales, support]);
        team.Content.Prompts.Add(NewPrompt(sales, "Follow-up email", "After a meeting", "Write a follow-up email about {topic}."));
        team.Content.Prompts.Add(NewPrompt(sales, "Discovery questions", "First call", "List discovery questions for {client}."));
        team.Content.Prompts.Add(NewPrompt(support, "Complaint reply", "Empathetic", "Reply to this complaint: {text}"));
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), TeamFile.Serialize(team));

        var service = NewService(host);
        var join = service.JoinAsync(folder);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!join.IsCompleted && DateTime.UtcNow < until) Sta.DoEvents();
        Assert.True(join.Result.Success, join.Result.Message);
        host.Team = service;
        return host;
    }

    private TeamLibraryService NewService(FakeHost host)
        => new(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), host.Settings, () => host.Library,
            host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50),
            seenStore: _seenStore = new TeamSeenStore(Path.Combine(_root, "cache", "team-seen.json")));

    private static Prompt NewPrompt(Category category, string title, string description, string text) => new()
    {
        Id = Guid.NewGuid(), CategoryId = category.Id, Title = title, Description = description, Text = text,
        CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
    };

    private static Category AddCategory(Library library, string name)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = name };
        library.Categories.Add(category);
        return category;
    }

    private static Prompt AddPrompt(Library library, Category category, string title)
    {
        var prompt = NewPrompt(category, title, "", title + " text");
        library.Prompts.Add(prompt);
        return prompt;
    }

    private static Prompt TeamPrompt(FakeHost host, string title) => host.Team!.Team!.Content.Prompts.First(p => p.Title == title);

    private static Category TeamCategory(FakeHost host, string name) => host.Team!.Team!.Content.Categories.First(c => c.Name == name);

    private static void OpenTeamPrompt(MainViewModel vm, string title)
    {
        vm.ShowTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == title);
        Assert.Equal(title, vm.Editor?.Title);
    }

    // ---- What changed and out-of-date copies ---------------------------------------------------

    [Fact]
    public void WhatChanged_ShownForUpdatedPromptWithSnapshot_OpeningClearsMarkerAndMarksSeen() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        var teamPrompt = TeamPrompt(host, "Follow-up email");
        teamPrompt.Text = "Write a much better follow-up email about {topic}.";
        Assert.Equal(TeamMarker.Updated, TeamUserState.MarkerFor(host.Settings, teamPrompt));
        Assert.True(host.Team!.TryGetChange(teamPrompt.Id, out var before) && !before.IsEmpty);

        OpenTeamPrompt(vm, "Follow-up email");

        Assert.True(vm.ShowWhatChanged);
        Assert.Equal(TeamMarker.None, TeamUserState.MarkerFor(host.Settings, teamPrompt)); // opening cleared it
        Assert.True(!host.Team.TryGetChange(teamPrompt.Id, out var after) || after.IsEmpty); // and called MarkSeen

        vm.WhatChangedCommand.Execute(null);

        Assert.True(Assert.Single(dialogs.Changes).TextChanged);
    });

    [Fact]
    public void WhatChanged_HiddenForUnchangedPromptAndWithoutSnapshot() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var vm = new MainViewModel(host, new FakeDialogs());
        OpenTeamPrompt(vm, "Follow-up email");
        Assert.False(vm.ShowWhatChanged); // not Updated

        // Updated, but no copy of what was seen (as for a library joined before 1.3).
        var other = TeamPrompt(host, "Complaint reply");
        other.Text = "Changed";
        _seenStore!.Remove(other.Id);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Complaint reply");
        Assert.False(vm.ShowWhatChanged);
    });

    [Fact]
    public void MarkAllSeen_MarksEveryPromptSeenInTheService() => Sta.Run(() =>
    {
        var host = NewHost(withTeam: true);
        var vm = new MainViewModel(host, new FakeDialogs());
        var a = TeamPrompt(host, "Follow-up email");
        var b = TeamPrompt(host, "Complaint reply");
        a.Text = "A changed";
        b.Text = "B changed";
        Assert.True(host.Team!.TryGetChange(a.Id, out var ca) && !ca.IsEmpty);
        vm.ShowTeamCommand.Execute(null);

        vm.MarkAllSeenCommand.Execute(null);

        Assert.True(!host.Team.TryGetChange(a.Id, out var da) || da.IsEmpty);
        Assert.True(!host.Team.TryGetChange(b.Id, out var db) || db.IsEmpty);
    });

    [Fact]
    public void CopyToYours_RemembersTheTeamPromptAndItsFingerprint() => Sta.Run(() =>
    {
        var (_, vm, copy, teamPrompt, _) = CopyAndOpen();

        Assert.Equal(teamPrompt.Id, copy.SourceTeamPromptId);
        Assert.Equal(TeamUserState.ContentFingerprint(teamPrompt), copy.SourceFingerprint);
        Assert.False(vm.ShowTeamUpdateBanner); // team unchanged
    });

    [Fact]
    public void Banner_ShownOnlyWhileTheTeamVersionDiffers() => Sta.Run(() =>
    {
        var (host, vm, copy, teamPrompt, _) = CopyAndOpen();
        Assert.False(vm.ShowTeamUpdateBanner);

        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id); // reopen
        Assert.True(vm.ShowTeamUpdateBanner);

        host.Team!.Team!.Content.Prompts.Remove(teamPrompt); // source deleted
        Assert.False(vm.ShowTeamUpdateBanner);

        // A personal prompt that is not a copy never shows it.
        var plain = AddPrompt(host.Library, host.Library.Categories[0], "Plain");
        vm.ShowTeamCommand.Execute(null);
        vm.ShowYoursCommand.Execute(null); // rebuilds the list
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == plain.Id);
        Assert.False(vm.ShowTeamUpdateBanner);
    });

    [Fact]
    public void Banner_NotShownWhenTheTeamPromptOnlyMovesCategory() => Sta.Run(() =>
    {
        var (host, vm, copy, teamPrompt, _) = CopyAndOpen();
        var team = host.Team!.Team!.Content;
        var other = team.Categories.First(c => c.Id != teamPrompt.CategoryId);
        teamPrompt.CategoryId = other.Id;

        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);
        Assert.False(vm.ShowTeamUpdateBanner);
    });

    [Fact]
    public void UpdateMyCopy_CopySavedWithoutContentChange_DoesNotAsk() => Sta.Run(() =>
    {
        // A save that leaves title, description and text as copied (for example a tag) is not "edited".
        var (_, vm, copy, teamPrompt, dialogs) = CopyAndOpen();
        vm.Editor!.Tags.Add("mine");
        Assert.True(vm.Save());
        Assert.True(copy.UpdatedAt > copy.CreatedAt);
        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);

        vm.UpdateMyCopyCommand.Execute(null);

        Assert.Equal(0, dialogs.Confirms);
        Assert.Equal("Newer team text", copy.Text);
    });

    [Fact]
    public void UpdateMyCopy_UntouchedCopy_UpdatesWithoutAskingAndKeepsOldVersion() => Sta.Run(() =>
    {
        var (_, vm, copy, teamPrompt, dialogs) = CopyAndOpen();
        var oldText = copy.Text;
        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);

        vm.UpdateMyCopyCommand.Execute(null);

        Assert.Equal(0, dialogs.Confirms);
        Assert.Equal("Newer team text", copy.Text);
        Assert.Equal(TeamUserState.ContentFingerprint(teamPrompt), copy.SourceFingerprint);
        Assert.False(vm.ShowTeamUpdateBanner);
        Assert.Equal("Updated from the team.", vm.StatusText);
        Assert.Contains(_history!.List(copy.Id), v => v.Text == oldText);
    });

    [Fact]
    public void UpdateMyCopy_ChangedCopy_AsksFirst() => Sta.Run(() =>
    {
        var (_, vm, copy, teamPrompt, dialogs) = CopyAndOpen();
        vm.Editor!.Text = "My own wording";
        Assert.True(vm.Save());
        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);

        dialogs.ConfirmAnswer = false;
        vm.UpdateMyCopyCommand.Execute(null);
        Assert.Equal(1, dialogs.Confirms);
        Assert.Equal("Replace your changes with the team's version?", dialogs.LastConfirm);
        Assert.Equal("My own wording", copy.Text);
        Assert.True(vm.ShowTeamUpdateBanner);

        dialogs.ConfirmAnswer = true;
        vm.UpdateMyCopyCommand.Execute(null);
        Assert.Equal("Newer team text", copy.Text);
        Assert.Contains(_history!.List(copy.Id), v => v.Text == "My own wording");
    });

    [Fact]
    public void KeepMine_StoresTheFingerprintAndHidesTheBanner() => Sta.Run(() =>
    {
        var (host, vm, copy, teamPrompt, _) = CopyAndOpen();
        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);
        Assert.True(vm.ShowTeamUpdateBanner);
        var saves = host.LibrarySaves;

        vm.KeepMineCommand.Execute(null);

        Assert.Equal(TeamUserState.ContentFingerprint(teamPrompt), copy.SourceFingerprint);
        Assert.True(host.LibrarySaves > saves);
        Assert.False(vm.ShowTeamUpdateBanner);
        Assert.NotEqual("Newer team text", copy.Text);

        teamPrompt.Text = "Newer still"; // the team changes it again
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);
        Assert.True(vm.ShowTeamUpdateBanner);
    });

    [Fact]
    public void SeeChanges_ShowsYourCopyAgainstTheTeamVersion() => Sta.Run(() =>
    {
        var (_, vm, copy, teamPrompt, dialogs) = CopyAndOpen();
        teamPrompt.Text = "Newer team text";
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == copy.Id);

        vm.SeeCopyChangesCommand.Execute(null);

        Assert.True(Assert.Single(dialogs.Changes).TextChanged);
    });

    private PromptHistoryStore? _history;
    private TeamSeenStore? _seenStore;

    private (FakeHost Host, MainViewModel Vm, Prompt Copy, Prompt TeamPrompt, FakeDialogs Dialogs) CopyAndOpen()
    {
        var host = NewHost(withTeam: true);
        var dialogs = new FakeDialogs();
        _history = new PromptHistoryStore(Path.Combine(_root, "history"));
        var vm = new MainViewModel(host, dialogs, _history);
        OpenTeamPrompt(vm, "Follow-up email");
        var teamPrompt = TeamPrompt(host, "Follow-up email");
        vm.CopyToYoursCommand.Execute(null);
        var copy = Assert.Single(host.Library.Prompts, p => p.Title == "Follow-up email");
        copy.CreatedAt = copy.UpdatedAt = DateTimeOffset.Now.AddDays(-1); // copied a while ago, not edited since
        return (host, vm, copy, teamPrompt, dialogs);
    }

    private sealed class FakeHost : IMainHost
    {
        public UpdateCoordinator? Updates => null;
        public TeamLibraryService? Team { get; set; }
        public Library Library { get; } = new();
        public AppSettings Settings { get; } = new();
        public string? BackupWarning => null;
        public string? BackupWarningDetail => null;
        public bool LibraryUnwritten => false;
        public int LibrarySaves { get; private set; }
        public int SettingsSaves { get; private set; }
        public int MenuRefreshes { get; private set; }

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() { LibrarySaves++; return true; }
        public bool SaveLibraryChange() { LibrarySaves++; return true; }
        public bool SaveSettings() { SettingsSaves++; return true; }
        public void RefreshQuickMenu() => MenuRefreshes++;
        public bool CopyText(string text, string title) => true;
        public Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true) => Task.FromResult<BackupResult?>(null);
        public void OpenRestore() { }
        public void OpenSettings(bool focusBackups = false) { }
        public void OpenOnboarding() { }
        public void ShowAbout() { }
        public void ExitApp(ExitReason reason = ExitReason.User) { }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public List<TeamChangeModel> Changes { get; } = [];
        public int Confirms { get; private set; }
        public string? LastConfirm { get; private set; }
        public bool ConfirmAnswer { get; set; } = true;

        public bool? AskSaveChanges(string question) => null;
        public bool ConfirmDestructive(string message, string action) { Confirms++; LastConfirm = message; return ConfirmAnswer; }
        public void ShowChanges(string title, string caption, TeamChangeModel change) => Changes.Add(change);
        public void ShowMessage(string message) { }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;
    }
}
