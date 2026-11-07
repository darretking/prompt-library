using System.Security.Cryptography;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Team;

/// <summary>
/// Keeps team ids unique. A hand-edited team file can repeat an id, and a team file built from someone's
/// library.json shares ids with their own prompts; either would make the wrong prompt paste from the menu, or a
/// lookup by id fail. The clashing team prompt or category gets a new id, in memory only (the file is not rejected
/// and not rewritten; the next save writes the new id). The new id is derived from the old one, so it is the same on
/// every read and pins, hides and New / Updated markers on it keep working.
/// </summary>
public static class TeamIds
{
    /// <summary>
    /// Gives every team category and prompt whose id repeats an earlier one in <paramref name="content"/>, or is one of
    /// the reserved (personal) ids, a new derived id. A category re-numbered because of a reserved id takes its prompts
    /// along; a repeated category keeps none (they stay with the first). Returns whether anything changed.
    /// </summary>
    public static bool ResolveClashes(Library content, IReadOnlySet<Guid>? reservedPromptIds = null,
        IReadOnlySet<Guid>? reservedCategoryIds = null)
    {
        var changed = false;

        var seenCategories = new HashSet<Guid>();
        foreach (var category in content.Categories)
        {
            var old = category.Id;
            var repeated = seenCategories.Contains(old);
            if (repeated || reservedCategoryIds?.Contains(old) == true)
            {
                category.Id = Fresh(old, seenCategories, reservedCategoryIds);
                changed = true;
                if (!repeated)
                    foreach (var prompt in content.Prompts.Where(p => p.CategoryId == old))
                        prompt.CategoryId = category.Id;
            }
            seenCategories.Add(old);
            seenCategories.Add(category.Id);
        }

        var seenPrompts = new HashSet<Guid>();
        foreach (var prompt in content.Prompts)
        {
            var old = prompt.Id;
            if (seenPrompts.Contains(old) || reservedPromptIds?.Contains(old) == true)
            {
                prompt.Id = Fresh(old, seenPrompts, reservedPromptIds);
                changed = true;
            }
            seenPrompts.Add(old);
            seenPrompts.Add(prompt.Id);
        }
        return changed;
    }

    // The first id derived from (old, n), n = 1, 2, …, that is neither taken nor reserved.
    private static Guid Fresh(Guid old, HashSet<Guid> taken, IReadOnlySet<Guid>? reserved)
    {
        for (var n = 1; ; n++)
        {
            var bytes = SHA256.HashData([.. old.ToByteArray(), .. BitConverter.GetBytes(n)]);
            var id = new Guid(bytes.AsSpan(0, 16));
            if (!taken.Contains(id) && reserved?.Contains(id) != true) return id;
        }
    }
}
