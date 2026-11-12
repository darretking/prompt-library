namespace PromptLibrary.Core.Templates;

/// <summary>The kind of input a placeholder needs.</summary>
public enum TemplateFieldKind { Text, Choice }

/// <summary>
/// One fill-in field of a prompt. <see cref="Name"/> is the placeholder's text without braces and hint (for a choice,
/// the whole <c>a / b</c> text); it is the key used by <see cref="PromptTemplate.Fill"/> and compared case-insensitively.
/// </summary>
public sealed record TemplateField(string Name, TemplateFieldKind Kind, IReadOnlyList<string> Options, string? Hint);

/// <summary>Where the dynamic tokens (<c>{clipboard}</c>, <c>{date}</c>, <c>{time}</c>) get their values; tests control it.</summary>
public interface ITokenValues
{
    /// <summary>The clipboard's text, or null when it holds none.</summary>
    string? Clipboard { get; }

    DateTime Now { get; }

    /// <summary>The user's culture, for the short date and time formats.</summary>
    System.Globalization.CultureInfo Culture { get; }
}

/// <summary>
/// The filled text. <see cref="CaretBackSteps"/> is how many characters (a line break counts as one) follow the
/// <c>{cursor}</c> marker, i.e. how many times Left must be pressed after pasting; null when there is no marker.
/// </summary>
public sealed record FillResult(string Text, int? CaretBackSteps);

/// <summary>
/// Finds and fills <c>{placeholders}</c> in prompt text. <c>{name}</c> is a text field, <c>{a / b}</c> a choice,
/// <c>{label, e.g. hint}</c> a text field with a hint. <c>{{</c> and <c>}}</c> are literal braces (always, fields or not).
/// A <c>{</c> is a placeholder only when what is inside looks like a label: it starts with a letter (or is a number), is at
/// most 60 characters on one line, hugs its braces, and has none of <c>; = ( ) [ ] &lt; &gt; ` : #</c> (a quote mark alone is code,
/// an apostrophe in a word is fine). Braces glued to a word or symbol (<c>\frac{a}{b}</c>, <c>${x}</c>), inside a code string
/// (<c>f"{x}"</c>), in inline code or in a fenced code block are literal; a fenced line that is only <c>{label}</c> still counts. The names <c>clipboard</c>,
/// <c>date</c>, <c>time</c> and <c>cursor</c> are reserved dynamic tokens: never form fields (see <see cref="IsToken"/>).
/// </summary>
public static class PromptTemplate
{
    private const int MaxInner = 60;

    private static readonly string[] TokenNames = ["clipboard", "date", "time", "cursor"];

    private sealed record Token(int Start, int Length, TemplateField Field)
    {
        public bool IsToken => PromptTemplate.IsToken(Field.Name);
    }

    /// <summary>True for the reserved dynamic token names (any case): clipboard, date, time, cursor.</summary>
    public static bool IsToken(string name) => TokenNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The distinct fields in order of first appearance (same name in any case = one field).</summary>
    public static IReadOnlyList<TemplateField> Parse(string? text)
    {
        var fields = new List<TemplateField>();
        foreach (var token in Scan(text).Where(t => !t.IsToken))
        {
            var i = fields.FindIndex(f => Same(f.Name, token.Field.Name));
            if (i < 0) fields.Add(token.Field);
            else if (fields[i].Hint is null && token.Field.Hint is not null)
                fields[i] = fields[i] with { Hint = token.Field.Hint };
        }
        return fields;
    }

    /// <summary>True when the text has at least one placeholder.</summary>
    public static bool HasFields(string? text) => Scan(text).Any(t => !t.IsToken);

    /// <summary>Where each placeholder and dynamic token (braces included) is in the text, for highlighting. Escaped braces are not included.</summary>
    public static IReadOnlyList<(int Start, int Length, string Name)> Spans(string? text)
        => Scan(text).Select(t => (t.Start, t.Length, t.Field.Name)).ToList();

    /// <summary>
    /// Replaces every placeholder with its value (looked up by name, ignoring case) and turns <c>{{</c>/<c>}}</c> into
    /// <c>{</c>/<c>}</c>. A missing or empty value leaves the original <c>{…}</c>. Text with no placeholders is returned unchanged.
    /// </summary>
    public static string Fill(string? text, IDictionary<string, string> values) => Fill(text, values, null).Text;

    /// <summary>
    /// As <see cref="Fill(string?, IDictionary{string, string})"/>, and the dynamic tokens are filled from
    /// <paramref name="tokens"/>: <c>{clipboard}</c> (left as is when the clipboard has no text), <c>{date}</c>,
    /// <c>{time}</c>; <c>{cursor}</c> is removed and its place reported as <see cref="FillResult.CaretBackSteps"/>.
    /// With <paramref name="tokens"/> null the tokens are left as written. Filled values are never scanned again.
    /// </summary>
    public static FillResult Fill(string? text, IDictionary<string, string> values, ITokenValues? tokens)
    {
        text ??= "";
        if (text.IndexOfAny(['{', '}']) < 0) return new FillResult(text, null);
        var scan = ScanAll(text);
        var found = scan.Tokens;

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in values) lookup[k] = v;

