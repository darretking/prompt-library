using System.Threading;
using System.Windows;
using PromptLibrary.App.Hotkeys;
using PromptLibrary.App.Startup;
using PromptLibrary.App.Tray;
using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Hotkeys;
using PromptLibrary.Core.Menu;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.App;

/// <summary>
/// Owns the running app: library and settings, tray icon, global hotkey and windows.
/// Created once on the UI thread by <see cref="App"/>.
/// </summary>
public sealed class AppHost
{
    private const string AppName = "Prompt Library";
    private const string DefaultHotkey = "Ctrl+Alt+P";

    private readonly ClipboardWriter _clipboard =
        new(text => System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText), Thread.Sleep);

    private GlobalHotkey? _hotkey;
    private HotkeyGesture? _activeGesture;
    private MainWindow? _mainWindow;

    public Library Library { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public TrayIcon Tray { get; private set; } = null!;

    public event EventHandler? LibraryChanged;

    /// <summary>Loads data and sets up the tray and hotkey. False if the app must exit instead.</summary>
    public bool Start()
    {
        Settings = LoadSettings();

        var libraryResult = JsonStore.LoadLibrary(Paths.LibraryFile);
        switch (libraryResult.Status)
        {
            case LoadStatus.Ok:
                Library = libraryResult.Value!;
                break;
            case LoadStatus.Missing:
                Library = SeedLibrary.Create(DateTimeOffset.Now);
                JsonStore.SaveLibrary(Paths.LibraryFile, Library);
                break;
            case LoadStatus.Unreadable:
                HandleUnreadableLibrary(Paths.LibraryFile, libraryResult.Error ?? "Unknown error.");
                return false;
        }

        Tray = new TrayIcon(TooltipFor(Settings.Hotkey));
        Tray.PromptClicked += (_, id) => CopyPrompt(id);
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.DoubleClicked += (_, _) => ShowMainWindow();
        Tray.ExitRequested += (_, _) => ExitApp();
        Tray.Rebuild(QuickMenuBuilder.Build(Library));

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => Tray.ShowMenuAtCursor(returnFocusAfterwards: true);
        ApplyHotkey(Settings.Hotkey);

        ApplyStartupRegistration();

        if (!Settings.FirstRunDone)
        {
            Tray.ShowMessage(AppName, $"Prompt Library is running. Press {Settings.Hotkey} or click this icon.");
            Settings.FirstRunDone = true;
            SaveSettings();
        }

        return true;
    }

    /// <summary>Saves the library, tells listeners and refreshes the quick menu.</summary>
    public void SaveLibrary()
    {
        JsonStore.SaveLibrary(Paths.LibraryFile, Library);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        Tray.Rebuild(QuickMenuBuilder.Build(Library));
    }

    public void SaveSettings() => JsonStore.SaveSettings(Paths.SettingsFile, Settings);

    /// <summary>Copies the prompt's text and confirms in the tray. False if the clipboard stayed busy.</summary>
    public bool CopyPrompt(Guid promptId)
    {
        var prompt = Library.Prompts.FirstOrDefault(p => p.Id == promptId);
        if (prompt is null) return false;

        if (_clipboard.TryCopy(prompt.Text))
        {
            Tray.ShowMessage(AppName, $"Copied: {prompt.Title}");
            return true;
        }

        Tray.ShowMessage(AppName, "Couldn't copy — clipboard busy. Try again.", isError: true);
        return false;
    }

    public void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow();
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>Opens the Settings window. Task 10 fills this in; for now it does nothing.</summary>
    public void OpenSettings()
    {
    }

    /// <summary>
    /// Registers <paramref name="text"/> as the global shortcut and records it in <see cref="Settings"/>
    /// (the caller saves). If another app owns it, says so in the tray, keeps the previous shortcut
    /// and returns false.
    /// </summary>
    public bool ApplyHotkey(string text)
    {
        if (_hotkey is null || !HotkeyGesture.TryParse(text, out var gesture))
            return false;

        if (!_hotkey.Register(gesture!))
        {
            if (_activeGesture is not null)
                _hotkey.Register(_activeGesture);
            Tray.ShowMessage(AppName, $"Shortcut {gesture} is in use by another app. Pick another in Settings.", isError: true);
            OpenSettings();
            return false;
        }

        _activeGesture = gesture;
        Settings.Hotkey = gesture!.ToString();
        Tray.ToolTipText = TooltipFor(Settings.Hotkey);
        return true;
    }

    public void ExitApp()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        Tray.Dispose();
        _mainWindow?.CloseForExit();
        Application.Current.Shutdown();
    }

    private static string TooltipFor(string hotkey) => $"{AppName} ({hotkey})";

    /// <summary>Missing or unreadable settings fall back to defaults; odd values are corrected.</summary>
    private static AppSettings LoadSettings()
    {
        var settings = JsonStore.LoadSettings(Paths.SettingsFile).Value ?? new AppSettings();

        if (!HotkeyGesture.TryParse(settings.Hotkey ?? "", out _))
            settings.Hotkey = DefaultHotkey;
        settings.BackupsToKeep = Math.Clamp(settings.BackupsToKeep, 1, 365);

        return settings;
    }

    private void ApplyStartupRegistration()
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
    /// The library file exists but cannot be read. It is never overwritten. Task 10 replaces the exit
    /// with opening the Restore window.
    /// </summary>
    private static void HandleUnreadableLibrary(string path, string error)
    {
        MessageBox.Show(
            $"Prompt Library can't read your library file:\n{path}\n\n{error}\n\n" +
            "Your file has not been changed. You can restore a backup from the Restore window (coming soon) or fix the file by hand.",
            AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        Application.Current.Shutdown();
    }
}
