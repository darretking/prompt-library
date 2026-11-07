using PromptLibrary.Core.Backup;

namespace PromptLibrary.App.ViewModels;

/// <summary>The words the Join and Start dialogs show for errors, and the shared checks behind them.</summary>
internal static class TeamDialogText
{
    public const string CantReachFolder = "Can't reach that folder.";

    /// <summary>
    /// <c>Can't reach that folder. Is OneDrive running?</c> when the folder is inside a known sync provider's folder,
    /// otherwise <c>Can't reach that folder. Check the path, and that the drive or network is connected.</c> Any other
    /// message passes through unchanged.
    /// </summary>
    public static string Describe(string message, string? provider)
    {
        if (message != CantReachFolder) return message;
        return provider is { Length: > 0 }
            ? $"{CantReachFolder} Is {provider} running?"
            : $"{CantReachFolder} Check the path, and that the drive or network is connected.";
    }

    /// <summary>The sync provider that holds <paramref name="path"/> (null for a link or an unknown place). Off the UI thread.</summary>
    public static string? ProviderOf(string path, Func<IReadOnlyList<CloudFolder>> detect)
    {
        try
        {
            return BackupLocation.ProviderFor(path.Trim().Trim('"'), detect());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
