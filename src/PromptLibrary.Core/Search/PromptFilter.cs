using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Search;

public static class PromptFilter
{
    public static IReadOnlyList<Prompt> Apply(Library lib, Guid? categoryId, string? query)
    {
        var q = query?.Trim() ?? "";
        IEnumerable<Prompt> prompts = lib.Prompts;

        if (categoryId is { } id)
            prompts = prompts.Where(p => p.CategoryId == id);

        if (q.Length > 0)
            prompts = prompts.Where(p => Contains(p.Title, q) || Contains(p.Description, q) || Contains(p.Text, q));

        return prompts.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);
}