        var sb = new System.Text.StringBuilder();
        var pos = 0;
        int? cursorAt = null;
        foreach (var t in found)
        {
            AppendUnescaped(sb, text, pos, t.Start, scan.Code);
            var original = text.Substring(t.Start, t.Length);
            if (t.IsToken)
            {
                if (tokens is null) sb.Append(original);
                else if (string.Equals(t.Field.Name, "cursor", StringComparison.OrdinalIgnoreCase))
                    cursorAt ??= sb.Length; // a second {cursor} is just removed
                else sb.Append(TokenText(t.Field.Name, tokens) ?? original);
            }
            else
                sb.Append(lookup.TryGetValue(t.Field.Name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : original);
            pos = t.Start + t.Length;
        }
        AppendUnescaped(sb, text, pos, text.Length, scan.Code);

        var result = sb.ToString();
        int? back = null;
        if (cursorAt is { } at)
            back = new System.Globalization.StringInfo(result[at..].Replace("\r\n", "\n")).LengthInTextElements;
        return new FillResult(result, back);
    }

    private static string? TokenText(string name, ITokenValues tokens) => name.ToLowerInvariant() switch
    {
        "clipboard" => string.IsNullOrEmpty(tokens.Clipboard) ? null : tokens.Clipboard,
        "date" => tokens.Now.ToString("d", tokens.Culture),
        "time" => tokens.Now.ToString("t", tokens.Culture),
        _ => null,
    };

    // Copies text[from..to), turning {{ and }} into { and }; code (fenced or inline) is copied exactly as written.
    private static void AppendUnescaped(System.Text.StringBuilder sb, string text, int from, int to, List<(int Start, int End)> code)
    {
        var i = from;
        while (i < to)
        {
            var at = i;
            var block = code.FindIndex(r => at >= r.Start && at < r.End);
            if (block >= 0)
            {
                var end = Math.Min(code[block].End, to);
                sb.Append(text, i, end - i);
                i = end;
                continue;
            }
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < to && text[i + 1] == c) { sb.Append(c); i += 2; continue; }
            sb.Append(c);
            i++;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static List<Token> Scan(string? text) => ScanAll(text).Tokens;

    private sealed record Scanned(List<Token> Tokens, List<(int Start, int End)> Code);

    private static Scanned ScanAll(string? text)
    {
        var result = new List<Token>();
        if (string.IsNullOrEmpty(text)) return new Scanned(result, []);

        var code = CodeRanges(text);
        var i = 0;
        while (i < text.Length)
        {
            var at = i;
            var block = code.FindIndex(r => at >= r.Start && at < r.End);
            if (block >= 0) { i = code[block].End; continue; }

            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c) { i += 2; continue; }
            if (c != '{') { i++; continue; }

            var close = FindClose(text, i + 1);
            if (close < 0 || InCodeString(text, i)) { i++; continue; }
            // The reserved tokens may touch a word (x{cursor}); other placeholders may not.
            if (!StandsAlone(text, i) && !IsToken(text.Substring(i + 1, close - i - 1).Trim())) { i++; continue; }

            var field = MakeField(text.Substring(i + 1, close - i - 1));
            if (field is null) { i++; continue; }

            result.Add(new Token(i, close - i + 1, field));
            i = close + 1;
        }
        return new Scanned(result, code);
    }

