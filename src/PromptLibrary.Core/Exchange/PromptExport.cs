using System.Text;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Exchange;

/// <summary>
/// Writes a library out so nothing is locked in: one <c>.json</c> file (the library.json schema plus a
/// <c>"format": "prompt-library-export/1"</c> marker) or a folder of Markdown files, one per prompt, in a subfolder per
/// category. Each <c>.md</c> starts with YAML front matter (title, description, category, the category's emoji when it has
/// one, tags, the Send to target when set); the rest of the file is
/// the exact prompt text.
/// </summary>
public static class PromptExport
{
    public const string Format = "prompt-library-export/1";

    /// <summary>The shape of the JSON export: the marker, then the same members as library.json.</summary>
    private sealed class ExportDocument
    {
        public string Format { get; set; } = PromptExport.Format;
        public int SchemaVersion { get; set; } = 1;
        public List<Category> Categories { get; set; } = [];
        public List<Prompt> Prompts { get; set; } = [];
    }

    /// <summary>The whole library as export JSON.</summary>
    public static string ToJson(Library library)
        => JsonStore.Serialize(new ExportDocument { Categories = library.Categories, Prompts = library.Prompts });

    /// <summary>Writes <see cref="ToJson"/> to <paramref name="path"/> (atomically). Returns the number of prompts.</summary>
    public static int WriteJson(string path, Library library)
    {
        AtomicFile.WriteAllText(path, ToJson(library));
        return library.Prompts.Count;
    }

    /// <summary>
    /// Writes one <c>&lt;title&gt;.md</c> per prompt into <c>folder/&lt;category&gt;/</c>. File names are made safe for Windows;
    /// a second file with the same name in a folder becomes <c>name (2).md</c>. Existing files are never overwritten (a
    /// taken name gets the same <c> (2)</c> treatment). Returns the number of prompts written.
    /// </summary>
    public static int WriteMarkdownFolder(string folder, Library library)
    {
        Directory.CreateDirectory(folder);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var categoryFolders = new Dictionary<Guid, string>();
        var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var category in library.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = UniqueName(SafeName(category.Name), usedFolders,
                existsOnDisk: n => Directory.Exists(Path.Combine(folder, n)));
            categoryFolders[category.Id] = name;
        }

        var usedFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prompt in library.Prompts.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var category = library.Categories.FirstOrDefault(c => c.Id == prompt.CategoryId);
            var categoryName = category?.Name ?? "Uncategorised";
            var subfolder = category is not null && categoryFolders.TryGetValue(category.Id, out var f)
                ? f
                : SafeName(categoryName);
            var directory = Path.Combine(folder, subfolder);
            Directory.CreateDirectory(directory);

            if (!usedFiles.TryGetValue(subfolder, out var used))
                usedFiles[subfolder] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var file = UniqueName(SafeName(prompt.Title), used,
                existsOnDisk: n => File.Exists(Path.Combine(directory, n + ".md")));

            File.WriteAllText(Path.Combine(directory, file + ".md"), ToMarkdown(prompt, categoryName, category?.Emoji), utf8);
            count++;
        }
        return count;
    }

    /// <summary>
    /// One prompt as a Markdown file: front matter, then the exact text. The category's emoji (optional) is written as
    /// <c>emoji:</c> so an import gives the category the same icon.
    /// </summary>
    public static string ToMarkdown(Prompt prompt, string categoryName, string? categoryEmoji = null)
    {
        var sb = new StringBuilder();
        sb.Append("---\r\n");
        sb.Append("title: ").Append(Quote(prompt.Title)).Append("\r\n");
        sb.Append("description: ").Append(Quote(prompt.Description)).Append("\r\n");
        sb.Append("category: ").Append(Quote(categoryName)).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(categoryEmoji))
            sb.Append("emoji: ").Append(Quote(categoryEmoji.Trim())).Append("\r\n");
        sb.Append("tags: [").Append(string.Join(", ", (prompt.Tags ?? []).Select(Quote))).Append("]\r\n");
        if (!string.IsNullOrWhiteSpace(prompt.SendTo))
            sb.Append("sendTo: ").Append(Quote(prompt.SendTo)).Append("\r\n");
        sb.Append("---\r\n");
        sb.Append(prompt.Text);
        return sb.ToString();
    }

    /// <summary>A Windows-safe file or folder name (no reserved characters or device names, no trailing dots or spaces).</summary>
    public static string SafeName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 || char.IsControl(ch) ? '_' : ch);

        var safe = sb.ToString().TrimEnd('.', ' ');
        if (safe.Length > 100) safe = safe[..100].TrimEnd('.', ' ');
        if (safe.Length == 0) return "Untitled";
        if (IsDeviceName(safe)) safe = "_" + safe;
        return safe;
    }

    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }

    /// <summary><paramref name="name"/>, or <c>name (2)</c>, <c>name (3)</c>… until it is neither used nor on disk.</summary>
    private static string UniqueName(string name, HashSet<string> used, Func<string, bool> existsOnDisk)
    {
        var candidate = name;
        for (var n = 2; used.Contains(candidate) || existsOnDisk(candidate); n++)
            candidate = $"{name} ({n})";
        used.Add(candidate);
        return candidate;
    }

    /// <summary>A double-quoted YAML string: backslash, quote and line breaks escaped.</summary>
    private static string Quote(string? value)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in value ?? "")
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "",
                '\t' => "\\t",
                _ => ch.ToString(),
            });
        return sb.Append('"').ToString();
    }
}
