namespace PromptLibrary.Core.History;

/// <summary>Whether a line is in both texts, only the new one, or only the old one.</summary>
public enum DiffKind { Same, Added, Removed }

/// <summary>One line of a <see cref="LineDiff"/> (without its line ending).</summary>
public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>A simple line diff (longest common subsequence). Line endings are ignored.</summary>
public static class LineDiff
{
    // Beyond this many line pairs the diff is simplified to "all removed, all added" to stay fast.
    private const long MaxCells = 4_000_000;

    /// <summary>The lines of the new text in order, with the old text's removed lines in between.</summary>
    public static IReadOnlyList<DiffLine> Compute(string? oldText, string? newText)
    {
        var a = Split(oldText);
        var b = Split(newText);
        var result = new List<DiffLine>();

        // Trim the common head and tail first; most edits are small.
        var head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        var tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;

        for (var i = 0; i < head; i++) result.Add(new(DiffKind.Same, a[i]));

        var ao = a[head..^tail];
        var bo = b[head..^tail];
        if ((long)ao.Length * bo.Length > MaxCells)
        {
            result.AddRange(ao.Select(l => new DiffLine(DiffKind.Removed, l)));
            result.AddRange(bo.Select(l => new DiffLine(DiffKind.Added, l)));
        }
        else Middle(ao, bo, result);

        for (var i = a.Length - tail; i < a.Length; i++) result.Add(new(DiffKind.Same, a[i]));
        return result;
    }

    private static void Middle(string[] a, string[] b, List<DiffLine> result)
    {
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { result.Add(new(DiffKind.Same, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) result.Add(new(DiffKind.Removed, a[x++]));
            else result.Add(new(DiffKind.Added, b[y++]));
        }
        while (x < a.Length) result.Add(new(DiffKind.Removed, a[x++]));
        while (y < b.Length) result.Add(new(DiffKind.Added, b[y++]));
    }

    private static string[] Split(string? s)
        => string.IsNullOrEmpty(s) ? [] : s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
