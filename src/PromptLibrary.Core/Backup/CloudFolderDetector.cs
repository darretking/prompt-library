using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace PromptLibrary.Core.Backup;

public enum CloudProvider { OneDrive, OneDriveBusiness, GoogleDrive, Dropbox, DropboxBusiness, ICloudDrive }

/// <summary>A detected cloud-synced folder. <paramref name="DisplayName"/> is <c>OneDrive</c>, <c>OneDrive – &lt;org&gt;</c>,
/// <c>Google Drive</c>, <c>Dropbox</c>, <c>Dropbox – Business</c> or <c>iCloud Drive</c>.</summary>
public sealed record CloudFolder(CloudProvider Provider, string DisplayName, string RootPath);

/// <summary>Everything the detector needs from the machine, so tests can fake it.</summary>
public interface ICloudProbe
{
    string? GetEnvironmentVariable(string name);

    /// <summary>OneDrive accounts from <c>HKCU\Software\Microsoft\OneDrive\Accounts</c>: the subkey name
    /// (<c>Personal</c>, <c>Business1</c>, …), its <c>UserFolder</c> and its <c>DisplayName</c>.</summary>
    IEnumerable<(string KeyName, string UserFolder, string? DisplayName)> GetOneDriveAccounts();

    /// <summary>Roots of the ready drives, such as <c>G:\</c>.</summary>
    IEnumerable<string> GetDriveRoots();

    /// <summary>The volume label of a drive root, or null if it can't be read.</summary>
    string? GetDriveLabel(string root);

    bool DirectoryExists(string path);

    /// <summary>The file's text, or null if it is missing or unreadable.</summary>
    string? ReadFileText(string path);

    string UserProfile { get; }
}

/// <summary>
/// Finds cloud-synced folders a backup can go to. Never throws: a probe call that fails counts as "not found".
/// Order: OneDrive, OneDrive business (A–Z), Google Drive, Dropbox, Dropbox business, iCloud Drive.
/// </summary>
public static class CloudFolderDetector
{
    public const string BackupSubfolder = "Prompt Library Backups";

    private const string OneDriveNamePrefix = "OneDrive - ";
    private const string OneDrivePersonalFolder = "OneDrive - Personal";
    private const string GoogleDriveLabel = "Google Drive";

    /// <summary>Google Drive's "My Drive" folder in the languages we know of.</summary>
    private static readonly string[] GoogleMyDriveNames =
    [
        "My Drive", "Mon Drive", "Meine Ablage", "Mi unidad", "Il mio Drive", "Meu Drive", "Mijn Drive",
        "Mój dysk", "Мой диск", "マイドライブ", "내 드라이브", "我的云端硬盘", "我的雲端硬碟",
    ];

    private sealed record Candidate(string Path, string DisplayName);

    public static IReadOnlyList<CloudFolder> Detect(ICloudProbe probe)
    {
        var userProfile = Usable(Safe(() => probe.UserProfile, null));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CloudFolder>();

        var (personal, business) = OneDriveCandidates(probe);
        Add(result, CloudProvider.OneDrive, Existing(personal, probe, seen));
        Add(result, CloudProvider.OneDriveBusiness,
            Existing(business, probe, seen).OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList());

        Add(result, CloudProvider.GoogleDrive, Existing(GoogleDriveCandidates(probe, userProfile), probe, seen));

        var (dropbox, dropboxBusiness) = DropboxCandidates(probe, userProfile);
        Add(result, CloudProvider.Dropbox, Existing(dropbox, probe, seen));
        Add(result, CloudProvider.DropboxBusiness, Existing(dropboxBusiness, probe, seen));

        if (userProfile is not null)
            Add(result, CloudProvider.ICloudDrive,
                Existing([new Candidate(Path.Combine(userProfile, "iCloudDrive"), "iCloud Drive")], probe, seen));

        return result;
    }

    public static string BackupFolderFor(CloudFolder f) => Path.Combine(f.RootPath, BackupSubfolder);

    private static (List<Candidate> Personal, List<Candidate> Business) OneDriveCandidates(ICloudProbe probe)
    {
        var personal = new List<Candidate>();
        var business = new List<Candidate>();

        // Registry accounts first: their key name says personal or business, and DisplayName gives the org name.
        foreach (var (keyName, userFolder, displayName) in Safe(() => probe.GetOneDriveAccounts().ToList(), []))
        {
            var folder = Usable(userFolder);
            if (folder is null) continue;

            if (keyName.Equals("Personal", StringComparison.OrdinalIgnoreCase))
                personal.Add(new Candidate(folder, "OneDrive"));
            else if (keyName.StartsWith("Business", StringComparison.OrdinalIgnoreCase))
            {
                var org = string.IsNullOrWhiteSpace(displayName) ? OrgFromFolderName(folder) : displayName;
                business.Add(new Candidate(folder, BusinessDisplayName(org)));
            }
            else
                AddByFolderName(folder, personal, business);
        }

        // %OneDrive% is only a fallback: with both account kinds signed in it may point at the business folder.
        var consumer = Usable(Env(probe, "OneDriveConsumer"));
        AddByFolderName(consumer ?? Usable(Env(probe, "OneDrive")), personal, business);

        var commercial = Usable(Env(probe, "OneDriveCommercial"));
        if (commercial is not null)
            business.Add(new Candidate(commercial, BusinessDisplayName(OrgFromFolderName(commercial))));

        return (personal, business);
    }

