namespace PromptLibrary.App.ViewModels;

/// <summary>Splits text into matched and unmatched pieces for highlighting.</summary>
public static class TextSegments
{
    /// <summary>
    /// <paramref name="text"/> cut at the given (start, length) ranges; the ranges are clipped to the text and may touch.
    /// With no ranges (or empty text) the result is one plain piece (or none for empty text).
    /// </summary>
    public static IReadOnlyList<TextSegment> Split(string? text, IEnumerable<(int Start, int Length)>? marks)
    {
        text ??= "";
        var result = new List<TextSegment>();
        var pos = 0;
        foreach (var (start0, length0) in (marks ?? []).OrderBy(m => m.Start))
        {
            var start = Math.Max(start0, pos);
            var end = Math.Min(text.Length, start0 + length0);
            if (end <= start) continue;
            if (start > pos) result.Add(new TextSegment(text[pos..start], false));
            result.Add(new TextSegment(text[start..end], true));
            pos = end;
        }
        if (pos < text.Length) result.Add(new TextSegment(text[pos..], false));
        return result;
    }
}
