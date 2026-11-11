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

    /// <summary>Library changes that couldn't be saved were written to this copy at the last exit; the next start says so once.</summary>
    public string? RecoveredLibraryFile { get; set; }

    /// <summary>File names of the copies restores made of the library first (labelled in the Restore window).</summary>
    public List<string> PreRestoreBackupFiles { get; set; } = [];

    /// <summary>Where this PC's team library lives (folder or link); null when not joined.</summary>
    public string? TeamSource { get; set; }

    /// <summary>Pinned prompts (personal or team), in pin order, newest last.</summary>
    public List<Guid> PinnedPromptIds { get; set; } = [];

    /// <summary>Team prompts this PC has hidden.</summary>
    public List<Guid> HiddenTeamPromptIds { get; set; } = [];

    /// <summary>Team prompt ID to the fingerprint of the copy this PC last saw (drives New / Updated markers).</summary>
    public Dictionary<Guid, string> SeenTeamPrompts { get; set; } = [];

    /// <summary>True once the first copy of the team library has been recorded as already seen.</summary>
    public bool TeamBaselineTaken { get; set; }

    /// <summary>How often and when each prompt (personal or team) was last used.</summary>
    public Dictionary<Guid, PromptUsage> Usage { get; set; } = [];

    /// <summary>The last value typed for each placeholder name (key lower-cased).</summary>
    public Dictionary<string, string> PlaceholderValues { get; set; } = [];

    /// <summary><c>search</c> (default) or <c>menu</c>: what the shortcut opens.</summary>
    public string ShortcutOpens { get; set; } = "search";

    /// <summary><c>system</c> (default), <c>light</c> or <c>dark</c>.</summary>
    public string Theme { get; set; } = "system";
}
