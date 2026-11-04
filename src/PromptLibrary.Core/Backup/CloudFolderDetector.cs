using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PromptLibrary.Core.Backup;

public enum CloudProvider { OneDrive, OneDriveBusiness, GoogleDrive }

/// <summary>A detected cloud-synced folder. <paramref name="DisplayName"/> is <c>OneDrive</c>,
/// <c>OneDrive – &lt;org&gt;</c> or <c>Google Drive</c>.</summary>
public sealed record CloudFolder(CloudProvider Provider, string DisplayName, string RootPath);

/// <summary>Everything the detector needs from the machine, so tests can fake it.</summary>
public interface ICloudProbe
{
    string? GetEnvironmentVariable(string name);
    IEnumerable<string> GetOneDriveAccountFolders();
    IEnumerable<string> GetDriveRoots();
    bool DirectoryExists(string path);
    string UserProfile { get; }
}

public static class CloudFolderDetector
{
    public const string BackupSubfolder = "Prompt Library Backups";

    private const string BusinessPrefix = "OneDrive - ";
    private const string GoogleMyDrive = "My Drive";

    public static IReadOnlyList<CloudFolder> Detect(ICloudProbe probe)
    {
        var personal = new List<string>();
        var business = new List<string>();

        AddOneDrive(probe.GetEnvironmentVariable("OneDriveConsumer"), personal, business);
        AddOneDrive(probe.GetEnvironmentVariable("OneDrive"), personal, business);
        AddOneDrive(probe.GetEnvironmentVariable("OneDriveCommercial"), personal, business);
        foreach (var folder in probe.GetOneDriveAccountFolders())
            AddOneDrive(folder, personal, business);

        var google = new List<string>();
        foreach (var root in probe.GetDriveRoots())
            google.Add(Path.Combine(root, GoogleMyDrive));
        google.Add(Path.Combine(probe.UserProfile, "Google Drive", GoogleMyDrive));
        google.Add(Path.Combine(probe.UserProfile, GoogleMyDrive));

        var result = new List<CloudFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Existing(personal, probe, seen))
            result.Add(new CloudFolder(CloudProvider.OneDrive, "OneDrive", path));

        var businessFolders = Existing(business, probe, seen)
            .Select(path => new CloudFolder(CloudProvider.OneDriveBusiness, BusinessDisplayName(path), path))
            .OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        result.AddRange(businessFolders);

        foreach (var path in Existing(google, probe, seen))
            result.Add(new CloudFolder(CloudProvider.GoogleDrive, "Google Drive", path));

        return result;
    }

    public static string BackupFolderFor(CloudFolder f) => Path.Combine(f.RootPath, BackupSubfolder);

    private static void AddOneDrive(string? path, List<string> personal, List<string> business)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var target = IsBusinessName(FolderName(path)) ? business : personal;
        target.Add(path);
    }

    private static bool IsBusinessName(string name) =>
        name.StartsWith(BusinessPrefix, StringComparison.OrdinalIgnoreCase);

    private static string FolderName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private static string BusinessDisplayName(string path)
    {
        var name = FolderName(path);
        var org = IsBusinessName(name) ? name[BusinessPrefix.Length..] : name;
        return "OneDrive – " + org;
    }

    /// <summary>Candidates that exist and haven't been seen yet (case-insensitive, trailing separator ignored).</summary>
    private static IEnumerable<string> Existing(IEnumerable<string> candidates, ICloudProbe probe, HashSet<string> seen)
    {
        foreach (var path in candidates)
        {
            var key = Path.TrimEndingDirectorySeparator(path);
            if (seen.Contains(key) || !probe.DirectoryExists(path)) continue;
            seen.Add(key);
            yield return path;
        }
    }
}

/// <summary>Real probe: environment, the OneDrive registry accounts and ready drives.</summary>
public sealed class SystemCloudProbe : ICloudProbe
{
    public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IEnumerable<string> GetOneDriveAccountFolders()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return ReadAccountFolders();
    }

    [SupportedOSPlatform("windows")]
    private static List<string> ReadAccountFolders()
    {
        var folders = new List<string>();
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            if (accounts is null) return folders;
            foreach (var name in accounts.GetSubKeyNames())
            {
                using var account = accounts.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is string folder && !string.IsNullOrWhiteSpace(folder))
                    folders.Add(folder);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry unreadable: report what we have.
        }
        return folders;
    }

    public IEnumerable<string> GetDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady) roots.Add(drive.RootDirectory.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip drives that can't be queried.
            }
        }
        return roots;
    }
}
