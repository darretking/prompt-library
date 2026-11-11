namespace PromptLibrary.Core.Templates;

/// <summary>The kind of input a placeholder needs.</summary>
public enum TemplateFieldKind { Text, Choice }

/// <summary>
/// One fill-in field of a prompt. <see cref="Name"/> is the placeholder's text without braces and hint (for a choice,
/// the whole <c>a / b</c> text); it is the key used by <see cref="PromptTemplate.Fill"/> and compared case-insensitively.
/// </summary>
public sealed record TemplateField(string Name, TemplateFieldKind Kind, IReadOnlyList<string> Options, string? Hint);

/// <summary>
/// Finds and fills <c>{placeholders}</c> in prompt text. <c>{name}</c> is a text field, <c>{a / b}</c> a choice,
/// <c>{label, e.g. hint}</c> a text field with a hint. <c>{{</c> and <c>}}</c> are literal braces. A <c>{</c> with no
/// closing <c>}</c> on the same line (or more than 60 characters on) is literal text.
/// </summary>
public static class PromptTemplate
{
    private const int MaxInner = 60;

    private sealed record Token(int Start, int Length, TemplateField Field);

    /// <summary>The distinct fields in order of first appearance (same name in any case = one field).</summary>
    public static IReadOnlyList<TemplateField> Parse(string? text)
    {
        var fields = new List<TemplateField>();
        foreach (var token in Scan(text))
        {
            var i = fields.FindIndex(f => Same(f.Name, token.Field.Name));
            if (i < 0) fields.Add(token.Field);
            else if (fields[i].Hint is null && token.Field.Hint is not null)
                fields[i] = fields[i] with { Hint = token.Field.Hint };
        }
        return fields;
    }

    /// <summary>True when the text has at least one placeholder.</summary>
    public static bool HasFields(string? text) => Scan(text).Count > 0;

    /// <summary>Where each placeholder (braces included) is in the text, for highlighting. Escaped braces are not included.</summary>
    public static IReadOnlyList<(int Start, int Length, string Name)> Spans(string? text)
        => Scan(text).Select(t => (t.Start, t.Length, t.Field.Name)).ToList();

    /// <summary>
    /// Replaces every placeholder with its value (looked up by name, ignoring case) and turns <c>{{</c>/<c>}}</c> into
    /// <c>{</c>/<c>}</c>. A missing or empty value leaves the original <c>{…}</c>. Text with no placeholders is returned unchanged.
    /// </summary>
    public static string Fill(string? text, IDictionary<string, string> values)
    {
        text ??= "";
        var tokens = Scan(text);
        if (tokens.Count == 0) return text;

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in values) lookup[k] = v;

        var sb = new System.Text.StringBuilder();
        var pos = 0;
        foreach (var t in tokens)
        {
            sb.Append(Unescape(text.AsSpan(pos, t.Start - pos)));
            var original = text.Substring(t.Start, t.Length);
            sb.Append(lookup.TryGetValue(t.Field.Name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : original);
            pos = t.Start + t.Length;
        }
        sb.Append(Unescape(text.AsSpan(pos)));
        return sb.ToString();
    }

    private static string Unescape(ReadOnlySpan<char> s) => s.ToString().Replace("{{", "{").Replace("}}", "}");

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static List<Token> Scan(string? text)
    {
        var result = new List<Token>();
        if (string.IsNullOrEmpty(text)) return result;

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c) { i += 2; continue; }
            if (c != '{') { i++; continue; }

            var close = FindClose(text, i + 1);
            if (close < 0) { i++; continue; }

            var field = MakeField(text.Substring(i + 1, close - i - 1));
            if (field is null) { i++; continue; }

            result.Add(new Token(i, close - i + 1, field));
            i = close + 1;
        }
        return result;
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
        inner = inner.Trim();
        if (inner.Length == 0) return null;

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
