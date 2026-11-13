using System.Globalization;
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

    /// <summary>Longest team note text. A longer note is rejected (read as no note), never cut.</summary>
    public const int MaxNoteLength = 280;

    /// <summary>Most fill-in values read; later entries are dropped.</summary>
    public const int MaxFillInValues = 50;

    /// <summary>Longest fill-in value; an entry with a longer value is dropped.</summary>
    public const int MaxFillInValueLength = 200;

    /// <summary>Reads a team file (a repeated id gets a new one, see <see cref="TeamIds"/>). Throws <see cref="InvalidDataException"/> with a plain message if it is not usable.</summary>
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
                if (!u.TryGetValue<string>(out var text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out updatedAt))
                    throw new InvalidDataException("The team file has an invalid date.");
            }

            using var doc = JsonDocument.Parse(raw.ToJsonString());
            var content = JsonStore.ValidateLibrary(doc.RootElement);
            content.SchemaVersion = version;
            TeamIds.ResolveClashes(content); // a repeated id never breaks a lookup or a save

            return new TeamLibrary
            {
                Name = name,
                UpdatedAt = updatedAt,
                SchemaVersion = version,
                Content = content,
                IsNewerSchema = version > 1,
                Note = ReadNote(raw["note"]),
                FillInValues = ReadFillInValues(raw["fillInValues"]),
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
        root["updatedAt"] = team.UpdatedAt.ToString("O", CultureInfo.InvariantCulture);
        WriteNote(root, team.Note);
        WriteFillInValues(root, team.FillInValues);
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

    private static TeamNote? ReadNote(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        if (!(o["text"] is JsonValue t && t.TryGetValue<string>(out var text)) || text.Length == 0 || text.Length > MaxNoteLength)
            return null;
        var at = DateTimeOffset.MinValue;
        if (o["at"] is JsonValue a && a.TryGetValue<string>(out var s))
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at);
        return new TeamNote(text, at);
    }

    // Keys are lower-cased and trimmed (a repeat keeps the first); bad entries are dropped, not errors.
    private static Dictionary<string, string> ReadFillInValues(JsonNode? node)
    {
        var result = new Dictionary<string, string>();
        if (node is not JsonObject o) return result;
        foreach (var (key, value) in o)
        {
            if (result.Count >= MaxFillInValues) break;
            var k = key.Trim().ToLowerInvariant();
            if (k.Length == 0 || !(value is JsonValue v && v.TryGetValue<string>(out var s)) || s.Length > MaxFillInValueLength) continue;
            result.TryAdd(k, s);
        }
        return result;
    }

    // Written only when the model has one; null or empty removes the field (that is how an owner clears it).
    private static void WriteNote(JsonObject root, TeamNote? note)
    {
        if (note is null) root.Remove("note");
        else root["note"] = new JsonObject
        {
            ["text"] = note.Text,
            ["at"] = note.At.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static void WriteFillInValues(JsonObject root, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0) { root.Remove("fillInValues"); return; }
        var o = new JsonObject();
        foreach (var (key, value) in values) o[key] = value;
        root["fillInValues"] = o;
    }

    // Builds the array in item order, updating the existing node with the same id (so unknown fields stay)
    // or creating a new one. Existing nodes whose item is gone are dropped. With a repeated id the first node is used
    // (a later one's item now has a new id, see TeamIds).
    private static readonly string[] OptionalKeys = ["featured", "sourceTeamPromptId", "sourceFingerprint"];

    private static JsonArray Merge<T>(JsonArray? existing, IEnumerable<T> items, Func<T, Guid> idOf)
    {
        var byId = new Dictionary<Guid, JsonObject>();
        foreach (var node in existing ?? [])
            if (node is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var id))
                byId.TryAdd(id, o);

        var result = new JsonArray();
        foreach (var item in items)
        {
            var fresh = (JsonObject)JsonSerializer.SerializeToNode(item, JsonStore.Options)!;
            if (byId.TryGetValue(idOf(item), out var old))
            {
                // Fields the serializer leaves out while empty must also leave the old node (e.g. featured turned off).
                foreach (var key in OptionalKeys)
                    if (!fresh.ContainsKey(key)) old.Remove(key);
                foreach (var (key, value) in fresh)
                    old[key] = value?.DeepClone();
                result.Add(old.DeepClone());
            }
            else result.Add(fresh);
        }
        return result;
    }
}