    // Fenced code blocks (``` lines) and inline code (`...` on one line): never placeholders, copied exactly on fill.
    private static List<(int Start, int End)> CodeRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        var lineStart = true;
        while (i < text.Length)
        {
            if (lineStart && IsFence(text, i))
            {
                var end = text.Length;
                for (var k = NextLine(text, i); k < text.Length; k = NextLine(text, k))
                    if (IsFence(text, k)) { end = NextLine(text, k); break; }
                AddFenced(text, i, end, ranges);
                i = end;
                continue;
            }
            var c = text[i];
            lineStart = c == '\n';
            if (c == '`')
            {
                var n = 0;
                while (i + n < text.Length && text[i + n] == '`') n++;
                var close = FindBackticks(text, i + n, n);
                if (close >= 0) { ranges.Add((i, close + n)); i = close + n; lineStart = false; continue; }
                i += n;
                continue;
            }
            i++;
        }
        return ranges;
    }

    // The whole fenced block is code, except a line that is just one placeholder ("{paste code}" on its own line):
    // that is a slot the user fills, the common way to ask for code to be pasted.
    private static void AddFenced(string text, int start, int end, List<(int Start, int End)> ranges)
    {
        var runStart = start;
        for (var k = start; k < end; k = NextLine(text, k))
        {
            var lineEnd = NextLine(text, k);
            var line = text.AsSpan(k, lineEnd - k).Trim();
            var slot = k != start && line.Length > 2 && line[0] == '{' && line[^1] == '}'
                && line[1..^1].IndexOfAny('{', '}') < 0 && MakeField(line[1..^1].ToString()) is not null;
            if (!slot) continue;
            var lead = text.AsSpan(k, lineEnd - k).Length - text.AsSpan(k, lineEnd - k).TrimStart().Length;
            if (k + lead > runStart) ranges.Add((runStart, k + lead));
            runStart = k + lead + line.Length;
        }
        if (runStart < end) ranges.Add((runStart, end));
    }

    // A line that starts (after up to 3 spaces) with three backticks.
    private static bool IsFence(string text, int lineStart)
    {
        var j = lineStart;
        while (j < text.Length && j - lineStart < 3 && text[j] == ' ') j++;
        return string.CompareOrdinal(text, j, "```", 0, 3) == 0;
    }

    private static int NextLine(string text, int from)
    {
        var nl = text.IndexOf('\n', from);
        return nl < 0 ? text.Length : nl + 1;
    }

    // Start of the next run of exactly n backticks on the same line, or -1.
    private static int FindBackticks(string text, int from, int n)
    {
        var i = from;
        while (i < text.Length && text[i] != '\n')
        {
            if (text[i] != '`') { i++; continue; }
            var run = 0;
            while (i + run < text.Length && text[i + run] == '`') run++;
            if (run == n) return i;
            i += run;
        }
        return -1;
    }

    // A "{" glued to a word or a code symbol (\frac{a}{b}, x^{2}, ${x}, #{x}, a{2,3}) is not a placeholder.
    private static bool StandsAlone(string text, int at)
    {
        if (at == 0) return true;
        var p = text[at - 1];
        return !(char.IsLetterOrDigit(p) || p is '_' or '^' or '$' or '#' or '%' or '@' or '\\' or '}' or ']' or ')');
    }

    // True when the "{" sits inside a quoted string of a code line: print("{x}"), x = "{y}", f"{x}", $"{x}", f'{x}'.
    private static bool InCodeString(string text, int at)
    {
        var lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        foreach (var quote in new[] { '"', '\'' })
        {
            var open = -1;
            for (var i = lineStart; i < at; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] != quote) continue;
                open = open < 0 ? i : -1;
            }
            if (open >= 0 && OpensCodeString(text, lineStart, open)) return true;
        }
        return false;
    }

    // A string opened right after f / $ / @ (an interpolated string) or after ( , = [ is code, not prose.
    private static bool OpensCodeString(string text, int lineStart, int open)
    {
        var prefix = open - 1;
        while (prefix >= lineStart && (char.IsLetter(text[prefix]) || text[prefix] is '$' or '@')) prefix--;
        var word = text.Substring(prefix + 1, open - prefix - 1).ToLowerInvariant();
        if (word.Length > 0 && word.All(ch => ch is 'f' or 'r' or '$' or '@') && word.Any(ch => ch is 'f' or '$'))
            return true;
        var k = open - 1;
        while (k >= lineStart && text[k] is ' ' or '\t') k--;
        return k >= lineStart && text[k] is '(' or ',' or '=' or '[';
    }

    // A label starts with a letter (or is a plain number like {5}) and has none of the characters code is made of.
    private static bool LooksLikeLabel(string inner)
    {
        if (inner.Length > MaxInner) return false;
        if (inner.All(char.IsAsciiDigit)) return true;
        // Starts with a letter, or a digit as in {3 words}; "2,3" (a regex repeat) has no word in it.
        if (!(char.IsLetter(inner[0]) || (char.IsAsciiDigit(inner[0]) && inner.Any(char.IsLetter)))) return false;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c is ';' or '=' or '(' or ')' or '[' or ']' or '<' or '>' or '`' or ':' or '#' or '\n' or '\r') return false;
            // An apostrophe inside a word (child's) is fine; a quote mark standing alone is code.
            if (c == '\'' && !(i > 0 && i + 1 < inner.Length && char.IsLetter(inner[i - 1]) && char.IsLetter(inner[i + 1]))) return false;
        }
        return true;
    }

    // Index of the "}" closing a placeholder that starts just before 'from', or -1 if this "{" is literal.
    private static int FindClose(string text, int from)
    {
        var end = Math.Min(text.Length, from + MaxInner + 1);
        for (var j = from; j < end; j++)
        {
            var c = text[j];
            if (c == '}') return j;
            if (c is '{' or '\n' or '\r') return -1;
        }
        return -1;
    }

    private static TemplateField? MakeField(string inner)
    {
        // Padding ({ return x }) is how code is written; a placeholder hugs its braces.
        var trimmed = inner.Trim();
        if (trimmed.Length == 0 || trimmed.Length != inner.Length || !LooksLikeLabel(trimmed)) return null;
        inner = trimmed;

        var options = inner.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (options.Length >= 2)
            return new TemplateField(string.Join(" / ", options), TemplateFieldKind.Choice, options, null);

        var comma = inner.IndexOf(',');
        if (comma > 0 && inner[(comma + 1)..].TrimStart().StartsWith("e.g.", StringComparison.OrdinalIgnoreCase))
        {
            var label = inner[..comma].Trim();
            if (label.Length > 0)
                return new TemplateField(label, TemplateFieldKind.Text, [], inner[(comma + 1)..].Trim());
        }
        return new TemplateField(inner, TemplateFieldKind.Text, [], null);
    }
}
