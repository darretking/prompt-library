using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Editing;

/// <summary>Clears the per-user references to prompts that no longer exist.</summary>
public static class PromptReferences
{
    /// <summary>Removes the ids from <see cref="AppSettings.PinnedPromptIds"/> and <see cref="AppSettings.Usage"/>.</summary>
    public static void Remove(AppSettings settings, IEnumerable<Guid> promptIds)
    {
        var ids = promptIds.ToHashSet();
        if (ids.Count == 0) return;
        settings.PinnedPromptIds.RemoveAll(ids.Contains);
        foreach (var id in ids) settings.Usage.Remove(id);
    }
}
