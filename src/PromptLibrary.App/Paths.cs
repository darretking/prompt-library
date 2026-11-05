using System.IO;

namespace PromptLibrary.App;

/// <summary>Where the app keeps its data: <c>%APPDATA%\PromptLibrary\</c>.</summary>
public static class Paths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptLibrary");

    public static string LibraryFile { get; } = Path.Combine(DataFolder, "library.json");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    /// <summary>Unsaved edits kept when the app was closed with nobody to ask (see <c>ExitEdits</c>).</summary>
    public static string RecoveredFolder { get; } = Path.Combine(DataFolder, "Recovered");

    /// <summary>Unexpected errors, appended by the crash handler.</summary>
    public static string CrashLogFile { get; } = Path.Combine(DataFolder, "crash.log");

    /// <summary>Safety copies made before a restore when no backup folder is set or it isn't available.</summary>
    public static string SafetyBackupsFolder { get; } = Path.Combine(DataFolder, "Safety backups");
}
