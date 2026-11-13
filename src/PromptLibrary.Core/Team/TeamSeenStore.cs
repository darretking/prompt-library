using System.Text.Json;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Team;

/// <summary>What this PC last saw of a team prompt (to show what changed).</summary>
public sealed record TeamSeenSnapshot(string Title, string Description, string Text, string CategoryName);

/// <summary>A small file beside the settings holding the last seen copy of each team prompt.</summary>
public sealed class TeamSeenStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private Dictionary<Guid, TeamSeenSnapshot> _items = [];

    /// <summary>Reads the file. A missing or unreadable file gives an empty store; this never throws.</summary>
    public void Load()
    {
        try
        {
            _items = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<Guid, TeamSeenSnapshot>>(File.ReadAllText(path), Options) ?? []
                : [];
            // A null snapshot in a damaged file would break callers.
            foreach (var id in _items.Where(p => p.Value is null).Select(p => p.Key).ToList()) _items.Remove(id);
        }
        catch (Exception)
        {
            _items = [];
        }
    }

    public TeamSeenSnapshot? Get(Guid id) => _items.GetValueOrDefault(id);

    public void Set(Guid id, TeamSeenSnapshot snapshot) => _items[id] = snapshot;

    public void SetMany(IEnumerable<KeyValuePair<Guid, TeamSeenSnapshot>> items)
    {
        foreach (var (id, snapshot) in items) _items[id] = snapshot;
    }

    public void Remove(Guid id) => _items.Remove(id);

    public void Clear() => _items.Clear();

    public void Save() => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(_items, Options));
}
