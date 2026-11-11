using System.Text.Json;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.History;

/// <summary>One earlier saved state of a personal prompt.</summary>
public sealed class PromptVersion
{
    public DateTimeOffset SavedAt { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Keeps the last 50 earlier versions of each personal prompt in <c>&lt;folder&gt;\&lt;promptId&gt;.json</c>.
/// History never blocks a save: on any IO problem the methods return false / an empty list (and log if a crash log is given).
/// </summary>
public sealed class PromptHistoryStore(string folder, string? crashLogPath = null, Func<DateTime>? clock = null)
{
    /// <summary>How many versions are kept per prompt.</summary>
    public const int MaxVersions = 50;

    /// <summary>
    /// Call after a save with the prompt as it was before (<paramref name="previous"/>) and as saved now. If the title,
    /// description, text or tags changed (line endings and tag case do not count), the previous state is stored, stamped with
    /// its own <see cref="Prompt.UpdatedAt"/>. Returns true when a version was stored.
    /// </summary>
    public bool Append(Guid promptId, Prompt previous, Prompt current)
    {
        try
        {
            if (!Changed(previous, current)) return false;
            var versions = Read(promptId);
            versions.Add(new PromptVersion
            {
                SavedAt = previous.UpdatedAt,
                Title = previous.Title,
                Description = previous.Description ?? "",
                Text = previous.Text,
                Tags = [.. previous.Tags ?? []],
            });
            if (versions.Count > MaxVersions) versions.RemoveRange(0, versions.Count - MaxVersions);
            AtomicFile.WriteAllText(PathFor(promptId), JsonStore.Serialize(versions));
            return true;
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Log(ex);
            return false;
        }
    }

    /// <summary>The stored versions, newest first (empty when there are none or the file can't be read).</summary>
    public IReadOnlyList<PromptVersion> List(Guid promptId)
    {
        try
        {
            var versions = Read(promptId);
            versions.Reverse();
            return versions;
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Log(ex);
            return [];
        }
    }

    /// <summary>Removes a prompt's history (for a deleted prompt). True when nothing is left afterwards.</summary>
    public bool Delete(Guid promptId)
    {
        try
        {
            File.Delete(PathFor(promptId));
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true; // no folder, so no history
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Log(ex);
            return false;
        }
    }

    /// <summary>True when the two states differ in title, description, text or tags.</summary>
    public static bool Changed(Prompt a, Prompt b)
        => a.Title != b.Title
           || Lf(a.Description) != Lf(b.Description)
           || Lf(a.Text) != Lf(b.Text)
           || !TagRules.Normalize(a.Tags).SequenceEqual(TagRules.Normalize(b.Tags));

    private string PathFor(Guid promptId) => Path.Combine(folder, promptId.ToString("D") + ".json");

    private static string Lf(string? s) => (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    // Missing or unreadable (corrupt) file reads as no history; real IO errors propagate to the caller's catch.
    private List<PromptVersion> Read(Guid promptId)
    {
        var path = PathFor(promptId);
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<PromptVersion>>(File.ReadAllText(path), JsonStore.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsIo(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private void Log(Exception ex)
    {
        if (crashLogPath is not null) CrashLog.Append(crashLogPath, ex, (clock ?? (() => DateTime.Now))());
    }
}
