using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App;

/// <summary>
/// What the main window's view model needs from the app: the personal library and its saves, settings, the team
/// library service and the app-level commands. <see cref="AppHost"/> implements it; tests use a stand-in.
/// </summary>
public interface IMainHost
{
    /// <summary>The update checker, or null when update checks are off.</summary>
    UpdateCoordinator? Updates { get; }

    /// <summary>The team library service, or null when it couldn't start.</summary>
    TeamLibraryService? Team { get; }

    /// <summary>The personal library (the object can be replaced by a restore).</summary>
    Library Library { get; }

    AppSettings Settings { get; }

    /// <summary>The warning bar's text ("Backups are off…" or "Backups paused…"), or null when backups are fine.</summary>
    string? BackupWarning { get; }

    /// <summary>The full backup folder while paused (the bar's tooltip).</summary>
    string? BackupWarningDetail { get; }

    /// <summary>True while the in-memory library has changes that library.json doesn't (the last write failed).</summary>
    bool LibraryUnwritten { get; }

    event EventHandler? BackupStateChanged;

    /// <summary>Saves the library after a prompt's Save; false (the user is told) when it couldn't be written.</summary>
    bool SaveLibrary();

    /// <summary>Saves the library after a change with no Save button to retry (category changes, deletions).</summary>
    bool SaveLibraryChange();

    /// <summary>Saves settings; false (the user is told) when they couldn't be written.</summary>
    bool SaveSettings();

    /// <summary>Rebuilds the quick menu from the in-memory libraries and settings.</summary>
    void RefreshQuickMenu();

    /// <summary>Copies <paramref name="text"/> and shows <c>Copied: title</c>; false if the clipboard stayed busy.</summary>
    bool CopyText(string text, string title);

    /// <summary>
    /// As <see cref="CopyText(string, string)"/>; with <paramref name="notifyInTray"/> false the tray says nothing about
    /// the copy (the window shows its own message, e.g. when sending to an AI app). A failure is still reported.
    /// </summary>
    bool CopyText(string text, string title, bool notifyInTray) => CopyText(text, title);

    /// <summary>Pins, usage or other per-user state changed outside the main window (the search window): lists refresh.</summary>
    event EventHandler? UserStateChanged { add { } remove { } }

    Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true);

    /// <summary>Cloud folders found on this PC (OneDrive, Google Drive, …), for the team invite. Slow-ish.</summary>
    IReadOnlyList<CloudFolder> DetectCloudFolders() => AppHost.DetectCloudFolders();

    void OpenRestore();

    void OpenSettings(bool focusBackups = false);

    void OpenOnboarding();

    void ShowAbout();

    void ExitApp(ExitReason reason = ExitReason.User);
}
