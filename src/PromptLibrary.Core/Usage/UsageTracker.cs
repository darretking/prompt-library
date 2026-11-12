using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Usage;

/// <summary>Records which prompts are used and ranks them by how often and how recently.</summary>
public static class UsageTracker
{
    /// <summary>How many days until a use counts half as much.</summary>
    public const double HalfLifeDays = 14;

    /// <summary>Counts one use of the prompt at <paramref name="now"/>.</summary>
    public static void Record(AppSettings settings, Guid promptId, DateTimeOffset now)
    {
        if (!settings.Usage.TryGetValue(promptId, out var usage) || usage is null)
            settings.Usage[promptId] = usage = new PromptUsage();
        usage.Count++;
        usage.LastUsedAt = now;
    }

    /// <summary>The use count weighted by recency: halves every 14 days since the last use. 0 when never used.</summary>
    public static double Frecency(PromptUsage? usage, DateTimeOffset now)
    {
        if (usage is null || usage.Count <= 0) return 0;
        var days = Math.Max(0, (now - usage.LastUsedAt).TotalDays);
        return usage.Count * Math.Pow(0.5, days / HalfLifeDays);
    }

    /// <summary>The frecency of a prompt by id (0 when never used).</summary>
    public static double Frecency(AppSettings settings, Guid promptId, DateTimeOffset now)
        => Frecency(settings.Usage.GetValueOrDefault(promptId), now);

    /// <summary>The last-used prompt ids, most recent first.</summary>
    public static IReadOnlyList<Guid> Recent(AppSettings settings, int n)
        => settings.Usage
            .OrderByDescending(kv => kv.Value.LastUsedAt)
            .Take(Math.Max(0, n))
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>Drops usage of prompts not in <paramref name="existingIds"/>. Returns how many entries were removed.</summary>
    public static int Prune(AppSettings settings, IEnumerable<Guid> existingIds)
    {
        var keep = existingIds as ISet<Guid> ?? existingIds.ToHashSet();
        var gone = settings.Usage.Keys.Where(id => !keep.Contains(id)).ToList();
        foreach (var id in gone) settings.Usage.Remove(id);
        return gone.Count;
    }
}
