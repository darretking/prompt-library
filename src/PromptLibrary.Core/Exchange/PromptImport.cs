using System.Text;
using System.Text.Json;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Send;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Exchange;

/// <summary>
/// What happened to one file of an import: prompts added, duplicates (same title in the same category) left out,
/// prompts in it that had no title or text, and, when the whole file couldn't be used, <see cref="FailureReason"/>
/// (plain words: <c>couldn't be read</c>, <c>empty</c>, <c>larger than 1 MB</c>, <c>damaged or not a prompt file</c>, <c>not a Prompt Library export</c>).
/// </summary>
public sealed record ImportFileOutcome(string File, int Imported, int Duplicates, int UnusablePrompts, string? FailureReason)
{
    public bool Failed => FailureReason is not null || UnusablePrompts > 0;

    /// <summary>The reason shown beside the file name, or null when nothing went wrong.</summary>
    public string? Problem => FailureReason
        ?? (UnusablePrompts > 0 ? $"{UnusablePrompts} prompt{(UnusablePrompts == 1 ? "" : "s")} with no title or text" : null);
}

/// <summary>A file that couldn't be (fully) used and why, for the import results list.</summary>
public sealed record ImportFailure(string File, string Reason);

/// <summary>What an import did, in total and file by file. Never thrown: a bad file is a failed outcome.</summary>
public sealed record ImportResult(IReadOnlyList<ImportFileOutcome> Files)
{
    public int Imported => Files.Sum(f => f.Imported);

    public int Duplicates => Files.Sum(f => f.Duplicates);

    /// <summary>Files that couldn't be used (or held prompts that couldn't).</summary>
    public int Unusable => Files.Count(f => f.Failed);

    public IReadOnlyList<ImportFailure> Failures
        => Files.Where(f => f.Failed).Select(f => new ImportFailure(f.File, f.Problem!)).ToList();

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

    internal static ImportResult Of(params ImportFileOutcome[] files) => new(files);
}

