using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

/// <summary>In-memory <see cref="ICloudProbe"/>: only the folders in <see cref="Existing"/> exist.</summary>
internal sealed class FakeCloudProbe : ICloudProbe
{
    public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string KeyName, string UserFolder, string? DisplayName)> Accounts { get; } = [];
    public List<string> Roots { get; } = [];
    public Dictionary<string, string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Queried { get; } = [];
    public string UserProfile { get; set; } = @"C:\Users\x";

    /// <summary>When set, every probe method throws it (to prove detection never throws).</summary>
    public Exception? ThrowFromEverything { get; set; }

    /// <summary>When set, every probe method except <see cref="DirectoryExists"/> and <see cref="GetEnvironmentVariable"/> throws it.</summary>
    public Exception? ThrowFromLookups { get; set; }

    public string? GetEnvironmentVariable(string name)
    {
        ThrowIfSet(ThrowFromEverything);
        return Env.GetValueOrDefault(name);
    }

    public IEnumerable<(string KeyName, string UserFolder, string? DisplayName)> GetOneDriveAccounts()
    {
        ThrowIfSet(ThrowFromEverything ?? ThrowFromLookups);
        return Accounts;
    }

    public IEnumerable<string> GetDriveRoots()
    {
        ThrowIfSet(ThrowFromEverything ?? ThrowFromLookups);
        return Roots;
    }

    public string? GetDriveLabel(string root)
    {
        ThrowIfSet(ThrowFromEverything ?? ThrowFromLookups);
        return Labels.GetValueOrDefault(root);
    }

    public string? ReadFileText(string path)
    {
        ThrowIfSet(ThrowFromEverything ?? ThrowFromLookups);
        return Files.GetValueOrDefault(path);
    }

    public bool DirectoryExists(string path)
    {
        ThrowIfSet(ThrowFromEverything);
        Queried.Add(path);
        return Existing.Contains(path);
    }

    private static void ThrowIfSet(Exception? ex)
    {
        if (ex is not null) throw ex;
    }
}
