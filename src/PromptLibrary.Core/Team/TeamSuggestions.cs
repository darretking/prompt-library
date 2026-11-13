using System.Globalization;
using System.Text.Json;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Team;

/// <summary>A prompt suggested by a team member. <c>FilePath</c> is set on suggestions read from disk.</summary>
public sealed record Suggestion(Guid Id, string Title, string Description, string Text, IReadOnlyList<string> Tags,
    string CategoryName, string? Note, string? From, DateTimeOffset CreatedAt, string? FilePath = null);

/// <summary>Whether a suggestion file could be written.</summary>
public enum SuggestionWriteResult { Written, NotWritable }

/// <summary>
/// Suggestion files that members write next to the team file and editors list, accept or dismiss. The folder is
/// untrusted input: only small <c>*.json</c> files directly in it are read, at most 200, and every field is capped.
/// </summary>
public static class TeamSuggestions
{
    public const string FolderName = "Prompt Library suggestions";
    public const string DoneFolderName = "done";
    public const string Kind = "prompt-library-suggestion";
    public const int MaxFiles = 200;
    public const int MaxFileBytes = 64 * 1024;
    public const int MaxTitle = 120;
    public const int MaxCategory = 60;
    public const int MaxNote = 280;
    public const int MaxFrom = 60;
    public const int DoneLimit = 50;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>The folder next to the team file that holds suggestions.</summary>
    public static string SuggestionsFolder(string teamFilePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(teamFilePath)) ?? ".", FolderName);

    /// <summary>Writes <c>&lt;guid&gt;.json</c> into the suggestions folder, creating it. Never throws.</summary>
    public static SuggestionWriteResult TryWrite(string teamFilePath, Suggestion s)
    {
        try
        {
            var folder = SuggestionsFolder(teamFilePath);
            Directory.CreateDirectory(folder);
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["kind"] = Kind,
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["description"] = s.Description,
                ["text"] = s.Text,
                ["tags"] = s.Tags,
                ["categoryName"] = s.CategoryName,
                ["note"] = s.Note,
                ["from"] = s.From,
                ["createdAt"] = s.CreatedAt,
            }, WriteOptions);
            AtomicFile.WriteAllText(Path.Combine(folder, s.Id.ToString("D") + ".json"), json);
            return SuggestionWriteResult.Written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return SuggestionWriteResult.NotWritable;
        }
    }

    /// <summary>The valid suggestions, newest first. Never throws.</summary>
    public static IReadOnlyList<Suggestion> List(string teamFilePath)
    {
        var result = new List<Suggestion>();
        try
        {
            var folder = SuggestionsFolder(teamFilePath);
            if (!Directory.Exists(folder)) return result;
            var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Take(MaxFiles);
            foreach (var path in files)
                if (TryRead(path) is { } s) result.Add(s);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Return what was found.
        }
        return result.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>Moves the suggestion's file to <c>done\</c>, then keeps only the newest 50 there. True if moved. Never throws.</summary>
    public static bool MarkDone(string teamFilePath, Guid id)
    {
        try
        {
            var folder = SuggestionsFolder(teamFilePath);
            var source = Path.Combine(folder, id.ToString("D") + ".json");
            if (!File.Exists(source))
                source = List(teamFilePath).FirstOrDefault(s => s.Id == id)?.FilePath ?? "";
            if (source.Length == 0 || !File.Exists(source)) return false;

            var doneDir = Path.Combine(folder, DoneFolderName);
            Directory.CreateDirectory(doneDir);
            var target = Path.Combine(doneDir, Path.GetFileName(source));
            File.Move(source, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);

            var old = Directory.EnumerateFiles(doneDir)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(DoneLimit)
                .ToList();
            foreach (var f in old)
            {
                try { f.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort. */ }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Plain text for pasting into a message when the suggestion can't be written to the folder.</summary>
    public static string ToClipboardText(Suggestion s)
    {
        var lines = new List<string> { "Prompt suggestion: " + s.Title, "Category: " + s.CategoryName, "", s.Text };
        if (!string.IsNullOrWhiteSpace(s.Note))
        {
            lines.Add("");
            lines.Add("Note: " + s.Note);
        }
        if (!string.IsNullOrWhiteSpace(s.From)) lines.Add("From: " + s.From);
        return string.Join(Environment.NewLine, lines);
    }

    private static Suggestion? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Str(root, "kind") != Kind) return null;

            var title = Cap(Str(root, "title"), MaxTitle);
            var text = Str(root, "text");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text)) return null;

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
                foreach (var e in t.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) tags.Add(e.GetString()!);

            var id = Guid.TryParse(Str(root, "id"), out var g) ? g
                : Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var g2) ? g2 : Guid.NewGuid();
            var created = DateTimeOffset.TryParse(Str(root, "createdAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var c) ? c : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

            var note = Cap(Str(root, "note"), MaxNote);
            var from = Cap(Str(root, "from"), MaxFrom);
            return new Suggestion(id, title, Str(root, "description")?.Trim() ?? "", text, TagRules.Normalize(tags),
                Cap(Str(root, "categoryName"), MaxCategory) ?? "", string.IsNullOrEmpty(note) ? null : note,
                string.IsNullOrEmpty(from) ? null : from, created, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Cap(string? s, int max)
    {
        s = s?.Trim();
        if (s is null || s.Length <= max) return s;
        var cut = max;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut].TrimEnd();
    }
}
