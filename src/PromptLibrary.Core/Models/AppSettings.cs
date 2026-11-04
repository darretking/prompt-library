namespace PromptLibrary.Core.Models;

public class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+P";
    public string? BackupFolder { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public int BackupsToKeep { get; set; } = 30;
    public DateTimeOffset? LastBackupAt { get; set; }
    public string? LastBackedUpLibraryHash { get; set; }
    public bool FirstRunDone { get; set; }
}
