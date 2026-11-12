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

    /// <summary>
    /// After waiting for the shortcut keys to be let go and the app to come back to the front: null when Ctrl+V can
    /// be sent, otherwise <see cref="PasteOutcome.Failed"/>. A modifier still held would turn Ctrl+V into another
    /// shortcut (Ctrl+Alt+V, Ctrl+Shift+V, Win+Ctrl+V…), so nothing is sent then.
    /// </summary>
    public static PasteOutcome? OutcomeAfterWait(bool modifiersStillDown, bool foregroundMatched)
        => modifiersStillDown || !foregroundMatched ? PasteOutcome.Failed : null;

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

    // ---- Adaptive waits (the Win32 polling lives in the app; these are the rules) ----------------

    /// <summary>How often the app checks whether the target window has the focus.</summary>
    public static readonly TimeSpan ForegroundPoll = TimeSpan.FromMilliseconds(10);

    /// <summary>The longest wait for the target window to come to the front before giving up on pasting.</summary>
    public static readonly TimeSpan ForegroundMax = TimeSpan.FromMilliseconds(1000);

    /// <summary>The least time the clipboard is left alone after Ctrl+V, so the target can read it.</summary>
    public static readonly TimeSpan RestoreMin = TimeSpan.FromMilliseconds(400);

    /// <summary>The most time spent waiting for the target to go idle after Ctrl+V.</summary>
    public static readonly TimeSpan RestoreMax = TimeSpan.FromMilliseconds(1500);

    /// <summary>True while the target window still isn't in front and there is time left to wait.</summary>
    public static bool KeepWaitingForForeground(bool foregroundMatched, TimeSpan waited)
        => !foregroundMatched && waited < ForegroundMax;

    /// <summary>
    /// True when the clipboard may be put back: at least <see cref="RestoreMin"/> has passed AND the target is idle
    /// (or <see cref="RestoreMax"/> has passed, so a busy or unknown target never holds it up forever).
    /// </summary>
    public static bool ClipboardRestoreReady(TimeSpan sincePaste, bool targetIdle)
        => sincePaste >= RestoreMin && (targetIdle || sincePaste >= RestoreMax);

    /// <summary>True only if the clipboard still holds the text we put there (a newer copy by the user is never overwritten).</summary>
    public static bool ClipboardStillOurs(string? current, string ours)
        => current is not null && Text.LineEndings.ToCrlf(current) == Text.LineEndings.ToCrlf(ours);

    /// <summary>Windows blocks input to an elevated window unless we are elevated too.</summary>
    public static bool CanSendKeysTo(bool targetElevated, bool weAreElevated) => !targetElevated || weAreElevated;

    /// <summary>Shown when the prompt is only on the clipboard because pasting wasn't possible.</summary>
    public const string CouldntPasteText = "Couldn't paste into that app. The prompt is on your clipboard.";

    /// <summary>What the toast says after a pick from the search window, and its kind.</summary>
    public static (string Text, ToastKind Kind) ToastFor(string title, PasteOutcome outcome) => outcome switch
    {
        PasteOutcome.Pasted => ($"Pasted: {title}", ToastKind.Success),
        PasteOutcome.Failed or PasteOutcome.Elevated => (CouldntPasteText, ToastKind.Error),
        _ => ($"Copied: {title}", ToastKind.Success),
    };
}

/// <summary>How a toast looks: a check for success, a warning mark for a problem.</summary>
public enum ToastKind { Success, Error }
