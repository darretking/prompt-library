using System.IO;
using System.Net.Http;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

/// <summary>Editing the team library in place (Edit team library … Done), against a real team file in a temp folder.</summary>
public sealed class TeamEditModeTests : IDisposable
{
    private const string ChangedByOtherMessage =
        "Someone else changed the team library, so it's been reloaded. Your change is still here: check it and save again.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-team-edit-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    public TeamEditModeTests() => Directory.CreateDirectory(_root);

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

    // ---- Tests ---------------------------------------------------------------------------------

    [Fact]
    public void EditButton_OnlyWhenCanEdit() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        Assert.False(vm.ShowEditTeamButton); // Your prompts side

        vm.ShowTeamCommand.Execute(null);
        Assert.True(host.Team!.CanEdit);
        Assert.True(vm.ShowEditTeamButton);
        Assert.False(vm.ShowTeamReadOnlyNote);
        Assert.False(vm.IsEditingTeam);

        vm.EditTeamCommand.Execute(null);

        Assert.True(vm.IsEditingTeam);
        Assert.False(vm.ShowEditTeamButton); // the edit bar's Done takes its place
        Assert.Equal("Editing team library: changes go to everyone", vm.TeamEditBarText);
        Assert.False(vm.CurrentDocument.IsReadOnly);
        Assert.True(vm.ShowEditButtons);
        Assert.False(vm.Editor!.IsReadOnly);
        Assert.Null(vm.LockLineText);

        vm.DoneEditingTeamCommand.Execute(null);
        Assert.False(vm.IsEditingTeam);
        Assert.True(vm.CurrentDocument.IsReadOnly);
        Assert.True(vm.Editor!.IsReadOnly);

