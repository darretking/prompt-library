using System.Globalization;

namespace PromptLibrary.Core.Backup;

/// <summary>Small rules and texts about the backup folder, shared by Settings, Restore and the main window.</summary>
public static class BackupLocation
{
    private const int PausedPathLength = 40;

    /// <summary>
    /// True when neither the folder nor its parent exists, e.g. a cloud drive that is signed out or a USB
    /// drive that is unplugged. (A missing folder with an existing parent is fine: the first backup creates it.)
    /// A probe that throws counts as "doesn't exist".
    /// </summary>
    public static bool IsUnavailable(string folder, Func<string, bool> directoryExists)
    {
        if (Exists(folder, directoryExists)) return false;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrEmpty(parent) || !Exists(parent, directoryExists);
    }

    /// <summary>How many safety copies are kept in the fallback folder (see <see cref="SafetyFolder"/>).</summary>
    public const int SafetyBackupsToKeep = 10;

    /// <summary>
    /// Where a restore's safety copy of the current library goes: the configured backup folder when it is set and
    /// available, otherwise <paramref name="fallbackFolder"/> (<c>%APPDATA%\PromptLibrary\Safety backups</c>).
    /// Never the folder being restored from, unless that is the configured folder: a browsed folder (an old USB
    /// stick, another PC's cloud folder) must not be written to or pruned.
    /// </summary>
    public static string SafetyFolder(string? configuredFolder, string fallbackFolder, Func<string, bool> directoryExists)
        => configuredFolder is not null && !IsUnavailable(configuredFolder, directoryExists)
            ? configuredFolder
            : fallbackFolder;

    /// <summary>The display name of the cloud folder that contains <paramref name="folder"/> (deepest root wins), or null.</summary>
    public static string? ProviderFor(string folder, IEnumerable<CloudFolder> clouds)
        => clouds
            .Where(c => IsInside(folder, c.RootPath))
            .OrderByDescending(c => c.RootPath.Length)
            .FirstOrDefault()?.DisplayName;

    /// <summary>Same folder, ignoring case and a trailing separator.</summary>
    public static bool SameFolder(string? a, string? b)
        => a is not null && b is not null
           && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
               StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The main window's paused bar: <c>Backups paused: &lt;provider&gt; isn't available. Is it signed in?</c>, or without a
    /// provider <c>Backups paused: &lt;folder&gt; isn't available.</c> with the folder shortened so the bar fits the window.
    /// </summary>
    public static string PausedMessage(string folder, string? provider)
        => provider is null
            ? $"Backups paused: {ShortenMiddle(folder, PausedPathLength)} isn't available."
            : $"Backups paused: {provider} isn't available. Is it signed in?";

    /// <summary><c>&lt;folder&gt; isn't available. Is &lt;provider&gt; signed in?</c> (the question only with a provider).</summary>
    public static string UnavailableText(string folder, string? provider)
        => provider is null
            ? $"{folder} isn't available."
            : $"{folder} isn't available. Is {provider} signed in?";

    /// <summary>Shortens a long path to <paramref name="maxLength"/> characters by replacing its middle with "…".</summary>
    public static string ShortenMiddle(string path, int maxLength)
    {
        if (path.Length <= maxLength || maxLength < 5) return path;
        var head = (maxLength - 1) / 3;
        var tail = maxLength - 1 - head;
        return path[..head] + "…" + path[^tail..];
    }

    /// <summary>A backup's time as shown in the Restore window, e.g. <c>4 Nov 2026, 09:05</c>.</summary>
    public static string BackupDateText(DateTime timestamp)
        => timestamp.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>Today, 09:05</c>, <c>Yesterday, 18:30</c>, otherwise <see cref="BackupDateText"/>.
    /// <paramref name="midSentence"/> gives <c>today</c>/<c>yesterday</c> for use inside a sentence.
    /// </summary>
    public static string RelativeTimeText(DateTime timestamp, DateTime now, bool midSentence = false)
    {
        var time = timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (timestamp.Date == now.Date) return $"{(midSentence ? "today" : "Today")}, {time}";
        if (timestamp.Date == now.Date.AddDays(-1)) return $"{(midSentence ? "yesterday" : "Yesterday")}, {time}";
        return BackupDateText(timestamp);
    }

    /// <summary><c>12 prompts</c>, <c>1 prompt</c>, or <c>Damaged — can't restore</c> for a count of -1 (see <see cref="BackupInfo"/>).</summary>
    public static string PromptCountText(int promptCount)
        => promptCount < 0 ? "Damaged — can't restore" : $"{promptCount} prompt{(promptCount == 1 ? "" : "s")}";

    /// <summary>A Restore window row: <c>Yesterday, 18:30 · 12 prompts</c>.</summary>
    public static string BackupRowText(BackupInfo backup, DateTime now)
        => $"{RelativeTimeText(backup.Timestamp, now)} · {PromptCountText(backup.PromptCount)}";

    private static bool IsInside(string folder, string root)
    {
        var r = Path.TrimEndingDirectorySeparator(root);
        var f = Path.TrimEndingDirectorySeparator(folder);
        if (f.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        return f.StartsWith(r, StringComparison.OrdinalIgnoreCase)
               && f.Length > r.Length
               && (f[r.Length] == Path.DirectorySeparatorChar || f[r.Length] == Path.AltDirectorySeparatorChar);
    }

    private static bool Exists(string path, Func<string, bool> directoryExists)
    {
        try
        {
            return directoryExists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
