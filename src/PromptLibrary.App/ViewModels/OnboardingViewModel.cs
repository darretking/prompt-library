using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Onboarding;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The first-run wizard: Welcome → Shortcut (try it, or change it) → Team (join or start a team library, or just
/// Next) → Backups (the shared picker in select-only mode; Next backs up to the selected folder) → Ready (start
/// with Windows, paste after copy). Finish applies step 5's choices; Skip and closing early keep the defaults. Both mark the first run done.
/// </summary>
public sealed class OnboardingViewModel : ObservableObject
{
    /// <summary>How long <c>Backed up to OneDrive.</c> stays readable before Next moves on.</summary>
    private static readonly TimeSpan BackedUpPause = TimeSpan.FromMilliseconds(800);

    private readonly AppHost _host;
    private readonly OnboardingFlow _flow = new();
    private readonly string? _folderAtStart;
    private BackupLocationPickerViewModel? _picker;

    private bool _isBusy;
    private bool _shortcutTried;
    private bool _shortcutChanged;
    private bool _isChangingShortcut;
    private string? _shortcutStatus;
    private bool _shortcutStatusIsError;
    private bool _startWithWindows;
    private bool _pasteAfterCopy;
    private bool _showFinishingBackup;

    public OnboardingViewModel(AppHost host)
    {
        _host = host;
        _folderAtStart = host.Settings.BackupFolder;
        _startWithWindows = host.Settings.StartWithWindows;
        _pasteAfterCopy = host.Settings.PasteAfterCopy;
        _isChangingShortcut = IsConflict; // the shortcut is taken: the capture box shows straight away
        Team = new TeamConnectionViewModel(host.Team);

        NextCommand = new AsyncRelayCommand(NextAsync, () => !IsBusy);
        BackCommand = new RelayCommand(() => Move(_flow.Back()), () => !IsBusy);
        SkipSetupCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        SkipBackupsCommand = new RelayCommand(() => Move(_flow.Next()), () => !IsBusy);
        ChangeShortcutCommand = new RelayCommand(StartChangingShortcut);
    }

    /// <summary>The wizard should close (Skip setup, or after Finish).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The capture box should start listening (Change shortcut, the conflict variant, a failed try).</summary>
    public event EventHandler? ListenRequested;

    public IAsyncRelayCommand NextCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand SkipSetupCommand { get; }
    public IRelayCommand SkipBackupsCommand { get; }
    public IRelayCommand ChangeShortcutCommand { get; }

    /// <summary>True after Finish (closing then has nothing more to do).</summary>
    public bool IsFinished { get; private set; }

    /// <summary>A backup is running (Next's, Try again, or a chosen folder): moving on or leaving waits for it.</summary>
    public bool IsBusy => _isBusy || _picker is { IsBusy: true };

    // Steps and footer
    public OnboardingStep Step => _flow.Step;
    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsShortcut => Step == OnboardingStep.Shortcut;
    public bool IsTeam => Step == OnboardingStep.Team;
    public bool IsBackups => Step == OnboardingStep.Backups;
    public bool IsReady => Step == OnboardingStep.Ready;
    public string StepText => _flow.StepText;
    public bool CanGoBack => _flow.CanGoBack;
    public string PrimaryText => _flow.PrimaryText;
    public bool ShowSkipSetup => _flow.ShowsSkipSetup;
    /// <summary>"Skip backups for now", until a backup has worked (then Next simply continues).</summary>
    public bool ShowSkipBackups => _flow.ShowsSkipBackups(backupsOn: _host.Settings.BackupFolder is not null);

    /// <summary>Esc or × was pressed while a backup runs: <c>Finishing the backup… one moment.</c> shows until it ends.</summary>
    public bool ShowFinishingBackup
    {
        get => _showFinishingBackup;
        private set => SetProperty(ref _showFinishingBackup, value);
    }

    // Step 2: shortcut
    public string Hotkey => _host.Settings.Hotkey;

    /// <summary>The shortcut in settings couldn't be registered (another app has it).</summary>
    public bool IsConflict => !_host.HotkeyRegistered;

    public IReadOnlyList<string> Keycaps => OnboardingText.Keycaps(Hotkey);
    public string KeycapsName => $"Current shortcut: {Hotkey}";
    public bool ShowKeycaps => !IsConflict;

    public string ShortcutHeading => IsConflict ? OnboardingText.InUseHeading(Hotkey) : "Open your prompts from any app";

    public string ShortcutBody => IsConflict
        ? OnboardingText.PickAnotherText(Hotkey)
        : _shortcutChanged ? "Saved. Try it now: press the shortcut." : "Try it now: press the shortcut.";

    /// <summary>The shortcut was pressed on step 2: the ✓ line shows.</summary>
    public bool ShortcutTried
    {
        get => _shortcutTried;
        private set => SetProperty(ref _shortcutTried, value);
    }

    public bool IsChangingShortcut
    {
        get => _isChangingShortcut;
        private set
        {
            if (SetProperty(ref _isChangingShortcut, value))
            {
                OnPropertyChanged(nameof(ShowChangeLink));
                OnPropertyChanged(nameof(ShowCaptureBox));
            }
        }
    }

    public bool ShowChangeLink => !_isChangingShortcut;

    /// <summary>The capture box: while changing, and always when the shortcut is taken (it then says "Not set").</summary>
    public bool ShowCaptureBox => _isChangingShortcut || IsConflict;

    /// <summary>What the idle capture box shows: the shortcut that works, or <c>Not set</c>.</summary>
    public string CaptureBoxText => IsConflict ? "Not set" : Hotkey;

    /// <summary>Under the capture box: the "use Ctrl or Alt" hint or the "already used" error.</summary>
    public string? ShortcutStatus
    {
        get => _shortcutStatus;
        private set => SetProperty(ref _shortcutStatus, value);
    }

    public bool ShortcutStatusIsError
    {
        get => _shortcutStatusIsError;
        private set => SetProperty(ref _shortcutStatusIsError, value);
    }

    // Step 3: team (Join and Start open the same dialogs as Settings; Next continues without a team library)
    public TeamConnectionViewModel Team { get; }

    // Step 5: ready
    public string ReadyText => OnboardingText.ReadyText(Hotkey, !IsConflict, _pasteAfterCopy);
    public string ShortcutSummary => OnboardingText.ShortcutSummary(Hotkey, !IsConflict);
    public string BackupSummary => OnboardingText.BackupSummary(_host.Settings.BackupFolder, _host.Settings.BackupProviderName);

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool PasteAfterCopy
    {
        get => _pasteAfterCopy;
        set
        {
            if (SetProperty(ref _pasteAfterCopy, value))
                OnPropertyChanged(nameof(ReadyText));
        }
    }

    /// <summary>Leaving now asks first: a backup folder was chosen in this wizard and the user is past step 1.</summary>
    public bool AsksBeforeLeaving
        => _flow.AsksBeforeLeaving(!BackupLocation.SameFolder(_folderAtStart, _host.Settings.BackupFolder));

    /// <summary>Step 4's picker; its busy state holds Next back.</summary>
    public void AttachPicker(BackupLocationPickerViewModel picker)
    {
        _picker = picker;
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackupLocationPickerViewModel.IsBusy))
                NotifyBusyChanged();
            else if (e.PropertyName == nameof(BackupLocationPickerViewModel.ResultText))
                OnPropertyChanged(nameof(ShowSkipBackups)); // a backup that worked turns backups on
        };
    }

    /// <summary>Leaving (Esc or ×) was refused because a backup is running: say why until it has finished.</summary>
    public void OnLeaveWhileBusy() => ShowFinishingBackup = IsBusy;

    /// <summary>
    /// The global shortcut was pressed. On step 2 the wizard takes it (the ✓ line shows) and true is returned;
    /// on any other step the menu opens as usual.
    /// </summary>
    public bool OnShortcutPressed()
    {
        if (!IsShortcut || IsChangingShortcut) return false;
        ShortcutTried = true;
        return true;
    }

    /// <summary>A new shortcut was pressed in the capture box: register it at once; if it's taken, say so.</summary>
    public void ApplyShortcut(string shortcut)
    {
        if (!_host.ApplyHotkey(shortcut))
        {
            SetShortcutStatus(OnboardingText.CaptureInUseText(shortcut), isError: true);
            ListenRequested?.Invoke(this, EventArgs.Empty); // ready for the next try
            return;
        }

        _host.SaveSettings();
        _shortcutChanged = true;
        ShortcutTried = false;
        IsChangingShortcut = false;
        SetShortcutStatus(null, isError: false);
        OnPropertyChanged(string.Empty); // keycaps, heading and summaries all follow the shortcut
    }

    /// <summary>
    /// The capture box stopped listening (Esc, Tab, a new shortcut, or leaving the step). Unless it is listening
    /// again for another try, changing is over: the box shows the shortcut that works (or "Not set") and the hint
    /// or error goes, so pressing the real shortcut on step 2 shows the ✓ line.
    /// </summary>
    public void OnCaptureEnded(bool listeningAgain)
    {
        if (listeningAgain || !IsChangingShortcut) return;
        IsChangingShortcut = false;
        SetShortcutStatus(null, isError: false);
    }

    /// <summary>Keys that can't be a shortcut were pressed in the capture box.</summary>
    public void ShowShortcutHint(string hint) => SetShortcutStatus(hint, isError: false);

    /// <summary>
    /// Skip setup, Esc or ×: the choices so far are kept (on step 5 including its checkboxes), everything else stays
    /// at the defaults, and the wizard won't open by itself again.
    /// </summary>
    public void Leave()
    {
        _flow.ApplyLeave(_host.Settings, _startWithWindows, _pasteAfterCopy);
        if (IsReady)
            _host.ApplyStartupRegistration();
        _host.SaveSettings();
    }

    private async Task NextAsync()
    {
        if (IsBackups && _picker is { SelectedCard.IsInUse: false } picker)
        {
            SetBusy(true);
            var backedUp = await picker.BackUpSelectedAsync();
            if (backedUp)
                await Task.Delay(BackedUpPause); // let "Backed up to OneDrive." be read
            SetBusy(false);
            if (!backedUp) return; // the failure and Try again show on the page
        }

        if (_flow.IsLastStep)
            Finish();
        else
            Move(_flow.Next());
    }

    private void Finish()
    {
        OnboardingFlow.ApplyFinish(_host.Settings, _startWithWindows, _pasteAfterCopy);
        _host.ApplyStartupRegistration();
        _host.SaveSettings();
        IsFinished = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Move(bool moved)
    {
        if (!moved) return;
        OnPropertyChanged(string.Empty);
        if (IsShortcut && IsChangingShortcut)
            ListenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StartChangingShortcut()
    {
        IsChangingShortcut = true;
        SetShortcutStatus(null, isError: false);
        ListenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetShortcutStatus(string? text, bool isError)
    {
        ShortcutStatus = text;
        ShortcutStatusIsError = isError;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        NotifyBusyChanged();
    }

    private void NotifyBusyChanged()
    {
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        SkipSetupCommand.NotifyCanExecuteChanged();
        SkipBackupsCommand.NotifyCanExecuteChanged();
        if (!IsBusy)
            ShowFinishingBackup = false;
    }
}
