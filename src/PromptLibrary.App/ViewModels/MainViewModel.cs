using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Exchange;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Send;
using PromptLibrary.Core.Team;
using PromptLibrary.Core.Templates;
using PromptLibrary.Core.Usage;

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
        "Someone else changed the team library, so it's been reloaded. Your change is still here: check it and save again.";

    // A refused change with no Save button (a delete, a category change): the reload undid it, so nothing is kept.
    private const string ChangeRedoMessage =
        "Someone else changed the team library, so it's been reloaded. Please make your change again.";
    private const string NewerSchemaMessage =
        "This team library was made with a newer Prompt Library. Update to edit it.";
    private const string RemovesForEveryone = "This removes it for everyone.";

    private readonly IMainHost _host;
    private readonly IDialogs _dialogs;
    private readonly PersonalDocument _personal;
    private readonly TeamDocument? _team;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private string? _statusText;

    // True from the start of a team save until its outcome has been applied on the UI thread; changes wait for it.
    private bool _teamSaving;

    // What the user was doing when "Save changes?" → Save started a team save: it goes on once the save went through.
    private Action? _afterTeamSave;

    private enum SaveAnswer { Proceed, Cancelled, Saving }

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
    private string? _newCategoryEmoji;

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

    // Earlier versions of personal prompts (null: none are kept, e.g. in tests) and how a Send opens its page.
    private readonly PromptHistoryStore? _history;
    private readonly Action<string> _openUrl;

    public MainViewModel(IMainHost host, IDialogs dialogs, PromptHistoryStore? history = null,
        Action<string>? openUrl = null)
    {
        _host = host;
        _dialogs = dialogs;
        _history = history;
        _openUrl = openUrl ?? OpenInBrowser;
        _personal = new PersonalDocument(host);
        if (host.Team is { } team)
        {
            _team = new TeamDocument(team);
            team.Changed += (_, _) => OnTeamChanged();
            RefreshSuggestionCount();
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
        ImportCommand = new RelayCommand(ImportPrompts, () => !IsTeamSide || IsEditingTeam);
        ExportCommand = new RelayCommand(ExportPrompts);
        AddStarterPromptsCommand = new RelayCommand(AddStarterPrompts, () => !IsTeamSide);
        OpenSettingsCommand = new RelayCommand(() => _host.OpenSettings());
        SetUpBackupsCommand = new RelayCommand(() => _host.OpenSettings(focusBackups: true));
        ExitCommand = new RelayCommand(() => _host.ExitApp());
        SetupGuideCommand = new RelayCommand(_host.OpenOnboarding);
        AboutCommand = new RelayCommand(_host.ShowAbout);
        ChangelogCommand = new RelayCommand(() => _openUrl(AppRepository.ChangelogUrl));
        OpenUpdateCommand = new RelayCommand(() => _host.Updates?.OpenDialog());
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => _host.Updates is not null);
        ShowYoursCommand = new RelayCommand(() => SwitchSide(LibrarySide.Yours));
        ShowTeamCommand = new RelayCommand(() => SwitchSide(LibrarySide.Team));
        SendCommand = new RelayCommand<string?>(Send);
        SetDefaultSendCommand = new RelayCommand<string?>(SetDefaultSend);
        ShowHistoryCommand = new RelayCommand(ShowHistory);
        MoveToCategoryCommand = new RelayCommand<Category?>(MoveToCategory);
        CopyToYoursCommand = new RelayCommand(CopyToYours);
        CopyToTeamCommand = new RelayCommand(CopyToTeam);
        SuggestCommand = new RelayCommand(SuggestToTeam);
        SuggestionsCommand = new RelayCommand(OpenSuggestions);
        PinCommand = new RelayCommand<PromptRow?>(TogglePin);
        HideCommand = new RelayCommand<PromptRow?>(ToggleHide);
        FeatureCommand = new RelayCommand<PromptRow?>(ToggleFeature);
        MarkAllSeenCommand = new RelayCommand(MarkAllSeen);
        WhatChangedCommand = new RelayCommand(WhatChanged);
        SeeCopyChangesCommand = new RelayCommand(SeeCopyChanges);
        UpdateMyCopyCommand = new RelayCommand(UpdateMyCopy);
        KeepMineCommand = new RelayCommand(KeepMine);
        RetryTeamCommand = new AsyncRelayCommand(() => _team?.Service.RefreshAsync() ?? Task.CompletedTask);
        EditTeamCommand = new RelayCommand(StartEditingTeam);
        DoneEditingTeamCommand = new RelayCommand(() => StopEditingTeam());
        TeamHistoryCommand = new RelayCommand(() => OpenTeamHistory(review: false));
        CopyInviteCommand = new RelayCommand(CopyInvite);
        TeamNoteCommand = new RelayCommand(EditTeamNote);
        TeamFillInValuesCommand = new RelayCommand(EditTeamFillInValues);
        TeamCheckCommand = new RelayCommand(CheckTeamLibrary);
        DismissTeamNoteCommand = new RelayCommand(DismissTeamNote);
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

        _host.UserStateChanged += (_, _) => RefreshUserState();

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
    /// be read <c>Last updated today, 07:52 · can't reach the team library</c> (<c>· can't read the team library</c>
    /// when the file is there but isn't usable, <c>· update Prompt Library to read it</c> when a newer app made it).
    /// Null when no team library is loaded.
    /// </summary>
    public string? TeamHeaderText
    {
        get
        {
            if (!TeamConnected || IsTeamLoading) return null;
            if (_team!.Service is not { Team: { } library } service) return "Can't reach the team library";
            if (!IsTeamOffline) return $"{library.Name} · updated {When(library.UpdatedAt)}";
            var why = service.LastReadStatus switch
            {
                TeamReadStatus.TooNew => "update Prompt Library to read it",
                TeamReadStatus.NotATeamFile => "can't read the team library",
                _ => "can't reach the team library",
            };
            return $"Last updated {When(service.LastChecked ?? library.UpdatedAt)} · {why}";
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
    public IRelayCommand CopyToTeamCommand { get; }
    public IRelayCommand SuggestCommand { get; }
    public IRelayCommand SuggestionsCommand { get; }

    /// <summary>"Suggest for the team…" in a personal prompt's menu: Your prompts side, a team joined that you can't edit
    /// (editors get "Copy to team prompts" instead).</summary>
    public bool ShowSuggest => IsYoursSide && TeamLoaded && !_team!.Service.CanEdit;

    private int _suggestionCount;
    private int _suggestionRefresh;

    /// <summary>The suggestions waiting for editors (file source), counted on each team refresh.</summary>
    public int SuggestionCount => _suggestionCount;

    /// <summary>The Team header's "Suggestions (n)" button: editors, a team file, at least one suggestion.</summary>
    public bool ShowSuggestionsButton => IsTeamSide && _suggestionCount > 0 && TeamLoaded && _team!.Service.CanEdit;

    public string SuggestionsLabel => $"Suggestions ({_suggestionCount})";

    /// <summary>"Copy to team prompts" in a personal prompt's menu: Your prompts side, a team joined that you can edit.</summary>
    public bool ShowCopyToTeam => IsYoursSide && TeamLoaded && _team!.Service.CanEdit;

    /// <summary>Copies the prompt and opens an AI app with it; the parameter is a <c>SendTargets</c> id, null = the prompt's default.</summary>
    public IRelayCommand<string?> SendCommand { get; }

    /// <summary>Makes a Send target this prompt's default (an unsaved change; Save keeps it).</summary>
    public IRelayCommand<string?> SetDefaultSendCommand { get; }

    /// <summary>Opens the version history of the open personal prompt.</summary>
    public IRelayCommand ShowHistoryCommand { get; }

    /// <summary>Puts the open prompt in another category (an unsaved change).</summary>
    public IRelayCommand<Category?> MoveToCategoryCommand { get; }

    /// <summary>
    /// Set by the window: shows version history for the given view model and returns when it closes. A Restore goes
    /// through <see cref="HistoryViewModel.Restore"/> into the editor.
    /// </summary>
    public Func<HistoryViewModel, bool>? HistoryDialog { get; set; }

    /// <summary>
    /// Set by the window: shows the fill-in dialog. Given a prompt's text with placeholders, it returns the text to
    /// use, or null when the user cancelled. Without it (and for prompts without placeholders) the text is used as is
    /// (apart from the dynamic tokens, see <see cref="TokenSource"/>).
    /// </summary>
    public Func<string, string?>? FillInResolver { get; set; }

    /// <summary>
    /// Set by the window: the values of <c>{clipboard}</c>, <c>{date}</c> and <c>{time}</c> at this moment, for the given
    /// text (the clipboard is only read when the text uses it). Without it the tokens stay as written.
    /// </summary>
    public Func<string, ITokenValues>? TokenSource { get; set; }

    /// <summary>The label of the fill-in dialog's main button while it is open: <c>Copy</c> or <c>Send</c>.</summary>
    public string FillInPrimaryLabel { get; private set; } = "Copy";

    /// <summary>The title of the prompt the fill-in dialog is open for.</summary>
    public string FillInTitle => _editor?.DisplayTitle ?? "";

    /// <summary>
    /// The one place Copy and Send get their text: the editor's text, passed through <see cref="FillInResolver"/> when
    /// it has placeholders (the dialog's button says <paramref name="primaryLabel"/>); a text with only dynamic tokens
    /// gets them filled without a dialog. Null means the user cancelled the fill-in.
    /// </summary>
    public string? ResolveTextForUse(string text, string primaryLabel = "Copy")
    {
        if (FillInResolver is not null && PromptTemplate.HasFields(text))
        {
            FillInPrimaryLabel = primaryLabel;
            return FillInResolver(text);
        }
        return TokenSource is { } tokens ? PromptTemplate.Fill(text, new Dictionary<string, string>(), tokens(text)).Text : text;
    }

    /// <summary>A short pill over the editor ("Copied", "Sent to ChatGPT", "Saved"); the window animates it.</summary>
    public event EventHandler<string>? ToastRequested;

    private void ShowToast(string text) => ToastRequested?.Invoke(this, text);

    /// <summary>The remembered placeholder values, shown in the preview's chips.</summary>
    public IDictionary<string, string> PlaceholderValues
        => new FillInDefaults(_host.Settings.PlaceholderValues, _team?.Service.Team?.FillInValues).Merge();

    /// <summary>Version history is for saved personal prompts (team libraries keep their own history).</summary>
    public bool CanShowHistory => _history is not null && !CurrentDocument.IsTeam && EditorPromptId is not null;

    /// <summary>The Send menu's targets, with a check on the open prompt's default (ChatGPT unless it set another).</summary>
    public IReadOnlyList<SendChoice> SendChoices
    {
        get
        {
            var current = SendTargets.Resolve(_editor?.SendTo).Id;
            return SendTargets.All.Select(t => new SendChoice(t.Id, t.Name, t.Id == current)).ToList();
        }
    }

    /// <summary>The default target's name, for tooltips.</summary>
    public string SendDefaultName => SendTargets.Resolve(_editor?.SendTo).Name;

    /// <summary>Pin / Unpin the given row's prompt, or the editor's when null.</summary>
    public IRelayCommand<PromptRow?> PinCommand { get; }

    /// <summary>Hide from menu / Unhide the given team row's prompt, or the editor's when null.</summary>
    public IRelayCommand<PromptRow?> HideCommand { get; }
    public IRelayCommand<PromptRow?> FeatureCommand { get; }

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

    /// <summary>The editor's <c>Feature for everyone</c> / <c>Stop featuring</c> (editors, team edit mode only).</summary>
    public bool ShowFeatureToggle => IsTeamSide && IsEditingTeam && EditorPromptId is not null;

    public string FeatureLabel => EditorPromptId is { } id && TeamContent?.Prompts.FirstOrDefault(p => p.Id == id)?.Featured == true
        ? "Stop featuring" : "Feature for everyone";

    /// <summary>The open prompt's category name (shown as text while the editor is read only).</summary>
    public string? EditorCategoryName => _editor?.CategoryId is { } id
        ? CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == id)?.Name
        : null;

    /// <summary>The open prompt's category emoji (beside the name while the editor is read only), or null.</summary>
    public string? EditorCategoryEmoji => _editor?.CategoryId is { } id
        ? CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == id)?.Emoji ?? CategoryItem.DefaultEmoji
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

    /// <summary>The Team header's ⋯ menu: Copy invite is for every member.</summary>
    public bool ShowTeamMoreMenu => IsTeamSide && TeamLoaded;

    /// <summary>The ⋯ menu's other items (note, fill-in values, check, restore) are for editors only.</summary>
    public bool ShowTeamEditorItems => IsTeamSide && TeamLoaded && _team!.Service.CanEdit;

    /// <summary>Copy invite: copies a message that tells a colleague how to get the app and join this team library.</summary>
    public IRelayCommand CopyInviteCommand { get; }

    /// <summary>Note for everyone…</summary>
    public IRelayCommand TeamNoteCommand { get; }

    /// <summary>Fill-in values for everyone…</summary>
    public IRelayCommand TeamFillInValuesCommand { get; }

    /// <summary>Check the team library</summary>
    public IRelayCommand TeamCheckCommand { get; }

    /// <summary>The × on the note line: hides this note until its text changes.</summary>
    public IRelayCommand DismissTeamNoteCommand { get; }

    /// <summary>The muted line under the Team header, <c>Note: text · 2 days ago</c>; null with no note or once hidden.</summary>
    public string? TeamNoteLine
    {
        get
        {
            if (!IsTeamSide || _team?.Service.Team?.Note is not { } note || string.IsNullOrWhiteSpace(note.Text)) return null;
            if (_host.Settings.SeenTeamNote == NoteHash(note.Text)) return null;
            var text = string.Join(' ', note.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return $"Note: {text} · {When(note.At)}";
        }
    }

    public bool ShowTeamNoteLine => TeamNoteLine is not null;

    private static string NoteHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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

    /// <summary>File → Import prompts…: reads an export file or a folder of Markdown/text files into your prompts (not on the Team side).</summary>
    public IRelayCommand ImportCommand { get; }

    /// <summary>The empty library's "Add starter prompts": puts the example prompts (those whose titles are missing) into your library.</summary>
    public IRelayCommand AddStarterPromptsCommand { get; }

    /// <summary>File → Export prompts…: writes your prompts to one JSON file or a folder of Markdown files (not on the Team side).</summary>
    public IRelayCommand ExportCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand SetUpBackupsCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand SetupGuideCommand { get; }
    public IRelayCommand AboutCommand { get; }

    /// <summary><c>Help → What's new</c>: opens the changelog in the repository.</summary>
    public IRelayCommand ChangelogCommand { get; }

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
            OnPropertyChanged(nameof(ShowNoPromptsYet));
            OnPropertyChanged(nameof(ShowNewPromptFooter));
            OnPropertyChanged(nameof(WindowTitle));
            RaiseEditorState();
        }
    }

    public bool HasEditor => _editor is not null;
    public bool HasNoEditor => _editor is null;

    /// <summary>"Prompt Library", with a "• " prefix while the editor has unsaved changes.</summary>
    public string WindowTitle => _editor is { IsDirty: true } ? "• " + AppTitle : AppTitle;

    /// <summary>
    /// The empty-library card (it spans the list and editor columns): only while nothing is open in the editor, so a
    /// new first prompt being written is never covered by it.
    /// </summary>
    public bool ShowNoPromptsYet => !IsTeamSide && Prompts.Count == 0 && string.IsNullOrWhiteSpace(_searchText) && _editor is null;

    /// <summary>The list's "+ New prompt" footer; hidden while the empty state shows its own New prompt button.</summary>
    public bool ShowNewPromptFooter => !CurrentDocument.IsReadOnly && !ShowNoPromptsYet;

    /// <summary>The Team side's empty state: "No team prompts yet."</summary>
    public bool ShowNoTeamPrompts => IsTeamSide && !IsTeamLoading && string.IsNullOrWhiteSpace(_searchText)
                                     && CurrentDocument.Library.Prompts.Count == 0;

    /// <summary>The Team side's list is empty only because what it would show is hidden from the menu.</summary>
    public bool ShowAllHidden => IsTeamSide && !_showHiddenTeamPrompts && Prompts.Count == 0
                                 && string.IsNullOrWhiteSpace(_searchText)
                                 && PromptFilter.Apply(CurrentDocument.Library, CategoryFilter, null).Count > 0;

    /// <summary>"All team prompts are hidden.", or "All prompts in this category are hidden." for one category.</summary>
    public string AllHiddenText => CategoryFilter is null || HiddenCount == CurrentDocument.Library.Prompts.Count
        ? "All team prompts are hidden."
        : "All prompts in this category are hidden.";

    public bool ShowNoMatches => Prompts.Count == 0 && !string.IsNullOrWhiteSpace(_searchText);
    public string NoMatchesText => $"No prompts match \"{_searchText.Trim()}\".";

    /// <summary>The no-match empty state's headline: the same sentence without the full stop.</summary>
    public string NoMatchesHeadline => $"No prompts match \"{_searchText.Trim()}\"";

    /// <summary>
    /// The faint hint in the editor area when no prompt is open. Empty when the library is empty: the list column already
    /// says so (one empty state per view).
    /// </summary>
    public string NoEditorText => CurrentDocument.Library.Prompts.Count == 0 || (IsTeamSide && IsTeamLoading)
        ? ""
        : IsTeamSide && CurrentDocument.IsReadOnly ? "Select a team prompt." : "Select a prompt, or create one.";

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
        private set
        {
            if (SetProperty(ref _isAddingCategory, value)) OnPropertyChanged(nameof(ShowNewCategoryButton));
        }
    }

    /// <summary>The rail's "New category" button: on an editable side, while no new category is being typed.</summary>
    public bool ShowNewCategoryButton => ShowEditButtons && !IsAddingCategory;

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

    /// <summary>The emoji picked for the category being added (null = none).</summary>
    public string? NewCategoryEmoji
    {
        get => _newCategoryEmoji;
        set => SetProperty(ref _newCategoryEmoji, value);
    }

    /// <summary>
    /// If the editor has unsaved changes, asks <c>Save changes to "saved title"?</c> (or <c>… to this new prompt?</c>):
    /// Save saves (false if that fails), Don't save throws the changes away, Cancel returns false. True when it's
    /// fine to move on.
    /// </summary>
    /// <remarks>
    /// On the Team side a save can't finish before this returns, so Save (or a team save still writing) gives false;
    /// callers that should go on once it has gone through use <see cref="ProceedOrDefer"/>.
    /// </remarks>
    public bool ConfirmDiscardOrSave() => AskToSave() == SaveAnswer.Proceed;

    /// <summary>
    /// Before the main window hides (or the app exits): offers to save unsaved changes, then ends editing the team
    /// library (so refreshes aren't held while nobody is looking). True when it can go now. When a team save has to
    /// finish first, false is returned and <paramref name="later"/> runs once the save has gone through.
    /// </summary>
    public bool ConfirmLeave(Action later)
    {
        if (!ProceedOrDefer(() =>
            {
                if (ConfirmLeave(later)) later();
            })) return false;
        return StopEditingTeam(ask: false);
    }

    /// <summary>
    /// Before leaving the team library (Settings → Leave team library): while the Team side is shown, a team prompt
    /// with unsaved changes is offered for saving and editing ends, as when the window closes. Your own prompts aren't
    /// affected by leaving, so nothing is asked about them. True when it can go on now; false on Cancel, or when a team
    /// save has to finish first (<paramref name="later"/> then runs once it has gone through).
    /// </summary>
    public bool ConfirmLeaveTeam(Action later) => !IsTeamSide || ConfirmLeave(later);

    /// <summary>
    /// At exit: gives a team save still writing up to <paramref name="limit"/> to finish, running the dispatcher
    /// meanwhile so its result applies. True when nothing is left writing.
    /// </summary>
    public bool WaitForTeamSave(TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (TeamWriting && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var tick = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background,
                (_, _) => frame.Continue = false, _dispatcher);
            Dispatcher.PushFrame(frame);
            tick.Stop();
        }
        return !TeamWriting;
    }

    private bool TeamWriting => _teamSaving || _team?.LibraryUnwritten == true;

    /// <summary>"Save changes?" with what came of it: go on, stay (Cancel, or a save that failed at once), or a team save writing.</summary>
    private SaveAnswer AskToSave()
    {
        if (_teamSaving) return SaveAnswer.Saving; // a team save is still writing: wait for it
        if (_editor is not { IsDirty: true } editor) return SaveAnswer.Proceed;

        var savedTitle = CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == editor.PromptId)?.Title;
        switch (_dialogs.AskSaveChanges(EditorText.SaveChangesQuestion(savedTitle)))
        {
            case true:
                if (Save()) return SaveAnswer.Proceed;
                return _teamSaving ? SaveAnswer.Saving : SaveAnswer.Cancelled;
            case false:
                DiscardChanges(editor);
                return SaveAnswer.Proceed;
            default:
                return SaveAnswer.Cancelled;
        }
    }

    /// <summary>
    /// <see cref="ConfirmDiscardOrSave"/> for an action that can wait: true when it can go on now. When a team save was
    /// started (or is still writing), <paramref name="retry"/> runs once it has gone through, as a personal save would
    /// have let it; after a refused or failed save it doesn't.
    /// </summary>
    private bool ProceedOrDefer(Action retry)
    {
        var answer = AskToSave();
        if (answer == SaveAnswer.Saving) _afterTeamSave = retry;
        return answer == SaveAnswer.Proceed;
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
        return new UnsavedEdit(editor.Title, editor.Description, editor.CategoryId, categoryName, editor.Text,
            [.. editor.Tags]);
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
        if (PruneTeamState()) SaveTeamState(); // pins of prompts the restore removed
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
        NewCategoryEmoji = null;
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
            var editor = NewLibraryEditor();
            category = editor.AddCategory(name);
            if (_newCategoryEmoji is not null) editor.SetCategoryEmoji(category.Id, _newCategoryEmoji);
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
        if (item is not { IsCategory: true } || CannotChange) return;
        CancelNewCategory();
        foreach (var other in Categories) other.IsRenaming = false;
        item.EditName = item.Name;
        item.EditEmoji = item.Emoji;
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
        var emojiChanged = item.EditEmoji != item.Emoji;
        if (name == item.Name && !emojiChanged)
        {
            CancelRename(item);
            return true;
        }

        if (name != item.Name)
        {
            item.Error = CategoryNameError(name, exceptId: id);
            if (item.Error is not null) return false;
        }

        try
        {
            var editor = NewLibraryEditor();
            if (name != item.Name) editor.RenameCategory(id, name);
            if (emojiChanged) editor.SetCategoryEmoji(id, item.EditEmoji);
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
        if (item is not { IsCategory: true, Id: { } id } || CannotChange) return;

        var promptCount = NewLibraryEditor().PromptCount(id);
        Guid? moveTo = null;
        var deletePrompts = false;

        if (promptCount == 0)
        {
            var question = CurrentDocument.IsTeam
                ? $"Delete the category \"{item.Name}\"? {RemovesForEveryone}"
                : $"Delete the category \"{item.Name}\"?";
            if (!_dialogs.ConfirmDestructive(question, "Delete")) return;
        }
        else
        {
            var targets = CurrentDocument.Library.Categories
                .Where(c => c.Id != id)
                .OrderBy(c => c.Name, NameOrder)
                .ToList();

            if (_dialogs.ChooseCategoryDeletion(item.Name, promptCount, targets) is not { } choice) return;
            deletePrompts = choice.DeletePrompts;
            moveTo = deletePrompts ? null : choice.MoveTo;
            if (!deletePrompts && moveTo is null) return;
        }

        IReadOnlyList<Guid> removed;
        try
        {
            removed = NewLibraryEditor().DeleteCategory(id, moveTo, deletePrompts);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        if (deletePrompts)
        {
            // The open prompt went with its category (what was typed in it too: the user chose to delete these prompts).
            if (_editor is { } open && (open.CategoryId == id || (open.PromptId is { } openId && removed.Contains(openId))))
            {
                if (_unwritten is { } unwritten && removed.Contains(unwritten.PromptId)) _unwritten = null;
                Editor = null;
            }
            // Pins, usage and history of the removed prompts go too (a team library has no history here, and its pins are
            // pruned once the save went through, as for a deleted prompt).
            if (!CurrentDocument.IsTeam)
            {
                foreach (var removedId in removed) _history?.Delete(removedId);
                var settings = _host.Settings;
                var referenced = removed.Any(r => settings.PinnedPromptIds.Contains(r) || settings.Usage.ContainsKey(r));
                PromptReferences.Remove(settings, removed);
                if (referenced) SaveTeamState();
            }
            else
            {
                // Usage is only the Recent list and ranking, so it goes now rather than after the team save.
                var cleared = removed.Count(r => _host.Settings.Usage.Remove(r));
                if (cleared > 0) SaveTeamState();
            }
        }
        else
        {
            _editor?.ReplaceCategory(id, moveTo);
        }
        SaveChange(); // on failure the deletion stays and is written later (the host says so)

        // Show where the prompts went; if the deleted category was empty or its prompts went too, fall back to All prompts.
        var selectId = _selectedCategory?.Id == id ? moveTo : _selectedCategory?.Id;
        RebuildCategories(selectId);
        RefreshPrompts();

        if (deletePrompts)
            ShowStatus($"Deleted \"{item.Name}\" and {Count(removed.Count, "prompt")}.");
        else if (moveTo is { } targetId && CurrentDocument.Library.Categories.FirstOrDefault(c => c.Id == targetId) is { } target)
            ShowStatus($"Moved {Count(promptCount, "prompt")} to {target.Name}.");
    }

    // ---- Import and export -----------------------------------------------------------------------

    /// <summary>
    /// Import prompts…: reads an export file (or a library file) or a folder of .md / .txt files into your prompts.
    /// Duplicates (same title in the same category) are skipped; the summary says what happened.
    /// </summary>
    private void ImportPrompts()
    {
        if (_teamSaving || (IsTeamSide && !IsEditingTeam)) return;
        if (_dialogs.ChooseImportSource() is not { } pick) return;

        var library = CurrentDocument.Library;
        ImportResult result;
        try
        {
            result = pick.IsFolder
                ? PromptImport.FromFolder(library, pick.Path, () => DateTimeOffset.Now)
                : PromptImport.FromJsonFile(library, pick.Path, () => DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage($"Couldn't import: {ex.Message}");
            return;
        }

        if (result.Imported > 0)
        {
            SaveChange(); // yours: on failure the prompts stay and are written later; team: one guarded save
            RebuildCategories(_selectedCategory?.Id);
            RefreshPrompts();
            OpenFirstPromptIfNone();
        }
        _dialogs.ShowImportResults(result, IsTeamSide);
    }

    /// <summary>
    /// Add starter prompts: the 39 example prompts of a new library, skipping any whose title is already there (categories
    /// are matched by name). Safe to press twice.
    /// </summary>
    private void AddStarterPrompts()
    {
        if (IsTeamSide || _teamSaving) return;
        var added = SeedLibrary.AddMissing(_personal.Library, DateTimeOffset.Now);
        if (added == 0)
        {
            ShowStatus("All the starter prompts are already in your library.", FeedbackDuration);
            return;
        }

        _personal.SaveLibraryChange(); // on failure the prompts stay and are written later (the host says so)
        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
        OpenFirstPromptIfNone();
        ShowStatus($"Added {Count(added, "starter prompt")}.", FeedbackDuration);
    }

    /// <summary>Export prompts…: one .json file, or a folder of .md files (one per prompt, a subfolder per category).</summary>
    private void ExportPrompts()
    {
        var library = CurrentDocument.Library;
        if (library.Prompts.Count == 0)
        {
            _dialogs.ShowMessage("There are no prompts to export yet.");
            return;
        }
        if (_dialogs.ChooseExportTarget() is not { } pick) return;

        try
        {
            var count = pick.IsFolder
                ? PromptExport.WriteMarkdownFolder(pick.Path, library)
                : PromptExport.WriteJson(pick.Path, library);
            ShowStatus($"Exported {Count(count, "prompt")}.", FeedbackDuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _dialogs.ShowMessage($"Couldn't export: {ex.Message}");
        }
    }

    // ---- Prompts -------------------------------------------------------------------------------

    /// <summary>
    /// Starts a new prompt in the selected category (the first category A–Z under "All prompts").
    /// It joins the library when it is saved.
    /// </summary>
    public void NewPrompt()
    {
        if (CannotChange) return;
        if (!ProceedOrDefer(NewPrompt)) return;

        var categoryId = CategoryFilter
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
        Prompt? before = null; // for the history of a personal prompt
        try
        {
            var library = NewLibraryEditor();
            if (editor.PromptId is { } existingId)
            {
                // After an earlier failed write, the earlier snapshot is still what's on disk.
                onDisk = _unwritten ?? PromptSnapshot.Capture(CurrentDocument.Library, existingId);
                if (!isTeam && CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == existingId) is { } existing)
                    before = ClonePrompt(existing);
                library.UpdatePrompt(existingId, editor.Title, editor.Description, editor.CategoryId!.Value, editor.Text,
                    editor.Tags, editor.SendTo);
                promptId = existingId;
            }
            else
            {
                promptId = library.AddPrompt(editor.CategoryId!.Value, editor.Title, editor.Description, editor.Text,
                    editor.Tags, editor.SendTo).Id;
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
            TrackTeamSave(fromPromptSave: true, () => AfterTeamPromptSaved(editor, promptId));
            FollowEditorCategory(editor);
            return false; // not written yet: the editor stays unsaved until the save reports back
        }

        var written = CurrentDocument.SaveLibrary();
        _unwritten = written ? null : onDisk;
        if (written)
        {
            var saved = CurrentDocument.Library.Prompts.First(p => p.Id == promptId);
            if (before is not null) _history?.Append(promptId, before, saved); // never blocks or fails the save
            editor.LoadSaved(saved);
            editor.ShowSavedFeedback();
            ShowToast("Saved");
        }

        FollowEditorCategory(editor);
        return written;
    }

    private static Prompt ClonePrompt(Prompt p) => new()
    {
        Id = p.Id, Title = p.Title, Description = p.Description, CategoryId = p.CategoryId, Text = p.Text,
        Tags = [.. p.Tags ?? []], SendTo = p.SendTo, CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt,
    };

    private static void OpenInBrowser(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>Rebuilds the lists after a prompt's Save; if the prompt moved out of the category shown, follows it.</summary>
    private void FollowEditorCategory(PromptEditorViewModel editor)
    {
        var showCategory = CategoryFilter is { } shown && shown != editor.CategoryId
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
        if (ResolveTextForUse(editor.Text) is not { } text) return;
        if (_host.CopyText(text, editor.DisplayTitle))
        {
            editor.ShowCopiedFeedback();
            ShowToast("Copied");
            RecordUsage(editor);
        }
    }

    private int UsesOf(Guid id) => _host.Settings.Usage.TryGetValue(id, out var usage) ? usage.Count : 0;

    /// <summary>Counts a copy or send for the ranking of the search window and quick menu.</summary>
    private void RecordUsage(PromptEditorViewModel editor)
    {
        if (editor.PromptId is not { } id) return;
        UsageTracker.Record(_host.Settings, id, DateTimeOffset.Now);
        editor.Uses = UsesOf(id);
        _host.SaveSettings();
        RefreshUserState(); // the Recent row and list
    }

    /// <summary>
    /// Copies the prompt, opens the chosen AI app with it in the browser (a long prompt opens the app's home page
    /// instead) and says so. The prompt is always on the clipboard.
    /// </summary>
    public void Send(string? targetId = null)
    {
        var editor = _editor;
        if (editor is null || !editor.CanCopy) return;
        if (ResolveTextForUse(editor.Text, "Send") is not { } text) return;

        var target = SendTargets.Resolve(targetId ?? editor.SendTo);
        if (!_host.CopyText(text, editor.DisplayTitle, notifyInTray: false)) return;
        try
        {
            _openUrl(target.BuildUrl(text));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or UriFormatException or PlatformNotSupportedException)
        {
            ShowStatus($"Couldn't open {target.Name}. The prompt is on your clipboard: press Ctrl+V to paste it.", FeedbackDuration);
            return;
        }

        RecordUsage(editor);
        ShowToast($"Sent to {target.Name}");
        ShowStatus(target.IsTooLong(text)
            ? $"Opened {target.Name}. The prompt is also on your clipboard: it was too long for the link, so paste it there."
            : $"Opened {target.Name}. The prompt is also on your clipboard.", FeedbackDuration);
    }

    private void SetDefaultSend(string? targetId)
    {
        if (_editor is not { IsEditable: true } editor || SendTargets.Find(targetId) is not { } target) return;
        editor.SendTo = target.Id;
    }

    private void MoveToCategory(Category? category)
    {
        if (_editor is { IsEditable: true } editor && category is not null) editor.CategoryId = category.Id;
    }

    private void ShowHistory()
    {
        if (!CanShowHistory || _editor is not { PromptId: { } id } editor || HistoryDialog is not { } show) return;
        var versions = _history!.List(id);
        var model = new HistoryViewModel(versions, editor.Text, DateTimeOffset.Now, editor.ApplyVersion);
        if (show(model) && model.CopyRequested is { } version)
            RestoreAsNewPrompt(version);
    }

    /// <summary>
    /// History's "Restore as a new prompt": a copy of the version, titled "&lt;title&gt; (restored)", goes into the same
    /// category as the open prompt and opens in the editor. The open prompt stays as it is (unsaved edits are asked about first).
    /// </summary>
    internal void RestoreAsNewPrompt(PromptVersion version)
    {
        if (_editor is not { PromptId: { } id } || IsTeamSide) return;
        if (_personal.Library.Prompts.FirstOrDefault(p => p.Id == id) is not { } source) return;
        if (!ProceedOrDefer(() => RestoreAsNewPrompt(version))) return;

        Prompt copy;
        try
        {
            var title = $"{(string.IsNullOrWhiteSpace(version.Title) ? source.Title : version.Title).Trim()} (restored)";
            copy = new LibraryEditor(_personal.Library, () => DateTimeOffset.Now)
                .AddPrompt(source.CategoryId, title, version.Description ?? "", version.Text, TagRules.Normalize(version.Tags), null);
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        _personal.SaveLibraryChange(); // on failure the copy stays and is written later (the host says so)
        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
        OpenPrompt(copy, byUser: true);
        ShowStatus("Restored as a new prompt", FeedbackDuration);
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
        var question = CurrentDocument.IsTeam ? $"Delete \"{title}\"? {RemovesForEveryone}" : $"Delete \"{title}\"?";
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
        // A deleted prompt is unpinned. A team delete is pruned once it has been saved (a refused one comes back pinned).
        if (!CurrentDocument.IsTeam)
        {
            _history?.Delete(id);
            var settings = _host.Settings;
            var referenced = settings.PinnedPromptIds.Contains(id) || settings.Usage.ContainsKey(id);
            PromptReferences.Remove(settings, [id]);
            if (referenced) SaveTeamState();
        }
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
                if (ProceedOrDefer(change))
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
        => _editor is null || id is null || id == CategoryItem.RecentId || id == CategoryItem.FeaturedId || _editor.CategoryId == id;

    /// <summary>The category the list is limited to: null for All prompts and Recent.</summary>
    private Guid? CategoryFilter => _selectedCategory is { IsCategory: true } category ? category.Id : null;

    /// <summary>
    /// The last-used prompts of the side shown, most recent first, at most 10 (hidden team prompts only with Show hidden).
    /// </summary>
    private List<Prompt> RecentPrompts()
    {
        var byId = new Dictionary<Guid, Prompt>();
        foreach (var prompt in CurrentDocument.Library.Prompts) byId.TryAdd(prompt.Id, prompt);

        var result = new List<Prompt>();
        foreach (var id in UsageTracker.Recent(_host.Settings, int.MaxValue))
        {
            if (!byId.TryGetValue(id, out var prompt) || !IsListed(prompt)) continue;
            result.Add(prompt);
            if (result.Count == RecentLimit) break;
        }
        return result;
    }

    private const int RecentLimit = 10;

    /// <summary>The most prompts the editors can feature.</summary>
    public const int MaxFeatured = 10;

    /// <summary>The team prompts the editors feature that this user hasn't hidden, in library order (team side only).</summary>
    private List<Prompt> FeaturedPrompts()
        => !CurrentDocument.IsTeam ? []
            : CurrentDocument.Library.Prompts
                .Where(p => p.Featured && !_host.Settings.HiddenTeamPromptIds.Contains(p.Id)).ToList();

    /// <summary>Features or unfeatures a team prompt (the editor's when null): editors in edit mode only, max 10.</summary>
    private void ToggleFeature(PromptRow? row)
    {
        if (!IsTeamSide || !IsEditingTeam || CannotChange) return;
        if ((row?.Id ?? EditorPromptId) is not { } id) return;
        if (CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == id) is not { } prompt) return;

        if (!prompt.Featured && CurrentDocument.Library.Prompts.Count(p => p.Featured) >= MaxFeatured)
        {
            ShowStatus($"Up to {MaxFeatured} prompts can be featured.", FeedbackDuration);
            return;
        }

        prompt.Featured = !prompt.Featured;
        SaveChange(); // one guarded team save; a refused or failed one puts the library back
        foreach (var item in Prompts.Where(r => r.Id == id)) item.IsFeatured = prompt.Featured;
        var before = _selectedCategory?.Id;
        RebuildCategories(before);
        if (before == CategoryItem.FeaturedId) RefreshPrompts();
        RaiseEditorState();
    }

    /// <summary>
    /// Pins, usage or hides changed somewhere else (the search window, the tray): the pin glyphs, the Recent row and its
    /// list follow. An open rename is left alone.
    /// </summary>
    public void RefreshUserState()
    {
        var pinned = _host.Settings.PinnedPromptIds;
        foreach (var row in Prompts) row.IsPinned = pinned.Contains(row.Id);
        RaiseEditorState();
        if (Categories.Any(c => c.IsRenaming)) return;

        var before = _selectedCategory?.Id;
        RebuildCategories(before);
        if (before == CategoryItem.RecentId || _selectedCategory?.Id != before) RefreshPrompts();
    }

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
        editor.Uses = UsesOf(prompt.Id);
        editor.IsReadOnly = CurrentDocument.IsReadOnly;
        // What changed since this PC last saw the prompt: taken before opening clears the marker, kept while it is open.
        _openChange = null;
        if (CurrentDocument.IsTeam && _team is { } teamDoc
            && TeamUserState.MarkerFor(_host.Settings, prompt) == TeamMarker.Updated
            && teamDoc.Service.TryGetChange(prompt.Id, out var change) && !change.IsEmpty)
            _openChange = change;
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
            if (RecentPrompts() is { Count: > 0 } recent)
                Categories.Add(new CategoryItem(CategoryItem.RecentId, "Recent", recent.Count, canEdit));
            if (FeaturedPrompts() is { Count: > 0 } featured)
                Categories.Add(new CategoryItem(CategoryItem.FeaturedId, "Featured", featured.Count, canEdit));
            foreach (var category in sorted)
                Categories.Add(new CategoryItem(category.Id, category.Name,
                    shown.Count(p => p.CategoryId == category.Id), canEdit, category.Emoji));

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
            var now = DateTimeOffset.Now;
            var isRecent = _selectedCategory?.IsRecent == true;
            var isFeatured = _selectedCategory?.IsFeatured == true;
            if (_searchingBoth)
            {
                // Both libraries, best match first; hidden team prompts are listed greyed so they can be unhidden.
                foreach (var hit in FuzzySearch.Search(_personal.Library, TeamContent, _host.Settings, _searchText, now))
                    Prompts.Add(NewRow(hit.Prompt, hit.IsTeam, showTeamIcon: hit.IsTeam, hit.TitleMatches, hit.DescriptionMatches));
            }
            else if (!FuzzySearch.ParseQuery(_searchText).IsEmpty)
            {
                // The shown category (or the Recent list), best match first.
                var categoryId = CategoryFilter;
                var recentIds = isRecent ? RecentPrompts().Select(p => p.Id).ToHashSet()
                    : isFeatured ? FeaturedPrompts().Select(p => p.Id).ToHashSet() : null;
                foreach (var hit in FuzzySearch.Search(CurrentDocument.Library, null, _host.Settings, _searchText, now))
                    if (IsListed(hit.Prompt)
                        && (categoryId is null || hit.Prompt.CategoryId == categoryId)
                        && (recentIds is null || recentIds.Contains(hit.Prompt.Id)))
                        Prompts.Add(NewRow(hit.Prompt, CurrentDocument.IsTeam, showTeamIcon: false,
                            hit.TitleMatches, hit.DescriptionMatches));
            }
            else if (isFeatured)
            {
                foreach (var prompt in FeaturedPrompts())
                    Prompts.Add(NewRow(prompt, true, showTeamIcon: false));
            }
            else if (isRecent)
            {
                foreach (var prompt in RecentPrompts())
                    Prompts.Add(NewRow(prompt, CurrentDocument.IsTeam, showTeamIcon: false));
            }
            else
            {
                foreach (var prompt in PromptFilter.Apply(CurrentDocument.Library, CategoryFilter, null))
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
        OnPropertyChanged(nameof(NoMatchesHeadline));
        OnPropertyChanged(nameof(NoEditorText));
        OnPropertyChanged(nameof(ShowNoTeamPrompts));
        OnPropertyChanged(nameof(ShowAllHidden));
        OnPropertyChanged(nameof(AllHiddenText));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PromptEditorViewModel.IsDirty))
            OnPropertyChanged(nameof(WindowTitle));
        else if (e.PropertyName == nameof(PromptEditorViewModel.SendTo))
        {
            OnPropertyChanged(nameof(SendChoices));
            OnPropertyChanged(nameof(SendDefaultName));
        }
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
        if (!ProceedOrDefer(() => SwitchSide(side, openPromptId))
            || (_side == LibrarySide.Team && !StopEditingTeam(ask: false)))
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
        if (side == LibrarySide.Team) RefreshSuggestionCount();
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
            copy = editor.AddPrompt(category.Id, source.Title, source.Description, source.Text, source.Tags, null);
            copy.SourceTeamPromptId = source.Id; // lets the copy offer an update when the team version changes
            copy.SourceFingerprint = TeamUserState.ContentFingerprint(source);
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

    /// <summary>
    /// Copy to team prompts: one click on a personal prompt. Adds a copy (new id, no source) to the team category of the
    /// same name (created when missing) and saves it guarded, without edit mode. When someone else changed the file the
    /// team library is reloaded and the add is applied once more; a second refusal shows the conflict text.
    /// </summary>
    private void CopyToTeam()
    {
        if (!IsYoursSide || _teamSaving || _team is not { } team || !TeamLoaded || !team.Service.CanEdit
            || EditorPromptId is not { } id
            || _personal.Library.Prompts.FirstOrDefault(p => p.Id == id) is not { } source) return;

        var categoryName = (_personal.Library.Categories.FirstOrDefault(c => c.Id == source.CategoryId)?.Name
                            ?? "Prompts").Trim();
        string? added = null;

        bool Apply()
        {
            var library = team.Library;
            var editor = new LibraryEditor(library, () => DateTimeOffset.Now);
            try
            {
                var category = library.Categories.FirstOrDefault(c =>
                                   string.Equals(c.Name.Trim(), categoryName, StringComparison.OrdinalIgnoreCase))
                               ?? editor.AddCategory(categoryName);
                editor.AddPrompt(category.Id, source.Title, source.Description, source.Text, source.Tags, source.SendTo);
                added = category.Name;
                return true;
            }
            catch (ArgumentException ex)
            {
                team.Service.RevertToLoaded();
                _dialogs.ShowMessage(ex.Message);
                return false;
            }
        }

        SaveTeamChange(team, Apply, () => $"Copied to team prompts · {added}");
    }

    /// <summary>
    /// Applies a change to the loaded team library and saves it guarded, without edit mode. When someone else changed the
    /// file the library is reloaded and the change applied once more; a second refusal shows the conflict text.
    /// </summary>
    private void SaveTeamChange(TeamDocument team, Func<bool> apply, Func<string> savedStatus, bool retry = false)
    {
        if (!apply()) return;
        var baseHash = team.IsEditing ? team.BaseHash : team.Service.CurrentHash;
        team.SaveWithoutEditing();
        if (team.PendingSave is not { } pending) return;
        _teamSaving = true;
        pending.ContinueWith(task =>
        {
            var result = task.IsCompletedSuccessfully
                ? task.Result
                : new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't save the team library.");
            _dispatcher.BeginInvoke(() =>
            {
                _teamSaving = false;
                if (_team is not { } current) return;
                switch (result.Status)
                {
                    case TeamSaveStatus.Saved:
                        current.Rebase();
                        _host.RefreshQuickMenu();
                        ShowStatus(savedStatus(), FeedbackDuration);
                        break;
                    case TeamSaveStatus.ChangedByOther when current.Service.CurrentHash != baseHash:
                        current.Rebase(); // the service has re-read the file
                        if (!retry)
                            SaveTeamChange(current, apply, savedStatus, retry: true);
                        else
                            _dialogs.ShowMessage(ChangeRedoMessage);
                        break;
                    default:
                        current.Service.RevertToLoaded();
                        _dialogs.ShowMessage(result.Message ?? "Couldn't save the team library.");
                        break;
                }
            });
        }, TaskScheduler.Default);
    }

    // ---- Suggestions: members suggest a prompt, editors review them ---------------------------------

    private string? SuggestionsTeamPath =>
        _team?.Service.Source is { Kind: TeamSourceKind.File } source ? source.Location : null;

    /// <summary>
    /// Suggest for the team…: asks for a team category, a note and a name, then writes a suggestion file next to the team
    /// file. A web source or a folder that can't be written gets the suggestion on the clipboard instead.
    /// </summary>
    private void SuggestToTeam()
    {
        if (!IsYoursSide || _team is not { } team || !TeamLoaded
            || EditorPromptId is not { } id
            || _personal.Library.Prompts.FirstOrDefault(p => p.Id == id) is not { } source) return;

        var own = _personal.Library.Categories.FirstOrDefault(c => c.Id == source.CategoryId)?.Name ?? "Prompts";
        var dialog = new SuggestViewModel(source.Title, own, team.Library.Categories.Select(c => c.Name),
            SuggestViewModel.DefaultName());
        if (!_dialogs.ShowSuggest(dialog) || !dialog.Sent) return;

        var suggestion = new Suggestion(Guid.NewGuid(), source.Title, source.Description, source.Text,
            source.Tags.ToList(), dialog.SelectedCategory.Trim(),
            string.IsNullOrWhiteSpace(dialog.Note) ? null : dialog.Note.Trim(),
            string.IsNullOrWhiteSpace(dialog.YourName) ? null : dialog.YourName.Trim(), DateTimeOffset.Now);

        void OnClipboard()
        {
            var text = TeamSuggestions.ToClipboardText(suggestion).Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (_host.CopyText(text, "Team suggestion", notifyInTray: false))
                ShowStatus("Couldn't reach the team folder. The suggestion is on your clipboard to send to the team owner.",
                    FeedbackDuration);
        }

        if (SuggestionsTeamPath is not { } path)
        {
            OnClipboard();
            return;
        }
        Task.Run(() => TeamSuggestions.TryWrite(path, suggestion)).ContinueWith(task =>
        {
            var written = task.IsCompletedSuccessfully && task.Result == SuggestionWriteResult.Written;
            _dispatcher.BeginInvoke(() =>
            {
                if (written) ShowStatus("Suggestion sent to the team.", FeedbackDuration);
                else OnClipboard();
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Counts the waiting suggestions off the UI thread; only editors of a team file are counted.</summary>
    private void RefreshSuggestionCount()
    {
        var generation = ++_suggestionRefresh;
        var path = _team?.Service.CanEdit == true ? SuggestionsTeamPath : null;
        if (path is null)
        {
            SetSuggestionCount(0);
            return;
        }
        Task.Run(() => TeamSuggestions.List(path).Count).ContinueWith(task =>
        {
            var count = task.IsCompletedSuccessfully ? task.Result : 0;
            _dispatcher.BeginInvoke(() =>
            {
                if (generation == _suggestionRefresh) SetSuggestionCount(count);
            });
        }, TaskScheduler.Default);
    }

    private void SetSuggestionCount(int count)
    {
        _suggestionCount = count;
        OnPropertyChanged(nameof(SuggestionCount));
        OnPropertyChanged(nameof(SuggestionsLabel));
        OnPropertyChanged(nameof(ShowSuggestionsButton));
    }

    /// <summary>The Suggestions window: read the list now (off the UI thread), then add or dismiss in the dialog.</summary>
    private void OpenSuggestions()
    {
        if (!IsTeamSide || _team is not { } team || !TeamLoaded || !team.Service.CanEdit
            || SuggestionsTeamPath is not { } path) return;
        Task.Run(() => TeamSuggestions.List(path)).ContinueWith(task =>
        {
            var list = task.IsCompletedSuccessfully ? task.Result : [];
            _dispatcher.BeginInvoke(() =>
            {
                if (_team is not { } current || !TeamLoaded) return;
                var dialog = new SuggestionsViewModel(path, list, current.Library.Categories.Select(c => c.Name),
                    DateTimeOffset.Now, (s, category, onSaved) => AddSuggestion(current, s, category, onSaved));
                _dialogs.ShowSuggestions(dialog);
                RefreshSuggestionCount();
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Adds a suggestion as a team prompt (new id, not featured) in the named category, created when missing.</summary>
    private void AddSuggestion(TeamDocument team, Suggestion suggestion, string categoryName, Action onSaved)
    {
        if (_teamSaving || !team.Service.CanEdit) return;
        string? added = null;

        bool Apply()
        {
            var library = team.Library;
            var editor = new LibraryEditor(library, () => DateTimeOffset.Now);
            try
            {
                var category = library.Categories.FirstOrDefault(c =>
                                   string.Equals(c.Name.Trim(), categoryName.Trim(), StringComparison.OrdinalIgnoreCase))
                               ?? editor.AddCategory(categoryName.Trim());
                editor.AddPrompt(category.Id, suggestion.Title, suggestion.Description, suggestion.Text, suggestion.Tags, null);
                added = category.Name;
                return true;
            }
            catch (ArgumentException ex)
            {
                team.Service.RevertToLoaded();
                _dialogs.ShowMessage(ex.Message);
                return false;
            }
        }

        SaveTeamChange(team, Apply, () =>
        {
            onSaved();
            RefreshSuggestionCount();
            return $"Added to team prompts · {added}";
        });
    }

    // ---- Copy invite, team note, fill-in values, team check (the Team header's ⋯ menu) ----------------

    private void RaiseTeamNote()
    {
        OnPropertyChanged(nameof(TeamNoteLine));
        OnPropertyChanged(nameof(ShowTeamNoteLine));
    }

    /// <summary>Copy invite: the invite text with the clouds found on this PC, line breaks as Windows wants them.</summary>
    private void CopyInvite()
    {
        if (!IsTeamSide || _team?.Service.Source is not { } source) return;
        var text = TeamInvite.Build(source, _host.DetectCloudFolders()).Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (_host.CopyText(text, "Team invite", notifyInTray: false))
            ShowStatus("Invite copied. Paste it into an email or chat.", FeedbackDuration);
    }

    /// <summary>Note for everyone…: sets, changes or removes the note members see under the Team header.</summary>
    private void EditTeamNote()
    {
        if (!IsTeamSide || _teamSaving || _team is not { } team || !TeamLoaded || !team.Service.CanEdit) return;
        var dialog = new TeamNoteViewModel(team.Service.Team?.Note?.Text);
        _dialogs.ShowTeamNote(dialog);
        if (dialog.Choice == TeamNoteChoice.None) return;

        var save = dialog.Choice == TeamNoteChoice.Save;
        var text = dialog.NoteText;
        SaveTeamChange(team, () =>
        {
            if (team.Service.Team is not { } library) return false;
            library.Note = save ? new TeamNote(text, DateTimeOffset.Now) : null;
            return true;
        }, () =>
        {
            RaiseTeamNote();
            return save ? "Note saved for everyone" : "Note removed";
        });
    }

    private void DismissTeamNote()
    {
        if (_team?.Service.Team?.Note is not { } note) return;
        _host.Settings.SeenTeamNote = NoteHash(note.Text);
        _host.SaveSettings();
        RaiseTeamNote();
    }

    /// <summary>Fill-in values for everyone…: values every member starts with in a field of that name.</summary>
    private void EditTeamFillInValues()
    {
        if (!IsTeamSide || _teamSaving || _team is not { } team || !TeamLoaded || !team.Service.CanEdit) return;
        var dialog = new TeamFillInValuesViewModel(team.Library.Prompts.Select(p => p.Text),
            team.Service.Team?.FillInValues ?? new Dictionary<string, string>());
        if (!_dialogs.ShowTeamFillInValues(dialog)) return;

        var values = dialog.Result;
        SaveTeamChange(team, () =>
        {
            if (team.Service.Team is not { } library) return false;
            library.FillInValues = new Dictionary<string, string>(values);
            return true;
        }, () => "Fill-in values saved for everyone");
    }

    /// <summary>Check the team library: lists problems; choosing one opens it (in edit mode) on the Team side.</summary>
    private void CheckTeamLibrary()
    {
        if (!IsTeamSide || _team is not { } team || !TeamLoaded || !team.Service.CanEdit) return;
        var dialog = new TeamCheckViewModel(TeamHealthCheck.Run(team.Library));
        _dialogs.ShowTeamCheck(dialog);
        if (dialog.Chosen is { } issue) GoToIssue(issue);
    }

    private void GoToIssue(HealthIssue issue)
    {
        if (!IsTeamSide || _team is not { } team || !TeamLoaded) return;
        if (!ProceedOrDefer(() => GoToIssue(issue))) return;
        if (!team.IsEditing && team.Service.CanEdit) StartEditingTeam();
        if (!string.IsNullOrWhiteSpace(_searchText)) SearchText = "";

        var prompt = issue.PromptId is { } id ? team.Library.Prompts.FirstOrDefault(p => p.Id == id) : null;
        SelectCategoryNow(prompt?.CategoryId ?? issue.CategoryId);
        if (prompt is not null) OpenPrompt(prompt, byUser: true);
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
            PruneTeamState(); // pins of prompts that are gone don't count towards the cap (saved with the pin below)
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

    private TeamChangeModel? _openChange;

    /// <summary>An Updated team prompt that is open and has a kept copy of what was seen: the "What changed" link shows.</summary>
    public bool ShowWhatChanged => IsTeamSide && _editor is not null && _openChange is not null;

    /// <summary>"What changed" sits at the end of the lock line when there is one…</summary>
    public bool ShowWhatChangedInLockLine => ShowWhatChanged && LockLineText is not null;

    /// <summary>…and on its own line while the team library is being edited (no lock line).</summary>
    public bool ShowWhatChangedAlone => ShowWhatChanged && LockLineText is null;

    public IRelayCommand WhatChangedCommand { get; }
    public IRelayCommand SeeCopyChangesCommand { get; }
    public IRelayCommand UpdateMyCopyCommand { get; }
    public IRelayCommand KeepMineCommand { get; }

    /// <summary>The team prompt the open personal copy came from, when the team has changed it since the copy (or the
    /// last Keep mine / update); otherwise null.</summary>
    private Prompt? OutOfDateSource(out Prompt? mine)
    {
        mine = null;
        if (!IsYoursSide || _editor?.PromptId is not { } id) return null;
        mine = _personal.Library.Prompts.FirstOrDefault(p => p.Id == id);
        if (mine?.SourceTeamPromptId is not { } sourceId || mine.SourceFingerprint is null) return null;
        var source = TeamContent?.Prompts.FirstOrDefault(p => p.Id == sourceId);
        return source is not null && TeamUserState.ContentFingerprint(source) != mine.SourceFingerprint ? source : null;
    }

    /// <summary>The quiet line in a personal copy's editor: the team changed the prompt it was copied from.</summary>
    public bool ShowTeamUpdateBanner => OutOfDateSource(out _) is not null;

    private void WhatChanged()
    {
        if (_openChange is not { } change || _editor is null || !IsTeamSide) return;
        _dialogs.ShowChanges(_editor.Title, "Since you last looked", change);
        if (CurrentDocument.Library.Prompts.FirstOrDefault(p => p.Id == _editor.PromptId) is { } prompt)
            MarkOpenedSeen(prompt); // opening the changes counts as seeing them
    }

    private void SeeCopyChanges()
    {
        if (OutOfDateSource(out var mine) is not { } source || mine is null) return;
        var change = TeamChange.Between(mine, TeamChange.CategoryName(_personal.Library, mine),
            source, TeamChange.CategoryName(TeamContent!, source));
        _dialogs.ShowChanges(source.Title, "Your copy to the team version", change);
    }

    private void UpdateMyCopy()
    {
        if (OutOfDateSource(out var mine) is not { } source || mine is null || _editor is not { } editor) return;
        // Edited = the copy's saved content no longer matches what it was copied (or last updated) from, or unsaved edits.
        var changed = editor.IsDirty || TeamUserState.ContentFingerprint(mine) != mine.SourceFingerprint;
        if (changed && !_dialogs.ConfirmDestructive("Replace your changes with the team's version?", "Replace")) return;

        editor.Title = source.Title;
        editor.Description = source.Description;
        editor.Text = source.Text;
        editor.Tags.Clear();
        foreach (var tag in source.Tags ?? []) editor.Tags.Add(tag);
        if (!Save()) return; // the editor keeps the team's version as an unsaved change
        mine.SourceFingerprint = TeamUserState.ContentFingerprint(source);
        _personal.SaveLibraryChange();
        RaiseEditorState();
        ShowStatus("Updated from the team.", FeedbackDuration);
    }

    private void KeepMine()
    {
        if (OutOfDateSource(out var mine) is not { } source || mine is null) return;
        mine.SourceFingerprint = TeamUserState.ContentFingerprint(source);
        _personal.SaveLibraryChange();
        RaiseEditorState();
    }

    private void MarkAllSeen()
    {
        if (TeamContent is not { } team) return;
        TeamUserState.MarkAllSeen(_host.Settings, team);
        foreach (var teamPrompt in team.Prompts) _team?.Service.MarkSeen(teamPrompt.Id);
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
        _team?.Service.MarkSeen(prompt.Id);
        SaveTeamState();
        foreach (var row in Prompts.Where(r => r.Id == prompt.Id)) row.Marker = TeamMarker.None;
        RaiseMarkerState();
    }

    /// <summary>
    /// Drops pins (and team hides and markers) of prompts that are gone: against your prompts and the loaded team
    /// library, or an empty one when no team library is joined. While one is joined but not loaded nothing is
    /// dropped, so a team that is briefly unreachable keeps its pins. True when something changed (not saved yet).
    /// </summary>
    private bool PruneTeamState()
        => TeamUserState.Prune(_host.Settings, _personal.Library,
            TeamUserState.TeamForPrune(joined: TeamConnected, loaded: TeamContent));

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
        if (CurrentDocument.IsTeam) TrackTeamSave(fromPromptSave: false);
    }

    /// <summary>
    /// Waits (without blocking) for the team save just started, then applies its outcome on the UI thread: saved runs
    /// <paramref name="onSaved"/> and says "Saved for everyone"; refused (someone else changed the file) or failed
    /// shows why, with the library reloaded or put back to the copy on disk. The editor keeps whatever was typed; a
    /// refused change that didn't come from a prompt's Save (<paramref name="fromPromptSave"/> false) was undone by the
    /// reload, so the message asks for it again.
    /// </summary>
    private void TrackTeamSave(bool fromPromptSave, Action? onSaved = null)
    {
        if (_team?.PendingSave is not { } pending) return;
        _teamSaving = true;
        var baseHash = _team.BaseHash;
        pending.ContinueWith(task =>
        {
            var result = task.IsCompletedSuccessfully
                ? task.Result
                : new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't save the team library.");
            _dispatcher.BeginInvoke(() => FinishTeamSave(result, baseHash, fromPromptSave, onSaved));
        }, TaskScheduler.Default);
    }

    private void FinishTeamSave(TeamSaveResult result, string? baseHash, bool fromPromptSave, Action? onSaved)
    {
        _teamSaving = false;
        var next = _afterTeamSave;
        _afterTeamSave = null;
        if (_team is not { } team) return;

        switch (result.Status)
        {
            case TeamSaveStatus.Saved:
                team.Rebase();
                onSaved?.Invoke();
                ShowStatus("Saved for everyone", FeedbackDuration);
                break;
            case TeamSaveStatus.ChangedByOther when team.Service.CurrentHash != baseHash:
                team.Rebase(); // the service has re-read the file
                _dialogs.ShowMessage(fromPromptSave ? ChangedByOtherMessage : ChangeRedoMessage);
                break;
            default:
                // Failed, or refused and the file couldn't be read back: what wasn't written doesn't stay in memory
                // (a new prompt then can't be added twice), and the message says only what happened.
                team.Service.RevertToLoaded();
                _dialogs.ShowMessage(result.Message ?? "Couldn't save the team library.");
                break;
        }

        if (IsTeamSide) ReloadTeamSide();
        _host.RefreshQuickMenu();
        if (result.Status == TeamSaveStatus.Saved) next?.Invoke(); // what "Save changes?" interrupted
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
        // Another editor deleted the prompt being edited: what was typed stays, as a new prompt in its category (or the
        // first one when that is gone too), so nothing typed is thrown away.
        var library = CurrentDocument.Library;
        if (_editor is { IsDirty: true, PromptId: { } goneId } kept && library.Prompts.All(p => p.Id != goneId))
        {
            var categoryId = library.Categories.Any(c => c.Id == kept.CategoryId)
                ? kept.CategoryId
                : library.Categories.OrderBy(c => c.Name, NameOrder).FirstOrDefault()?.Id;
            kept.BecomeNew(categoryId);
            _selectedPrompt = null;
            OnPropertyChanged(nameof(SelectedPrompt));
        }

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
        if (_teamSaving || (ask && !ProceedOrDefer(() => StopEditingTeam()))) return false;

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
        if (!ProceedOrDefer(() => OpenTeamHistory(review))) return;
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
        RefreshSuggestionCount();
        OnPropertyChanged(nameof(PlaceholderValues)); // the team's fill-in values may have changed
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
        if (target is not null && target != CategoryItem.RecentId
            && _editor?.CategoryId is { } editorCategory && editorCategory != target)
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

    private PromptRow NewRow(Prompt prompt, bool isTeam, bool showTeamIcon,
        IReadOnlyList<(int Start, int Length)>? titleMatches = null,
        IReadOnlyList<(int Start, int Length)>? descriptionMatches = null)
    {
        var settings = _host.Settings;
        return new PromptRow(prompt, isTeam,
            isHidden: isTeam && settings.HiddenTeamPromptIds.Contains(prompt.Id),
            isPinned: settings.PinnedPromptIds.Contains(prompt.Id),
            marker: isTeam ? TeamUserState.MarkerFor(settings, prompt) : TeamMarker.None,
            showTeamIcon, titleMatches, descriptionMatches)
        { IsFeatured = isTeam && prompt.Featured };
    }

    private void RaiseEditorState()
    {
        OnPropertyChanged(nameof(ShowWhatChanged));
        OnPropertyChanged(nameof(ShowWhatChangedInLockLine));
        OnPropertyChanged(nameof(ShowWhatChangedAlone));
        OnPropertyChanged(nameof(ShowTeamUpdateBanner));
        OnPropertyChanged(nameof(LockLineText));
        OnPropertyChanged(nameof(PinLabel));
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(HideLabel));
        OnPropertyChanged(nameof(ShowFeatureToggle));
        OnPropertyChanged(nameof(FeatureLabel));
        OnPropertyChanged(nameof(EditorCategoryName));
        OnPropertyChanged(nameof(EditorCategoryEmoji));
        OnPropertyChanged(nameof(CanShowHistory));
        OnPropertyChanged(nameof(SendChoices));
        OnPropertyChanged(nameof(SendDefaultName));
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
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        AddStarterPromptsCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(ShowCopyToTeam));
        OnPropertyChanged(nameof(ShowSuggest));
        OnPropertyChanged(nameof(ShowSuggestionsButton));
        OnPropertyChanged(nameof(ShowTeamEditBar));
        OnPropertyChanged(nameof(TeamEditBlockedText));
        OnPropertyChanged(nameof(ShowTeamEditBlocked));
        OnPropertyChanged(nameof(ShowTeamMoreMenu));
        OnPropertyChanged(nameof(ShowTeamEditorItems));
        RaiseTeamNote();
        OnPropertyChanged(nameof(ShowConflictBar));
        OnPropertyChanged(nameof(ShowTeamBar));
        OnPropertyChanged(nameof(ShowBothTeamNotices));
        OnPropertyChanged(nameof(ShowEditButtons));
        OnPropertyChanged(nameof(ShowNewCategoryButton));
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
