using System.Text.Json;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Updates;

/// <summary>Reads and writes <c>update-check.json</c>. Kept apart from settings.json so background writes never race it.</summary>
public static class UpdateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The saved state, or an empty one when the file is missing or unreadable. Never throws.</summary>
    public static UpdateState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UpdateState();
            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path), Options) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UpdateState();
        }
    }

    /// <summary>Writes atomically; IO errors are thrown for the caller to ignore or report.</summary>
    public static void Save(string path, UpdateState state)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state, Options));
}
