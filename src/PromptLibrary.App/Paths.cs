using System.IO;

namespace PromptLibrary.App;

/// <summary>Where the app keeps its data: <c>%APPDATA%\PromptLibrary\</c>.</summary>
public static class Paths
{
    /// <summary>
    /// Developer/test switch: when the <c>PROMPTLIBRARY_DATA_DIR</c> environment variable holds an absolute path, the app
    /// keeps all its data there instead, runs beside the normal copy, and skips update checks. Used for screenshots and
    /// reviews so the owner's real library is never touched.
    /// </summary>
    public static string? SandboxFolder { get; } =
        Environment.GetEnvironmentVariable("PROMPTLIBRARY_DATA_DIR") is { Length: > 0 } dir && Path.IsPathFullyQualified(dir)
            ? dir : null;

    public static bool IsSandbox => SandboxFolder is not null;

    /// <summary>Suffix for named kernel objects (single-instance mutex, show event) so a sandbox copy runs alongside.</summary>
    public static string InstanceSuffix { get; } = SandboxFolder is { } s
        ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.ToUpperInvariant())))[..12]
        : "";

    public static string DataFolder { get; } = SandboxFolder ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptLibrary");

    public static string LibraryFile { get; } = Path.Combine(DataFolder, "library.json");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    /// <summary>Unsaved edits kept when the app was closed with nobody to ask (see <c>ExitEdits</c>).</summary>
    public static string RecoveredFolder { get; } = Path.Combine(DataFolder, "Recovered");

    /// <summary>Unexpected errors, appended by the crash handler.</summary>
    public static string CrashLogFile { get; } = Path.Combine(DataFolder, "crash.log");

    /// <summary>Safety copies made before a restore when no backup folder is set or it isn't available.</summary>
    public static string SafetyBackupsFolder { get; } = Path.Combine(DataFolder, "Safety backups");

    /// <summary>When the app last asked GitHub for a newer version, and what it was told (see <c>UpdateStore</c>).</summary>
    public static string UpdateStateFile { get; } = Path.Combine(DataFolder, "update-check.json");

    /// <summary>The offline copy of the joined team library (see <c>TeamCache</c>).</summary>
    public static string TeamCacheFile { get; } = Path.Combine(DataFolder, "team-cache.json");

    /// <summary>Where a downloaded installer waits to be run: <c>%TEMP%\PromptLibrary\</c>.</summary>
    public static string UpdateDownloadFolder { get; } = Path.Combine(Path.GetTempPath(), "PromptLibrary");
}
