using PromptLibrary.Core.History;

namespace PromptLibrary.Core.Tests.History;

public class LineDiffTests
{
    private static string Render(string a, string b)
        => string.Join("|", LineDiff.Compute(a, b).Select(l => (l.Kind switch { DiffKind.Same => " ", DiffKind.Added => "+", _ => "-" }) + l.Text));

    [Fact]
    public void Identical_AllSame() => Assert.Equal(" a| b", Render("a\nb", "a\nb"));

    [Fact]
    public void AddedLine() => Assert.Equal(" a|+x| b", Render("a\nb", "a\nx\nb"));

    [Fact]
    public void RemovedLine() => Assert.Equal(" a|-x| b", Render("a\nx\nb", "a\nb"));

    [Fact]
    public void ChangedLine_IsRemovedThenAdded() => Assert.Equal(" a|-b|+c| d", Render("a\nb\nd", "a\nc\nd"));

    [Fact]
    public void LineEndingsAreIgnored() => Assert.Equal(" a| b", Render("a\r\nb", "a\nb"));

    [Fact]
    public void EmptyOld_AllAdded() => Assert.Equal("+a|+b", Render("", "a\nb"));

    [Fact]
    public void EmptyNew_AllRemoved() => Assert.Equal("-a|-b", Render("a\nb", ""));

    [Fact]
    public void BothEmpty_NoLines() => Assert.Empty(LineDiff.Compute("", null));

    [Fact]
    public void Reordering_UsesLongestCommonRun() => Assert.Equal("-a| b| c|+a", Render("a\nb\nc", "b\nc\na"));

    [Fact]
    public void RemovedPlusSameLines_RebuildTheOldText()
    {
        const string a = "one\ntwo\nthree\nfour";
        const string b = "one\n2\nthree\nfour\nfive";
        var diff = LineDiff.Compute(a, b);
        Assert.Equal(a, string.Join("\n", diff.Where(l => l.Kind != DiffKind.Added).Select(l => l.Text)));
        Assert.Equal(b, string.Join("\n", diff.Where(l => l.Kind != DiffKind.Removed).Select(l => l.Text)));
    }

    [Fact]
    public void VeryLongInputs_FallBackQuickly()
    {
        var a = string.Join("\n", Enumerable.Range(0, 3000).Select(i => "a" + i));
        var b = string.Join("\n", Enumerable.Range(0, 3000).Select(i => "b" + i));
        var diff = LineDiff.Compute(a, b);
        Assert.Equal(3000, diff.Count(l => l.Kind == DiffKind.Removed));
        Assert.Equal(3000, diff.Count(l => l.Kind == DiffKind.Added));
    }
}
