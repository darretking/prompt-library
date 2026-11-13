namespace PromptLibrary.Core.Templates;

/// <summary>
/// Where a field's starting value comes from: the member's own remembered value, else the team's shared fill-in value,
/// else none. Names are case-insensitive; the dynamic tokens ({clipboard} {date} {time} {cursor}) are never looked up.
/// </summary>
public sealed class FillInDefaults(IReadOnlyDictionary<string, string>? personal, IReadOnlyDictionary<string, string>? team)
{
    /// <param name="value">The value (never blank) when found.</param>
    /// <param name="fromTeam">True when the value is the team's, not the member's own.</param>
    public bool TryGet(string name, out string value, out bool fromTeam)
    {
        value = "";
        fromTeam = false;
        if (string.IsNullOrEmpty(name) || PromptTemplate.IsToken(name)) return false;
        if (Find(personal, name) is { } own) { value = own; return true; }
        if (Find(team, name) is { } shared) { value = shared; fromTeam = true; return true; }
        return false;
    }

    /// <summary>Own values over team values as one case-insensitive map, for the preview (blank values left out).</summary>
    public Dictionary<string, string> Merge()
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { team, personal })
            if (source is not null)
                foreach (var (k, v) in source)
                    if (!string.IsNullOrWhiteSpace(v) && !PromptTemplate.IsToken(k)) all[k] = v;
        return all;
    }

    private static string? Find(IReadOnlyDictionary<string, string>? map, string name)
    {
        if (map is null) return null;
        if (map.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        foreach (var (k, val) in map)
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) return val;
        return null;
    }
}
