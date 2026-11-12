using System.Text.RegularExpressions;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Views.Controls;

public enum HighlightKind { Placeholder, Heading, Bold, CodeBlock }

/// <summary>A highlighted stretch of the prompt text.</summary>
public readonly record struct HighlightSpan(int Start, int Length, HighlightKind Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds what the prompt editor and its preview colour: placeholders (and the reserved <c>{clipboard}</c>, <c>{date}</c>,
/// <c>{time}</c>, <c>{cursor}</c> tokens), Markdown <c>#</c> headings, fenced code blocks and <c>**bold**</c>.
/// Pure text in, spans out, so it is testable without a window.
/// </summary>
public static class PromptHighlighting
{
    private static readonly Regex Token = new(@"\{(?:clipboard|date|time|cursor)\}", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*[^*\r\n]+?\*\*", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^ {0,3}#{1,6}[ \t]", RegexOptions.Compiled);

    public static IReadOnlyList<HighlightSpan> Compute(string? text)
    {
        var spans = new List<HighlightSpan>();
        if (string.IsNullOrEmpty(text)) return spans;

        foreach (var (start, length, _) in PromptTemplate.Spans(text))
            spans.Add(new HighlightSpan(start, length, HighlightKind.Placeholder));
        foreach (Match m in Token.Matches(text))
            spans.Add(new HighlightSpan(m.Index, m.Length, HighlightKind.Placeholder));

        var inFence = false;
        var fenceStart = 0;
        var pos = 0;
        while (pos < text.Length)
        {
            var end = pos;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            var line = text.AsSpan(pos, end - pos);

            if (line.TrimStart().StartsWith("```"))
            {
                if (!inFence) { inFence = true; fenceStart = pos; }
                else
                {
                    inFence = false;
                    spans.Add(new HighlightSpan(fenceStart, end - fenceStart, HighlightKind.CodeBlock));
                }
            }
            else if (!inFence)
            {
                var s = line.ToString();
                if (Heading.IsMatch(s))
                    spans.Add(new HighlightSpan(pos, end - pos, HighlightKind.Heading));
                else
                    foreach (Match m in Bold.Matches(s))
                        spans.Add(new HighlightSpan(pos + m.Index, m.Length, HighlightKind.Bold));
            }

            // Past the line break (CRLF counts once).
            pos = end;
            if (pos < text.Length && text[pos] == '\r') pos++;
            if (pos < text.Length && text[pos] == '\n') pos++;
        }
        if (inFence) spans.Add(new HighlightSpan(fenceStart, text.Length - fenceStart, HighlightKind.CodeBlock));

        return spans;
    }
}
