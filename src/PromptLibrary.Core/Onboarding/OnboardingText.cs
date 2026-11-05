using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Onboarding;

/// <summary>The onboarding wizard's words that depend on the shortcut, backups and paste settings.</summary>
public static class OnboardingText
{
    private const int SummaryPathLength = 56;

    /// <summary><c>Ctrl+Alt+P</c> → <c>Ctrl</c>, <c>Alt</c>, <c>P</c> (one keycap each).</summary>
    public static IReadOnlyList<string> Keycaps(string shortcut)
        => shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Step 2's heading when the shortcut couldn't be registered.</summary>
    public static string InUseHeading(string shortcut) => $"{shortcut} is used by another app";

    public static string PickAnotherText(string taken) => $"Press a different shortcut, like {Suggestion(taken)}.";

    /// <summary>A newly pressed shortcut that another app already has (the previous one stays).</summary>
    public static string CaptureInUseText(string shortcut)
        => $"{shortcut} is already used by another app. Try another, like {Suggestion(shortcut)}.";

    public static string ShortcutSummary(string shortcut, bool registered)
        => registered ? $"Shortcut: {shortcut}" : "Shortcut: none. Click the tray icon to open your prompts.";

    public static string BackupSummary(string? folder, string? providerName)
        => folder is null
            ? "Backups: off"
            : $"Backups: {providerName ?? BackupLocation.ShortenMiddle(folder, SummaryPathLength)}";

    /// <summary>Step 4's "how you'll use it" line: the first copy after setup.</summary>
    public static string ReadyText(string shortcut, bool registered, bool pasteAfterCopy)
    {
        if (!registered)
            return "Prompt Library waits in the tray, near the clock.";
        var result = pasteAfterCopy ? "it's pasted where you were typing" : "it's copied, ready to paste with Ctrl+V";
        return $"Press {shortcut} in any app and pick a prompt — {result}. Prompt Library waits in the tray.";
    }

    private static string Suggestion(string taken) => taken == "Ctrl+Alt+L" ? "Ctrl+Alt+K" : "Ctrl+Alt+L";
}
