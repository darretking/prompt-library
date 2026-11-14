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

/// <summary>The main window's Pinned rail row and the most-used list order, against a stand-in host.</summary>
public sealed class MainViewModelPinnedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-main-pinned-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    public MainViewModelPinnedTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static Category AddCategory(Library library, string name)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = name };
        library.Categories.Add(category);
        return category;
    }

    private static Prompt AddPrompt(Library library, Category category, string title)
    {
        var prompt = new Prompt
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Title = title, Text = title + " text",
            CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
        };
        library.Prompts.Add(prompt);
        return prompt;
    }

    private static void Use(FakeHost host, Prompt prompt, int count)
        => host.Settings.Usage[prompt.Id] = new PromptUsage { Count = count, LastUsedAt = DateTimeOffset.Now };

    private static string[] Titles(MainViewModel vm) => vm.Prompts.Select(r => r.Title).ToArray();

    private MainViewModel NewVm(FakeHost host)
        => new(host, new FakeDialogs(), new PromptHistoryStore(Path.Combine(_root, "History")), _ => { });

    /// <summary>Your side: Coding has Alpha, Bravo, Charlie; Email has Delta.</summary>
    private (FakeHost Host, MainViewModel Vm, Category Coding, Category Email, Prompt A, Prompt B, Prompt C, Prompt D) Build()
    {
        var host = new FakeHost();
        var coding = AddCategory(host.Library, "Coding");
        var email = AddCategory(host.Library, "Email");
        var a = AddPrompt(host.Library, coding, "Alpha");
        var b = AddPrompt(host.Library, coding, "Bravo");
        var c = AddPrompt(host.Library, coding, "Charlie");
        var d = AddPrompt(host.Library, email, "Delta");
        return (host, NewVm(host), coding, email, a, b, c, d);
    }

    private FakeHost NewTeamHost()
    {
        var host = new FakeHost();
        var folder = Path.Combine(_root, "team");
        Directory.CreateDirectory(folder);
        var team = new TeamLibrary { Name = "Acme", UpdatedAt = DateTimeOffset.Now };
        var sales = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        team.Content.Categories.Add(sales);
        foreach (var title in new[] { "Follow-up email", "Discovery questions" })
            team.Content.Prompts.Add(new Prompt
            {
                Id = Guid.NewGuid(), CategoryId = sales.Id, Title = title, Text = title + " text",
                CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
            });
        File.WriteAllText(Path.Combine(folder, TeamFile.FileName), TeamFile.Serialize(team));

        var service = new TeamLibraryService(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), host.Settings,
            () => host.Library, host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50),
            seenStore: new TeamSeenStore(Path.Combine(_root, "cache", "team-seen.json")));
        var join = service.JoinAsync(folder);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!join.IsCompleted && DateTime.UtcNow < until) Sta.DoEvents();
        Assert.True(join.Result.Success, join.Result.Message);
        host.Team = service;
        return host;
    }

    [Fact]
    public void Rail_PinnedRow_AfterAllPrompts_BeforeRecent_WhenSideHasPins() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PinnedPromptIds.AddRange([w.A.Id, w.D.Id]);
        Use(w.Host, w.B, 1);
        w.Vm.RefreshUserState();

        var names = w.Vm.Categories.Select(c => c.Name).ToList();
        Assert.Equal(["All prompts", "Pinned", "Recent", "Coding", "Email"], names);
        Assert.Equal(2, w.Vm.Categories.Single(c => c.IsPinnedRow).Count);
    });

    [Fact]
    public void Rail_NoPinsOnSide_NoPinnedRow() => Sta.Run(() =>
    {
        var host = NewTeamHost();
        host.Settings.PinnedPromptIds.Add(host.Team!.Team!.Content.Prompts[0].Id);
        var vm = NewVm(host);

        Assert.DoesNotContain(vm.Categories, c => c.IsPinnedRow); // Yours side: the pin is a team prompt
        vm.ShowTeamCommand.Execute(null);
        Assert.Equal(1, vm.Categories.Single(c => c.IsPinnedRow).Count);
    });

    [Fact]
    public void PinnedRow_ListsPinsMostUsedFirst() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PinnedPromptIds.AddRange([w.A.Id, w.B.Id, w.C.Id]);
        Use(w.Host, w.C, 5);
        Use(w.Host, w.B, 2);
        Use(w.Host, w.D, 50); // not pinned
        w.Vm.RefreshUserState();

        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.IsPinnedRow);

        Assert.Equal(["Charlie", "Bravo", "Alpha"], Titles(w.Vm));
    });

    [Fact]
    public void PinnedRow_HiddenTeamPin_NotListed() => Sta.Run(() =>
    {
        var host = NewTeamHost();
        var prompts = host.Team!.Team!.Content.Prompts;
        host.Settings.PinnedPromptIds.AddRange([prompts[0].Id, prompts[1].Id]);
        host.Settings.HiddenTeamPromptIds.Add(prompts[1].Id);
        var vm = NewVm(host);
        vm.ShowTeamCommand.Execute(null);

        Assert.Equal(1, vm.Categories.Single(c => c.IsPinnedRow).Count);
        vm.SelectedCategory = vm.Categories.Single(c => c.IsPinnedRow);
        Assert.Equal([prompts[0].Title], Titles(vm));
    });

    [Fact]
    public void PinnedRow_UnpinLastWhileSelected_FallsBackToAllPrompts() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PinnedPromptIds.Add(w.A.Id);
        w.Vm.RefreshUserState();
        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.IsPinnedRow);
        Assert.Equal(["Alpha"], Titles(w.Vm));

        w.Host.Settings.PinnedPromptIds.Clear();
        w.Vm.RefreshUserState();

        Assert.DoesNotContain(w.Vm.Categories, c => c.IsPinnedRow);
        Assert.True(w.Vm.SelectedCategory!.IsAll);
        Assert.Equal(4, w.Vm.Prompts.Count);
    });

    [Fact]
    public void PinnedRow_EditorSurvivesSwitch() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PinnedPromptIds.Add(w.D.Id);
        w.Vm.RefreshUserState();
        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);
        w.Vm.SelectedPrompt = w.Vm.Prompts.First(r => r.Title == "Alpha");
        w.Vm.Editor!.Title = "Alpha edited";

        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.IsPinnedRow);

        Assert.NotNull(w.Vm.Editor);
        Assert.Equal("Alpha edited", w.Vm.Editor!.Title); // switching to Pinned didn't ask or discard
    });

    [Fact]
    public void AllPrompts_ByUse_MostUsedFirst() => Sta.Run(() =>
    {
        var w = Build();
        Use(w.Host, w.C, 3);
        Use(w.Host, w.D, 9);
        w.Vm.RefreshUserState();

        w.Vm.SelectedCategory = w.Vm.Categories.First(c => c.IsAll);

        Assert.Equal(["Delta", "Charlie", "Alpha", "Bravo"], Titles(w.Vm));
    });

    [Fact]
    public void Category_ByName_AToZ() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PromptSort = "name";
        Use(w.Host, w.C, 3);
        w.Vm.RefreshUserState();

        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);

        Assert.Equal(["Alpha", "Bravo", "Charlie"], Titles(w.Vm));
    });

    [Fact]
    public void PromptSortChanged_RaiseUserState_AllPromptsResorted() => Sta.Run(() =>
    {
        var w = Build();
        Use(w.Host, w.D, 9);
        w.Vm.RefreshUserState();
        Assert.Equal("Delta", Titles(w.Vm)[0]);

        w.Host.Settings.PromptSort = "name";
        w.Vm.RefreshUserState();

        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], Titles(w.Vm));
    });

    [Fact]
    public void PinnedRow_UnpinInMainWindow_UpdatesRow() => Sta.Run(() =>
    {
        var w = Build();
        w.Host.Settings.PinnedPromptIds.AddRange([w.A.Id, w.B.Id]);
        w.Vm.RefreshUserState();
        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.IsPinnedRow);

        w.Vm.PinCommand.Execute(w.Vm.Prompts.First(r => r.Title == "Alpha"));

        Assert.Equal(1, w.Vm.Categories.Single(c => c.IsPinnedRow).Count);
        Assert.Equal(["Bravo"], Titles(w.Vm));

        w.Vm.PinCommand.Execute(w.Vm.Prompts.First());
        Assert.DoesNotContain(w.Vm.Categories, c => c.IsPinnedRow);
        Assert.True(w.Vm.SelectedCategory!.IsAll);
    });

    [Fact]
    public void PinnedRow_HideTeamPrompt_DropsItFromRow() => Sta.Run(() =>
    {
        var host = NewTeamHost();
        var prompts = host.Team!.Team!.Content.Prompts;
        host.Settings.PinnedPromptIds.AddRange([prompts[0].Id, prompts[1].Id]);
        var vm = NewVm(host);
        vm.ShowTeamCommand.Execute(null);
        vm.SelectedCategory = vm.Categories.Single(c => c.IsPinnedRow);

        vm.HideCommand.Execute(vm.Prompts.First(r => r.Id == prompts[0].Id));

        Assert.Equal(1, vm.Categories.Single(c => c.IsPinnedRow).Count);
        Assert.Equal([prompts[1].Title], Titles(vm));
    });

    [Fact]
    public void Copy_RefreshesQuickMenu() => Sta.Run(() =>
    {
        var w = Build();
        w.Vm.SelectedCategory = w.Vm.Categories.Single(c => c.Id == w.Coding.Id);
        w.Vm.SelectedPrompt = w.Vm.Prompts.First();
        var before = w.Host.MenuRefreshes;

        w.Vm.Copy();

        Assert.True(w.Host.MenuRefreshes > before);
    });

    private sealed class FakeHost : IMainHost
    {
        public UpdateCoordinator? Updates => null;
        public TeamLibraryService? Team { get; set; }
        public Library Library { get; } = new();
        public AppSettings Settings { get; } = new();
        public string? BackupWarning => null;
        public string? BackupWarningDetail => null;
        public bool LibraryUnwritten => false;
        public int MenuRefreshes { get; private set; }

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() => true;
        public bool SaveLibraryChange() => true;
        public bool SaveSettings() => true;
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
        public void ShowChanges(string title, string caption, TeamChangeModel change) { }
        public void ShowMessage(string message) { }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;
    }
}
