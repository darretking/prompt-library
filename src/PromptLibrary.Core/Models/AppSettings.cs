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
}
