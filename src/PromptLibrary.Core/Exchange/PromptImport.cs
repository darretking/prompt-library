using System.Text;
using System.Text.Json;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Exchange;

/// <summary>What an import did: prompts added, duplicates (same title in the same category) left out, files that held nothing usable.</summary>
public sealed record ImportResult(int Imported, int Duplicates, int Unusable)
{
    /// <summary><c>Imported 12 prompts. Skipped 3 duplicates.</c></summary>
    public string Summary
    {
        get
        {
            var text = $"Imported {Imported} prompt{(Imported == 1 ? "" : "s")}.";
            if (Duplicates > 0) text += $" Skipped {Duplicates} duplicate{(Duplicates == 1 ? "" : "s")}.";
            if (Unusable > 0) text += $" Skipped {Unusable} file{(Unusable == 1 ? "" : "s")} that couldn't be used.";
            return text;
        }
    }
}

/// <summary>
/// Reads prompts into a library from an export <c>.json</c> (or a library.json) or from a folder of <c>.md</c> / <c>.txt</c>
/// files. Categories are matched by name (created when missing); a prompt whose title already exists in its category is
/// skipped. Imported prompts get new ids. The caller saves the library.
/// </summary>
public static class PromptImport
{
    private const long MaxFileBytes = 1024 * 1024;

    /// <summary>Reads an export (or library) JSON file. Throws <see cref="InvalidDataException"/> when it isn't one.</summary>
    public static ImportResult FromJsonFile(Library target, string path, Func<DateTimeOffset> now)
    {
        Library source;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("This isn't a Prompt Library export.");
            if (root.TryGetProperty("format", out var format)
                && format.ValueKind == JsonValueKind.String
                && format.GetString() is { } marker && marker != PromptExport.Format)
                throw new InvalidDataException("This export was made by a newer Prompt Library. Update to import it.");
            if (!root.TryGetProperty("prompts", out _) || !root.TryGetProperty("categories", out _))
                throw new InvalidDataException("This isn't a Prompt Library export.");
            source = JsonStore.ValidateLibrary(root);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This isn't a Prompt Library export: " + ex.Message, ex);
        }

        var editor = new LibraryEditor(target, now);
        var (imported, duplicates, unusable) = (0, 0, 0);
        foreach (var prompt in source.Prompts)
        {
            var category = source.Categories.First(c => c.Id == prompt.CategoryId);
            switch (Add(editor, target, category.Name, category.Emoji, prompt.Title, prompt.Description, prompt.Text,
                        prompt.Tags, prompt.SendTo))
            {
                case Outcome.Added: imported++; break;
                case Outcome.Duplicate: duplicates++; break;
                default: unusable++; break;
            }
        }
        return new ImportResult(imported, duplicates, unusable);
    }

    /// <summary>
    /// Reads every <c>.md</c> and <c>.txt</c> file under <paramref name="folder"/>. The file name is the title and the
    /// folder it is in is the category, unless the file's front matter says otherwise.
    /// </summary>
    public static ImportResult FromFolder(Library target, string folder, Func<DateTimeOffset> now)
    {
        var editor = new LibraryEditor(target, now);
        var (imported, duplicates, unusable) = (0, 0, 0);

        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

        foreach (var file in files)
        {
            string content;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes) { unusable++; continue; }
                content = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unusable++;
                continue;
            }

            var parsed = ParseMarkdown(content);
            var parent = new DirectoryInfo(Path.GetDirectoryName(file)!).Name;
            var title = parsed.Title ?? Path.GetFileNameWithoutExtension(file);
            var category = string.IsNullOrWhiteSpace(parsed.Category) ? parent : parsed.Category!;

            switch (Add(editor, target, category, null, title, parsed.Description ?? "", parsed.Body, parsed.Tags, null))
            {
                case Outcome.Added: imported++; break;
                case Outcome.Duplicate: duplicates++; break;
                default: unusable++; break;
            }
        }
        return new ImportResult(imported, duplicates, unusable);
    }

    private enum Outcome { Added, Duplicate, Unusable }

    private static Outcome Add(LibraryEditor editor, Library target, string categoryName, string? emoji, string title,
        string description, string text, IEnumerable<string>? tags, string? sendTo)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(categoryName))
            return Outcome.Unusable;

        var name = categoryName.Trim();
        var category = target.Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (category is not null
            && target.Prompts.Any(p => p.CategoryId == category.Id
                                       && string.Equals(p.Title.Trim(), title.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return Outcome.Duplicate;

        if (category is null)
        {
            category = editor.AddCategory(name);
            if (!string.IsNullOrWhiteSpace(emoji)) editor.SetCategoryEmoji(category.Id, emoji);
        }
        editor.AddPrompt(category.Id, title, description ?? "", text, tags, sendTo);
        return Outcome.Added;
    }

    /// <summary>One <c>.md</c>/<c>.txt</c> file: optional front matter, then the body (kept exactly).</summary>
    internal sealed record MarkdownFile(string? Title, string? Description, string? Category, List<string> Tags, string Body);

    internal static MarkdownFile ParseMarkdown(string content)
    {
        if (content.Length > 0 && content[0] == '﻿') content = content[1..];
        var none = new MarkdownFile(null, null, null, [], content);

        if (LineEnd(content, 0) is not { } first || content[..first.End].TrimEnd('\r') != "---") return none;

        string? title = null, description = null, category = null;
        var tags = new List<string>();
        var position = first.Next;
        string? lastKey = null;

        while (LineEnd(content, position) is { } line)
        {
            var text = content[position..line.End].TrimEnd('\r');
            position = line.Next;

            if (text == "---")
                return new MarkdownFile(title, description, category, TagRules.Normalize(tags), content[position..]);

            if (lastKey == "tags" && text.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                tags.Add(Unquote(text.TrimStart()[2..].Trim()));
                continue;
            }

            var colon = text.IndexOf(':');
            if (colon <= 0) { lastKey = null; continue; }
            var key = text[..colon].Trim().ToLowerInvariant();
            var value = text[(colon + 1)..].Trim();
            lastKey = key;
            switch (key)
            {
                case "title": title = Unquote(value); break;
                case "description": description = Unquote(value); break;
                case "category": category = Unquote(value); break;
                case "tags": tags.AddRange(ParseList(value)); break;
            }
        }
        return none; // no closing line: it was not front matter
    }

    private static (int End, int Next)? LineEnd(string s, int start)
    {
        if (start >= s.Length) return null;
        var i = s.IndexOf('\n', start);
        return i < 0 ? (s.Length, s.Length) : (i, i + 1);
    }

    private static IEnumerable<string> ParseList(string value)
    {
        if (value.Length == 0) yield break;
        if (value.StartsWith('[') && value.EndsWith(']')) value = value[1..^1];

        var current = new StringBuilder();
        char? quote = null;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (quote is { } q)
            {
                current.Append(ch);
                if (ch == '\\' && q == '"' && i + 1 < value.Length) current.Append(value[++i]);
                else if (ch == q) quote = null;
            }
            else if (ch is '"' or '\'') { quote = ch; current.Append(ch); }
            else if (ch == ',')
            {
                var item = Unquote(current.ToString().Trim());
                current.Clear();
                if (item.Length > 0) yield return item;
            }
            else current.Append(ch);
        }
        var last = Unquote(current.ToString().Trim());
        if (last.Length > 0) yield return last;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < value.Length - 1; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length - 1)
                    sb.Append(value[++i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var c => c });
                else
                    sb.Append(value[i]);
            }
            return sb.ToString();
        }
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        return value;
    }
}
