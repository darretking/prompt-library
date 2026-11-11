using System.Globalization;
using System.Text.RegularExpressions;

namespace PromptLibrary.Core.Templates;

/// <summary>How a piece of the preview is drawn.</summary>
public enum PreviewPieceKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>A placeholder with a remembered value (or today's date or time): a filled chip showing the value.</summary>
    Filled,

    /// <summary>A placeholder with no value yet: a muted chip showing the field name (a choice shows <c>a / b</c>).</summary>
    Empty,

    /// <summary><c>{clipboard}</c>: a muted chip saying "clipboard".</summary>
    Clipboard,

    /// <summary><c>{cursor}</c>: a thin caret marker, no text.</summary>
    Cursor,
}

/// <summary>A run of text or a chip. <see cref="Bold"/> is <c>**bold**</c> text.</summary>
public sealed record PreviewPiece(PreviewPieceKind Kind, string Text, bool Bold = false);

/// <summary>What kind of block a line (or run of lines) is.</summary>
public enum PreviewBlockKind { Paragraph, Heading, Bullet, Code, Blank }

/// <summary>
/// One block of the preview: a paragraph line, a heading, a bullet (with its marker and nesting level), the lines of a
/// fenced code block (joined by <c>\n</c> text pieces) or an empty line.
/// </summary>
public sealed record PreviewBlock(PreviewBlockKind Kind, IReadOnlyList<PreviewPiece> Pieces, string Marker = "", int Level = 0);

/// <summary>
/// The prompt as it would come out: <see cref="Build"/> replaces placeholders with the remembered values, today's
/// date and time, and shows light Markdown (headings, bold, bullets, fenced code) as blocks. Pure, so it can be tested
/// without a window; the editor draws the blocks.
/// </summary>
public static class PromptPreview
{
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(?<indent>[ \t]*)(?<marker>[-*•]|\d{1,3}[.)])[ \t]+(?<rest>.*)$", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*\r\n]+?)\*\*", RegexOptions.Compiled);

    /// <param name="text">The prompt text.</param>
    /// <param name="values">Remembered values by field name (any case); null or empty for none.</param>
    /// <param name="now">Today, for <c>{date}</c> and <c>{time}</c>.</param>
    /// <param name="culture">The user's culture for the short date and time; the current culture when null.</param>
    public static IReadOnlyList<PreviewBlock> Build(string? text, IDictionary<string, string>? values, DateTime now,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var blocks = new List<PreviewBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is not null)
            foreach (var (k, v) in values)
                if (!string.IsNullOrWhiteSpace(v)) lookup[k] = v;

        var spans = PromptTemplate.Spans(text);
        var fields = PromptTemplate.Parse(text).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var inFence = false;
        List<PreviewPiece>? code = null;
        var pos = 0;
        while (pos <= text.Length)
        {
            var end = pos;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            var line = text[pos..end];

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence) { inFence = true; code = []; }
                else
                {
                    inFence = false;
                    blocks.Add(new PreviewBlock(PreviewBlockKind.Code, code!));
                    code = null;
                }
            }
            else if (inFence)
            {
                if (code!.Count > 0) code.Add(new PreviewPiece(PreviewPieceKind.Text, "\n"));
                code.AddRange(Pieces(text, pos, end, spans, fields, lookup, now, culture, markdown: false));
            }
            else if (line.Trim().Length == 0)
                blocks.Add(new PreviewBlock(PreviewBlockKind.Blank, []));
            else if (Heading.Match(line) is { Success: true } h)
            {
                var offset = h.Groups[2].Index;
                blocks.Add(new PreviewBlock(PreviewBlockKind.Heading,
                    Pieces(text, pos + offset, end, spans, fields, lookup, now, culture, markdown: false),
                    Level: h.Groups[1].Length));
            }
            else if (Bullet.Match(line) is { Success: true } b)
            {
                var marker = b.Groups["marker"].Value;
                var indent = b.Groups["indent"].Value.Replace("\t", "    ").Length;
                blocks.Add(new PreviewBlock(PreviewBlockKind.Bullet,
                    Pieces(text, pos + b.Groups["rest"].Index, end, spans, fields, lookup, now, culture, markdown: true),
                    char.IsDigit(marker[0]) ? marker : "•", Level: Math.Min(indent / 2, 4)));
            }
            else
                blocks.Add(new PreviewBlock(PreviewBlockKind.Paragraph,
                    Pieces(text, pos, end, spans, fields, lookup, now, culture, markdown: true)));

            if (end >= text.Length) break;
            pos = end;
            if (text[pos] == '\r') pos++;
            if (pos < text.Length && text[pos] == '\n') pos++;
            if (pos >= text.Length) break;
        }
        if (inFence && code is not null) blocks.Add(new PreviewBlock(PreviewBlockKind.Code, code)); // never closed
        return blocks;
    }

    private static List<PreviewPiece> Pieces(string text, int start, int end,
        IReadOnlyList<(int Start, int Length, string Name)> spans, IReadOnlyDictionary<string, TemplateField> fields,
        IReadOnlyDictionary<string, string> values, DateTime now, CultureInfo culture, bool markdown)
    {
        var pieces = new List<PreviewPiece>();
        var cursor = start;
        foreach (var (s, length, name) in spans)
        {
            if (s < start || s + length > end || s < cursor) continue;
            AddText(pieces, text[cursor..s], markdown);
            pieces.Add(Chip(name, fields, values, now, culture));
            cursor = s + length;
        }
        AddText(pieces, text[cursor..end], markdown);
        return pieces;
    }

    private static PreviewPiece Chip(string name, IReadOnlyDictionary<string, TemplateField> fields,
        IReadOnlyDictionary<string, string> values, DateTime now, CultureInfo culture)
    {
        if (PromptTemplate.IsToken(name))
            return name.ToLowerInvariant() switch
            {
                "date" => new PreviewPiece(PreviewPieceKind.Filled, now.ToString("d", culture)),
                "time" => new PreviewPiece(PreviewPieceKind.Filled, now.ToString("t", culture)),
                "cursor" => new PreviewPiece(PreviewPieceKind.Cursor, ""),
                _ => new PreviewPiece(PreviewPieceKind.Clipboard, "clipboard"),
            };

        // The field's own name (a choice's is the whole "a / b"), as Parse found it; a hint is not part of it.
        return values.TryGetValue(name, out var value)
            ? new PreviewPiece(PreviewPieceKind.Filled, value)
            : new PreviewPiece(PreviewPieceKind.Empty, fields.TryGetValue(name, out var f) ? f.Name : name);
    }

    private static void AddText(List<PreviewPiece> pieces, string raw, bool markdown)
    {
        if (raw.Length == 0) return;
        var plain = Unescape(raw);
        if (!markdown)
        {
            pieces.Add(new PreviewPiece(PreviewPieceKind.Text, plain));
            return;
        }

        // Bold is found in the raw text (escaped braces can't contain it) and each part is unescaped on its own.
        var at = 0;
        foreach (Match m in Bold.Matches(raw))
        {
            if (m.Index > at) pieces.Add(new PreviewPiece(PreviewPieceKind.Text, Unescape(raw[at..m.Index])));
            pieces.Add(new PreviewPiece(PreviewPieceKind.Text, Unescape(m.Groups[1].Value), Bold: true));
            at = m.Index + m.Length;
        }
        if (at < raw.Length) pieces.Add(new PreviewPiece(PreviewPieceKind.Text, Unescape(raw[at..])));
    }

    private static string Unescape(string s) => s.Replace("{{", "{").Replace("}}", "}");
}