/// <summary>
/// Reads prompts into a library from an export <c>.json</c> (or a library.json) or from a folder of <c>.md</c> / <c>.txt</c>
/// files. Categories are matched by name (created when missing); a prompt whose title already exists in its category is
/// skipped. Imported prompts get new ids. The caller saves the library.
/// </summary>
public static class PromptImport
{
    private const long MaxFileBytes = 1024 * 1024;
    private const long MaxJsonBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Reads an export (or library) JSON file. A file that can't be used (unreadable, empty, too large, invalid JSON, not
    /// an export, made by a newer app) comes back as a failed outcome, never as an exception.
    /// </summary>
    public static ImportResult FromJsonFile(Library target, string path, Func<DateTimeOffset> now)
    {
        var name = Path.GetFileName(path);
        Library source;
        try
        {
            var size = new FileInfo(path).Length;
            if (size == 0) return Fail(name, "empty");
            if (size > MaxJsonBytes) return Fail(name, "larger than 64 MB");
            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content)) return Fail(name, "empty");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(name, "not a Prompt Library export");
            if (root.TryGetProperty("format", out var format)
                && format.ValueKind == JsonValueKind.String
                && format.GetString() is { } marker && marker != PromptExport.Format)
                return Fail(name, "made by a newer Prompt Library");
            if (!root.TryGetProperty("prompts", out _) || !root.TryGetProperty("categories", out _))
                return Fail(name, "not a Prompt Library export");
            source = JsonStore.ValidateLibrary(root);
        }
        catch (JsonException)
        {
            return Fail(name, "damaged or not a prompt file");
        }
        catch (InvalidDataException)
        {
            return Fail(name, "not a Prompt Library export");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Fail(name, "couldn't be read");
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
        return ImportResult.Of(new ImportFileOutcome(name, imported, duplicates, unusable, null));
    }

    private static ImportResult Fail(string file, string reason)
        => ImportResult.Of(new ImportFileOutcome(file, 0, 0, 0, reason));

    /// <summary>
    /// Reads every <c>.md</c> and <c>.txt</c> file under <paramref name="folder"/>. The file name is the title and the
    /// folder it is in is the category, unless the file's front matter says otherwise.
    /// </summary>
    public static ImportResult FromFolder(Library target, string folder, Func<DateTimeOffset> now)
    {
        var editor = new LibraryEditor(target, now);
        var outcomes = new List<ImportFileOutcome>();

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(Path.GetFileName(folder.TrimEnd('\\', '/')), "couldn't be read");
        }

        foreach (var file in files)
        {
            var name = Path.GetRelativePath(folder, file);
            string content;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes)
                {
                    outcomes.Add(new ImportFileOutcome(name, 0, 0, 0, "larger than 1 MB"));
                    continue;
                }
                content = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new ImportFileOutcome(name, 0, 0, 0, "couldn't be read"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                outcomes.Add(new ImportFileOutcome(name, 0, 0, 0, "empty"));
                continue;
            }

            var parsed = ParseMarkdown(content);
            var parent = new DirectoryInfo(Path.GetDirectoryName(file)!).Name;
            var title = parsed.Title ?? Path.GetFileNameWithoutExtension(file);
            var category = string.IsNullOrWhiteSpace(parsed.Category) ? parent : parsed.Category!;

            outcomes.Add(Add(editor, target, category, parsed.Emoji, title, parsed.Description ?? "", parsed.Body, parsed.Tags,
                SendTargets.Find(parsed.SendTo)?.Id) switch
            {
                Outcome.Added => new ImportFileOutcome(name, 1, 0, 0, null),
                Outcome.Duplicate => new ImportFileOutcome(name, 0, 1, 0, null),
                _ => new ImportFileOutcome(name, 0, 0, 0, "empty"),
            });
        }
        return new ImportResult(outcomes);
    }

    private enum Outcome { Added, Duplicate, Unusable }

    private static Outcome Add(LibraryEditor editor, Library target, string categoryName, string? emoji, string title,
        string description, string text, IEnumerable<string>? tags, string? sendTo)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(categoryName))
            return Outcome.Unusable;

        var name = categoryName.Trim();
        var category = target.Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        // The category's emoji (icon) is optional. A new category takes it, and an existing one without an emoji gets it;
        // one that already has an emoji keeps its own. Anything that isn't a single emoji is ignored.
        var icon = UsableEmoji(emoji);
        if (category is not null && icon is not null && string.IsNullOrWhiteSpace(category.Emoji))
            editor.SetCategoryEmoji(category.Id, icon);

        if (category is not null
            && target.Prompts.Any(p => p.CategoryId == category.Id
                                       && string.Equals(p.Title.Trim(), title.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return Outcome.Duplicate;

        if (category is null)
        {
            category = editor.AddCategory(name);
            if (icon is not null) editor.SetCategoryEmoji(category.Id, icon);
        }
        editor.AddPrompt(category.Id, title, description ?? "", text, tags, sendTo);
        return Outcome.Added;
    }

    /// <summary>The trimmed emoji when it is a single emoji (one text element, at most 16 UTF-16 units, not a letter or digit), else null.</summary>
    internal static string? UsableEmoji(string? emoji)
    {
        var e = emoji?.Trim();
        if (string.IsNullOrEmpty(e) || e.Length > 16 || char.IsLetterOrDigit(e, 0)) return null;
        return new System.Globalization.StringInfo(e).LengthInTextElements == 1 ? e : null;
    }

    /// <summary>One <c>.md</c>/<c>.txt</c> file: optional front matter, then the body (kept exactly). <c>emoji:</c> (or
    /// <c>icon:</c>) is the category's optional emoji.</summary>
    internal sealed record MarkdownFile(string? Title, string? Description, string? Category, List<string> Tags, string Body,
        string? Emoji = null, string? SendTo = null);

    internal static MarkdownFile ParseMarkdown(string content)
    {
        if (content.Length > 0 && content[0] == '﻿') content = content[1..];
        var none = new MarkdownFile(null, null, null, [], content);

        if (LineEnd(content, 0) is not { } first || content[..first.End].TrimEnd('\r') != "---") return none;

        string? title = null, description = null, category = null, emoji = null, sendTo = null;
        var tags = new List<string>();
        var position = first.Next;
        string? lastKey = null;

        while (LineEnd(content, position) is { } line)
        {
            var text = content[position..line.End].TrimEnd('\r');
            position = line.Next;

            if (text == "---")
                return new MarkdownFile(title, description, category, TagRules.Normalize(tags), content[position..], emoji, sendTo);

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
                case "emoji" or "icon": emoji = Unquote(value); break;
                case "sendto": sendTo = Unquote(value); break;
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