        // This PC can't write to the file any more: no edit button, the muted "Read only" instead.
        File.SetAttributes(TeamPath, FileAttributes.ReadOnly);
        WaitFor(host.Team.RefreshAsync());
        Assert.False(host.Team.CanEdit);
        Assert.False(vm.ShowEditTeamButton);
        Assert.True(vm.ShowTeamReadOnlyNote);
        vm.EditTeamCommand.Execute(null);
        Assert.False(vm.IsEditingTeam);
        Assert.True(vm.CurrentDocument.IsReadOnly);
    });

    [Fact]
    public void EditMode_HoldsRefresh_DoneReleases() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);

        // Another editor changes the file; the hourly refresh (or the file watcher) asks for a refresh.
        ChangeFileElsewhere("Discovery questions", "Discovery questions (v2)");
        WaitFor(host.Team!.RequestRefreshAsync());
        Pump(TimeSpan.FromMilliseconds(300));

        Assert.Contains(host.Team.Team!.Content.Prompts, p => p.Title == "Discovery questions"); // held
        Assert.DoesNotContain(vm.Prompts, r => r.Title == "Discovery questions (v2)");

        vm.DoneEditingTeamCommand.Execute(null);

        Until(() => vm.Prompts.Any(r => r.Title == "Discovery questions (v2)"));
        Assert.False(vm.IsEditingTeam);
        Assert.Equal("Team prompts updated", vm.StatusText);
    });

    [Fact]
    public void Delete_AsksRemovesForEveryone() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { Confirm = false };
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        var id = vm.Editor!.PromptId!.Value;

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete \"Follow-up email\"? This removes it for everyone.", dialogs.LastConfirm);
        Assert.Contains(host.Team!.Team!.Content.Prompts, p => p.Id == id); // cancelled

        dialogs.Confirm = true;
        vm.DeleteCommand.Execute(null);

        Until(() => ReadTeamFile() is { Length: > 0 } text && !text.Contains(id.ToString()));
        Until(() => vm.StatusText == "Saved for everyone");
        Assert.DoesNotContain(vm.Prompts, r => r.Id == id);
        Assert.Empty(host.Library.Prompts); // nothing reached your prompts
        Assert.Equal(0, host.LibrarySaves);
    });

    [Fact]
    public void Save_ChangedByOther_KeepsTypedText_ShowsMessage() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Text = "My typed text";

        ChangeFileElsewhere("Discovery questions", "Discovery questions (v2)");
        vm.SaveCommand.Execute(null);

        Until(() => dialogs.Messages.Count > 0);
        Assert.Equal(ChangedByOtherMessage, dialogs.Messages.Single());
        Until(() => vm.Prompts.Any(r => r.Title == "Discovery questions (v2)")); // reloaded under the editor
        Assert.Equal("My typed text", vm.Editor!.Text); // nothing typed is thrown away
        Assert.True(vm.Editor.IsDirty);
        Assert.DoesNotContain("My typed text", ReadTeamFile());

        // Saving again goes onto the fresh copy. The editor stays unsaved until the write has finished.
        vm.SaveCommand.Execute(null);
        Assert.True(vm.Editor!.IsDirty);

        Until(() => !vm.Editor!.IsDirty);
        var file = ReadTeamFile();
        Assert.Contains("My typed text", file);
        Assert.Contains("Discovery questions (v2)", file); // the other editor's change survives
        Assert.Equal("Saved for everyone", vm.StatusText);
        Assert.Single(dialogs.Messages);
        Assert.Equal(0, host.LibrarySaves);
    });

    [Fact]
    public void DeleteCategory_WithItsPrompts_RemovesThemForEveryone() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { Deletion = new CategoryDeletion(null, DeletePrompts: true) };
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        var sales = vm.Categories.Single(c => c.Name == "Sales");
        var salesIds = host.Team!.Team!.Content.Prompts.Where(p => p.CategoryId == sales.Id).Select(p => p.Id).ToList();
        Assert.Equal(2, salesIds.Count);

        vm.DeleteCategoryCommand.Execute(sales);

        Assert.Equal(("Sales", 2), dialogs.LastDeletionAsked);
        Assert.Equal("Deleted \"Sales\" and 2 prompts.", vm.StatusText);
        Until(() => ReadTeamFile() is { Length: > 0 } text && !text.Contains("Follow-up email"));
        Until(() => vm.StatusText == "Saved for everyone");
        var file = ReadTeamFile();
        Assert.DoesNotContain("Discovery questions", file);
        Assert.Contains("Complaint reply", file); // the other category is untouched
        Assert.DoesNotContain(vm.Categories, c => c.Name == "Sales");
        Assert.Empty(host.Library.Prompts); // nothing in your prompts changed
        Assert.Equal(0, host.LibrarySaves);
    });

    [Fact]
    public void ImportAndExport_AreForYourPromptsOnly() => Sta.Run(() =>
    {
        var vm = new MainViewModel(NewHost(), new FakeDialogs());
        Assert.True(vm.ImportCommand.CanExecute(null));
        Assert.True(vm.ExportCommand.CanExecute(null));

        vm.ShowTeamCommand.Execute(null);

        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.ExportCommand.CanExecute(null));
    });

    [Fact]
    public void NewerSchema_NoEditButton_ShowsReason() => Sta.Run(() =>
    {
        var host = NewHost(schemaVersion: 2);
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);

        Assert.True(host.Team!.Team!.IsNewerSchema);
        Assert.False(host.Team.CanEdit);
        Assert.False(vm.ShowEditTeamButton);
        Assert.Equal("This team library was made with a newer Prompt Library. Update to edit it.", vm.TeamEditBlockedText);

        vm.EditTeamCommand.Execute(null);
        Assert.False(vm.IsEditingTeam);
        Assert.True(vm.CurrentDocument.IsReadOnly);
    });

    [Fact]
    public void Review_ListsConflictCopyFirst_RestoreBringsVersionBack() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        Assert.True(vm.ShowTeamMoreMenu);
        Assert.False(vm.ShowConflictBar);

        // An edit makes a history copy of the version before it.
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Title = "Follow-up email (edited)";
        vm.SaveCommand.Execute(null);
        Until(() => vm.StatusText == "Saved for everyone");
        vm.DoneEditingTeamCommand.Execute(null);

        // File sync leaves a second version beside the file.
        File.Copy(TeamPath, Path.Combine(Path.GetDirectoryName(TeamPath)!, "team-prompts-OTHERPC.json"));
        WaitFor(host.Team!.RefreshAsync());
        Assert.True(vm.ShowConflictBar);
        Assert.Equal("Two versions of the team library found", vm.ConflictBarText);

        vm.ReviewConflictCommand.Execute(null);
        Assert.True(dialogs.LastHistoryReview);

        var list = host.Team.ListHistoryAsync();
        WaitFor(list);
        var items = list.Result;
        Assert.True(items[0].IsConflictCopy);
        Assert.StartsWith("Other version from sync · today, ", Views.TeamHistoryWindow.RowText(items[0], DateTime.Now));
        Assert.EndsWith(" · 3 prompts", Views.TeamHistoryWindow.RowText(items[1], DateTime.Now));
        var older = Assert.Single(items, i => !i.IsConflictCopy);

        var restore = host.Team.RestoreAsync(older);
        WaitFor(restore);
        Assert.Equal(TeamSaveStatus.Saved, restore.Result.Status);
        Until(() => vm.Prompts.Any(r => r.Title == "Follow-up email"));
        Assert.Contains("\"Follow-up email\"", ReadTeamFile());
        var after = host.Team.ListHistoryAsync();
        WaitFor(after);
        Assert.Equal(2, after.Result.Count(i => !i.IsConflictCopy)); // the version it replaced was kept
    });

    // ---- Fix round 1 ---------------------------------------------------------------------------

    [Fact]
    public void CloseWindow_EndsEditMode_AppliesHeldRefresh() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { SaveAnswer = true };
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        WaitFor(host.Team!.RequestRefreshAsync()); // the hourly refresh, held while editing
        Assert.True(host.Team.HasHeldRefresh);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Text = "Typed before closing";
        var hidden = 0;

        // "Save changes?" → Save: the window doesn't hide mid-write; it hides once the save has gone through.
        Assert.False(vm.ConfirmLeave(() => hidden++));
        Assert.Equal(0, hidden);
        Assert.True(vm.IsEditingTeam);
        Until(() => hidden == 1);
        Assert.False(vm.IsEditingTeam);
        Assert.False(host.Team.HasHeldRefresh); // released, and the held refresh ran
        Assert.Equal("Team prompts updated", vm.StatusText);
        // Read with pauses: ReadTeamFile gives "" while the released refresh probes the file for writing.
        Until(() => { Thread.Sleep(20); return ReadTeamFile().Contains("Typed before closing"); });

        // Nothing unsaved: it can hide at once, and editing ends.
        vm.EditTeamCommand.Execute(null);
        Assert.True(vm.ConfirmLeave(() => hidden++));
        Assert.False(vm.IsEditingTeam);
        Assert.Equal(1, hidden);
    });

    [Fact]
    public void Save_PromptDeletedByOther_KeepsTypedTextAsNewPrompt() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        var salesId = vm.Editor!.CategoryId;
        vm.Editor.Text = "Typed into a prompt someone deleted";

        var other = TeamFile.Parse(File.ReadAllText(TeamPath));
        other.Content.Prompts.RemoveAll(p => p.Title == "Follow-up email");
        File.WriteAllText(TeamPath, TeamFile.Serialize(other));
        vm.SaveCommand.Execute(null);

        Until(() => dialogs.Messages.Count > 0);
        Assert.Equal(ChangedByOtherMessage, dialogs.Messages[0]);
        Assert.NotNull(vm.Editor);
        Assert.True(vm.Editor!.IsNew);
        Assert.True(vm.Editor.IsDirty);
        Assert.Equal("Follow-up email", vm.Editor.Title);
        Assert.Equal("Typed into a prompt someone deleted", vm.Editor.Text);
        Assert.Equal(salesId, vm.Editor.CategoryId);

        vm.SaveCommand.Execute(null);
        Until(() => vm.Editor is { IsDirty: false });
        Assert.Single(TeamFile.Parse(ReadTeamFile()).Content.Prompts, p => p.Text == "Typed into a prompt someone deleted");
    });

    [Fact]
    public void SaveChanges_Save_ContinuesPendingAction_OnlyWhenSaved() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { SaveAnswer = true };
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Text = "Saved on the way out";

        vm.ShowYoursCommand.Execute(null); // "Save changes?" → Save
        Assert.Equal(LibrarySide.Team, vm.Side); // not yet: the save is writing
        Until(() => vm.Side == LibrarySide.Yours);
        Assert.Contains("Saved on the way out", ReadTeamFile());
        Assert.False(vm.IsEditingTeam);

        // A refused save doesn't continue: the user stays, with what they typed.
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Complaint reply");
        vm.Editor!.Text = "Refused text";
        ChangeFileElsewhere("Discovery questions", "Discovery questions (v2)");
        vm.DoneEditingTeamCommand.Execute(null);

        Until(() => dialogs.Messages.Count > 0);
        Pump(TimeSpan.FromMilliseconds(200));
        Assert.True(vm.IsEditingTeam);
        Assert.Equal("Refused text", vm.Editor!.Text);
    });

    [Fact]
    public void Save_ChangedByOther_ReloadFails_RevertsAndSaysSo() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        var good = File.ReadAllText(TeamPath);
        vm.NewPromptCommand.Execute(null);
        vm.Editor!.Title = "Brand new";
        vm.Editor.Text = "New text";

        File.WriteAllText(TeamPath, "{ half written"); // changed, and can't be read back
        vm.SaveCommand.Execute(null);

        Until(() => dialogs.Messages.Count > 0);
        Assert.NotEqual(ChangedByOtherMessage, dialogs.Messages[0]); // it wasn't reloaded
        Assert.Equal("The team library was changed by someone else.", dialogs.Messages[0]);
        Assert.DoesNotContain(host.Team!.Team!.Content.Prompts, p => p.Title == "Brand new"); // the refused change is gone
        Assert.True(vm.Editor!.IsNew);
        Assert.Equal("New text", vm.Editor.Text);

        // The file is readable again: the next save adds the prompt once.
        File.WriteAllText(TeamPath, good);
        WaitFor(host.Team.RefreshAsync());
        vm.SaveCommand.Execute(null);
        Until(() => vm.Editor is { IsDirty: false });
        Assert.Single(TeamFile.Parse(ReadTeamFile()).Content.Prompts, p => p.Title == "Brand new");
        Assert.Single(host.Team.Team!.Content.Prompts, p => p.Title == "Brand new");
    });

    [Fact]
    public void Exit_WaitsForTeamSaveInFlight() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Text = "Written before exit";
        vm.SaveCommand.Execute(null);
        Assert.True(vm.CurrentDocument.LibraryUnwritten);

        Assert.True(vm.WaitForTeamSave(TimeSpan.FromSeconds(5)));

        Assert.False(vm.CurrentDocument.LibraryUnwritten);
        Assert.Contains("Written before exit", ReadTeamFile());
        Assert.True(vm.WaitForTeamSave(TimeSpan.FromSeconds(1))); // nothing writing: at once
    });

    [Fact]
    public void Review_KeepOrRestore_MovesConflictCopiesIntoHistory() => Sta.Run(() =>
    {
        var host = NewHost();
        var vm = new MainViewModel(host, new FakeDialogs());
        vm.ShowTeamCommand.Execute(null);
        var folder = Path.GetDirectoryName(TeamPath)!;
        var historyDir = Path.Combine(folder, TeamWriter.HistoryFolderName);
        var copy = Path.Combine(folder, "team-prompts-OTHERPC.json");

        // Keep the current version.
        File.Copy(TeamPath, copy);
        WaitFor(host.Team!.RefreshAsync());
        Assert.True(vm.ShowConflictBar);
        WaitFor(host.Team.KeepCurrentVersionAsync());
        Assert.False(File.Exists(copy));
        Assert.Single(Directory.GetFiles(historyDir)); // moved, not deleted
        Assert.Empty(host.Team.ConflictCopies);
        Assert.False(vm.ShowConflictBar);

        // Restore the other version from sync.
        var theirs = TeamFile.Parse(File.ReadAllText(TeamPath));
        theirs.Content.Prompts.First(p => p.Title == "Complaint reply").Title = "Complaint reply (theirs)";
        File.WriteAllText(copy, TeamFile.Serialize(theirs));
        WaitFor(host.Team.RefreshAsync());
        Assert.True(vm.ShowConflictBar);
        var list = host.Team.ListHistoryAsync();
        WaitFor(list);
        var restore = host.Team.RestoreAsync(list.Result.First(i => i.IsConflictCopy));
        WaitFor(restore);
        Assert.Equal(TeamSaveStatus.Saved, restore.Result.Status);

        Until(() => !vm.ShowConflictBar);
        Assert.False(File.Exists(copy));
        Assert.Contains("Complaint reply (theirs)", ReadTeamFile());
        Assert.Equal(3, Directory.GetFiles(historyDir).Length); // first copy, the replaced version, the restored copy
    });

    // ---- Final review fixes ----------------------------------------------------------------------

    [Fact]
    public void Delete_ChangedByOther_SaysToMakeTheChangeAgain() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        var id = vm.Editor!.PromptId!.Value;

        ChangeFileElsewhere("Discovery questions", "Discovery questions (v2)");
        vm.DeleteCommand.Execute(null);

        Until(() => dialogs.Messages.Count > 0);
        Assert.Equal(
            "Someone else changed the team library, so it's been reloaded. Please make your change again.",
            dialogs.Messages.Single());
        Until(() => vm.Prompts.Any(r => r.Title == "Discovery questions (v2)"));
        Assert.Contains(vm.Prompts, r => r.Id == id); // the reload undid the delete
    });

    [Fact]
    public void LeaveTeam_WhileEditingWithUnsavedText_AsksFirst_CancelKeepsEverything() => Sta.Run(() =>
    {
        var host = NewHost();
        var dialogs = new FakeDialogs { SaveAnswer = null };
        var vm = new MainViewModel(host, dialogs);
        vm.ShowTeamCommand.Execute(null);
        vm.EditTeamCommand.Execute(null);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Title == "Follow-up email");
        vm.Editor!.Text = "My typed text";
        var later = 0;

        Assert.False(vm.ConfirmLeaveTeam(() => later++)); // Cancel

        Assert.Equal("My typed text", vm.Editor!.Text);
        Assert.True(vm.IsEditingTeam);
        Assert.NotNull(host.Team!.Source);

        dialogs.SaveAnswer = false; // Don't save
        Assert.True(vm.ConfirmLeaveTeam(() => later++));
        Assert.False(vm.IsEditingTeam);
        Assert.Equal(0, later);
    });

    [Fact]
    public void LeaveTeam_OnYourPrompts_DoesNotAskAboutYourPrompt() => Sta.Run(() =>
    {
        var host = NewHost();
        var category = new Category { Id = Guid.NewGuid(), Name = "Mine" };
        host.Library.Categories.Add(category);
        host.Library.Prompts.Add(NewPrompt(category, "Essay", "text"));
        var dialogs = new FakeDialogs { SaveAnswer = null };
        var vm = new MainViewModel(host, dialogs);
        vm.Editor!.Text = "Typed into my own prompt";

        Assert.True(vm.ConfirmLeaveTeam(() => { })); // leaving the team doesn't touch your prompts

        Assert.Equal("Typed into my own prompt", vm.Editor!.Text);
    });

    // ---- Helpers -------------------------------------------------------------------------------

    private FakeHost NewHost(int schemaVersion = 1)
    {
        var host = new FakeHost();
        Directory.CreateDirectory(Path.GetDirectoryName(TeamPath)!);
        var team = new TeamLibrary { Name = "Acme best practice", UpdatedAt = DateTimeOffset.Now, SchemaVersion = schemaVersion };
        var sales = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        var support = new Category { Id = Guid.NewGuid(), Name = "Support" };
        team.Content.Categories.AddRange([sales, support]);
        team.Content.Prompts.Add(NewPrompt(sales, "Follow-up email", "Write a follow-up email about {topic}."));
        team.Content.Prompts.Add(NewPrompt(sales, "Discovery questions", "List discovery questions for {client}."));
        team.Content.Prompts.Add(NewPrompt(support, "Complaint reply", "Reply to this complaint: {text}"));
        File.WriteAllText(TeamPath, TeamFile.Serialize(team));

        var service = new TeamLibraryService(_http, "1.0.0", Path.Combine(_root, "cache", "team-cache.json"), host.Settings,
            () => host.Library, host.SaveSettings, () => DateTimeOffset.Now, _ => { }, TimeSpan.FromMilliseconds(50));
        var join = service.JoinAsync(Path.GetDirectoryName(TeamPath)!);
        WaitFor(join);
        Assert.True(join.Result.Success, join.Result.Message);
        host.Team = service;
        return host;
    }

    /// <summary>Another editor's save: renames a prompt in the shared file.</summary>
    private void ChangeFileElsewhere(string title, string newTitle)
    {
        var team = TeamFile.Parse(File.ReadAllText(TeamPath));
        team.Content.Prompts.First(p => p.Title == title).Title = newTitle;
        File.WriteAllText(TeamPath, TeamFile.Serialize(team));
    }

    private static Prompt NewPrompt(Category category, string title, string text) => new()
    {
        Id = Guid.NewGuid(), CategoryId = category.Id, Title = title, Description = "", Text = text,
        CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now,
    };

    /// <summary>The shared file's text; empty while a save is replacing it.</summary>
    private string ReadTeamFile()
    {
        try { return File.ReadAllText(TeamPath); }
        catch (IOException) { return ""; }
    }

    private static void WaitFor(Task task) => Until(() => task.IsCompleted);

    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until) Sta.DoEvents();
        Assert.True(condition(), "Timed out waiting.");
    }

    private static void Pump(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until) Sta.DoEvents();
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

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() { LibrarySaves++; return true; }
        public bool SaveLibraryChange() { LibrarySaves++; return true; }
        public bool SaveSettings() => true;
        public void RefreshQuickMenu() { }
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
        public bool Confirm { get; set; } = true;
        public string? LastConfirm { get; private set; }
        public List<string> Messages { get; } = [];

        /// <summary>The answer to "Save changes?": true = Save, false = Don't save, null = Cancel.</summary>
        public bool? SaveAnswer { get; set; }

        public bool? AskSaveChanges(string question) => SaveAnswer;

        public bool ConfirmDestructive(string message, string action)
        {
            LastConfirm = message;
            return Confirm;
        }

        public void ShowMessage(string message) => Messages.Add(message);

        /// <summary>The answer to the delete-category question; null = cancel.</summary>
        public CategoryDeletion? Deletion { get; set; }
        public (string Name, int Count)? LastDeletionAsked { get; private set; }

        public CategoryDeletion? ChooseCategoryDeletion(string categoryName, int promptCount, IReadOnlyList<Category> targets)
        {
            LastDeletionAsked = (categoryName, promptCount);
            return Deletion;
        }

        public bool? LastHistoryReview { get; private set; }

        public bool ShowTeamHistory(TeamLibraryService service, bool review)
        {
            LastHistoryReview = review;
            return false;
        }
    }
}
