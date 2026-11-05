using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Team;

/// <summary>Reads and writes the shared team library file.</summary>
public static class TeamFile
{
    /// <summary>The file name inside a team folder.</summary>
    public const string FileName = "team-prompts.json";

    /// <summary>The value of the file's <c>kind</c> field.</summary>
    public const string Kind = "prompt-library-team";

    /// <summary>Reads a team file. Throws <see cref="InvalidDataException"/> with a plain message if it is not usable.</summary>
    public static TeamLibrary Parse(string json)
    {
        try
        {
            var raw = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("This is not a team prompts file.");

            if (!(raw["kind"] is JsonValue k && k.TryGetValue<string>(out var kind) && kind == Kind))
                throw new InvalidDataException("This is not a team prompts file.");

            if (!(raw["schemaVersion"] is JsonValue sv && sv.TryGetValue<int>(out var version) && version >= 1))
                throw new InvalidDataException("The team file has a missing or unsupported version.");

            if (!(raw["name"] is JsonValue n && n.TryGetValue<string>(out var name)))
                throw new InvalidDataException("The team file has no name.");

            var updatedAt = DateTimeOffset.MinValue;
            if (raw["updatedAt"] is JsonValue u)
            {
                if (!u.TryGetValue<string>(out var text) || !DateTimeOffset.TryParse(text, out updatedAt))
                    throw new InvalidDataException("The team file has an invalid date.");
            }

            using var doc = JsonDocument.Parse(raw.ToJsonString());
            var content = JsonStore.ValidateLibrary(doc.RootElement);
            content.SchemaVersion = version;

            return new TeamLibrary
            {
                Name = name,
                UpdatedAt = updatedAt,
                SchemaVersion = version,
                Content = content,
                IsNewerSchema = version > 1,
                Raw = raw,
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The team file is not valid: " + ex.Message, ex);
        }
    }

    /// <summary>Writes the team to text, keeping fields in <see cref="TeamLibrary.Raw"/> that this version does not know.</summary>
    public static string Serialize(TeamLibrary team)
    {
        var root = (JsonObject)team.Raw.DeepClone();
        root["kind"] = Kind;
        root["schemaVersion"] = team.SchemaVersion;
        root["name"] = team.Name;
        root["updatedAt"] = JsonSerializer.SerializeToNode(team.UpdatedAt, JsonStore.Options);
        root["categories"] = Merge(root["categories"] as JsonArray, team.Content.Categories, c => c.Id);
        root["prompts"] = Merge(root["prompts"] as JsonArray, team.Content.Prompts, p => p.Id);
        return JsonStore.Serialize(root);
    }

    /// <summary>The SHA-256 (lowercase hex) of the text with line endings normalised to LF.</summary>
    public static string Hash(string json)
    {
        var normalised = json.Replace("\r\n", "\n").Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();
    }

    // Builds the array in item order, updating the existing node with the same id (so unknown fields stay)
    // or creating a new one. Existing nodes whose item is gone are dropped.
    private static JsonArray Merge<T>(JsonArray? existing, IEnumerable<T> items, Func<T, Guid> idOf)
    {
        var byId = new Dictionary<Guid, JsonObject>();
        foreach (var node in existing ?? [])
            if (node is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var id))
                byId[id] = o;

        var result = new JsonArray();
        foreach (var item in items)
        {
            var fresh = (JsonObject)JsonSerializer.SerializeToNode(item, JsonStore.Options)!;
            if (byId.TryGetValue(idOf(item), out var old))
            {
                foreach (var (key, value) in fresh)
                    old[key] = value?.DeepClone();
                result.Add(old.DeepClone());
            }
            else result.Add(fresh);
        }
        return result;
    }
}
