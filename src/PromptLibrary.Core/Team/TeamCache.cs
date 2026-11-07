using System.Globalization;
using System.Text.Json.Nodes;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Team;

/// <summary>The last good copy of the team library, kept so it works offline.</summary>
public static class TeamCache
{
    /// <summary>Saves the copy atomically.</summary>
    public static void Save(string path, TeamSource source, string json, DateTimeOffset fetchedAt)
    {
        var root = new JsonObject
        {
            ["kind"] = source.Kind.ToString(),
            ["location"] = source.Location,
            ["fetchedAt"] = fetchedAt.ToString("O", CultureInfo.InvariantCulture),
            ["json"] = json,
        };
        AtomicFile.WriteAllText(path, JsonStore.Serialize(root));
    }

    /// <summary>The saved copy, or null when there is none or it can't be read.</summary>
    public static (TeamSource Source, TeamLibrary Team, string Json, DateTimeOffset FetchedAt)? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return null;
            var kind = Enum.Parse<TeamSourceKind>(root["kind"]!.GetValue<string>());
            var location = root["location"]!.GetValue<string>();
            var json = root["json"]!.GetValue<string>();
            var fetched = DateTimeOffset.Parse(root["fetchedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            return (new TeamSource(kind, location), TeamFile.Parse(json), json, fetched);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Removes the saved copy, if any.</summary>
    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Applies a read result to the offline copy.</summary>
public static class TeamRefresh
{
    /// <summary>
    /// Saves the copy only when the read was <see cref="TeamReadStatus.Ok"/>, so a bad or missing read keeps the last
    /// good copy. A cache that can't be written is left as it was and counts as no change. Returns true when the library changed (its hash differs from the saved copy, or there was none).
    /// </summary>
    public static bool Apply(TeamReadResult r, string cachePath, TeamSource s, DateTimeOffset now)
    {
        if (r.Status != TeamReadStatus.Ok || r.Json is null) return false;
        var before = TeamCache.Load(cachePath);
        var changed = before is null || TeamFile.Hash(before.Value.Json) != r.Hash;
        try
        {
            TeamCache.Save(cachePath, s, r.Json, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return changed;
    }
}
