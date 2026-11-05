using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Search;

/// <summary>A combined-search result. Hidden team prompts are returned (shown greyed) so they can be found and unhidden.</summary>
public sealed record SearchHit(Prompt Prompt, bool IsTeam, bool IsHidden);

public static class PromptFilter
{
    /// <summary>Searches both libraries: yours first, then the team's, each A-Z. Empty query returns nothing.</summary>
    public static IReadOnlyList<SearchHit> Search(Library personal, Library? team, AppSettings s, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var hits = Apply(personal, null, query).Select(p => new SearchHit(p, false, false)).ToList();
        if (team is not null)
            hits.AddRange(Apply(team, null, query)
                .Select(p => new SearchHit(p, true, s.HiddenTeamPromptIds.Contains(p.Id))));
        return hits;
    }

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
