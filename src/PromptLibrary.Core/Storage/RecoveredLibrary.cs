using System.Globalization;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Storage;

/// <summary>
/// A copy of the whole library written to <c>Recovered\</c> when changes couldn't be saved to library.json before
/// the app quit, so they aren't lost. library.json itself is never touched here.
/// </summary>
public static class RecoveredLibrary
{
    /// <summary><c>library-yyyy-MM-dd_HHmmss.json</c> (local time).</summary>
    public static string FileName(DateTime now)
        => $"library-{now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}.json";

    /// <summary>
    /// Writes <paramref name="library"/> into <paramref name="folder"/> (created if needed) in library.json's format
    /// and returns the path. A file from the same second is never overwritten: the name then ends in <c>-2</c>, <c>-3</c>…
    /// </summary>
    public static string Write(string folder, Library library, DateTime now)
    {
        var name = Path.GetFileNameWithoutExtension(FileName(now));
        var path = Path.Combine(folder, name + ".json");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{name}-{n}.json");

        JsonStore.SaveLibrary(path, library, keepPreviousSave: false);
        return path;
    }
}
