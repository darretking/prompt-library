namespace PromptLibrary.Core.Hotkeys;

/// <summary>
/// What Settings says when a shortcut is taken: it suggests the first of a few shortcuts that is free right now,
/// or asks for a different combination when none is.
/// </summary>
public static class ShortcutSuggestion
{
    /// <summary>Tried in this order (never Ctrl+Shift+P).</summary>
    public static readonly IReadOnlyList<string> Candidates = ["Ctrl+Alt+L", "Ctrl+Alt+K", "Ctrl+Alt+Shift+P"];

    /// <param name="isFree">True when the shortcut could be registered now (probing it must not keep it).</param>
    public static string InUseText(Func<string, bool> isFree)
        => Candidates.FirstOrDefault(isFree) is { } free
            ? $"That shortcut is in use by another app. Try another, like {free}."
            : "That shortcut is in use by another app. Try a different combination.";
}
