using System.Text.Json;
using PromptLibrary.Core.Models;
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

    /// <summary>Stores the current copy of every prompt of a team library.</summary>
    public void SnapshotAll(Library team)
        => SetMany(team.Prompts.Select(p => new KeyValuePair<Guid, TeamSeenSnapshot>(p.Id, TeamChange.Snapshot(p, team))));

    public void Remove(Guid id) => _items.Remove(id);

    public void Clear() => _items.Clear();

    /// <summary>Drops snapshots of prompts the team no longer has. Returns true when any were dropped.</summary>
    public bool PruneTo(Library team)
    {
        var ids = team.Prompts.Select(p => p.Id).ToHashSet();
        var gone = _items.Keys.Where(id => !ids.Contains(id)).ToList();
        foreach (var id in gone) _items.Remove(id);
        return gone.Count > 0;
    }

    public void Save() => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(_items, Options));
}
