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

    private static readonly TimeSpan BackupCheckInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan FirstBackupCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PickedFolderBackupTimeout = TimeSpan.FromSeconds(15);

    private readonly ClipboardWriter _clipboard =
        new(text => System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText), Thread.Sleep);

    private readonly BackupService _backups = new(() => DateTime.Now);

    private GlobalHotkey? _hotkey;
    private HotkeyGesture? _activeGesture;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _backupTimer;
    private DispatcherTimer? _firstBackupTimer;
    private bool _started;
    private bool _disposed;

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
        Tray.PromptClicked += (_, id) => CopyPrompt(id);
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.SettingsRequested += (_, _) => OpenSettings();
        Tray.DoubleClicked += (_, _) => ShowMainWindow();
        Tray.ExitRequested += (_, _) => ExitApp();
        Tray.Rebuild(QuickMenuBuilder.Build(Library));
        _started = true;

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => Tray.ShowMenuAtCursor(returnFocusAfterwards: true);
        var hotkeyRegistered = ApplyHotkey(Settings.Hotkey);

        ApplyStartupRegistration();
        RefreshBackupState();
        StartBackupSchedule();

        if (restoredFrom is not null)
        {
            ShowMainWindow();
            Tray.ShowMessage(AppName, $"Library restored from {restoredFrom}.");
        }

        if (!hotkeyRegistered)
        {
            Tray.ShowMessage(AppName, $"Shortcut {Settings.Hotkey} is in use by another app. Pick another in Settings.", isError: true);
            OpenSettings();
        }
        else if (!Settings.FirstRunDone)
        {
            // If the shortcut is taken, the conflict message is the one that matters; the welcome waits
            // for a start where the shortcut works.
            Tray.ShowMessage(AppName, $"Prompt Library is running. Press {Settings.Hotkey} or click this icon.");
            Settings.FirstRunDone = true;
            SaveSettings();
        }

        return true;
    }

    /// <summary>
    /// Saves the library, tells listeners and refreshes the quick menu. If the file can't be written
    /// the user is told, the in-memory library is kept and false is returned.
    /// </summary>
    public bool SaveLibrary()
    {
        var saved = WriteLibraryFile();
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

    /// <summary>Copies the prompt's text and confirms in the tray. False if the clipboard stayed busy.</summary>
    public bool CopyPrompt(Guid promptId)
    {
        var prompt = Library.Prompts.FirstOrDefault(p => p.Id == promptId);
        return prompt is not null && CopyText(prompt.Text, prompt.Title);
    }

    /// <summary>
    /// Copies <paramref name="text"/> (normalised to CRLF) and shows <c>Copied: title</c> in the tray.
    /// False if the clipboard stayed busy.
    /// </summary>
    public bool CopyText(string text, string title)
    {
        if (_clipboard.TryCopy(text))
        {
            Tray.ShowMessage(AppName, $"Copied: {title}");
            return true;
        }

        Tray.ShowMessage(AppName, "Couldn't copy — clipboard busy. Try again.", isError: true);
        return false;
    }

    /// <summary>Shows the one main window, creating it the first time.</summary>
    public void ShowMainWindow()
    {
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

    /// <summary>Opens the Restore window (over Settings or the main window when one is open).</summary>
    public void OpenRestore()
    {
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
    /// Before a restore replaces the library: offers to save unsaved edits in the main window.
    /// False if the user cancelled.
    /// </summary>
    public bool ConfirmUnsavedChangesBeforeRestore()
        => _mainWindow is not { IsVisible: true } || _mainWindow.ViewModel.ConfirmDiscardOrSave();

    /// <summary>
    /// Re-checks whether the backup folder can be reached (main window activated). When a paused folder is
    /// back, the bar clears and the skipped backup runs.
    /// </summary>
    public void RecheckBackups()
    {
        if (!_started || _disposed) return;
        var wasPaused = _backupsPaused;
        RefreshBackupState();
        if (wasPaused && !_backupsPaused)
            RunBackup(BackupTrigger.Daily);
    }

    /// <summary>
    /// Backs up library.json to the backup folder if <paramref name="trigger"/> calls for it.
    /// Success records the time and hash in settings; Manual says <c>Backup saved.</c> in the tray and a
    /// failure says <c>Backup failed: …</c> (unless <paramref name="showInTray"/> is false because the caller
    /// shows the result itself). Returns the result, or null when no backup was attempted.
    /// </summary>
    public BackupResult? RunBackup(BackupTrigger trigger, bool showInTray = true)
    {
        var folder = Settings.BackupFolder;
        if (folder is null)
        {
            // "Back up now" with backups off: take the user to where they can turn them on.
            if (trigger == BackupTrigger.Manual && showInTray)
                OpenSettings();
            return null;
        }

        RefreshBackupState();
        if (_backupsPaused && trigger != BackupTrigger.Manual)
        {
            if (trigger == BackupTrigger.Daily && !_pausedNoticeShown && BackupWarning is { } paused)
            {
                _pausedNoticeShown = true;
                Tray.ShowMessage(AppName, paused);
            }
            return null;
        }

        var hash = TryHashLibrary();
        if (hash is null && trigger != BackupTrigger.Manual)
            return null;
        if (hash is not null && !_backups.ShouldBackup(trigger, hash, Settings))
            return null;

        var result = _backups.BackupNow(Paths.LibraryFile, folder, Settings.BackupsToKeep);
        if (result.Success)
        {
            Settings.LastBackupAt = DateTimeOffset.Now;
            Settings.LastBackedUpLibraryHash = result.Hash;
            SaveSettings();
            RefreshBackupState();
            if (trigger == BackupTrigger.Manual && showInTray)
                Tray.ShowMessage(AppName, "Backup saved.");
        }
        else
        {
            if (_backupsPaused)
                result = result with { Error = BackupLocation.UnavailableText(folder, PausedProvider(folder)) };
            if (showInTray && trigger != BackupTrigger.OnExit)
                Tray.ShowMessage(AppName, $"Backup failed: {result.Error}", isError: true);
        }

        return result;
    }

    /// <summary>
    /// Backs up to <paramref name="folder"/> (off the UI thread, at most 15 s) and only if that works makes it the
    /// backup folder; on failure the previous folder stays. Used by the backup location picker.
    /// </summary>
    public async Task<BackupResult> BackUpToNewFolderAsync(string folder, string? providerName)
    {
        var keep = Settings.BackupsToKeep;
        var backup = Task.Run(() => _backups.BackupNow(Paths.LibraryFile, folder, keep));
        var finished = await Task.WhenAny(backup, Task.Delay(PickedFolderBackupTimeout));

        BackupResult result;
        try
        {
            result = finished == backup
                ? await backup
                : new BackupResult(false, null, null, "The folder didn't respond within 15 seconds.");
        }
        catch (Exception ex)
        {
            result = new BackupResult(false, null, null, ex.Message);
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

    /// <summary>Backups in <paramref name="folder"/>, newest first (empty if the folder can't be read).</summary>
    public IReadOnlyList<BackupInfo> ListBackups(string folder) => _backups.List(folder);

    /// <summary>
    /// Restores <paramref name="backup"/> and loads it. The current library is saved first as a safety copy, in
    /// the configured backup folder if it is set and available, otherwise in
    /// <c>%APPDATA%\PromptLibrary\Safety backups</c> (kept to 10). The folder the backup came from is never written
    /// to or pruned unless it is the configured one. After startup the windows and quick menu are refreshed and the
    /// tray says <c>Library restored from &lt;date&gt;.</c> Returns null on success, otherwise the error to show.
    /// </summary>
    public string? RestoreBackup(BackupInfo backup)
    {
        var safetyFolder = BackupLocation.SafetyFolder(Settings.BackupFolder, Paths.SafetyBackupsFolder, Directory.Exists);
        var keep = safetyFolder == Paths.SafetyBackupsFolder ? BackupLocation.SafetyBackupsToKeep : Settings.BackupsToKeep;

        var result = _backups.Restore(backup.Path, Paths.LibraryFile, safetyFolder, keep);
        if (!result.Success)
            return result.Error;

        var loaded = JsonStore.LoadLibrary(Paths.LibraryFile);
        if (loaded.Status != LoadStatus.Ok)
            return $"The backup was restored but can't be read: {loaded.Error}";

        Library = loaded.Value!;
        if (_started)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
            RefreshQuickMenu();
            _mainWindow?.ViewModel.ReloadAfterRestore();
            Tray.ShowMessage(AppName, $"Library restored from {BackupLocation.RelativeTimeText(backup.Timestamp, DateTime.Now, midSentence: true)}.");
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
    /// changed. When paused, the cloud provider is looked up in the background to complete the message.
    /// </summary>
    public void RefreshBackupState()
    {
        var version = ++_backupStateVersion;
        var folder = Settings.BackupFolder;

        if (folder is null)
        {
            _backupsPaused = false;
            SetBackupWarning(BackupsOffText, null);
            return;
        }

        _backupsPaused = BackupLocation.IsUnavailable(folder, Directory.Exists);
        if (!_backupsPaused)
        {
            SetBackupWarning(null, null);
            return;
        }

        SetBackupWarning(BackupLocation.PausedMessage(folder, Settings.BackupProviderName), folder);
        _ = CompletePausedMessageAsync(folder, version);
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
    /// Quits the app. Unsaved edits in the main window are offered for saving first (Cancel stays);
    /// then the library is backed up if it changed.
    /// </summary>
    public void ExitApp()
    {
        if (_mainWindow is { IsVisible: true } && !_mainWindow.ViewModel.ConfirmDiscardOrSave())
            return;

        RunBackup(BackupTrigger.OnExit);
        Dispose();
        _settingsWindow?.Close();
        _mainWindow?.CloseForExit();
        Application.Current.Shutdown();
    }

    /// <summary>Windows is logging off or shutting down: ask nothing more and clean up now.</summary>
    public void OnSessionEnding()
    {
        IsSessionEnding = true;
        Dispose();
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
        Tray?.Dispose();
    }

    private Window? VisibleMainWindow => _mainWindow is { IsVisible: true } ? _mainWindow : null;

    private static string TooltipFor(string hotkey) => $"{AppName} ({hotkey})";

    /// <summary>Checks every hour (and once a minute after start) whether a daily backup is due.</summary>
    private void StartBackupSchedule()
    {
        _backupTimer = new DispatcherTimer { Interval = BackupCheckInterval };
        _backupTimer.Tick += (_, _) => RunBackup(BackupTrigger.Daily);
        _backupTimer.Start();

        _firstBackupTimer = new DispatcherTimer { Interval = FirstBackupCheckDelay };
        _firstBackupTimer.Tick += (_, _) =>
        {
            _firstBackupTimer.Stop();
            RunBackup(BackupTrigger.Daily);
        };
        _firstBackupTimer.Start();
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

    /// <summary>Writes library.json. On failure tells the user and returns false; memory is left as is.</summary>
    private bool WriteLibraryFile()
    {
        try
        {
            JsonStore.SaveLibrary(Paths.LibraryFile, Library);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Couldn't save your library: {WithoutFinalPeriod(ex.Message)}. Your changes are still open — try again.");
            return false;
        }
    }

    private void ShowError(string text)
    {
        if (_mainWindow is { IsVisible: true })
            MessageBox.Show(_mainWindow, text, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(text, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
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
    /// The library file exists but cannot be read; it is never overwritten here. The user can restore a backup
    /// (startup then continues) or exit. Returns the restored backup's time text, or null after shutting the
    /// app down.
    /// </summary>
    private string? HandleUnreadableLibrary(string path, string error)
    {
        System.Diagnostics.Debug.WriteLine($"Unreadable library {path}: {error}");

        while (ConfirmDialog.Ask(null,
                   "Prompt Library couldn't open your prompts file. It hasn't been changed.",
                   "Restore from backup…", "Exit",
                   "Show file", () => ShowFileInExplorer(path)))
        {
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
