namespace PromptLibrary.Core.Clipboard;

/// <summary>What happened after a prompt from the shortcut menu was copied.</summary>
public enum PasteOutcome
{
    /// <summary>Only copied: the setting is off, it came from the tray menu, or the target was our own window.</summary>
    NotAttempted,

    /// <summary>Ctrl+V was sent to the app the user was typing in.</summary>
    Pasted,

    /// <summary>The app couldn't be brought back to the front, or Windows didn't take all the keys.</summary>
    Failed,

    /// <summary>The app runs as administrator, so Windows won't let us send it keys.</summary>
    Elevated,
}

/// <summary>
/// The rules for "paste into the app I was using": when to try, and what the tray says afterwards.
/// The Win32 work itself lives in the app (<c>Tray/AutoPaster</c>).
/// </summary>
public static class PasteDecision
{
    /// <summary>
    /// True when a pick should be pasted: the setting is on, the menu was opened by the global shortcut (the tray
    /// menu only copies: the foreground is then the taskbar), and there is a window to go back to that isn't ours.
    /// </summary>
    public static bool ShouldPaste(bool pasteAfterCopy, bool fromShortcutMenu, bool hasTargetWindow, bool targetIsOurs)
        => pasteAfterCopy && fromShortcutMenu && hasTargetWindow && !targetIsOurs;

    /// <summary>Pasted only if Windows accepted every key event that was sent (UIPI silently drops some).</summary>
    public static PasteOutcome OutcomeOfSend(int sent, int accepted)
        => accepted >= sent ? PasteOutcome.Pasted : PasteOutcome.Failed;

    /// <summary>The tray message after a copy, or null when the paste itself shows it worked.</summary>
    public static string? CopiedMessage(string title, PasteOutcome outcome) => outcome switch
    {
        PasteOutcome.Pasted => null,
        PasteOutcome.Failed => $"Copied: {title}. Press Ctrl+V to paste.",
        PasteOutcome.Elevated =>
            $"Copied: {title}. Press Ctrl+V to paste — Windows doesn't allow pasting into apps running as administrator.",
        _ => $"Copied: {title}",
    };
}
