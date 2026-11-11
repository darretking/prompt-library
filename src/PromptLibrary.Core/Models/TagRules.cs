namespace PromptLibrary.Core.Models;

/// <summary>The rules for a prompt's tags: trimmed, no leading #, lower-case, unique, max 10 of max 24 characters.</summary>
public static class TagRules
{
    public const int MaxTags = 10;
    public const int MaxLength = 24;

    /// <summary>Cleans a tag list; blank tags are dropped, longer ones are cut, extras beyond 10 are ignored.</summary>
    public static List<string> Normalize(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = (raw ?? "").Trim().TrimStart('#').Trim().ToLowerInvariant();
            if (tag.Length > MaxLength) tag = tag[..MaxLength].TrimEnd();
            if (tag.Length == 0 || result.Contains(tag)) continue;
            result.Add(tag);
            if (result.Count == MaxTags) break;
        }
        return result;
    }
}
