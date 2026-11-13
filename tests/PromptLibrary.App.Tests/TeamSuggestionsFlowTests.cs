using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

/// <summary>Suggest for the team and the editors' Suggestions window at view model level, with a temp team folder.</summary>
public sealed class TeamSuggestionsFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-team-sugg-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    public TeamSuggestionsFlowTests() => Directory.CreateDirectory(_root);

    private string TeamPath => Path.Combine(_root, "team", TeamFile.FileName);

    public void Dispose()
    {
        _http.Dispose();
        try
        {
            if (File.Exists(TeamPath)) File.SetAttributes(TeamPath, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void Suggest_FileSource_WritesASuggestionFile() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        var vm = new MainViewModel(host, dialogs);
        Assert.True(vm.ShowSuggest);
        dialogs.Suggest = d => { d.Note = "Handy for renewals"; d.YourName = "Ana"; };

        vm.SuggestCommand.Execute(null);
        Until(() => vm.StatusText == "Suggestion sent to the team.");

        var list = TeamSuggestions.List(TeamPath);
        var s = Assert.Single(list);
        Assert.Equal("Renewal email", s.Title);
        Assert.Equal("Sales", s.CategoryName); // the team has Sales, so it is the default
        Assert.Equal("Handy for renewals", s.Note);
        Assert.Equal("Ana", s.From);
        Assert.Null(host.Copied);
    });

    [Fact]
    public void Suggest_NotWritable_PutsItOnTheClipboard() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        File.WriteAllText(TeamSuggestions.SuggestionsFolder(TeamPath), "a file where the folder should be");
        var vm = new MainViewModel(host, dialogs);

        vm.SuggestCommand.Execute(null);
        Until(() => vm.StatusText?.StartsWith("Couldn't reach the team folder.") == true);

        Assert.Contains("Prompt suggestion: Renewal email", host.Copied);
        Assert.Contains("\r\n", host.Copied);
        Assert.Equal("Couldn't reach the team folder. The suggestion is on your clipboard to send to the team owner.", vm.StatusText);
    });

    [Fact]
    public void Suggest_UrlSource_PutsItOnTheClipboard() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        var vm = new MainViewModel(host, dialogs);
        typeof(TeamLibraryService).GetProperty(nameof(TeamLibraryService.Source))!.SetValue(host.Team,
            new TeamSource(TeamSourceKind.Url, "https://example.com/team.json"));

        vm.SuggestCommand.Execute(null);

        Assert.Contains("Prompt suggestion: Renewal email", host.Copied);
        Assert.StartsWith("Couldn't reach the team folder.", vm.StatusText);
        Assert.Empty(TeamSuggestions.List(TeamPath));
    });

    [Fact]
    public void Count_EditorsSeeIt_MembersDont() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        TeamSuggestions.TryWrite(TeamPath, Sample("One", "Sales"));
        TeamSuggestions.TryWrite(TeamPath, Sample("Two", "Sales"));
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        WaitFor(host.Team!.RefreshAsync());
        Until(() => vm.SuggestionCount == 2);
        Assert.True(vm.ShowSuggestionsButton);
        Assert.Equal("Suggestions (2)", vm.SuggestionsLabel);

        File.SetAttributes(TeamPath, FileAttributes.ReadOnly);
        WaitFor(host.Team.RefreshAsync());
        Assert.False(host.Team.CanEdit);
        Until(() => vm.SuggestionCount == 0);
        Assert.False(vm.ShowSuggestionsButton);
    });

    [Fact]
    public void Add_PutsThePromptInTheTeamAndMovesTheFile() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        var s = Sample("Pricing reply", "Brand new");
        TeamSuggestions.TryWrite(TeamPath, s);
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        dialogs.Review = d =>
        {
            Assert.Equal("new: Brand new", d.Category!.Display);
            d.AddCommand.Execute(null);
        };

        vm.SuggestionsCommand.Execute(null);
        Until(() => vm.StatusText == "Added to team prompts · Brand new");

        var team = TeamFile.Parse(File.ReadAllText(TeamPath));
        var category = Assert.Single(team.Content.Categories, c => c.Name == "Brand new");
        var prompt = Assert.Single(team.Content.Prompts, p => p.Title == "Pricing reply");
        Assert.Equal(category.Id, prompt.CategoryId);
        Assert.NotEqual(s.Id, prompt.Id);
        Assert.False(prompt.Featured);
        Assert.Empty(TeamSuggestions.List(TeamPath));
        Assert.True(File.Exists(Path.Combine(TeamSuggestions.SuggestionsFolder(TeamPath), "done", s.Id + ".json")));
        Until(() => vm.SuggestionCount == 0);
    });

    [Fact]
    public void Dismiss_MovesTheFile_AndAnEmptyListSaysSo() => Sta.Run(() =>
    {
        var (host, dialogs) = NewHost();
        var s = Sample("Pricing reply", "Sales");
        TeamSuggestions.TryWrite(TeamPath, s);
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        dialogs.Review = d =>
        {
            d.DismissCommand.Execute(null);
            Assert.True(d.IsEmpty);
            Assert.False(d.HasSelection);
        };

        vm.SuggestionsCommand.Execute(null);
        Until(() => dialogs.Reviewed == 1);

        Assert.Empty(TeamSuggestions.List(TeamPath));
        Assert.True(File.Exists(Path.Combine(TeamSuggestions.SuggestionsFolder(TeamPath), "done", s.Id + ".json")));
        Until(() => vm.SuggestionCount == 0);
    });

    [Fact]
    public void CategoryDefaults_FollowTheSuggestedName()
    {
        var existing = SuggestionsViewModel.OptionsFor("support", ["Sales", "Support"]);
        Assert.Equal("Support", existing[0].Display);
        Assert.Equal(2, existing.Count);

        var created = SuggestionsViewModel.OptionsFor("Legal", ["Sales", "Support"]);
        Assert.Equal("new: Legal", created[0].Display);
        Assert.Equal("Legal", created[0].Name);

        var own = new SuggestViewModel("T", "support", ["Sales", "Support"], "Ana");
        Assert.Equal("Support", own.SelectedCategory);
        var other = new SuggestViewModel("T", "Legal", ["Sales", "Support"], "Ana");
        Assert.Equal("Sales", other.SelectedCategory);
        Assert.Contains("Legal", other.Categories);
        other.Note = new string('x', 281);
        Assert.False(other.Send());
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static Suggestion Sample(string title, string category) => new(Guid.NewGuid(), title, "A description",
        "Reply about {topic}.", ["pricing"], category, "From the field", "Ana", DateTimeOffset.Now);

    private (FakeHost host, FakeDialogs dialogs) NewHost()
    {
        var host = new FakeHost();
        var dialogs = new FakeDialogs();
        Directory.CreateDirectory(Path.GetDirectoryName(TeamPath)!);
        var team = new TeamLibrary { Name = "Acme best practice", UpdatedAt = DateTimeOffset.Now };
        var sales = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        team.Content.Categories.Add(sales);
        team.Content.Prompts.Add(new Prompt
        {
            Id = Guid.NewGuid(), CategoryId = sales.Id, Title = "Follow-up email", Text = "Write a follow-up.",
            CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
        });
        File.WriteAllText(TeamPath, TeamFile.Serialize(team));

        var mine = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        host.Library.Categories.Add(mine);
        host.Library.Prompts.Add(new Prompt
        {
            Id = Guid.NewGuid(), CategoryId = mine.Id, Title = "Renewal email", Description = "For renewals",
            Text = "Write a renewal email for {client}.", CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
        });

        var service = new TeamLibraryService(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), host.Settings,
            () => host.Library, host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50));
        var join = service.JoinAsync(Path.GetDirectoryName(TeamPath)!);
        WaitFor(join);
        Assert.True(join.Result.Success, join.Result.Message);
        host.Team = service;
        return (host, dialogs);
    }

    private static void WaitFor(Task task) => Until(() => task.IsCompleted);

    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until) Sta.DoEvents();
        Assert.True(condition(), "Timed out waiting.");
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
        public string? Copied { get; private set; }

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() => true;
        public bool SaveLibraryChange() => true;
        public bool SaveSettings() => true;
        public void RefreshQuickMenu() { }
        public bool CopyText(string text, string title) { Copied = text; return true; }
        public IReadOnlyList<CloudFolder> DetectCloudFolders() => [];
        public Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true) => Task.FromResult<BackupResult?>(null);
        public void OpenRestore() { }
        public void OpenSettings(bool focusBackups = false) { }
        public void OpenOnboarding() { }
        public void ShowAbout() { }
        public void ExitApp(ExitReason reason = ExitReason.User) { }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public Action<SuggestViewModel>? Suggest { get; set; }
        public Action<SuggestionsViewModel>? Review { get; set; }
        public int Reviewed { get; private set; }

        public bool? AskSaveChanges(string question) => null;
        public bool ConfirmDestructive(string message, string action) => true;
        public void ShowMessage(string message) { }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;

        public bool ShowSuggest(SuggestViewModel dialog)
        {
            Suggest?.Invoke(dialog);
            return dialog.Send();
        }

        public void ShowSuggestions(SuggestionsViewModel dialog)
        {
            Review?.Invoke(dialog);
            Reviewed++;
        }
    }
}
