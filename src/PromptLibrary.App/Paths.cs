using System.IO;

namespace PromptLibrary.App;

/// <summary>Where the app keeps its data: <c>%APPDATA%\PromptLibrary\</c>.</summary>
public static class Paths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptLibrary");

    public static string LibraryFile { get; } = Path.Combine(DataFolder, "library.json");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    /// <summary>Safety copies made before a restore when no backup folder is set or it isn't available.</summary>
    public static string SafetyBackupsFolder { get; } = Path.Combine(DataFolder, "Safety backups");
}
