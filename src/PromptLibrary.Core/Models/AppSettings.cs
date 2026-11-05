namespace PromptLibrary.Core.Models;

public class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+P";
    public string? BackupFolder { get; set; }

    /// <summary>The cloud provider's name (e.g. <c>Google Drive</c>) when the folder was picked from a detected
    /// cloud folder; null for a folder the user chose. Used in the "Backups paused" message.</summary>
    public string? BackupProviderName { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public int BackupsToKeep { get; set; } = 30;
    public DateTimeOffset? LastBackupAt { get; set; }
    public string? LastBackedUpLibraryHash { get; set; }
    public bool FirstRunDone { get; set; }

    /// <summary>After a pick from the shortcut menu, also paste it into the app the user was typing in.</summary>
    public bool PasteAfterCopy { get; set; } = true;

    /// <summary>True once the tray has said "Still running…" after the main window was first closed.</summary>
    public bool HideToTrayNoticeShown { get; set; }

    /// <summary>The backup at the last exit didn't finish in time; the next start says so once.</summary>
    public bool LastBackupUnfinished { get; set; }

    /// <summary>Unsaved edits were written to this recovery file at the last exit; the next start says so once.</summary>
    public string? RecoveredEditsFile { get; set; }

    /// <summary>File names of the copies restores made of the library first (labelled in the Restore window).</summary>
    public List<string> PreRestoreBackupFiles { get; set; } = [];
}