    /// <summary>Without registry information, a folder named "OneDrive - X" is business (org X), except "OneDrive - Personal".</summary>
    private static void AddByFolderName(string? folder, List<Candidate> personal, List<Candidate> business)
    {
        if (folder is null) return;
        var name = FolderName(folder);
        if (name.StartsWith(OneDriveNamePrefix, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(OneDrivePersonalFolder, StringComparison.OrdinalIgnoreCase))
            business.Add(new Candidate(folder, BusinessDisplayName(OrgFromFolderName(folder))));
        else
            personal.Add(new Candidate(folder, "OneDrive"));
    }

    private static string OrgFromFolderName(string folder)
    {
        var name = FolderName(folder);
        return name.StartsWith(OneDriveNamePrefix, StringComparison.OrdinalIgnoreCase) ? name[OneDriveNamePrefix.Length..] : name;
    }

    private static string BusinessDisplayName(string org) => "OneDrive – " + org.Trim();

    private static List<Candidate> GoogleDriveCandidates(ICloudProbe probe, string? userProfile)
    {
        var roots = Safe(() => probe.GetDriveRoots().ToList(), [])
            .Select(Usable)
            .OfType<string>()
            .ToList();

        // Drives labelled "Google Drive" first; any other drive with a My Drive folder counts too.
        var labelled = roots
            .Where(root => string.Equals(Safe(() => probe.GetDriveLabel(root), null), GoogleDriveLabel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var ordered = labelled.Concat(roots.Except(labelled));

        var bases = ordered.ToList();
        if (userProfile is not null)
        {
            bases.Add(Path.Combine(userProfile, "Google Drive"));
            bases.Add(userProfile);
        }

        return bases
            .SelectMany(b => GoogleMyDriveNames.Select(name => new Candidate(Path.Combine(b, name), "Google Drive")))
            .ToList();
    }

    private static (List<Candidate> Personal, List<Candidate> Business) DropboxCandidates(ICloudProbe probe, string? userProfile)
    {
        var personal = new List<Candidate>();
        var business = new List<Candidate>();

        foreach (var variable in new[] { "LOCALAPPDATA", "APPDATA" })
        {
            var appData = Usable(Env(probe, variable));
            if (appData is null) continue;
            var json = Safe(() => probe.ReadFileText(Path.Combine(appData, "Dropbox", "info.json")), null);
            if (json is null) continue;

            var (personalPath, businessPath) = ParseDropboxInfo(json);
            if (personalPath is not null) personal.Add(new Candidate(personalPath, "Dropbox"));
            if (businessPath is not null) business.Add(new Candidate(businessPath, "Dropbox – Business"));
        }

        // The default folder only when Dropbox's own info.json told us nothing (a stale default folder isn't offered).
        if (personal.Count == 0 && business.Count == 0 && userProfile is not null)
            personal.Add(new Candidate(Path.Combine(userProfile, "Dropbox"), "Dropbox"));

        return (personal, business);
    }

    /// <summary>Reads <c>personal.path</c> and <c>business.path</c>; anything malformed gives nulls.</summary>
    private static (string? Personal, string? Business) ParseDropboxInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return (AccountPath(doc.RootElement, "personal"), AccountPath(doc.RootElement, "business"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return (null, null);
        }

        static string? AccountPath(JsonElement root, string account) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(account, out var entry)
            && entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("path", out var path)
            && path.ValueKind == JsonValueKind.String
                ? Usable(path.GetString())
                : null;
    }

    private static void Add(List<CloudFolder> result, CloudProvider provider, IEnumerable<Candidate> found) =>
        result.AddRange(found.Select(c => new CloudFolder(provider, c.DisplayName, c.Path)));

    /// <summary>Candidates that exist and haven't been seen yet (case-insensitive, trailing separator ignored).</summary>
    private static List<Candidate> Existing(IEnumerable<Candidate> candidates, ICloudProbe probe, HashSet<string> seen)
    {
        var found = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            var key = Path.TrimEndingDirectorySeparator(candidate.Path);
            if (seen.Contains(key) || !Safe(() => probe.DirectoryExists(candidate.Path), false)) continue;
            seen.Add(key);
            found.Add(candidate);
        }
        return found;
    }

    private static string? Env(ICloudProbe probe, string name) => Safe(() => probe.GetEnvironmentVariable(name), null);

    /// <summary>The value if it is a full path (never blank or relative), otherwise null.</summary>
    private static string? Usable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;

    private static string FolderName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    /// <summary>Runs a probe call; any exception means "not found".</summary>
    private static T Safe<T>(Func<T> probeCall, T fallback)
    {
        try
        {
            return probeCall();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

/// <summary>Real probe: environment, the OneDrive registry accounts, ready drives and files on disk.</summary>
public sealed class SystemCloudProbe : ICloudProbe
{
    public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? ReadFileText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public string? GetDriveLabel(string root)
    {
        try
        {
            return new DriveInfo(root).VolumeLabel;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IEnumerable<(string KeyName, string UserFolder, string? DisplayName)> GetOneDriveAccounts()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return ReadAccounts();
    }

    [SupportedOSPlatform("windows")]
    private static List<(string KeyName, string UserFolder, string? DisplayName)> ReadAccounts()
    {
        var accounts = new List<(string, string, string?)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            if (key is null) return accounts;
            foreach (var name in key.GetSubKeyNames())
            {
                using var account = key.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is string folder && !string.IsNullOrWhiteSpace(folder))
                    accounts.Add((name, folder, account.GetValue("DisplayName") as string));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry unreadable: report what we have.
        }
        return accounts;
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
