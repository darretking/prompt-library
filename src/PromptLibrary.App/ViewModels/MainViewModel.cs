using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// State and actions of the main window: category rail, prompt list with search, and the editor.
/// Everything shown and changed goes through <see cref="CurrentDocument"/>; every successful change is saved through
/// its <see cref="ILibraryDocument.SaveLibrary"/> (a prompt's Save) or <see cref="ILibraryDocument.SaveLibraryChange"/>
/// (category changes and deletions).
/// Once a team library is loaded, a switch shows either Your prompts or Team prompts (read only, with Make my copy, Pin
/// and Hide from menu); a search then covers both libraries. An editor can unlock the team library (Edit team library …
/// Done): it is then edited like your prompts, each save going to everyone without blocking the window, and older
/// versions can be restored.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const string AppTitle = "Prompt Library";
    private static readonly StringComparer NameOrder = StringComparer.CurrentCultureIgnoreCase;
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RestoredStatusDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromSeconds(4);

    private const string ChangedByOtherMessage =
        "Someone else changed the team library. It's been reloaded. Please make your change again.";
    private const string NewerSchemaMessage =
        "This team library was made with a newer Prompt Library. Update to edit it.";
    private const string RemovesForEveryoneMessage = "This removes it for everyone. Delete?";

    private readonly IMainHost _host;
    private readonly IDialogs _dialogs;
    private readonly PersonalDocument _personal;
    private readonly TeamDocument? _team;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private string? _statusText;

    // True from the start of a team save until its outcome has been applied on the UI thread; changes wait for it.
    private bool _teamSaving;

    // The user answered Cancel to "Save changes?" while the search was hiding the open prompt: the editor stays
    // and the search doesn't ask again until the editor changes or the search is cleared.
    private bool _searchKeepsEditor;

    private CategoryItem? _selectedCategory;
    private PromptRow? _selectedPrompt;
    private string _searchText = "";
    private PromptEditorViewModel? _editor;
    private bool _isAddingCategory;
    private string _newCategoryName = "";
    private string? _newCategoryError;

    // True while the lists are being rebuilt; the list boxes push null selections then, which are ignored.
    private bool _refreshing;

    // True while a deferred "Save changes?" is on screen.
    private bool _asking;

    // The editor's prompt as it is on disk, while a change to it is in memory but failed to write.
    // "Don't save" restores it, so the change is never written later by another save.
    private PromptSnapshot? _unwritten;

    private LibrarySide _side = LibrarySide.Yours;

    // The category and prompt each side last showed, so switching back returns to them.
    private readonly Dictionary<LibrarySide, (Guid? CategoryId, Guid? PromptId)> _remembered = new();

    private bool _showHiddenTeamPrompts;

    // True while a search covers both libraries (the rail shows All prompts); the category it showed before.
    private bool _searchingBoth;
    private Guid? _categoryBeforeSearch;

    public MainViewModel(IMainHost host, IDialogs dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        _personal = new PersonalDocument(host);
        if (host.Team is { } team)
        {
            _team = new TeamDocument(team);
            team.Changed += (_, _) => OnTeamChanged();
        }

        NewCategoryCommand = new RelayCommand(StartNewCategory);
        RenameCategoryCommand = new RelayCommand<CategoryItem?>(item => StartRename(item ?? SelectedCategory));
        DeleteCategoryCommand = new RelayCommand<CategoryItem?>(item => DeleteCategory(item ?? SelectedCategory));
        NewPromptCommand = new RelayCommand(NewPrompt);
        SaveCommand = new RelayCommand(() => Save());
        CopyCommand = new RelayCommand(Copy);
        DeleteCommand = new RelayCommand(Delete);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        BackupNowCommand = new RelayCommand(() => _ = _host.RunBackupAsync(BackupTrigger.Manual));
        RestoreCommand = new RelayCommand(_host.OpenRestore);
        OpenSettingsCommand = new RelayCommand(() => _host.OpenSettings());
        SetUpBackupsCommand = new RelayCommand(() => _host.OpenSettings(focusBackups: true));
        ExitCommand = new RelayCommand(() => _host.ExitApp());
        SetupGuideCommand = new RelayCommand(_host.OpenOnboarding);
        AboutCommand = new RelayCommand(_host.ShowAbout);
        OpenUpdateCommand = new RelayCommand(() => _host.Updates?.OpenDialog());
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => _host.Updates is not null);
        ShowYoursCommand = new RelayCommand(() => SwitchSide(LibrarySide.Yours));
        ShowTeamCommand = new RelayCommand(() => SwitchSide(LibrarySide.Team));
        CopyToYoursCommand = new RelayCommand(CopyToYours);
        PinCommand = new RelayCommand<PromptRow?>(TogglePin);
        HideCommand = new RelayCommand<PromptRow?>(ToggleHide);
        MarkAllSeenCommand = new RelayCommand(MarkAllSeen);
        RetryTeamCommand = new AsyncRelayCommand(() => _team?.Service.RefreshAsync() ?? Task.CompletedTask);
        EditTeamCommand = new RelayCommand(StartEditingTeam);
        DoneEditingTeamCommand = new RelayCommand(() => StopEditingTeam());
        TeamHistoryCommand = new RelayCommand(() => OpenTeamHistory(review: false));
        ReviewConflictCommand = new RelayCommand(() => OpenTeamHistory(review: true));

        if (_host.Updates is { } updates)
            updates.ReminderChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(UpdateReminder));
                OnPropertyChanged(nameof(ShowUpdateReminder));
            };

        _host.BackupStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BackupWarning));
            OnPropertyChanged(nameof(BackupWarningDetail));
            OnPropertyChanged(nameof(ShowBackupWarning));
        };

        _statusTimer = new DispatcherTimer { Interval = StatusDuration };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText = null;
        };

        RefreshAll();
        OpenFirstPromptIfNone(); // the window opens on a prompt, not on an empty editor
    }

    /// <summary>Raised after a new prompt is started, so the view can put the cursor in Title.</summary>
    public event EventHandler? TitleFocusRequested;

    public ObservableCollection<CategoryItem> Categories { get; } = new();

    /// <summary>The categories the editor's Category box offers (A–Z, no "All prompts").</summary>
    public ObservableCollection<Category> CategoryChoices { get; } = new();

    public ObservableCollection<PromptRow> Prompts { get; } = new();

    // ---- Your prompts | Team prompts ------------------------------------------------------------

    /// <summary>The library being shown and edited: your prompts, or the team's.</summary>
    public ILibraryDocument CurrentDocument => _side == LibrarySide.Team && _team is not null ? _team : _personal;

    public LibrarySide Side => _side;
    public bool IsTeamSide => _side == LibrarySide.Team;
    public bool IsYoursSide => _side == LibrarySide.Yours;

    /// <summary>The "Your prompts | Team prompts" switch: only once a team library is connected.</summary>
    public bool ShowSwitch => TeamConnected;

    /// <summary>Ctrl+1: shows Your prompts.</summary>
    public IRelayCommand ShowYoursCommand { get; }

    /// <summary>Ctrl+2: shows Team prompts (when a team library is loaded).</summary>
    public IRelayCommand ShowTeamCommand { get; }

    /// <summary>
    /// The Team side's muted header line: <c>Acme best practice · updated today, 07:52</c>, or while the source can't
    /// be read <c>Last updated today, 07:52 · can't reach the team library</c>. Null when no team library is loaded.
    /// </summary>
    public string? TeamHeaderText
    {
        get
        {
            if (!TeamConnected || IsTeamLoading) return null;
            if (_team!.Service is not { Team: { } library } service) return "Can't reach the team library";
            return IsTeamOffline
                ? $"Last updated {When(service.LastChecked ?? library.UpdatedAt)} · can't reach the team library"
                : $"{library.Name} · updated {When(library.UpdatedAt)}";
        }
    }

    /// <summary>Why the last read failed (the offline header's tooltip), or null.</summary>
    public string? TeamHeaderDetail => IsTeamOffline ? _team!.Service.StatusMessage : null;

    /// <summary>
    /// "Loading team prompts…": connected, with no copy loaded yet and the first read not back (it says why when it
    /// fails, which ends loading).
    /// </summary>
    public bool IsTeamLoading => TeamConnected && !TeamLoaded && _team!.Service.StatusMessage is null;

    /// <summary>
    /// True once a read of the team source has failed (<see cref="TeamLibraryService.StatusMessage"/> says why): the last
    /// good copy, if any, is shown and the header offers Retry. The offline copy loaded at start, before any read has
    /// come back, isn't offline yet.
    /// </summary>
    public bool IsTeamOffline => TeamConnected && !IsTeamLoading && _team!.Service.Status == TeamStatus.Offline
                                 && _team.Service.StatusMessage is not null;

    /// <summary>
    /// The muted "Read only" in the Team header (tooltip "You can't change files in this folder"), where Edit team
    /// library would be when this PC can write to the team file.
    /// </summary>
    public bool ShowTeamReadOnlyNote => IsTeamSide && TeamLoaded && !_team!.Service.CanEdit;

    /// <summary>The header line shows on the Team side.</summary>
    public bool ShowTeamHeader => IsTeamSide && TeamHeaderText is not null;

    /// <summary>The header's Retry: reads the team source now.</summary>
    public IAsyncRelayCommand RetryTeamCommand { get; }

    /// <summary>True while any team prompt is New or Updated (the switch shows a dot).</summary>
    public bool TeamHasUnseen => TeamContent is { } team && TeamUserState.HasUnseen(_host.Settings, team);

    /// <summary>The Team prompts switch's name for screen readers (says when something is new).</summary>
    public string TeamSwitchName => TeamHasUnseen ? "Team prompts, new or updated prompts" : "Team prompts";

    /// <summary>"Mark all as seen" in the Team header: only while something is unseen.</summary>
    public bool ShowMarkAllSeen => IsTeamSide && TeamHasUnseen;

    public IRelayCommand MarkAllSeenCommand { get; }

    /// <summary>
    /// Make my copy: copies the open team prompt into your category of the same name (created when missing) and
    /// opens the copy on Your prompts.
    /// </summary>
    public IRelayCommand CopyToYoursCommand { get; }

    /// <summary>Pin / Unpin the given row's prompt, or the editor's when null.</summary>
    public IRelayCommand<PromptRow?> PinCommand { get; }

    /// <summary>Hide from menu / Unhide the given team row's prompt, or the editor's when null.</summary>
    public IRelayCommand<PromptRow?> HideCommand { get; }

    /// <summary>"Show hidden (n)": lists the team prompts hidden from the menu, greyed, each with Unhide.</summary>
    public bool ShowHiddenTeamPrompts
    {
        get => _showHiddenTeamPrompts;
        set
        {
            if (!SetProperty(ref _showHiddenTeamPrompts, value)) return;
            if (IsTeamSide)
            {
                RebuildCategories(_selectedCategory?.Id);
                RefreshPrompts();
            }
        }
    }

    /// <summary>How many team prompts are hidden from the menu.</summary>
    public int HiddenCount => TeamContent?.Prompts.Count(p => _host.Settings.HiddenTeamPromptIds.Contains(p.Id)) ?? 0;

    /// <summary>"Show hidden (n)" sits under the search box on the Team side while something is hidden.</summary>
    public bool ShowHiddenToggle => IsTeamSide && HiddenCount > 0;

    public string ShowHiddenText => $"Show hidden ({HiddenCount})";

    /// <summary><c>Team prompt · Acme best practice · read only</c> above a read-only team prompt; otherwise null.</summary>
    public string? LockLineText => IsTeamSide && _editor is not null && CurrentDocument.IsReadOnly
        ? $"Team prompt · {_team!.Name} · read only"
        : null;

    /// <summary>The editing buttons (Delete, Save) and editing in the rail and list: not on a read-only side.</summary>
    public bool ShowEditButtons => !CurrentDocument.IsReadOnly;

    /// <summary>The team member's buttons (Hide from menu, Make my copy).</summary>
    public bool ShowTeamButtons => CurrentDocument.IsTeam && CurrentDocument.IsReadOnly;

    /// <summary>The editor's Pin button: <c>Pin</c>, or <c>Unpin</c> when the open prompt is pinned.</summary>
    public string PinLabel => EditorPromptId is { } id && _host.Settings.PinnedPromptIds.Contains(id) ? "Unpin" : "Pin";

    /// <summary>A saved prompt that isn't hidden can be pinned (a hidden prompt can't).</summary>
    public bool CanPin => EditorPromptId is { } id && !IsHiddenTeamPrompt(id);

    /// <summary>The editor's <c>Hide from menu</c>, or <c>Unhide</c> when the open team prompt is hidden.</summary>
    public string HideLabel => EditorPromptId is { } id && IsHiddenTeamPrompt(id) ? "Unhide" : "Hide from menu";

    /// <summary>The open prompt's category name (shown as text while the editor is read only).</summary>
    public string? EditorCategoryName => _editor?.CategoryId is { } id
        ? CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == id)?.Name
        : null;

    private bool TeamConnected => _team?.Service.Source is not null;

    private bool TeamLoaded => _team?.Service.Team is not null;

    private Library? TeamContent => _team?.Service.Team?.Content;

    private Guid? EditorPromptId => _editor is { IsNew: false } editor ? editor.PromptId : null;

    private bool IsHiddenTeamPrompt(Guid id)
        => TeamContent?.Prompts.Any(p => p.Id == id) == true && _host.Settings.HiddenTeamPromptIds.Contains(id);

    private static string When(DateTimeOffset time)
        => BackupLocation.RelativeTimeText(time.LocalDateTime, DateTime.Now, midSentence: true);

    // ---- Editing the team library ----------------------------------------------------------------

    /// <summary>True while the team library is being edited (Edit team library … Done).</summary>
    public bool IsEditingTeam => _team?.IsEditing == true;

    /// <summary>"Edit team library" in the Team header: only when this PC can edit the file, and not while editing.</summary>
    public bool ShowEditTeamButton => IsTeamSide && TeamLoaded && _team!.Service.CanEdit && !IsEditingTeam;

    /// <summary>Edit team library: unlocks editing; automatic refreshes are held until Done.</summary>
    public IRelayCommand EditTeamCommand { get; }

    /// <summary>The edit bar's Done: locks the team library again and applies any refresh held meanwhile.</summary>
    public IRelayCommand DoneEditingTeamCommand { get; }

    /// <summary>The edit bar (the quiet warning-bar style) while the team library is being edited.</summary>
    public bool ShowTeamEditBar => IsTeamSide && IsEditingTeam;

    public string TeamEditBarText => "Editing team library: changes go to everyone";

    /// <summary>
    /// Why this PC can write to the team file but may not edit it (a newer app made it), or null. Shown under the
    /// Team header in place of Edit team library.
    /// </summary>
    public string? TeamEditBlockedText => TeamLoaded && _team!.Service is { CanWrite: true, Team.IsNewerSchema: true }
        ? NewerSchemaMessage
        : null;

    public bool ShowTeamEditBlocked => IsTeamSide && TeamEditBlockedText is not null;

    /// <summary>The "Read only" note's tooltip: why this PC can't edit the team library.</summary>
    public string TeamReadOnlyReason => TeamEditBlockedText ?? "You can't change files in this folder";

    /// <summary>The Team header's ⋯ menu ("Restore an older version…"): for editors only.</summary>
    public bool ShowTeamMoreMenu => IsTeamSide && TeamLoaded && _team!.Service.CanEdit;

    /// <summary>"Restore an older version…": lists the team file's history (and any sync conflict copies) to restore one.</summary>
    public IRelayCommand TeamHistoryCommand { get; }

    /// <summary>The conflict notice's Review: the same list, with the other versions from sync first.</summary>
    public IRelayCommand ReviewConflictCommand { get; }

    /// <summary>"Two versions of the team library found" + Review: for editors, while file sync left a conflict copy.</summary>
    public bool ShowConflictBar => IsTeamSide && TeamLoaded && _team!.Service.CanEdit && _team.Service.ConflictCopies.Count > 0;

    public string ConflictBarText => "Two versions of the team library found";

    /// <summary>The one Team notice bar shows while either notice applies; both share its line.</summary>
    public bool ShowTeamBar => ShowTeamEditBar || ShowConflictBar;

    public bool ShowBothTeamNotices => ShowTeamEditBar && ShowConflictBar;

    public IRelayCommand NewCategoryCommand { get; }
    public IRelayCommand<CategoryItem?> RenameCategoryCommand { get; }
    public IRelayCommand<CategoryItem?> DeleteCategoryCommand { get; }
    public IRelayCommand NewPromptCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand BackupNowCommand { get; }
    public IRelayCommand RestoreCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand SetUpBackupsCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand SetupGuideCommand { get; }
    public IRelayCommand AboutCommand { get; }

    /// <summary>The rail's "Update available: v1.0.2" link: opens the update dialog.</summary>
    public IRelayCommand OpenUpdateCommand { get; }

    /// <summary><c>Help → Check for updates…</c>.</summary>
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary><c>Update available: v1.0.2</c> while a newer version is known; otherwise null.</summary>
    public string? UpdateReminder => _host.Updates?.ReminderText;

    public bool ShowUpdateReminder => UpdateReminder is not null;

    private async Task CheckForUpdatesAsync()
    {
        if (_host.Updates is not { } updates) return;
        ShowStatus("Checking for updates…", TimeSpan.FromSeconds(30));
        try
        {
            await updates.CheckNowAsync();
        }
        finally
        {
            if (StatusText == "Checking for updates…")
            {
                _statusTimer.Stop();
                StatusText = null;
            }
        }
    }

    public CategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_refreshing || value is null || ReferenceEquals(value, _selectedCategory)) return;
            if (_searchingBoth) EndSearchForCategory();
            if (EditorSurvivesCategory(value.Id) || _editor is not { IsDirty: true })
                ShowCategory(value.Id);
            else
                AfterAskingToSave(() => ShowCategory(value.Id), nameof(SelectedCategory));
        }
    }

    public PromptRow? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            if (_refreshing || ReferenceEquals(value, _selectedPrompt)) return;

            // Clearing the selection, or re-selecting what the editor already shows, keeps the editor.
            if (value is null || _editor?.PromptId == value.Id)
            {
                _selectedPrompt = value;
                OnPropertyChanged();
                return;
            }

            if (_editor is { IsDirty: true })
                AfterAskingToSave(() => OpenRow(value), nameof(SelectedPrompt));
            else
                OpenRow(value);
        }
    }

    /// <summary>
    /// Filters the list by title, description and text: within the selected category, or across both libraries (the
    /// rail showing All prompts) once a team library is loaded.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? "")) return;
            UpdateSearchingBoth();
            RefreshPrompts();
            OnPropertyChanged(nameof(HasSearchText));
            FollowSearch();
        }
    }

    /// <summary>Shows the search box's clear (×) button.</summary>
    public bool HasSearchText => _searchText.Length > 0;

    public PromptEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (ReferenceEquals(value, _editor)) return;
            if (_editor is not null)
            {
                _editor.PropertyChanged -= OnEditorChanged;
                _editor.StopTimers();
            }
            _editor = value;
            _searchKeepsEditor = false;
            if (_editor is not null) _editor.PropertyChanged += OnEditorChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEditor));
            OnPropertyChanged(nameof(HasNoEditor));
            OnPropertyChanged(nameof(WindowTitle));
            RaiseEditorState();
        }
    }

    public bool HasEditor => _editor is not null;
    public bool HasNoEditor => _editor is null;

    /// <summary>"Prompt Library", with a "• " prefix while the editor has unsaved changes.</summary>
    public string WindowTitle => _editor is { IsDirty: true } ? "• " + AppTitle : AppTitle;

    public bool ShowNoPromptsYet => !IsTeamSide && Prompts.Count == 0 && string.IsNullOrWhiteSpace(_searchText);

    /// <summary>The list's "+ New prompt" footer; hidden while the empty state shows its own New prompt button.</summary>
    public bool ShowNewPromptFooter => !CurrentDocument.IsReadOnly && !ShowNoPromptsYet;

    /// <summary>The Team side's empty state: "No team prompts yet."</summary>
    public bool ShowNoTeamPrompts => IsTeamSide && !IsTeamLoading && string.IsNullOrWhiteSpace(_searchText)
                                     && CurrentDocument.Library.Prompts.Count == 0;

    /// <summary>The Team side's list is empty only because what it would show is hidden from the menu.</summary>
    public bool ShowAllHidden => IsTeamSide && !_showHiddenTeamPrompts && Prompts.Count == 0
                                 && string.IsNullOrWhiteSpace(_searchText)
                                 && PromptFilter.Apply(CurrentDocument.Library, _selectedCategory?.Id, null).Count > 0;

    /// <summary>"All team prompts are hidden.", or "All prompts in this category are hidden." for one category.</summary>
    public string AllHiddenText => _selectedCategory?.Id is null || HiddenCount == CurrentDocument.Library.Prompts.Count
        ? "All team prompts are hidden."
        : "All prompts in this category are hidden.";

    public bool ShowNoMatches => Prompts.Count == 0 && !string.IsNullOrWhiteSpace(_searchText);
    public string NoMatchesText => $"No prompts match \"{_searchText.Trim()}\".";

    /// <summary>What the editor area says when no prompt is open.</summary>
    public string NoEditorText => IsTeamSide && CurrentDocument.IsReadOnly
        ? IsTeamLoading ? "" : CurrentDocument.Library.Prompts.Count == 0 ? "No team prompts yet." : "Select a team prompt."
        : CurrentDocument.Library.Prompts.Count == 0
            ? "No prompts yet. Press Ctrl+N or choose New prompt."
            : "Select a prompt, or create one.";

    /// <summary>A short muted confirmation under the categories, e.g. <c>Moved 2 prompts to Code review.</c>; null when none.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The warning bar's text ("Backups are off…" or "Backups paused…"), or null when backups are fine.</summary>
    public string? BackupWarning => _host.BackupWarning;

    public bool ShowBackupWarning => _host.BackupWarning is not null;

    /// <summary>The full backup folder while paused (the bar's tooltip).</summary>
    public string? BackupWarningDetail => _host.BackupWarningDetail;

    public bool IsAddingCategory
    {
        get => _isAddingCategory;
        private set => SetProperty(ref _isAddingCategory, value);
    }

    public string NewCategoryName
    {
        get => _newCategoryName;
        set
        {
            if (SetProperty(ref _newCategoryName, value ?? ""))
                NewCategoryError = null;
        }
    }

    public string? NewCategoryError
    {
        get => _newCategoryError;
        private set => SetProperty(ref _newCategoryError, value);
    }

    /// <summary>
    /// If the editor has unsaved changes, asks <c>Save changes to "saved title"?</c> (or <c>… to this new prompt?</c>):
    /// Save saves (false if that fails), Don't save throws the changes away, Cancel returns false. True when it's
    /// fine to move on.
    /// </summary>
    public bool ConfirmDiscardOrSave()
    {
        if (_editor is not { IsDirty: true } editor) return true;

        var savedTitle = CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == editor.PromptId)?.Title;
        switch (_dialogs.AskSaveChanges(EditorText.SaveChangesQuestion(savedTitle)))
        {
            case true:
                return Save();
            case false:
                DiscardChanges(editor);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// For an exit nobody can be asked about (installer, logoff): saves unsaved edits that are complete and can be
    /// written, as Save would. Otherwise returns them, for a recovery file. Null when nothing is left unsaved.
    /// </summary>
    public UnsavedEdit? SaveForExit()
    {
        if (CurrentUnsavedEdit() is not { } edit) return null;
        // A team save can't finish before the exit, so a team edit goes to the recovery file instead.
        if (CurrentDocument.IsTeam) return edit;
        return ExitEdits.Choose(edit) == ExitEditAction.Save && Save() ? null : edit;
    }

    /// <summary>What the editor holds that isn't saved, or null. Only reads (used by the crash handler).</summary>
    public UnsavedEdit? CurrentUnsavedEdit()
    {
        if (_editor is not { IsDirty: true } editor) return null;

        var categoryName = CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == editor.CategoryId)?.Name;
        return new UnsavedEdit(editor.Title, editor.Description, editor.CategoryId, categoryName, editor.Text);
    }

    /// <summary>Rebuilds rail, list and choices from the library, keeping selections where they still exist.</summary>
    public void RefreshAll()
    {
        // A prompt removed from the library (e.g. by a restore) can't stay open.
        if (_editor is { PromptId: { } id } && CurrentDocument.Library.Prompts.All(p => p.Id != id))
            Editor = null;

        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
        OnPropertyChanged(nameof(BackupWarning));
        OnPropertyChanged(nameof(ShowBackupWarning));
        RaiseSideState();
    }

    /// <summary>
    /// Before a restore replaces the library: offers to save unsaved edits, then, if a change is still only in
    /// memory because writing it failed, warns that the restore will lose it. False if the user cancelled.
    /// </summary>
    public bool ConfirmBeforeRestore()
    {
        if (!ConfirmDiscardOrSave()) return false;
        return (_unwritten is null && !_personal.LibraryUnwritten) || _dialogs.ConfirmDestructive(
            "Some of your changes couldn't be saved to disk. Restoring now will lose them.", "Restore anyway");
    }

    /// <summary>
    /// The library was replaced by a restore: close the editor (its saved values are out of date; any unsaved
    /// edits were offered for saving before the restore), rebuild everything, open the first prompt and show
    /// <paramref name="restoredText"/> (<c>Library restored from …</c>) under the categories for 4 s.
    /// </summary>
    public void ReloadAfterRestore(string restoredText)
    {
        _unwritten = null;
        Editor = null;
        if (IsTeamSide)
        {
            // The restore replaced your prompts: show them.
            _remembered.Remove(LibrarySide.Yours);
            SwitchSide(LibrarySide.Yours);
        }
        RefreshAll();
        OpenFirstPromptIfNone();
        ShowStatus(restoredText, RestoredStatusDuration);
    }

    // ---- Categories ----------------------------------------------------------------------------

    public void StartNewCategory()
    {
        if (CannotChange) return;
        foreach (var item in Categories) item.IsRenaming = false;
        NewCategoryName = "";
        NewCategoryError = null;
        IsAddingCategory = true;
    }

    public void CancelNewCategory()
    {
        IsAddingCategory = false;
        NewCategoryError = null;
    }

    /// <summary>Adds the typed category and selects it. False (with an inline message) if the name isn't valid.</summary>
    public bool CommitNewCategory()
    {
        if (CannotChange) return false;
        var name = _newCategoryName.Trim();
        NewCategoryError = CategoryNameError(name, exceptId: null);
        if (NewCategoryError is not null) return false;

        Category category;
        try
        {
            category = NewLibraryEditor().AddCategory(name);
        }
        catch (ArgumentException)
        {
            NewCategoryError = CategoryNameError(name, exceptId: null) ?? "Give the category a name.";
            return false;
        }

        IsAddingCategory = false;
        SaveChange(); // on failure the category stays and is written later (the host says so)
        RebuildCategories(_selectedCategory?.Id);
        SelectCategoryNow(category.Id);
        return true;
    }

    public void StartRename(CategoryItem? item)
    {
        if (item is null || item.IsAll || CannotChange) return;
        CancelNewCategory();
        foreach (var other in Categories) other.IsRenaming = false;
        item.EditName = item.Name;
        item.Error = null;
        item.IsRenaming = true;
    }

    public void CancelRename(CategoryItem item)
    {
        item.IsRenaming = false;
        item.Error = null;
    }

    /// <summary>Renames the category to the typed name. False (with an inline message) if the name isn't valid.</summary>
    public bool CommitRename(CategoryItem item)
    {
        if (!item.IsRenaming || item.Id is not { } id || CurrentDocument.IsReadOnly) return true;
        if (_teamSaving) return false; // the rename stays open until the save in progress is done

        var name = item.EditName.Trim();
        if (name == item.Name)
        {
            CancelRename(item);
            return true;
        }

        item.Error = CategoryNameError(name, exceptId: id);
        if (item.Error is not null) return false;

        try
        {
            NewLibraryEditor().RenameCategory(id, name);
        }
        catch (ArgumentException ex)
        {
            item.Error = CategoryNameError(name, exceptId: id) ?? ex.Message;
            return false;
        }

        item.IsRenaming = false;
        SaveChange(); // on failure the new name stays and is written later (the host says so)
        RebuildCategories(_selectedCategory?.Id);
        return true;
    }

    public void DeleteCategory(CategoryItem? item)
    {
        if (item?.Id is not { } id || CannotChange) return;

        var promptCount = NewLibraryEditor().PromptCount(id);
        Guid? moveTo = null;

        if (promptCount == 0)
        {
            var question = CurrentDocument.IsTeam ? RemovesForEveryoneMessage : $"Delete the category \"{item.Name}\"?";
            if (!_dialogs.ConfirmDestructive(question, "Delete")) return;
        }
        else
        {
            var targets = CurrentDocument.Library.Categories
                .Where(c => c.Id != id)
                .OrderBy(c => c.Name, NameOrder)
                .ToList();
            if (targets.Count == 0)
            {
                _dialogs.ShowMessage(
                    $"\"{item.Name}\" still has {Count(promptCount, "prompt")}. " +
                    "Create another category first so they have somewhere to go.");
                return;
            }

            moveTo = _dialogs.ChooseMoveTarget(item.Name, promptCount, targets);
            if (moveTo is null) return;
        }

        try
        {
            NewLibraryEditor().DeleteCategory(id, moveTo);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        _editor?.ReplaceCategory(id, moveTo);
        SaveChange(); // on failure the deletion stays and is written later (the host says so)

        // Show where the prompts went; if the deleted category was empty, fall back to All prompts.
        var selectId = _selectedCategory?.Id == id ? moveTo : _selectedCategory?.Id;
        RebuildCategories(selectId);
        RefreshPrompts();

        if (moveTo is { } targetId && CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == targetId) is { } target)
            ShowStatus($"Moved {Count(promptCount, "prompt")} to {target.Name}.");
    }

    // ---- Prompts -------------------------------------------------------------------------------

    /// <summary>
    /// Starts a new prompt in the selected category (the first category A–Z under "All prompts").
    /// It joins the library when it is saved.
    /// </summary>
    public void NewPrompt()
    {
        if (CannotChange) return;
        if (!ConfirmDiscardOrSave()) return;

        var categoryId = _selectedCategory?.Id
            ?? CurrentDocument.Library.Categories.OrderBy(c => c.Name, NameOrder).FirstOrDefault()?.Id;
        if (categoryId is null)
        {
            _dialogs.ShowMessage("Create a category first, then add prompts to it.");
            StartNewCategory();
            return;
        }

        Editor = PromptEditorViewModel.ForNewPrompt(categoryId.Value);
        _selectedPrompt = null;
        OnPropertyChanged(nameof(SelectedPrompt));
        TitleFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves the editor. False if there's nothing valid to save or the file couldn't be written
    /// (the editor then keeps its changes so the user can try again).
    /// </summary>
    public bool Save()
    {
        var editor = _editor;
        if (editor is null || !editor.IsDirty || CurrentDocument.IsReadOnly) return true;
        if (_teamSaving) return false; // the save in progress finishes first
        if (!editor.Validate()) return false;
        var isTeam = CurrentDocument.IsTeam;

        Guid promptId;
        PromptSnapshot onDisk;
        try
        {
            var library = NewLibraryEditor();
            if (editor.PromptId is { } existingId)
            {
                // After an earlier failed write, the earlier snapshot is still what's on disk.
                onDisk = _unwritten ?? PromptSnapshot.Capture(CurrentDocument.Library, existingId);
                library.UpdatePrompt(existingId, editor.Title, editor.Description, editor.CategoryId!.Value, editor.Text);
                promptId = existingId;
            }
            else
            {
                promptId = library.AddPrompt(editor.CategoryId!.Value, editor.Title, editor.Description, editor.Text).Id;
                onDisk = PromptSnapshot.ForNewPrompt(promptId);
                // A retry after a failed write must update, not add again. A team prompt counts as created only once
                // the save has gone through: a refused save reloads the library without it, and the editor stays new.
                if (!isTeam) editor.MarkCreated(promptId);
            }
        }
        catch (ArgumentException ex)
        {
            if (!editor.ShowValidationError(ex))
                _dialogs.ShowMessage(ex.Message);
            return false;
        }

        if (isTeam)
        {
            CurrentDocument.SaveLibrary();
            TrackTeamSave(() => AfterTeamPromptSaved(editor, promptId));
            FollowEditorCategory(editor);
            return false; // not written yet: the editor stays unsaved until the save reports back
        }

        var written = CurrentDocument.SaveLibrary();
        _unwritten = written ? null : onDisk;
        if (written)
        {
            editor.LoadSaved(CurrentDocument.Library.Prompts.First(p => p.Id == promptId));
            editor.ShowSavedFeedback();
        }

        FollowEditorCategory(editor);
        return written;
    }

    /// <summary>Rebuilds the lists after a prompt's Save; if the prompt moved out of the category shown, follows it.</summary>
    private void FollowEditorCategory(PromptEditorViewModel editor)
    {
        var showCategory = _selectedCategory?.Id is { } shown && shown != editor.CategoryId
            ? editor.CategoryId
            : _selectedCategory?.Id;
        RebuildCategories(showCategory);
        RefreshPrompts();
    }

    /// <summary>Copies what is in the editor now, unsaved edits included.</summary>
    public void Copy()
    {
        var editor = _editor;
        if (editor is null || !editor.CanCopy) return;
        if (_host.CopyText(editor.Text, editor.DisplayTitle))
            editor.ShowCopiedFeedback();
    }

    /// <summary>Deletes the prompt in the editor after asking (a new, unsaved prompt is just dropped).</summary>
    public void Delete()
    {
        var editor = _editor;
        if (editor is null || CannotChange) return;

        if (editor.PromptId is not { } id)
        {
            if (!editor.IsDirty || _dialogs.ConfirmDestructive($"Delete \"{editor.DisplayTitle}\"?", "Delete"))
                Editor = null;
            return;
        }

        var title = CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == id)?.Title ?? editor.DisplayTitle;
        var question = CurrentDocument.IsTeam ? RemovesForEveryoneMessage : $"Delete \"{title}\"?";
        if (!_dialogs.ConfirmDestructive(question, "Delete")) return;

        try
        {
            NewLibraryEditor().DeletePrompt(id);
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        if (_unwritten?.PromptId == id) _unwritten = null;
        Editor = null;
        SaveChange(); // on failure the deletion stays and is written later (the host says so)
        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>
    /// "Don't save": the editor goes back to what is saved. If a change to this prompt reached
    /// memory but not the disk, the library is rolled back too (a new prompt is removed).
    /// </summary>
    private void DiscardChanges(PromptEditorViewModel editor)
    {
        if (_unwritten is not { } snapshot || snapshot.PromptId != editor.PromptId)
        {
            if (editor.IsNew)
                Editor = null;
            else
                editor.Revert();
            return;
        }

        snapshot.Restore(CurrentDocument.Library);
        _unwritten = null;
        _host.RefreshQuickMenu();

        var restored = CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == snapshot.PromptId);
        if (restored is null)
            Editor = null;
        else
            editor.LoadSaved(restored);

        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
    }

    // The host's Library object can be replaced (restore), so make an editor for the current one.
    private LibraryEditor NewLibraryEditor() => new(CurrentDocument.Library, () => DateTimeOffset.Now);

    private string? CategoryNameError(string name, Guid? exceptId)
    {
        if (name.Length == 0) return "Give the category a name.";
        var clash = CurrentDocument.Library.Categories.Any(c =>
            c.Id != exceptId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return clash ? $"There's already a category called \"{name}\"." : null;
    }

    /// <summary>
    /// Runs <paramref name="change"/> once the user has answered "Save changes?". Deferred so the
    /// dialog is not shown from inside the list box's selection change; meanwhile the list snaps
    /// back to the current selection, which stays if the user cancels.
    /// </summary>
    private void AfterAskingToSave(Action change, string selectionProperty)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            SnapBackSelection(selectionProperty);
            if (_asking) return; // already asking; one question at a time

            _asking = true;
            try
            {
                if (ConfirmDiscardOrSave())
                    change();
            }
            finally
            {
                _asking = false;
            }
        });
    }

    /// <summary>
    /// After the search text changed: if the search hides the open prompt, open the first match instead (or close
    /// the editor when nothing matches). Unsaved changes are offered for saving first; if the user cancels, the
    /// editor and the search text stay as they are.
    /// </summary>
    private void FollowSearch()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            _searchKeepsEditor = false;
            return;
        }
        if (!SearchHidesEditor()) return;

        if (_editor is not { IsDirty: true })
        {
            OpenFirstMatch();
            return;
        }
        if (_searchKeepsEditor || _asking) return;

        // Deferred so the question isn't shown from inside the search box's text change.
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (_asking || !SearchHidesEditor()) return;
            _asking = true;
            try
            {
                if (!ConfirmDiscardOrSave())
                    _searchKeepsEditor = true;
                else if (SearchHidesEditor()) // saving may have made the prompt match
                    OpenFirstMatch();
            }
            finally
            {
                _asking = false;
            }
        });
    }

    /// <summary>True when the list shows search results that don't include the editor's prompt (or there's no editor).</summary>
    private bool SearchHidesEditor()
        => !string.IsNullOrWhiteSpace(_searchText)
           && (_editor is null
               ? Prompts.Any(r => r.IsTeam == IsTeamSide)
               : _editor.PromptId is not { } id || Prompts.All(p => p.Id != id));

    private void OpenFirstMatch()
    {
        if (Prompts.FirstOrDefault(r => r.IsTeam == IsTeamSide) is { } first)
        {
            OpenPrompt(first.Prompt);
            return;
        }
        Editor = null;
        _selectedPrompt = null;
        OnPropertyChanged(nameof(SelectedPrompt));
    }

    private void ShowStatus(string text, TimeSpan? duration = null)
    {
        StatusText = text;
        _statusTimer.Stop();
        _statusTimer.Interval = duration ?? StatusDuration;
        _statusTimer.Start();
    }

    /// <summary>Opens the first prompt of the shown side in the list when no prompt (or new prompt) is open.</summary>
    private void OpenFirstPromptIfNone()
    {
        if (_editor is null && Prompts.FirstOrDefault(r => r.IsTeam == IsTeamSide) is { } first)
            OpenPrompt(first.Prompt);
    }

    /// <summary>
    /// Puts the list box back on the current selection after the user clicked another row.
    /// Re-raising PropertyChanged with an unchanged value is ignored by the binding, so the
    /// selection is cleared and restored to force the update.
    /// </summary>
    private void SnapBackSelection(string selectionProperty)
    {
        _refreshing = true;
        try
        {
            if (selectionProperty == nameof(SelectedPrompt))
            {
                var keep = _selectedPrompt;
                _selectedPrompt = null;
                OnPropertyChanged(nameof(SelectedPrompt));
                _selectedPrompt = keep;
            }
            else
            {
                var keep = _selectedCategory;
                _selectedCategory = null;
                OnPropertyChanged(nameof(SelectedCategory));
                _selectedCategory = keep;
            }
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(selectionProperty);
    }

    /// <summary>Selects a category from code (e.g. after adding it), asking about unsaved changes if needed.</summary>
    private void SelectCategoryNow(Guid? id)
    {
        if (!EditorSurvivesCategory(id) && !ConfirmDiscardOrSave())
            return;
        ShowCategory(id);
    }

    /// <summary>True if switching to category <paramref name="id"/> can keep the editor open.</summary>
    private bool EditorSurvivesCategory(Guid? id)
        => _editor is null || id is null || _editor.CategoryId == id;

    private void ShowCategory(Guid? id)
    {
        _selectedCategory = Categories.FirstOrDefault(c => c.Id == id) ?? Categories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
        if (!EditorSurvivesCategory(_selectedCategory?.Id))
            Editor = null;
        RefreshPrompts();
        OpenFirstPromptIfNone(); // choosing a category shows its first prompt
    }

    /// <summary>
    /// Opens <paramref name="prompt"/> in the editor. Only an open the user asked for (<paramref name="byUser"/>: a row
    /// or search result they chose) clears a team prompt's New / Updated marker; the first prompt a side, category or
    /// refresh opens by itself stays marked until it is chosen.
    /// </summary>
    private void OpenPrompt(Prompt prompt, bool byUser = false)
    {
        if (!CurrentDocument.Library.Prompts.Contains(prompt)) return; // deleted meanwhile
        var editor = PromptEditorViewModel.ForPrompt(prompt);
        editor.IsReadOnly = CurrentDocument.IsReadOnly;
        Editor = editor;
        _selectedPrompt = Prompts.FirstOrDefault(r => ReferenceEquals(r.Prompt, prompt));
        OnPropertyChanged(nameof(SelectedPrompt));
        if (byUser && CurrentDocument.IsTeam) MarkOpenedSeen(prompt);
    }

    private void RebuildCategories(Guid? selectId)
    {
        var library = CurrentDocument.Library;
        var sorted = library.Categories.OrderBy(c => c.Name, NameOrder).ToList();

        _refreshing = true;
        try
        {
            Categories.Clear();
            var shown = library.Prompts.Where(IsListed).ToList();
            var canEdit = !CurrentDocument.IsReadOnly;
            Categories.Add(new CategoryItem(null, "All prompts", shown.Count, canEdit));
            foreach (var category in sorted)
                Categories.Add(new CategoryItem(category.Id, category.Name,
                    shown.Count(p => p.CategoryId == category.Id), canEdit));

            CategoryChoices.Clear();
            foreach (var category in sorted)
                CategoryChoices.Add(category);

            _selectedCategory = Categories.FirstOrDefault(c => c.Id == selectId) ?? Categories[0];
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedCategory));
        _editor?.RefreshCategoryChoice();
    }

    private void RefreshPrompts()
    {
        var keepId = _editor?.PromptId ?? _selectedPrompt?.Id;

        _refreshing = true;
        try
        {
            Prompts.Clear();
            if (_searchingBoth)
            {
                foreach (var hit in PromptFilter.Search(_personal.Library, TeamContent, _host.Settings, _searchText))
                    Prompts.Add(NewRow(hit.Prompt, hit.IsTeam, showTeamIcon: hit.IsTeam));
            }
            else
            {
                foreach (var prompt in PromptFilter.Apply(CurrentDocument.Library, _selectedCategory?.Id, _searchText))
                    if (IsListed(prompt))
                        Prompts.Add(NewRow(prompt, CurrentDocument.IsTeam, showTeamIcon: false));
            }

            // While the items were replaced the list box pushed null selections, and the binding may have left
            // its SelectedItem at the kept prompt with nothing actually selected. Re-raising an unchanged value is
            // ignored, so clear it first (as SnapBackSelection does) to make the final update stick.
            _selectedPrompt = null;
            OnPropertyChanged(nameof(SelectedPrompt));
            _selectedPrompt = Prompts.FirstOrDefault(p => p.Id == keepId);
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedPrompt));
        OnPropertyChanged(nameof(ShowNoPromptsYet));
        OnPropertyChanged(nameof(ShowNewPromptFooter));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(NoEditorText));
        OnPropertyChanged(nameof(ShowNoTeamPrompts));
        OnPropertyChanged(nameof(ShowAllHidden));
        OnPropertyChanged(nameof(AllHiddenText));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PromptEditorViewModel.IsDirty))
            OnPropertyChanged(nameof(WindowTitle));
    }

    // ---- Team side -----------------------------------------------------------------------------

    /// <summary>
    /// Shows <paramref name="side"/>, back on the category and prompt it showed last (or on
    /// <paramref name="openPromptId"/>). Unsaved changes are offered for saving first; false when the user cancelled or
    /// there's no team library to show. A search of both libraries keeps its text and results.
    /// </summary>
    private bool SwitchSide(LibrarySide side, Guid? openPromptId = null)
    {
        if (side == _side)
        {
            if (openPromptId is { } id && CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == id) is { } same)
                OpenPrompt(same, byUser: true);
            return true;
        }
        if (side == LibrarySide.Team && !TeamConnected) return false;
        // Leaving the Team side ends editing it (Done), so refreshes aren't held while it isn't in view.
        if (!ConfirmDiscardOrSave() || (_side == LibrarySide.Team && !StopEditingTeam(ask: false)))
        {
            RaiseSideState(); // puts the switch back on the side still shown
            return false;
        }

        CancelNewCategory();
        foreach (var item in Categories) item.IsRenaming = false;
        _remembered[_side] = (_searchingBoth ? _categoryBeforeSearch : _selectedCategory?.Id, _editor?.PromptId);

        _side = side;
        Editor = null;
        var (categoryId, promptId) = _remembered.GetValueOrDefault(side);
        if (_searchingBoth)
        {
            _categoryBeforeSearch = categoryId;
            RebuildCategories(null);
        }
        else
        {
            RebuildCategories(categoryId);
        }
        RefreshPrompts();

        if ((openPromptId ?? promptId) is { } open && CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == open) is { } prompt)
            OpenPrompt(prompt, byUser: openPromptId is not null); // a search result the user chose, or the side's last prompt
        else
            OpenFirstPromptIfNone();
        RaiseSideState();
        return true;
    }

    /// <summary>Opens a list row; a row from the other library (a search result) switches to its side first.</summary>
    private void OpenRow(PromptRow row)
    {
        if (row.IsTeam == IsTeamSide)
            OpenPrompt(row.Prompt, byUser: true);
        else
            SwitchSide(row.IsTeam ? LibrarySide.Team : LibrarySide.Yours, row.Id);
    }

    /// <summary>
    /// Make my copy: one click, no dialog. The copy (with a new id) goes into your category of the same name, which is
    /// created when missing; then Your prompts shows it, open in the editor.
    /// </summary>
    private void CopyToYours()
    {
        if (!IsTeamSide || EditorPromptId is not { } id
            || CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == id) is not { } source) return;

        var categoryName = (CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == source.CategoryId)?.Name
                            ?? "Team prompts").Trim();
        var personal = _personal.Library;
        var editor = new LibraryEditor(personal, () => DateTimeOffset.Now);
        Category category;
        Prompt copy;
        try
        {
            category = personal.Categories.FirstOrDefault(c =>
                           string.Equals(c.Name.Trim(), categoryName, StringComparison.OrdinalIgnoreCase))
                       ?? editor.AddCategory(categoryName);
            copy = editor.AddPrompt(category.Id, source.Title, source.Description, source.Text);
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        _personal.SaveLibraryChange(); // on failure the copy stays and is written later (the host says so)
        _remembered[LibrarySide.Yours] = (category.Id, copy.Id);
        if (!SwitchSide(LibrarySide.Yours)) return;
        ShowStatus($"Copied to your prompts · {category.Name}", FeedbackDuration);
    }

    /// <summary>Pins or unpins <paramref name="row"/>'s prompt (the editor's when null). Pins are capped at 10.</summary>
    private void TogglePin(PromptRow? row)
    {
        if ((row?.Id ?? EditorPromptId) is not { } id) return;
        var settings = _host.Settings;

        if (settings.PinnedPromptIds.Contains(id))
        {
            TeamUserState.Unpin(settings, id);
            ShowStatus("Unpinned", FeedbackDuration);
        }
        else
        {
            if (IsHiddenTeamPrompt(id)) return; // a hidden prompt can't be pinned
            if (!TeamUserState.Pin(settings, id))
            {
                ShowStatus($"You can pin up to {TeamUserState.MaxPins} prompts.", FeedbackDuration);
                return;
            }
            ShowStatus("Pinned", FeedbackDuration);
        }

        SaveTeamState();
        foreach (var item in Prompts.Where(r => r.Id == id))
            item.IsPinned = settings.PinnedPromptIds.Contains(id);
        RaiseEditorState();
    }

    /// <summary>Hides a team prompt from the menu (which unpins it), or unhides it; the editor's when null.</summary>
    private void ToggleHide(PromptRow? row)
    {
        var id = row?.Id ?? (IsTeamSide ? EditorPromptId : null);
        if (id is not { } promptId || TeamContent?.Prompts.Any(p => p.Id == promptId) != true) return;
        var settings = _host.Settings;

        if (settings.HiddenTeamPromptIds.Contains(promptId))
        {
            TeamUserState.Unhide(settings, promptId);
            ShowStatus("Unhidden", FeedbackDuration);
        }
        else
        {
            TeamUserState.Hide(settings, promptId);
            ShowStatus("Hidden from the menu", FeedbackDuration);
        }

        SaveTeamState();
        if (HiddenCount == 0) _showHiddenTeamPrompts = false;
        if (IsTeamSide) RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts(); // a hidden prompt leaves the list; the editor keeps it open, offering Unhide
        RaiseSideState();
    }

    private void MarkAllSeen()
    {
        if (TeamContent is not { } team) return;
        TeamUserState.MarkAllSeen(_host.Settings, team);
        SaveTeamState();
        foreach (var row in Prompts.Where(r => r.IsTeam)) row.Marker = TeamMarker.None;
        RaiseMarkerState();
    }

    /// <summary>Opening a New or Updated team prompt counts as seeing it.</summary>
    private void MarkOpenedSeen(Prompt prompt)
    {
        var settings = _host.Settings;
        if (TeamUserState.MarkerFor(settings, prompt) == TeamMarker.None) return;
        TeamUserState.MarkSeen(settings, prompt);
        SaveTeamState();
        foreach (var row in Prompts.Where(r => r.Id == prompt.Id)) row.Marker = TeamMarker.None;
        RaiseMarkerState();
    }

    /// <summary>Pins, hides and markers live in the settings, and the quick menu shows pins and hides.</summary>
    private void SaveTeamState()
    {
        _host.SaveSettings();
        _host.RefreshQuickMenu();
    }

    // ---- Editing the team library (helpers) ----------------------------------------------------

    /// <summary>Nothing can change: the side is read only, or a team save is still writing (changes wait for it).</summary>
    private bool CannotChange => CurrentDocument.IsReadOnly || (CurrentDocument.IsTeam && _teamSaving);

    /// <summary>Saves a change with no Save button (category changes, deletions); a team save reports back later.</summary>
    private void SaveChange()
    {
        CurrentDocument.SaveLibraryChange();
        if (CurrentDocument.IsTeam) TrackTeamSave();
    }

    /// <summary>
    /// Waits (without blocking) for the team save just started, then applies its outcome on the UI thread: saved runs
    /// <paramref name="onSaved"/> and says "Saved for everyone"; refused (someone else changed the file) or failed
    /// shows why, with the library reloaded or put back to the copy on disk. The editor keeps whatever was typed.
    /// </summary>
    private void TrackTeamSave(Action? onSaved = null)
    {
        if (_team?.PendingSave is not { } pending) return;
        _teamSaving = true;
        pending.ContinueWith(task =>
        {
            var result = task.IsCompletedSuccessfully
                ? task.Result
                : new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't save the team library.");
            _dispatcher.BeginInvoke(() => FinishTeamSave(result, onSaved));
        }, TaskScheduler.Default);
    }

    private void FinishTeamSave(TeamSaveResult result, Action? onSaved)
    {
        _teamSaving = false;
        if (_team is not { } team) return;

        switch (result.Status)
        {
            case TeamSaveStatus.Saved:
                team.Rebase();
                onSaved?.Invoke();
                ShowStatus("Saved for everyone", FeedbackDuration);
                break;
            case TeamSaveStatus.ChangedByOther:
                team.Rebase(); // the service has already re-read the file
                _dialogs.ShowMessage(ChangedByOtherMessage);
                break;
            default:
                team.Service.RevertToLoaded(); // what wasn't written doesn't stay in memory
                _dialogs.ShowMessage(result.Message ?? "Couldn't save the team library.");
                break;
        }

        if (IsTeamSide) ReloadTeamSide();
        _host.RefreshQuickMenu();
    }

    /// <summary>
    /// A team prompt's Save went through: the editor takes the saved prompt as its baseline. Anything typed while the
    /// save was writing stays in the fields, still unsaved.
    /// </summary>
    private void AfterTeamPromptSaved(PromptEditorViewModel editor, Guid promptId)
    {
        if (!ReferenceEquals(_editor, editor) || _team?.Library.Prompts.FirstOrDefault(p => p.Id == promptId) is not { } saved)
            return;

        var (title, description, categoryId, text) = (editor.Title, editor.Description, editor.CategoryId, editor.Text);
        if (editor.IsNew) editor.MarkCreated(promptId);
        editor.LoadSaved(saved);
        editor.Title = title;
        editor.Description = description;
        editor.CategoryId = categoryId;
        editor.Text = text;
        if (!editor.IsDirty) editor.ShowSavedFeedback();
        RaiseEditorState();
    }

    /// <summary>
    /// Rebuilds the Team side from the loaded copy. A prompt open without unsaved changes follows the new copy; one with
    /// unsaved changes keeps them (a new prompt stays new).
    /// </summary>
    private void ReloadTeamSide()
    {
        RefreshAll();
        if (_editor is { } editor)
        {
            if (editor is { IsDirty: false, PromptId: { } id }
                && CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == id) is { } fresh)
                editor.LoadSaved(fresh);
            editor.IsReadOnly = CurrentDocument.IsReadOnly;
            RaiseEditorState();
        }
        OpenFirstPromptIfNone();
    }

    /// <summary>Edit team library: unlocks the Team side for editing and holds automatic refreshes until Done.</summary>
    private void StartEditingTeam()
    {
        if (!IsTeamSide || _team is not { IsEditing: false } team || !TeamLoaded || !team.Service.CanEdit) return;
        team.IsEditing = true;
        team.Service.HoldRefresh(true);
        ReloadTeamSide();
        RaiseSideState();
    }

    /// <summary>
    /// Done (or leaving the Team side): offers to save unsaved changes, locks the team library again and applies a
    /// refresh held meanwhile ("Team prompts updated"). False when the user cancelled or a save is still writing.
    /// </summary>
    private bool StopEditingTeam(bool ask = true)
    {
        if (_team is not { IsEditing: true } team) return true;
        if (_teamSaving || (ask && !ConfirmDiscardOrSave())) return false;

        CancelNewCategory();
        foreach (var item in Categories) item.IsRenaming = false;
        if (_editor is { IsNew: true }) Editor = null; // a new, empty prompt can't stay open read only

        team.IsEditing = false;
        var held = team.Service.HasHeldRefresh;
        team.Service.HoldRefresh(false);
        if (IsTeamSide) ReloadTeamSide();
        RaiseSideState();
        if (held) ShowStatus("Team prompts updated", FeedbackDuration);
        return true;
    }

    /// <summary>
    /// "Restore an older version…" (or Review): after unsaved changes are dealt with, shows the team file's versions;
    /// a restore reloads the Team side.
    /// </summary>
    private void OpenTeamHistory(bool review)
    {
        if (!IsTeamSide || _team is not { } team || !team.Service.CanEdit || _teamSaving) return;
        if (!ConfirmDiscardOrSave()) return;
        if (!_dialogs.ShowTeamHistory(team.Service, review)) return;

        team.Rebase();
        ReloadTeamSide();
        RaiseSideState();
        ShowStatus("Restored for everyone", FeedbackDuration);
    }

    /// <summary>
    /// The team library was refreshed, joined or left. A team prompt open unchanged follows the new copy; leaving
    /// goes back to Your prompts.
    /// </summary>
    private void OnTeamChanged()
    {
        if (!TeamConnected && _team is { IsEditing: true } editing)
        {
            editing.IsEditing = false;
            editing.Service.HoldRefresh(false);
        }
        _team?.Rebase(); // while editing, saves now guard against the copy just loaded

        if (IsTeamSide && !TeamConnected)
        {
            Editor = null; // read only, so nothing unsaved is lost
            SwitchSide(LibrarySide.Yours);
            _remembered.Remove(LibrarySide.Team);
        }

        UpdateSearchingBoth();
        if (IsTeamSide)
        {
            ReloadTeamSide();
        }
        else if (_searchingBoth)
        {
            RefreshPrompts();
        }
        RaiseSideState();
    }

    /// <summary>
    /// A search covers both libraries once a team library is loaded: the rail then shows All prompts, and the category
    /// it showed comes back when the search is cleared (or the open prompt's category, so it stays in view).
    /// </summary>
    private void UpdateSearchingBoth()
    {
        var both = TeamLoaded && !string.IsNullOrWhiteSpace(_searchText);
        if (both == _searchingBoth) return;
        _searchingBoth = both;

        if (both)
        {
            _categoryBeforeSearch = _selectedCategory?.Id;
            SetRailSelection(null);
            return;
        }

        var target = _categoryBeforeSearch;
        if (target is not null && _editor?.CategoryId is { } editorCategory && editorCategory != target)
            target = editorCategory;
        SetRailSelection(target);
    }

    /// <summary>Choosing a category while searching both libraries ends the search; the category then shows as usual.</summary>
    private void EndSearchForCategory()
    {
        _searchingBoth = false;
        _searchText = "";
        _searchKeepsEditor = false;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(HasSearchText));
        RefreshPrompts();
    }

    /// <summary>Moves the rail's selection without changing the list or the editor.</summary>
    private void SetRailSelection(Guid? id)
    {
        _selectedCategory = Categories.FirstOrDefault(c => c.Id == id) ?? Categories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
    }

    /// <summary>Whether the side's list shows <paramref name="prompt"/>: hidden team prompts only with Show hidden.</summary>
    private bool IsListed(Prompt prompt)
        => !CurrentDocument.IsTeam || _showHiddenTeamPrompts || !_host.Settings.HiddenTeamPromptIds.Contains(prompt.Id);

    private PromptRow NewRow(Prompt prompt, bool isTeam, bool showTeamIcon)
    {
        var settings = _host.Settings;
        return new PromptRow(prompt, isTeam,
            isHidden: isTeam && settings.HiddenTeamPromptIds.Contains(prompt.Id),
            isPinned: settings.PinnedPromptIds.Contains(prompt.Id),
            marker: isTeam ? TeamUserState.MarkerFor(settings, prompt) : TeamMarker.None,
            showTeamIcon);
    }

    private void RaiseEditorState()
    {
        OnPropertyChanged(nameof(LockLineText));
        OnPropertyChanged(nameof(PinLabel));
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(HideLabel));
        OnPropertyChanged(nameof(EditorCategoryName));
    }

    private void RaiseMarkerState()
    {
        OnPropertyChanged(nameof(TeamHasUnseen));
        OnPropertyChanged(nameof(TeamSwitchName));
        OnPropertyChanged(nameof(ShowMarkAllSeen));
    }

    private void RaiseSideState()
    {
        OnPropertyChanged(nameof(CurrentDocument));
        OnPropertyChanged(nameof(Side));
        OnPropertyChanged(nameof(IsTeamSide));
        OnPropertyChanged(nameof(IsYoursSide));
        OnPropertyChanged(nameof(ShowSwitch));
        OnPropertyChanged(nameof(TeamHeaderText));
        OnPropertyChanged(nameof(TeamHeaderDetail));
        OnPropertyChanged(nameof(IsTeamLoading));
        OnPropertyChanged(nameof(IsTeamOffline));
        OnPropertyChanged(nameof(ShowTeamHeader));
        OnPropertyChanged(nameof(ShowTeamReadOnlyNote));
        OnPropertyChanged(nameof(TeamReadOnlyReason));
        OnPropertyChanged(nameof(IsEditingTeam));
        OnPropertyChanged(nameof(ShowEditTeamButton));
        OnPropertyChanged(nameof(ShowTeamEditBar));
        OnPropertyChanged(nameof(TeamEditBlockedText));
        OnPropertyChanged(nameof(ShowTeamEditBlocked));
        OnPropertyChanged(nameof(ShowTeamMoreMenu));
        OnPropertyChanged(nameof(ShowConflictBar));
        OnPropertyChanged(nameof(ShowTeamBar));
        OnPropertyChanged(nameof(ShowBothTeamNotices));
        OnPropertyChanged(nameof(ShowEditButtons));
        OnPropertyChanged(nameof(ShowTeamButtons));
        OnPropertyChanged(nameof(ShowHiddenTeamPrompts));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(ShowHiddenToggle));
        OnPropertyChanged(nameof(ShowHiddenText));
        OnPropertyChanged(nameof(ShowNoPromptsYet));
        OnPropertyChanged(nameof(ShowNewPromptFooter));
        OnPropertyChanged(nameof(ShowNoTeamPrompts));
        OnPropertyChanged(nameof(ShowAllHidden));
        OnPropertyChanged(nameof(AllHiddenText));
        OnPropertyChanged(nameof(NoEditorText));
        RaiseMarkerState();
        RaiseEditorState();
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
