using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Usage;

/// <summary>How prompt lists are ordered: most used first (default) or A to Z.</summary>
public static class UsageOrder
{
    public const string ByUse = "used";
    public const string ByName = "name";

    /// <summary>True unless the user chose A to Z; unknown values count as most used.</summary>
    public static bool SortsByUse(AppSettings s) => s.PromptSort != ByName;

    /// <summary>How often the prompt was used; 0 when never (or the entry is missing or null).</summary>
    public static int Uses(AppSettings s, Guid promptId) => s.Usage.GetValueOrDefault(promptId)?.Count ?? 0;

    /// <summary>Most used first, ties A to Z (or just A to Z when sorting by name). Stable.</summary>
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, int> uses, Func<T, string> name, AppSettings s)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        return SortsByUse(s)
            ? items.OrderByDescending(uses).ThenBy(name, comparer).ToList()
            : items.OrderBy(name, comparer).ToList();
    }

    public static IReadOnlyList<Prompt> Prompts(IEnumerable<Prompt> prompts, AppSettings s)
        => Sort(prompts, p => Uses(s, p.Id), p => p.Title, s);
}
