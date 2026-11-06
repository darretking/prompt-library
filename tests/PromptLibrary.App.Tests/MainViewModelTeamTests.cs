using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
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
        for (var i = 0; i < TeamUserState.MaxPins; i++) host.Settings.PinnedPromptIds.Add(Guid.NewGuid());
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
            host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50));

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
        public bool? AskSaveChanges(string question) => null;
        public bool ConfirmDestructive(string message, string action) => true;
        public Guid? ChooseMoveTarget(string categoryName, int promptCount, IReadOnlyList<Category> targets) => null;
        public void ShowMessage(string message) { }
    }
}
