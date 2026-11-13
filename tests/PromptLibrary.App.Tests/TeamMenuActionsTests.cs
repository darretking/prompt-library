using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

/// <summary>The Team header's ⋯ menu actions (Copy invite, note, fill-in values, team check) at view model level.</summary>
public sealed class TeamMenuActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-team-menu-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    public TeamMenuActionsTests() => Directory.CreateDirectory(_root);

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
    public void CopyInvite_CopiesWindowsLineBreaksAndSaysSo() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);

        vm.CopyInviteCommand.Execute(null);

        var expected = TeamInvite.Build(host.Team!.Source!, host.Clouds).Replace("\n", "\r\n");
        Assert.Equal(expected, host.Copied);
        Assert.DoesNotContain("\n", host.Copied!.Replace("\r\n", ""));
        Assert.Equal("Invite copied. Paste it into an email or chat.", vm.StatusText);
    });

    [Fact]
    public void EditorOnlyItems_HiddenForMembers_CopyInviteStays() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        Assert.True(vm.ShowTeamMoreMenu);
        Assert.True(vm.ShowTeamEditorItems);

        File.SetAttributes(TeamPath, FileAttributes.ReadOnly);
        WaitFor(host.Team!.RefreshAsync());
        Assert.False(host.Team.CanEdit);

        Assert.True(vm.ShowTeamMoreMenu);
        Assert.False(vm.ShowTeamEditorItems);
        vm.TeamNoteCommand.Execute(null);
        vm.TeamFillInValuesCommand.Execute(null);
        vm.TeamCheckCommand.Execute(null);
        Assert.Equal(0, host.Dialogs.Opened); // the actions do nothing for a member
    });

    [Fact]
    public void Note_SaveAndRemove_RoundTripThroughTheFile() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { NoteText = "Use the Sales prompts first." };
        host.Dialogs = dialogs;
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        Assert.Null(vm.TeamNoteLine);

        vm.TeamNoteCommand.Execute(null);
        Until(() => vm.StatusText == "Note saved for everyone");

        Assert.False(dialogs.LastNoteCanRemove);
        Assert.Equal("Use the Sales prompts first.", TeamFile.Parse(File.ReadAllText(TeamPath)).Note?.Text);
        Assert.StartsWith("Note: Use the Sales prompts first. · ", vm.TeamNoteLine);

        dialogs.NoteText = null; // Remove note
        vm.TeamNoteCommand.Execute(null);
        Until(() => vm.StatusText == "Note removed");

        Assert.True(dialogs.LastNoteCanRemove);
        Assert.Null(TeamFile.Parse(File.ReadAllText(TeamPath)).Note);
        Assert.Null(vm.TeamNoteLine);
    });

    [Fact]
    public void NoteDialog_LimitsTheTextAndOffersRemoveOnlyWithANote()
    {
        var empty = new TeamNoteViewModel(null);
        Assert.False(empty.CanSave);
        Assert.False(empty.CanRemove);
        empty.Text = new string('a', TeamFile.MaxNoteLength);
        Assert.True(empty.CanSave);
        Assert.Equal("280 / 280", empty.CountText);
        empty.Text += "b";
        Assert.False(empty.CanSave);

        var existing = new TeamNoteViewModel("Old note");
        Assert.True(existing.CanRemove);
        Assert.True(existing.Remove());
        Assert.Equal(TeamNoteChoice.Remove, existing.Choice);
    }

    [Fact]
    public void NoteLine_DismissStoresHash_ReappearsWhenTheTextChanges() => Sta.Run(() =>
    {
        var host = NewHost(note: "Freeze on Friday.");
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        Assert.True(vm.ShowTeamNoteLine);
        Assert.StartsWith("Note: Freeze on Friday. · ", vm.TeamNoteLine);

        vm.DismissTeamNoteCommand.Execute(null);

        Assert.False(vm.ShowTeamNoteLine);
        Assert.False(string.IsNullOrEmpty(host.Settings.SeenTeamNote));
        Assert.DoesNotContain("Freeze", host.Settings.SeenTeamNote);
        Assert.True(host.SettingsSaves > 0);

        // The same text stays hidden; a new text shows again.
        var seen = host.Settings.SeenTeamNote;
        var vm2 = new MainViewModel(host, new FakeDialogs());
        vm2.ShowTeamCommand.Execute(null);
        Assert.False(vm2.ShowTeamNoteLine);

        host.Team!.Team!.Note = new TeamNote("Freeze moved to Monday.", DateTimeOffset.Now);
        Assert.True(vm2.ShowTeamNoteLine);
        Assert.Equal(seen, host.Settings.SeenTeamNote);
    });

    [Fact]
    public void NoteLine_OnlyOnTheTeamSide() => Sta.Run(() =>
    {
        var host = NewHost(note: "Hello team.");
        var vm = new MainViewModel(host, new FakeDialogs());
        Assert.False(vm.ShowTeamNoteLine);
        vm.ShowTeamCommand.Execute(null);
        Assert.True(vm.ShowTeamNoteLine);
    });

    [Fact]
    public void FillInValues_PrefillFromPromptFields_SortedWithoutTokens()
    {
        var vm = new TeamFillInValuesViewModel(
            ["Write about {topic} for {Client}.", "{date} {clipboard} {cursor} {topic} {Region}", "No fields here", "{pick / one / two}"],
            new Dictionary<string, string> { ["client"] = "Acme", ["Legacy"] = "kept" });

        Assert.Equal(["Client", "Legacy", "Region", "topic"], vm.Rows.Select(r => r.Name).ToArray());
        Assert.Equal("Acme", vm.Rows.First(r => r.Name == "Client").Value);
        Assert.Equal("kept", vm.Rows.First(r => r.Name == "Legacy").Value);
        Assert.Equal("", vm.Rows.First(r => r.Name == "topic").Value);
    }

    [Fact]
    public void FillInValues_Save_StoresOnlyFilledRows()
    {
        var vm = new TeamFillInValuesViewModel(["{topic} {client}"], new Dictionary<string, string>());
        vm.Rows.First(r => r.Name == "client").Value = "  Acme  ";
        var added = vm.AddField();
        added.Name = "Region";
        added.Value = "EMEA";

        Assert.True(vm.TrySave());
        Assert.Equal(2, vm.Result.Count);
        Assert.Equal("Acme", vm.Result["client"]);
        Assert.Equal("EMEA", vm.Result["Region"]);
    }

    [Fact]
    public void FillInValues_Save_RefusesTooManyTooLongAndUnnamed()
    {
        var many = new TeamFillInValuesViewModel([], new Dictionary<string, string>());
        for (var i = 0; i <= TeamFile.MaxFillInValues; i++)
        {
            var row = many.AddField();
            row.Name = "f" + i;
            row.Value = "v";
        }
        Assert.False(many.TrySave());
        Assert.Equal("You can store up to 50 values. Clear some and try again.", many.Message);

        var long1 = new TeamFillInValuesViewModel(["{topic}"], new Dictionary<string, string>());
        long1.Rows[0].Value = new string('x', TeamFile.MaxFillInValueLength + 1);
        Assert.False(long1.TrySave());
        Assert.Equal("\"topic\" is too long. Values can be up to 200 characters.", long1.Message);
        long1.Rows[0].Value = new string('x', TeamFile.MaxFillInValueLength);
        Assert.True(long1.TrySave());
        Assert.Null(long1.Message);

        var unnamed = new TeamFillInValuesViewModel([], new Dictionary<string, string>());
        unnamed.AddField().Value = "orphan";
        Assert.False(unnamed.TrySave());
        Assert.Equal("Give each value a field name, or clear the value.", unnamed.Message);
    }

    [Fact]
    public void FillInValues_SavedThroughTheGuardedSave() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { FillIn = vm => vm.Rows.First(r => r.Name == "topic").Value = "Q3 results" };
        host.Dialogs = dialogs;
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);

        vm.TeamFillInValuesCommand.Execute(null);
        Until(() => vm.StatusText == "Fill-in values saved for everyone");

        Assert.Equal(["client", "text", "topic"], dialogs.LastFillInNames);
        Assert.Equal("Q3 results", TeamFile.Parse(File.ReadAllText(TeamPath)).FillInValues["topic"]);
        Assert.False(vm.IsEditingTeam); // no edit mode needed
    });

    [Fact]
    public void Check_CleanLibrary_ListsNothing() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        host.Dialogs = dialogs;
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);

        vm.TeamCheckCommand.Execute(null);

        Assert.True(dialogs.LastCheck!.IsEmpty);
        Assert.Equal("Everything looks good.", dialogs.LastCheck.EmptyText);
        Assert.False(vm.IsEditingTeam);
    });

    [Fact]
    public void Check_ClickingAPromptIssue_EntersEditModeAndSelectsIt() => Sta.Run(() =>
    {
        var host = NewHost(tweak: t =>
        {
            var support = t.Content.Categories.First(c => c.Name == "Support");
            t.Content.Prompts.Add(new Prompt
            {
                Id = Guid.NewGuid(), CategoryId = support.Id, Title = "Quick note", Description = "", Text = "Jot {thing}",
                CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
            });
        });
        var dialogs = new FakeDialogs { CheckPick = rows => rows.First(r => r.Issue.Kind == HealthIssueKind.NoDescription) };
        host.Dialogs = dialogs;
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        Assert.False(vm.IsEditingTeam);

        vm.TeamCheckCommand.Execute(null);

        var row = Assert.Single(dialogs.LastCheck!.Rows);
        Assert.Equal("\"Quick note\" has no description.", row.Message);
        Assert.True(vm.IsEditingTeam);
        Assert.Equal("Quick note", vm.Editor?.Title);
        Assert.False(vm.Editor!.IsReadOnly);
    });

    [Fact]
    public void Check_ClickingACategoryIssue_SelectsTheCategory() => Sta.Run(() =>
    {
        var host = NewHost(tweak: t => t.Content.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Ghost" }));
        var dialogs = new FakeDialogs { CheckPick = rows => rows.First(r => r.Issue.Kind == HealthIssueKind.EmptyCategory) };
        host.Dialogs = dialogs;
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);

        vm.TeamCheckCommand.Execute(null);

        Assert.True(vm.IsEditingTeam);
        Assert.Equal("Ghost", vm.SelectedCategory?.Name);
    });

    // ---- Helpers -------------------------------------------------------------------------------

    private FakeHost NewHost(string? note = null, Action<TeamLibrary>? tweak = null)
    {
        var host = new FakeHost();
        Directory.CreateDirectory(Path.GetDirectoryName(TeamPath)!);
        var team = new TeamLibrary { Name = "Acme best practice", UpdatedAt = DateTimeOffset.Now };
        if (note is not null) team.Note = new TeamNote(note, DateTimeOffset.Now);
        var sales = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        var support = new Category { Id = Guid.NewGuid(), Name = "Support" };
        team.Content.Categories.AddRange([sales, support]);
        team.Content.Prompts.Add(NewPrompt(sales, "Follow-up email", "Write a follow-up email about {topic}."));
        team.Content.Prompts.Add(NewPrompt(sales, "Discovery questions", "List discovery questions for {client}."));
        team.Content.Prompts.Add(NewPrompt(support, "Complaint reply", "Reply to this complaint: {text} {date}"));
        tweak?.Invoke(team);
        File.WriteAllText(TeamPath, TeamFile.Serialize(team));

        var service = new TeamLibraryService(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), host.Settings,
            () => host.Library, host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50));
        var join = service.JoinAsync(Path.GetDirectoryName(TeamPath)!);
        WaitFor(join);
        Assert.True(join.Result.Success, join.Result.Message);
        host.Team = service;
        return host;
    }

    private static Prompt NewPrompt(Category category, string title, string text) => new()
    {
        Id = Guid.NewGuid(), CategoryId = category.Id, Title = title, Description = title + " description", Text = text,
        CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
    };

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
        public int SettingsSaves { get; private set; }
        public string? Copied { get; private set; }
        public IReadOnlyList<CloudFolder> Clouds { get; } = [];
        public FakeDialogs Dialogs { get; set; } = new();

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() => true;
        public bool SaveLibraryChange() => true;
        public bool SaveSettings() { SettingsSaves++; return true; }
        public void RefreshQuickMenu() { }
        public bool CopyText(string text, string title) { Copied = text; return true; }
        public IReadOnlyList<CloudFolder> DetectCloudFolders() => Clouds; // never looks at this PC's cloud folders
        public Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true) => Task.FromResult<BackupResult?>(null);
        public void OpenRestore() { }
        public void OpenSettings(bool focusBackups = false) { }
        public void OpenOnboarding() { }
        public void ShowAbout() { }
        public void ExitApp(ExitReason reason = ExitReason.User) { }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public int Opened { get; private set; }

        /// <summary>Text the note dialog saves; null presses Remove note.</summary>
        public string? NoteText { get; set; }
        public bool LastNoteCanRemove { get; private set; }

        public Action<TeamFillInValuesViewModel>? FillIn { get; set; }
        public string[] LastFillInNames { get; private set; } = [];

        public Func<IReadOnlyList<TeamCheckRow>, TeamCheckRow?>? CheckPick { get; set; }
        public TeamCheckViewModel? LastCheck { get; private set; }

        public bool? AskSaveChanges(string question) => null;
        public bool ConfirmDestructive(string message, string action) => true;
        public void ShowMessage(string message) { }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;

        public void ShowTeamNote(TeamNoteViewModel dialog)
        {
            Opened++;
            LastNoteCanRemove = dialog.CanRemove;
            if (NoteText is { } text)
            {
                dialog.Text = text;
                dialog.Save();
            }
            else dialog.Remove();
        }

        public bool ShowTeamFillInValues(TeamFillInValuesViewModel dialog)
        {
            Opened++;
            LastFillInNames = dialog.Rows.Select(r => r.Name).ToArray();
            FillIn?.Invoke(dialog);
            return dialog.TrySave();
        }

        public void ShowTeamCheck(TeamCheckViewModel dialog)
        {
            Opened++;
            LastCheck = dialog;
            if (CheckPick?.Invoke(dialog.Rows) is { } row) dialog.Choose(row);
        }
    }
}

