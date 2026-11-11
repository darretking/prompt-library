namespace PromptLibrary.Core.Exchange;

/// <summary>Picks the folder the import and export dialogs open in: never one that syncs to a cloud drive.</summary>
public static class ExportLocations
{
    private static readonly string[] CloudWords = ["onedrive", "dropbox", "google drive", "googledrive", "icloud", "my drive"];

    /// <summary>True when the path is inside one of the cloud roots, or has a cloud drive's name in it.</summary>
    public static bool IsCloud(string path, IEnumerable<string> cloudRoots)
    {
        var full = Normalize(path);
        foreach (var root in cloudRoots)
        {
            var r = Normalize(root);
            if (r.Length > 0 && (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                                 || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return CloudWords.Any(w => full.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The first existing candidate that isn't a cloud folder, or null (the dialog then uses its own default).</summary>
    public static string? SafeStartFolder(IEnumerable<string?> candidates, IEnumerable<string> cloudRoots)
    {
        var roots = cloudRoots.ToList();
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) && !IsCloud(candidate, roots))
                return candidate;
        return null;
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
