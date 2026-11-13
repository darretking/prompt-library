using System.Diagnostics;

namespace PromptLibrary.Core.Team;

/// <summary>A team file found under a cloud folder. <paramref name="ShortLocation"/> is <c>OneDrive - Acme\Shared\Prompts</c>.</summary>
public sealed record FoundTeamFile(string Path, string LibraryName, string ShortLocation);

/// <summary>
/// Looks for <c>team-prompts.json</c> files under cloud roots: depth ≤ 5, no hidden/system folders, <c>node_modules</c>,
/// <c>.git</c> or reparse points; stops at 50 hits or the time budget. Never throws.
/// </summary>
public static class TeamFileFinder
{
    public const int MaxDepth = 5;
    public const int MaxHits = 50;
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(3);

    /// <param name="elapsed">Time since the search began; defaults to a real stopwatch (tests inject a fake).</param>
    public static Task<IReadOnlyList<FoundTeamFile>> FindAsync(IEnumerable<string> roots, CancellationToken ct,
        Func<TimeSpan>? elapsed = null, TimeSpan? budget = null, int maxHits = MaxHits, int maxDepth = MaxDepth)
    {
        var rootList = roots.ToList();
        return Task.Run<IReadOnlyList<FoundTeamFile>>(() =>
        {
            var watch = Stopwatch.StartNew();
            var clock = elapsed ?? (() => watch.Elapsed);
            var limit = budget ?? DefaultBudget;
            var hits = new List<FoundTeamFile>();
            try
            {
                foreach (var root in rootList)
                {
                    if (Stop()) break;
                    Walk(root, root, 0);
                }
            }
            catch (Exception)
            {
                // Never throws: return what was found.
            }
            return hits;

            bool Stop() => ct.IsCancellationRequested || hits.Count >= maxHits || clock() >= limit;

            void Walk(string root, string dir, int depth)
            {
                if (Stop()) return;
                var file = System.IO.Path.Combine(dir, TeamFile.FileName);
                try
                {
                    if (File.Exists(file) && TeamFile.Parse(File.ReadAllText(file)) is { } lib)
                        hits.Add(new FoundTeamFile(file, lib.Name, ShortLocation(root, dir)));
                }
                catch (Exception)
                {
                    // Unreadable or not a team file: skip it.
                }
                if (depth >= maxDepth) return;

                List<string> subs;
                try
                {
                    subs = Directory.EnumerateDirectories(dir).Where(Descend).ToList();
                }
                catch (Exception)
                {
                    return;
                }
                foreach (var sub in subs)
                {
                    if (Stop()) return;
                    Walk(root, sub, depth + 1);
                }
            }
        }, CancellationToken.None);
    }

    private static bool Descend(string dir)
    {
        var name = System.IO.Path.GetFileName(dir);
        if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".git", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var a = File.GetAttributes(dir);
            return (a & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ShortLocation(string root, string dir)
    {
        var rootName = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(root));
        var rel = System.IO.Path.GetRelativePath(root, dir);
        return rel == "." ? rootName : System.IO.Path.Combine(rootName, rel);
    }
}
