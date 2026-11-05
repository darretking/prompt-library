using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PromptLibrary.App.Hotkeys;
using PromptLibrary.App.Startup;
using PromptLibrary.App.Tray;
using PromptLibrary.App.Views;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Hotkeys;
using PromptLibrary.Core.Menu;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.App;

/// <summary>
/// Owns the running app: library and settings, tray icon, global hotkey, backups and windows.
/// Created once on the UI thread by <see cref="App"/>.
/// </summary>
public sealed class AppHost : IDisposable
{
    private const string AppName = "Prompt Library";
    private const string DefaultHotkey = "Ctrl+Alt+P";
    private const string BackupsOffText = "Backups are off.";
    private const string PromptSaveFailedAdvice = "Your changes are still open — try again.";
    private const string ChangeSaveFailedAdvice = "Your change is kept and will be saved when it can.";

    private static readonly TimeSpan BackupCheckInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan FirstBackupCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PickedFolderBackupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ExitBackupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionEndBackupTimeout = TimeSpan.FromSeconds(5);

    private readonly ClipboardWriter _clipboard =
        new(text => System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText), Thread.Sleep);

    private readonly BackupService _backups = new(() => DateTime.Now);

    private GlobalHotkey? _hotkey;
    private CloseRequestWindow? _closeRequestWindow;
    private HotkeyGesture? _activeGesture;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private OnboardingWindow? _onboardingWindow;
    private DispatcherTimer? _backupTimer;
    private DispatcherTimer? _firstBackupTimer;
    private bool _started;
    private bool _disposed;
    private bool _exiting;

    // Set while the app exits without asking (installer, logoff): error messages are not shown then.
    private bool _quietExit;

    // Set (0 → 1) by the first OnCrash.
    private int _crashed;

    // One backup (or restore) at a time. Scheduled backups skip if one is running; the others wait for it.
    private readonly SemaphoreSlim _backupGate = new(1, 1);

    // A backup to a newly picked folder that outlived its 15 s timeout and is still running.
    private Task? _slowPickedFolderBackup;

    // Backups paused: the folder (and its parent) can't be reached. Scheduled and exit backups skip quietly;
    // the tray says so at most once per run.
    private bool _backupsPaused;
    private bool _pausedNoticeShown;
    private int _backupStateVersion;

    public Library Library { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public TrayIcon Tray { get; private set; } = null!;

    /// <summary>
    /// The main window's warning bar text: backups off, backups paused, or null when backups are fine.
    /// </summary>
    public string? BackupWarning { get; private set; }

    /// <summary>The full backup folder while backups are paused (the bar's tooltip), otherwise null.</summary>
    public string? BackupWarningDetail { get; private set; }

    /// <summary>True once Windows has said the session is ending; no questions may be asked then.</summary>
    public bool IsSessionEnding { get; private set; }

    /// <summary>True once the app has started exiting; windows then close without their usual questions.</summary>
    public bool IsExiting => _exiting;

    /// <summary>True while nothing may be asked or shown: Windows is ending the session or the installer closes the app.</summary>
    public bool AsksNothing => IsSessionEnding || _quietExit;

    /// <summary>True once <see cref="Start"/> has set up the tray (an exit can only be handled from then on).</summary>
    public bool IsRunning => _started;

    /// <summary>True when the shortcut in <see cref="Settings"/> is the one registered with Windows.</summary>
    public bool HotkeyRegistered => _activeGesture is not null && _activeGesture.ToString() == Settings.Hotkey;

    public event EventHandler? LibraryChanged;

    /// <summary>Raised when <see cref="BackupWarning"/> changes.</summary>
    public event EventHandler? BackupStateChanged;

    /// <summary>Loads data and sets up the tray, hotkey and backups. False if the app must exit instead.</summary>
    public bool Start()
    {
        Settings = LoadSettings();

        string? restoredFrom = null;
        var libraryResult = JsonStore.LoadLibrary(Paths.LibraryFile);
        switch (libraryResult.Status)
        {
            case LoadStatus.Ok:
                Library = libraryResult.Value!;
                break;
            case LoadStatus.Missing:
                Library = SeedLibrary.Create(DateTimeOffset.Now);
                WriteLibraryFile(); // on failure the message is shown and the app runs on in memory
                break;
            case LoadStatus.Unreadable:
                restoredFrom = HandleUnreadableLibrary(Paths.LibraryFile, libraryResult.Error ?? "Unknown error.");
                if (restoredFrom is null)
                    return false;
                break;
        }

        Tray = new TrayIcon(TooltipFor(Settings.Hotkey));
        Tray.PromptClicked += (_, pick) => OnPromptPicked(pick);
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.SettingsRequested += (_, _) => OpenSettings();
        Tray.DoubleClicked += (_, _) => ShowMainWindow();
        Tray.ExitRequested += (_, _) => ExitApp();
        Tray.Rebuild(QuickMenuBuilder.Build(Library));
        _started = true;

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => OnHotkeyPressed();

        // The installer closes a running copy before an upgrade or uninstall by sending it WM_CLOSE.
        _closeRequestWindow = new CloseRequestWindow();
        _closeRequestWindow.CloseRequested += (_, _) => ExitApp(ExitReason.External);

        var hotkeyRegistered = ApplyHotkey(Settings.Hotkey);

        ApplyStartupRegistration();
        RefreshBackupState();
        StartBackupSchedule();

        if (restoredFrom is not null)
        {
            ShowMainWindow();
            Tray.ShowMessage(AppName, $"Library restored from {restoredFrom}.");
        }

        if (!Settings.FirstRunDone)
        {
            // The wizard replaces the first-run tray message; if the shortcut is taken, its step 2 says so (so the
            // tray message and Settings stay out of the way).
            OpenOnboarding();
        }
        else if (!hotkeyRegistered)
        {
            ShowHotkeyConflictMessage();
            OpenSettings();
        }

        if (Settings.LastBackupUnfinished)
        {
            Tray.ShowMessage(AppName, "The last backup didn't finish.");
            Settings.LastBackupUnfinished = false;
            SaveSettings();
        }

        if (Settings.RecoveredEditsFile is { } recovered)
        {
            Tray.ShowMessage(AppName, $"Some unsaved changes were recovered to {recovered}.");
            Settings.RecoveredEditsFile = null;
            SaveSettings();
        }

        if (Settings.RecoveredLibraryFile is { } recoveredLibrary)
        {
            Tray.ShowMessage(AppName, $"Some library changes couldn't be saved last time. They were recovered to {recoveredLibrary}.");
            Settings.RecoveredLibraryFile = null;
            SaveSettings();
        }

        return true;
    }

    /// <summary>The main window was closed (hidden): the first time ever, the tray says the app is still running.</summary>
    public void OnMainWindowHidden()
    {
        if (Settings.HideToTrayNoticeShown || _exiting || IsSessionEnding) return;
        Tray.ShowMessage(AppName, $"Still running. Press {Settings.Hotkey} or click the tray icon.");
        Settings.HideToTrayNoticeShown = true;
        SaveSettings();
    }

    /// <summary>
    /// Saves the library after a prompt's Save, tells listeners and refreshes the quick menu. If the file can't be
    /// written the user is told (to try again), the in-memory library is kept and false is returned.
    /// </summary>
    public bool SaveLibrary() => SaveLibrary(PromptSaveFailedAdvice);

    /// <summary>
    /// As <see cref="SaveLibrary()"/>, for a change that has no Save button to try again with (adding, renaming or
    /// deleting a category, deleting a prompt). On failure the change stays in memory and is written with the next
    /// save that works, or at exit (see <see cref="LibraryUnwritten"/>).
    /// </summary>
    public bool SaveLibraryChange() => SaveLibrary(ChangeSaveFailedAdvice);

    /// <summary>
    /// True while the in-memory library has changes that library.json doesn't (the last write failed). The next
    /// write that works clears it; the exit tries once more and otherwise keeps a copy in <c>Recovered\</c>.
    /// </summary>
    public bool LibraryUnwritten { get; private set; }

    private bool SaveLibrary(string failureAdvice)
    {
        var saved = WriteLibraryFile(failureAdvice);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        RefreshQuickMenu();
        return saved;
    }

    /// <summary>Rebuilds the quick menu from the in-memory library (e.g. after an unsaved change was rolled back).</summary>
    public void RefreshQuickMenu() => Tray.Rebuild(QuickMenuBuilder.Build(Library));

    /// <summary>Saves settings; if the file can't be written the user is told and false is returned.</summary>
    public bool SaveSettings()
    {
        try
        {
            JsonStore.SaveSettings(Paths.SettingsFile, Settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't save your settings: {WithoutFinalPeriod(ex.Message)}. Try again.");
            return false;
        }
    }

    /// <summary>
    /// A prompt was picked from the quick menu: it is copied, and when the menu came from the global shortcut (and
    /// <see cref="AppSettings.PasteAfterCopy"/> is on) it is also pasted into the app the user was typing in. The
    /// tray confirms the copy unless the paste worked; if pasting wasn't possible it says to press Ctrl+V.
    /// </summary>
    private async void OnPromptPicked(PromptPick pick)
    {
        if (_exiting) return;
        var prompt = Library.Prompts.FirstOrDefault(p => p.Id == pick.Id);
        if (prompt is null) return;
        if (!_clipboard.TryCopy(prompt.Text))
        {
            Tray.ShowMessage(AppName, "Couldn't copy — clipboard busy. Try again.", isError: true);
            return;
        }

        var hasTarget = AutoPaster.IsOtherAppWindow(pick.ReturnTo, out var isOurs);
        var outcome = PasteDecision.ShouldPaste(Settings.PasteAfterCopy, fromShortcutMenu: pick.ReturnTo != IntPtr.Zero,
            hasTarget, isOurs)
            // The tray gives that window its focus back as the menu closes; the paster waits until it is really in
            // front (asking again itself) before it sends Ctrl+V.
            ? await AutoPaster.PasteAsync(pick.ReturnTo)
            : PasteOutcome.NotAttempted;

        if (!_disposed && PasteDecision.CopiedMessage(prompt.Title, outcome) is { } message)
            Tray.ShowMessage(AppName, message);
    }

    /// <summary>
    /// Copies <paramref name="text"/> (normalised to CRLF) and shows <c>Copied: title</c> in the tray.
    /// False if the clipboard stayed busy.
    /// </summary>
    public bool CopyText(string text, string title)
    {
        if (_clipboard.TryCopy(text))
        {
            Tray.ShowMessage(AppName, PasteDecision.CopiedMessage(title, PasteOutcome.NotAttempted)!);
            return true;
        }

        Tray.ShowMessage(AppName, "Couldn't copy — clipboard busy. Try again.", isError: true);
        return false;
    }

    /// <summary>Shows the one main window, creating it the first time.</summary>
    public void ShowMainWindow()
    {
        if (_exiting) return; // the window is closed and the app is waiting for its exit backup
        if (_mainWindow?.IsAskingToClose == true)
        {
            // Its "Save changes?" is open: that answer decides whether the window stays.
            _mainWindow.ActivateCloseQuestion();
            return;
        }
        if (_mainWindow is null)
            _mainWindow = new MainWindow(this);
        else if (!_mainWindow.IsVisible)
            _mainWindow.ViewModel.RefreshAll(); // pick up anything that changed while it was hidden

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>
    /// Opens the Settings window, or brings it to the front if it is already open. With
    /// <paramref name="focusBackups"/> the first backup folder card gets the focus ("Set up backups").
    /// </summary>
    public void OpenSettings(bool focusBackups = false)
    {
        if (_exiting) return;
        if (_settingsWindow is null)
        {
            var owner = VisibleMainWindow;
            _settingsWindow = new SettingsWindow(this)
            {
                Owner = owner,
                ShowInTaskbar = owner is null,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;

        _settingsWindow.Activate();
        if (focusBackups)
            _settingsWindow.FocusBackups();
    }

    /// <summary>
    /// Opens the onboarding wizard (first run, or <c>Help → Setup guide…</c>), or brings it to the front if it is
    /// already open. Over the main window when that is showing; otherwise centred on the screen, in the taskbar.
    /// </summary>
    public void OpenOnboarding()
    {
        if (_exiting) return;
        if (_onboardingWindow is null)
        {
            var owner = VisibleMainWindow;
            _onboardingWindow = new OnboardingWindow(this)
            {
                Owner = owner,
                ShowInTaskbar = owner is null,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            };
            _onboardingWindow.Closed += (_, _) =>
            {
                var finished = _onboardingWindow?.ViewModel.IsFinished == true;
                _onboardingWindow = null;
                // Left before Finish (step 4 explains a missing shortcut): say why the shortcut doesn't work.
                if (!finished && !_exiting && !IsSessionEnding && !HotkeyRegistered)
                    ShowHotkeyConflictMessage();
            };
            _onboardingWindow.Show();
        }
        else if (_onboardingWindow.WindowState == WindowState.Minimized)
            _onboardingWindow.WindowState = WindowState.Normal;

        _onboardingWindow.Activate();
    }

    private void ShowHotkeyConflictMessage()
        => Tray.ShowMessage(AppName, $"Shortcut {Settings.Hotkey} is in use by another app. Pick another in Settings.", isError: true);

    /// <summary><c>Help → About Prompt Library</c>: the name, version and a link to where the prompts are stored.</summary>
    public void ShowAbout()
        => AboutDialog.Open(VisibleMainWindow, typeof(AppHost).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            Paths.DataFolder);

    /// <summary>Opens the Restore window (over Settings or the main window when one is open).</summary>
    public void OpenRestore()
    {
        if (_exiting) return;
        Window? owner = _settingsWindow is { IsVisible: true } ? _settingsWindow : VisibleMainWindow;
        var window = new RestoreWindow(this, isStartup: false)
        {
            Owner = owner,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        if (window.ShowDialog() == true)
            ShowMainWindow(); // show the restored library
    }

    /// <summary>
    /// Before a restore replaces the library: offers to save unsaved edits in the main window and warns about
    /// changes that couldn't be written. False if the user cancelled.
    /// </summary>
    public bool ConfirmUnsavedChangesBeforeRestore()
        => _mainWindow is null || _mainWindow.ViewModel.ConfirmBeforeRestore();

    /// <summary>
    /// Re-checks (off the UI thread) whether the backup folder can be reached (main window activated). When a
    /// paused folder is back, the bar clears and the skipped backup runs.
    /// </summary>
    public async void RecheckBackups()
    {
        if (!_started || _disposed) return;
        var wasPaused = _backupsPaused;
        var paused = await RefreshBackupStateAsync();
        if (wasPaused && !paused && !_disposed)
            await RunBackupAsync(BackupTrigger.Daily);
    }

    /// <summary>
    /// Backs up library.json to the backup folder if <paramref name="trigger"/> calls for it. The file work runs off
    /// the UI thread; the result is applied back on it. Success records the time and hash in settings; Manual says
    /// <c>Backup saved.</c> in the tray and a failure says <c>Backup failed: …</c> (unless
    /// <paramref name="showInTray"/> is false because the caller shows the result itself). Returns the result, or
    /// null when no backup was attempted. A scheduled backup is skipped while another backup is running.
    /// </summary>
    public async Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true)
    {
        var folder = Settings.BackupFolder;
        if (folder is null)
        {
            // "Back up now" with backups off: take the user to where they can turn them on.
            if (trigger == BackupTrigger.Manual && showInTray)
                OpenSettings();
            return null;
        }

        if (trigger == BackupTrigger.Daily)
        {
            if (!await _backupGate.WaitAsync(0)) return null;
        }
        else
            await _backupGate.WaitAsync();

        try
        {
            var job = NewBackupJob(trigger, folder);
            var outcome = await Task.Run(() => RunBackupJob(job));
            return ApplyBackupOutcome(job, outcome, showInTray);
        }
        finally
        {
            _backupGate.Release();
        }
    }

    /// <summary>
    /// Backs up to <paramref name="folder"/> (off the UI thread, at most 15 s) and only if that works makes it the
    /// backup folder; on failure the previous folder stays. Used by the backup location picker. If the folder
    /// doesn't answer in time the copy goes on in the background (see <see cref="SlowPickedFolderBackup"/>).
    /// </summary>
    public async Task<BackupResult> BackUpToNewFolderAsync(string folder, string? providerName)
    {
        var keep = Settings.BackupsToKeep;
        var backup = Task.Run(() =>
        {
            var result = _backups.BackupNow(Paths.LibraryFile, folder, keep);
            // A technical "not available" error becomes the friendly one (e.g. "Is Google Drive signed in?").
            return !result.Success && BackupLocation.IsUnavailable(folder, Directory.Exists)
                ? result with
                {
                    Error = BackupLocation.UnavailableText(folder, providerName),
                    Reason = BackupFailureReason.Unavailable,
                }
                : result;
        });
        var finished = await Task.WhenAny(backup, Task.Delay(PickedFolderBackupTimeout));

        BackupResult result;
        try
        {
            if (finished == backup)
                result = await backup;
            else
            {
                _slowPickedFolderBackup = backup;
                result = new BackupResult(false, null, null, "The folder didn't respond within 15 seconds.",
                    BackupFailureReason.TimedOut);
            }
        }
        catch (Exception ex)
        {
            result = new BackupResult(false, null, null, ex.Message, BackupFailureText.Classify(ex));
        }
        if (!result.Success)
            return result;

        Settings.BackupFolder = folder;
        Settings.BackupProviderName = providerName;
        Settings.LastBackupAt = DateTimeOffset.Now;
        Settings.LastBackedUpLibraryHash = result.Hash;
        SaveSettings();
        _pausedNoticeShown = false;
        RefreshBackupState();
        return result;
    }

    /// <summary>
    /// The backup to a newly picked folder that timed out but is still running, or null. Its "Try again" waits
    /// for it, so two copies to the same folder don't run at once.
    /// </summary>
    public Task? SlowPickedFolderBackup => _slowPickedFolderBackup is { IsCompleted: false } running ? running : null;

    /// <summary>Backups in <paramref name="folder"/>, newest first (empty if the folder can't be read).</summary>
    public IReadOnlyList<BackupInfo> ListBackups(string folder) => _backups.List(folder);

    /// <summary>
    /// Restores <paramref name="backup"/> and loads it (the file work runs off the UI thread). The current library is
    /// saved first as a safety copy, in the configured backup folder if it is set and available, otherwise in
    /// <c>%APPDATA%\PromptLibrary\Safety backups</c> (kept to 10). The folder the backup came from is never written
    /// to or pruned unless it is the configured one. After startup the windows and quick menu are refreshed and the
    /// tray says <c>Library restored from &lt;date&gt;.</c> Returns null on success, otherwise the error to show.
    /// </summary>
    public async Task<string?> RestoreBackupAsync(BackupInfo backup)
    {
        var configured = Settings.BackupFolder;
        var keepConfigured = Settings.BackupsToKeep;

        await _backupGate.WaitAsync(); // not while a backup is copying library.json
        (BackupResult Result, LoadResult<Library>? Loaded) outcome;
        try
        {
            outcome = await Task.Run(() =>
            {
                var safetyFolder = BackupLocation.SafetyFolder(configured, Paths.SafetyBackupsFolder, Directory.Exists);
                var keep = safetyFolder == Paths.SafetyBackupsFolder ? BackupLocation.SafetyBackupsToKeep : keepConfigured;
                var result = _backups.Restore(backup.Path, Paths.LibraryFile, safetyFolder, keep);
                return (result, result.Success ? JsonStore.LoadLibrary(Paths.LibraryFile) : null);
            });
        }
        finally
        {
            _backupGate.Release();
        }

        if (!outcome.Result.Success)
            return outcome.Result.Error;
        var loaded = outcome.Loaded!;
        if (loaded.Status != LoadStatus.Ok)
            return $"The backup was restored but can't be read: {loaded.Error}";

        Library = loaded.Value!;
        LibraryUnwritten = false; // memory is what is on disk again (changes that weren't written were warned about)
        if (outcome.Result.BackupPath is { } preRestoreCopy)
        {
            BackupLocation.RememberPreRestoreCopy(Settings, preRestoreCopy); // labelled in the Restore window
            SaveSettings();
        }
        if (_started)
        {
            var restored = $"Library restored from {BackupLocation.RelativeTimeText(backup.Timestamp, DateTime.Now, midSentence: true)}.";
            LibraryChanged?.Invoke(this, EventArgs.Empty);
            RefreshQuickMenu();
            // Created if need be (shown after the Restore window closes), so it can say so too.
            (_mainWindow ??= new MainWindow(this)).ViewModel.ReloadAfterRestore(restored);
            Tray.ShowMessage(AppName, restored);
        }
        return null;
    }

    /// <summary>
    /// Registers <paramref name="text"/> as the global shortcut and records it in <see cref="Settings"/>
    /// (the caller saves). If another app owns it, the previous shortcut stays and false is returned;
    /// the caller tells the user.
    /// </summary>
    public bool ApplyHotkey(string text)
    {
        if (_hotkey is null || !HotkeyGesture.TryParse(text, out var gesture))
            return false;

        if (!_hotkey.Register(gesture!))
        {
            if (_activeGesture is not null)
                _hotkey.Register(_activeGesture);
            return false;
        }

        _activeGesture = gesture;
        Settings.Hotkey = gesture!.ToString();
        Tray.ToolTipText = TooltipFor(Settings.Hotkey);
        return true;
    }

    /// <summary>True if <paramref name="text"/> could be registered right now (nothing is kept registered).</summary>
    public bool IsShortcutFree(string text)
        => _hotkey is not null && HotkeyGesture.TryParse(text, out var gesture) && _hotkey.IsFree(gesture!);

    /// <summary>Lets go of the global shortcut while the user types a new one (so pressing it reaches the box).</summary>
    public void SuspendHotkey() => _hotkey?.Unregister();

    /// <summary>Takes the active shortcut back after <see cref="SuspendHotkey"/>.</summary>
    public void ResumeHotkey()
    {
        if (_hotkey is not null && _activeGesture is not null)
            _hotkey.Register(_activeGesture);
    }

    /// <summary>Writes the "start with Windows" entry from <see cref="Settings"/> (skipped in Debug builds).</summary>
    public void ApplyStartupRegistration()
    {
#if DEBUG
        // A Debug build must not register bin\Debug to start at login.
        System.Diagnostics.Debug.WriteLine(
            $"Debug build: skipped startup registration (StartWithWindows = {Settings.StartWithWindows}).");
#else
        try
        {
            StartupRegistration.Apply(Settings.StartWithWindows, Environment.ProcessPath!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // Not being able to write the Run key should not stop the app from working.
        }
#endif
    }

    /// <summary>
    /// Works out the backup warning (off / paused / none) from the current settings and tells listeners if it
    /// changed. Whether the folder can be reached is checked off the UI thread (a signed-out cloud drive or a
    /// network folder can be slow to answer).
    /// </summary>
    public void RefreshBackupState() => _ = RefreshBackupStateAsync();

    /// <summary>As <see cref="RefreshBackupState"/>; the result is true when backups are paused.</summary>
    private async Task<bool> RefreshBackupStateAsync()
    {
        var version = ++_backupStateVersion;
        var folder = Settings.BackupFolder;

        if (folder is null)
        {
            _backupsPaused = false;
            SetBackupWarning(BackupsOffText, null);
            return false;
        }

        var unavailable = await Task.Run(() => BackupLocation.IsUnavailable(folder, Directory.Exists));
        if (version != _backupStateVersion) return _backupsPaused; // a newer check has taken over
        ApplyAvailability(folder, unavailable);
        return unavailable;
    }

    /// <summary>
    /// Sets the paused state and bar for <paramref name="folder"/>. When paused, the cloud provider is looked up in
    /// the background to complete the message.
    /// </summary>
    private void ApplyAvailability(string folder, bool unavailable)
    {
        if (!BackupLocation.SameFolder(folder, Settings.BackupFolder)) return; // the folder changed meanwhile

        _backupsPaused = unavailable;
        if (!unavailable)
        {
            SetBackupWarning(null, null);
            return;
        }

        SetBackupWarning(BackupLocation.PausedMessage(folder, Settings.BackupProviderName), folder);
        _ = CompletePausedMessageAsync(folder, _backupStateVersion);
    }

    /// <summary>Detected cloud folders (OneDrive, Google Drive, …). Slow-ish: call it off the UI thread.</summary>
    public static IReadOnlyList<CloudFolder> DetectCloudFolders()
    {
#if DEBUG
        // Lets a developer see the "nothing detected" state on a PC that has cloud folders.
        if (Environment.GetEnvironmentVariable("PROMPTLIBRARY_DEBUG_NO_CLOUDS") == "1")
            return [];
#endif
        return CloudFolderDetector.Detect(new SystemCloudProbe());
    }

    /// <summary>
    /// Quits the app. For <see cref="ExitReason.User"/> unsaved edits in the main window are offered for saving first
    /// (Cancel stays); for <see cref="ExitReason.External"/> nothing is asked (see <see cref="KeepUnsavedWorkWithoutAsking"/>).
    /// Then the windows close and the library is backed up if it changed, waiting at most 10 s. If the backup hasn't
    /// finished by then the app exits anyway and the next start says <c>The last backup didn't finish.</c>
    /// Only the first call counts.
    /// </summary>
    public async void ExitApp(ExitReason reason = ExitReason.User)
    {
        if (_exiting) return;
        if (reason == ExitReason.External)
            KeepUnsavedWorkWithoutAsking();
        else
        {
            // A close question is open ("Save changes?", "Leave setup?"): it is answered first, not asked twice.
            if (_mainWindow?.IsAskingToClose == true)
            {
                _mainWindow.ActivateCloseQuestion();
                return;
            }
            if (_onboardingWindow?.IsAskingToClose == true)
            {
                _onboardingWindow.ActivateCloseQuestion();
                return;
            }
            if (_mainWindow is { IsVisible: true } && !_mainWindow.ViewModel.ConfirmDiscardOrSave())
                return;
        }

        // From here nothing can reopen a window or start a copy: the tray icon, shortcut and timers go first
        // (the backup result is still recorded in settings; it just isn't announced).
        _exiting = true;
        Dispose();
        _settingsWindow?.Close();
        _onboardingWindow?.CloseForExit();
        _mainWindow?.CloseForExit();
        KeepUnwrittenLibrary(); // before the exit backup, so it backs up what was written

        var backup = RunBackupAsync(BackupTrigger.OnExit);
        if (await Task.WhenAny(backup, Task.Delay(ExitBackupTimeout)) != backup)
            RecordUnfinishedBackup();

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Windows is logging off or shutting down: ask nothing more, try the exit backup (at most 5 s, as Windows
    /// won't wait long) and clean up now.
    /// </summary>
    public void OnSessionEnding()
    {
        if (IsSessionEnding) return;
        IsSessionEnding = true;
        if (!_exiting && _started && !_disposed)
        {
            KeepUnsavedWorkWithoutAsking();
            KeepUnwrittenLibrary();
        }

        if (!_exiting && _started && !_disposed && Settings.BackupFolder is { } folder)
        {
            // Blocking on purpose: the session may end as soon as this returns. The job needs nothing from the UI
            // thread, so waiting here can't deadlock.
            // One copy at a time, also here. (A scheduled backup that is running can't hand the gate back while this
            // thread is blocked, as it finishes on this thread; it then counts as unfinished.)
            var waited = System.Diagnostics.Stopwatch.StartNew();
            if (!_backupGate.Wait(SessionEndBackupTimeout))
                RecordUnfinishedBackup();
            else
            {
                try
                {
                    var job = NewBackupJob(BackupTrigger.OnExit, folder);
                    var work = Task.Run(() => RunBackupJob(job));
                    var left = SessionEndBackupTimeout - waited.Elapsed;
                    if (left > TimeSpan.Zero && work.Wait(left))
                        ApplyBackupOutcome(job, work.Result, showInTray: false);
                    else
                        RecordUnfinishedBackup();
                }
                finally
                {
                    _backupGate.Release();
                }
            }
        }

        Dispose();
    }

    /// <summary>
    /// For an exit nobody can be asked about (the installer closing the app, Windows logging off): unsaved edits in
    /// the main window are saved when they are complete and can be written; otherwise they go to
    /// <c>Recovered\recovered-&lt;time&gt;.txt</c> and the next start says where. An open setup wizard is left,
    /// keeping the choices made so far. Error messages are not shown from here on.
    /// </summary>
    private void KeepUnsavedWorkWithoutAsking()
    {
        _quietExit = true;

        if (_mainWindow?.ViewModel.SaveForExit() is { } unsaved)
        {
            try
            {
                Settings.RecoveredEditsFile = ExitEdits.WriteRecoveryFile(Paths.RecoveredFolder, unsaved, DateTime.Now);
                SaveSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more can be done without asking; the edits are lost.
            }
        }

        if (_onboardingWindow is { ViewModel.IsFinished: false } wizard)
            wizard.ViewModel.Leave();
    }

    /// <summary>True once <see cref="OnCrash"/> has run.</summary>
    public bool HasCrashed => _crashed != 0;

    /// <summary>
    /// An exception nothing caught: the app is about to die. Only the first call counts. Logs it to
    /// <c>crash.log</c>, keeps the editor's unsaved edits (and library changes that couldn't be written) in
    /// <c>Recovered\</c> for the next start to announce, and removes the tray icon so none is left behind. library.json
    /// is never written here: the library in memory may be half-way through a change. Call on the UI thread.
    /// </summary>
    public void OnCrash(Exception exception)
    {
        if (Interlocked.Exchange(ref _crashed, 1) != 0) return;
        CrashLog.Append(Paths.CrashLogFile, exception, DateTime.Now);
        _quietExit = true;

        try
        {
            var now = DateTime.Now;
            if (_mainWindow?.ViewModel.CurrentUnsavedEdit() is { } unsaved)
                Settings.RecoveredEditsFile = ExitEdits.WriteRecoveryFile(Paths.RecoveredFolder, unsaved, now);
            if (LibraryUnwritten)
                Settings.RecoveredLibraryFile = RecoveredLibrary.Write(Paths.RecoveredFolder, Library, now);
            JsonStore.SaveSettings(Paths.SettingsFile, Settings);
        }
        catch (Exception ex)
        {
            CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now); // best effort; on with the clean-up
        }

        try
        {
            Dispose();
        }
        catch (Exception ex)
        {
            CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now);
        }
    }

    /// <summary>Removes the tray icon, stops the backup timers and releases the global shortcut. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _backupTimer?.Stop();
        _firstBackupTimer?.Stop();
        _hotkey?.Dispose();
        _hotkey = null;
        _closeRequestWindow?.Dispose();
        _closeRequestWindow = null;
        Tray?.Dispose();
    }

    private Window? VisibleMainWindow => _mainWindow is { IsVisible: true } ? _mainWindow : null;

    /// <summary>
    /// The global shortcut was pressed: the quick menu opens, except while the wizard shows its shortcut step,
    /// which takes the press as the user trying it out.
    /// </summary>
    private void OnHotkeyPressed()
    {
        if (_onboardingWindow?.ViewModel.OnShortcutPressed() == true)
        {
            _onboardingWindow.Activate();
            return;
        }
        Tray.ShowMenuAtCursor(returnFocusAfterwards: true);
    }

    private static string TooltipFor(string hotkey) => $"{AppName} ({hotkey})";

    /// <summary>Checks every hour (and once a minute after start) whether a daily backup is due.</summary>
    private void StartBackupSchedule()
    {
        _backupTimer = new DispatcherTimer { Interval = BackupCheckInterval };
        _backupTimer.Tick += (_, _) => _ = RunBackupAsync(BackupTrigger.Daily);
        _backupTimer.Start();

        _firstBackupTimer = new DispatcherTimer { Interval = FirstBackupCheckDelay };
        _firstBackupTimer.Tick += (_, _) =>
        {
            _firstBackupTimer.Stop();
            _ = RunBackupAsync(BackupTrigger.Daily);
        };
        _firstBackupTimer.Start();
    }

    /// <summary>What a backup needs from settings, read on the UI thread before the work moves off it.</summary>
    private sealed record BackupJob(BackupTrigger Trigger, string Folder, int Keep, AppSettings Decision);

    /// <summary>
    /// What happened off the UI thread: whether the folder could be reached, and the backup result (null when no
    /// backup was needed or the folder is paused).
    /// </summary>
    private sealed record BackupOutcome(bool Unavailable, BackupResult? Result);

    private BackupJob NewBackupJob(BackupTrigger trigger, string folder)
        => new(trigger, folder, Settings.BackupsToKeep, new AppSettings
        {
            LastBackupAt = Settings.LastBackupAt,
            LastBackedUpLibraryHash = Settings.LastBackedUpLibraryHash,
        });

    /// <summary>The file work of a backup. Runs off the UI thread and touches no UI or shared state.</summary>
    private BackupOutcome RunBackupJob(BackupJob job)
    {
        try
        {
            var unavailable = BackupLocation.IsUnavailable(job.Folder, Directory.Exists);
            if (unavailable && job.Trigger != BackupTrigger.Manual)
                return new BackupOutcome(true, null);

            var hash = TryHashLibrary();
            if (hash is null && job.Trigger != BackupTrigger.Manual)
                return new BackupOutcome(unavailable, null);
            if (hash is not null && !_backups.ShouldBackup(job.Trigger, hash, job.Decision))
                return new BackupOutcome(unavailable, null);

            return new BackupOutcome(unavailable, _backups.BackupNow(Paths.LibraryFile, job.Folder, job.Keep));
        }
        catch (Exception ex)
        {
            return new BackupOutcome(false, new BackupResult(false, null, null, ex.Message));
        }
    }

    /// <summary>Back on the UI thread: records a successful backup, updates the bar and tells the user.</summary>
    private BackupResult? ApplyBackupOutcome(BackupJob job, BackupOutcome outcome, bool showInTray)
    {
        var folder = job.Folder;
        var sameFolder = BackupLocation.SameFolder(folder, Settings.BackupFolder);
        if (!_disposed)
            ApplyAvailability(folder, outcome.Unavailable);

        if (outcome.Result is not { } result)
        {
            if (outcome.Unavailable && job.Trigger == BackupTrigger.Daily && !_pausedNoticeShown && !_disposed
                && BackupWarning is { } paused)
            {
                _pausedNoticeShown = true;
                Tray.ShowMessage(AppName, paused);
            }
            return null;
        }

        if (result.Success)
        {
            if (sameFolder) // the user may have picked another folder meanwhile
            {
                Settings.LastBackupAt = DateTimeOffset.Now;
                Settings.LastBackedUpLibraryHash = result.Hash;
                Settings.LastBackupUnfinished = false; // it did finish, just after the exit stopped waiting
                SaveSettings();
            }
            if (job.Trigger == BackupTrigger.Manual && showInTray && !_disposed)
                Tray.ShowMessage(AppName, "Backup saved.");
        }
        else
        {
            if (outcome.Unavailable)
                result = result with { Error = BackupLocation.UnavailableText(folder, PausedProvider(folder)) };
            if (showInTray && job.Trigger != BackupTrigger.OnExit && !_disposed)
                Tray.ShowMessage(AppName, $"Backup failed: {result.Error}", isError: true);
        }

        return result;
    }

    /// <summary>The exit backup ran out of time: remember it so the next start can say so.</summary>
    private void RecordUnfinishedBackup()
    {
        Settings.LastBackupUnfinished = true;
        try
        {
            JsonStore.SaveSettings(Paths.SettingsFile, Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Exiting anyway; there's no one left to tell.
        }
    }

    private void SetBackupWarning(string? text, string? detail)
    {
        if (text == BackupWarning && detail == BackupWarningDetail) return;
        BackupWarning = text;
        BackupWarningDetail = detail;
        BackupStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The provider to name in a paused message: a detected cloud folder containing it, else the saved name.</summary>
    private string? PausedProvider(string folder, IReadOnlyList<CloudFolder>? clouds = null)
        => (clouds is null ? null : BackupLocation.ProviderFor(folder, clouds)) ?? Settings.BackupProviderName;

    private async Task CompletePausedMessageAsync(string folder, int version)
    {
        IReadOnlyList<CloudFolder> clouds;
        try
        {
            clouds = await Task.Run(DetectCloudFolders);
        }
        catch (Exception)
        {
            return; // detection is best effort; the message without a provider stands
        }

        // Ignore the answer if the settings changed meanwhile.
        if (version != _backupStateVersion || !_backupsPaused) return;
        SetBackupWarning(BackupLocation.PausedMessage(folder, PausedProvider(folder, clouds)), folder);
    }

    private static string? TryHashLibrary()
    {
        try
        {
            return LibraryHash.OfFile(Paths.LibraryFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes library.json and records whether memory and disk now agree (<see cref="LibraryUnwritten"/>). On
    /// failure tells the user (<c>Couldn't save your library: reason. advice</c>) and returns false; memory is
    /// left as is.
    /// </summary>
    private bool WriteLibraryFile(string failureAdvice = ChangeSaveFailedAdvice)
    {
        try
        {
            JsonStore.SaveLibrary(Paths.LibraryFile, Library);
            LibraryUnwritten = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LibraryUnwritten = true;
            ShowError($"Couldn't save your library: {WithoutFinalPeriod(ex.Message)}. {failureAdvice}");
            return false;
        }
    }

    /// <summary>
    /// At exit, when library.json is behind memory (<see cref="LibraryUnwritten"/>): one more try at writing it,
    /// and if that fails too a copy of the whole library goes to <c>Recovered\library-&lt;time&gt;.json</c>, which
    /// the next start announces. Nothing is shown from here.
    /// </summary>
    private void KeepUnwrittenLibrary()
    {
        if (!LibraryUnwritten || WriteLibraryFile()) return;
        try
        {
            Settings.RecoveredLibraryFile = RecoveredLibrary.Write(Paths.RecoveredFolder, Library, DateTime.Now);
            JsonStore.SaveSettings(Paths.SettingsFile, Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the folder is probably as unwritable as library.json.
        }
    }

    // Not while exiting either: the windows are gone and nobody is left to answer.
    private void ShowError(string text)
    {
        if (AsksNothing || _exiting) return;
        ConfirmDialog.Inform(VisibleMainWindow, text);
    }

    private static string WithoutFinalPeriod(string message) => message.Trim().TrimEnd('.');

    /// <summary>Missing or unreadable settings fall back to defaults; odd values are corrected.</summary>
    private static AppSettings LoadSettings()
    {
        var settings = JsonStore.LoadSettings(Paths.SettingsFile).Value ?? new AppSettings();

        settings.Hotkey = HotkeyGesture.TryParse(settings.Hotkey ?? "", out var gesture)
            ? gesture!.ToString()
            : DefaultHotkey;
        settings.BackupsToKeep = Math.Clamp(settings.BackupsToKeep, 1, 365);
        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
            settings.BackupFolder = null;

        return settings;
    }

    /// <summary>
    /// The library file exists but cannot be read; it is never overwritten here. The user can restore the previous
    /// save (<c>library.json.bak</c>, offered when it can be read) or a backup (startup then continues), or exit.
    /// Returns what was restored, for <c>Library restored from …</c>, or null after shutting the app down.
    /// </summary>
    private string? HandleUnreadableLibrary(string path, string error)
    {
        System.Diagnostics.Debug.WriteLine($"Unreadable library {path}: {error}");
        const string Unreadable = "Prompt Library couldn't open your prompts file. It hasn't been changed.";
        var previous = JsonStore.PreviousSavePath(path);

        while (true)
        {
            if (JsonStore.LoadLibrary(previous).Status == LoadStatus.Ok)
            {
                var savedAt = BackupLocation.RelativeTimeText(File.GetLastWriteTime(previous), DateTime.Now, midSentence: true);
                var choice = ConfirmDialog.Choose(null,
                    $"{Unreadable} The previous save, from {savedAt}, can be restored.",
                    "Restore the previous save", "Restore from backup…", "Exit",
                    "Show file", () => ShowFileInExplorer(path));
                if (choice is null) break;
                if (choice == true)
                {
                    if (RestorePreviousSave(previous) is not { } failed)
                        return "the previous save";
                    ConfirmDialog.Inform(null, $"Couldn't restore the previous save: {WithoutFinalPeriod(failed)}.");
                    continue;
                }
            }
            else if (!ConfirmDialog.Ask(null, Unreadable, "Restore from backup…", "Exit",
                         "Show file", () => ShowFileInExplorer(path)))
                break;

            var window = new RestoreWindow(this, isStartup: true)
            {
                ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            if (window.ShowDialog() == true && window.RestoredFrom is { } restored)
                return BackupLocation.RelativeTimeText(restored.Timestamp, DateTime.Now, midSentence: true);
            // Closing the Restore window goes back to the question, so the user can still choose Exit.
        }

        Application.Current.Shutdown();
        return null;
    }

    /// <summary>
    /// Restores <paramref name="previousSave"/> as a backup restore would: checked first, the unreadable file kept as
    /// a safety copy, then loaded. Returns null on success, otherwise the error.
    /// </summary>
    private string? RestorePreviousSave(string previousSave)
    {
        var safetyFolder = BackupLocation.SafetyFolder(Settings.BackupFolder, Paths.SafetyBackupsFolder, Directory.Exists);
        var keep = safetyFolder == Paths.SafetyBackupsFolder ? BackupLocation.SafetyBackupsToKeep : Settings.BackupsToKeep;
        var result = _backups.Restore(previousSave, Paths.LibraryFile, safetyFolder, keep);
        if (!result.Success)
            return result.Error ?? "Unknown error.";

        var loaded = JsonStore.LoadLibrary(Paths.LibraryFile);
        if (loaded.Status != LoadStatus.Ok)
            return $"It was restored but can't be read: {loaded.Error}";

        Library = loaded.Value!;
        if (result.BackupPath is { } safetyCopy)
        {
            BackupLocation.RememberPreRestoreCopy(Settings, safetyCopy); // labelled in the Restore window
            SaveSettings();
        }
        return null;
    }

    private static void ShowFileInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Explorer couldn't be started; nothing more to do.
        }
    }
}
