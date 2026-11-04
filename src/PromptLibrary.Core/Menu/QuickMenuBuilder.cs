using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Menu;

public record QuickMenuPrompt(Guid Id, string Title);

public record QuickMenuCategory(string Name, IReadOnlyList<QuickMenuPrompt> Prompts);

public static class QuickMenuBuilder
{
    public static IReadOnlyList<QuickMenuCategory> Build(Library lib)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return lib.Categories
            .OrderBy(c => c.Name, comparer)
            .Select(c => new QuickMenuCategory(
                c.Name,
                lib.Prompts
                    .Where(p => p.CategoryId == c.Id)
                    .OrderBy(p => p.Title, comparer)
                    .Select(p => new QuickMenuPrompt(p.Id, p.Title))
                    .ToList()))
            .Where(c => c.Prompts.Count > 0)
            .ToList();
    }
}
