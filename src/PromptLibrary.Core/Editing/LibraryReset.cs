using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Editing;

/// <summary>
/// Settings → "Delete all my prompts": empties the personal library (every prompt and category) so the user can start
/// fresh without the starter prompts. Only the personal library: team prompts, and everything this PC keeps about them
/// (team pins, hidden team prompts, new/updated markers, team usage), are never touched.
/// </summary>
public static class LibraryReset
{
    /// <summary>The word the user types to confirm.</summary>
    public const string ConfirmWord = "DELETE";

    /// <summary>
    /// True when <paramref name="typed"/> is the confirm word: spaces around it and letter case are ignored, anything
    /// else (a part of it, a full stop after it, another word) is not.
    /// </summary>
    public static bool IsConfirmed(string? typed)
        => string.Equals(typed?.Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when there is something to delete (a prompt or a category).</summary>
    public static bool HasAnything(Library library) => library.Prompts.Count > 0 || library.Categories.Count > 0;

    /// <summary>
    /// Removes every personal prompt and category from <paramref name="library"/>, and the pins and usage this PC kept
    /// for those prompts. Pins and usage of other prompts (the team's) stay. Returns the ids of the removed prompts
    /// (so their version history can be deleted too).
    /// </summary>
    public static IReadOnlyList<Guid> ClearPersonal(Library library, AppSettings settings)
    {
        var ids = library.Prompts.Select(p => p.Id).ToList();
        var removed = ids.ToHashSet();
        library.Prompts.Clear();
        library.Categories.Clear();
        settings.PinnedPromptIds.RemoveAll(removed.Contains);
        foreach (var id in ids)
            settings.Usage.Remove(id);
        return ids;
    }
}
