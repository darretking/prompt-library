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
    public List<string> PreRestoreBackupFiles { get => _preRestoreBackupFiles; set => _preRestoreBackupFiles = value ?? []; }
    private List<string> _preRestoreBackupFiles = [];

    /// <summary>Where this PC's team library lives (folder or link); null when not joined.</summary>
    public string? TeamSource { get; set; }

    /// <summary>Pinned prompts (personal or team), in pin order, newest last.</summary>
    public List<Guid> PinnedPromptIds { get => _pinned; set => _pinned = value ?? []; }
    private List<Guid> _pinned = [];

    /// <summary>Team prompts this PC has hidden.</summary>
    public List<Guid> HiddenTeamPromptIds { get => _hidden; set => _hidden = value ?? []; }
    private List<Guid> _hidden = [];

    /// <summary>Team prompt ID to the fingerprint of the copy this PC last saw (drives New / Updated markers).</summary>
    public Dictionary<Guid, string> SeenTeamPrompts { get => _seen; set => _seen = value ?? []; }
    private Dictionary<Guid, string> _seen = [];

    /// <summary>True once the first copy of the team library has been recorded as already seen.</summary>
    public bool TeamBaselineTaken { get; set; }

    /// <summary>The preset team source (from the install) that was already offered or joined, so it is only offered once.</summary>
    public string? PresetTeamSourceApplied { get; set; }

    /// <summary>The team note text this PC has already seen (drives the "new note" marker).</summary>
    public string? SeenTeamNote { get; set; }

    /// <summary>How often and when each prompt (personal or team) was last used.</summary>
    public Dictionary<Guid, PromptUsage> Usage { get => _usage; set => _usage = value ?? []; }
    private Dictionary<Guid, PromptUsage> _usage = [];

    /// <summary>The last value typed for each placeholder name (key lower-cased).</summary>
    public Dictionary<string, string> PlaceholderValues { get => _placeholders; set => _placeholders = value ?? []; }
    private Dictionary<string, string> _placeholders = [];

    /// <summary><c>search</c> (default) or <c>menu</c>: what the shortcut opens.</summary>
    public string ShortcutOpens { get => _shortcutOpens; set => _shortcutOpens = value ?? "search"; }
    private string _shortcutOpens = "search";

    /// <summary>After an auto-paste, put the text that was on the clipboard before back (only when it held nothing but text).</summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary><c>system</c> (default), <c>light</c> or <c>dark</c>.</summary>
    public string Theme { get => _theme; set => _theme = value ?? "system"; }
    private string _theme = "system";
}
