using PromptLibrary.Core.Menu;

namespace PromptLibrary.Core.Tests;

public class MenuTextTests
{
    [Fact]
    public void Escape_DoublesAmpersand()
        => Assert.Equal("Q&&A", MenuText.Escape("Q&A"));

    [Fact]
    public void Truncate_61Chars_Returns59PlusEllipsis()
    {
        var result = MenuText.Truncate(new string('a', 61));
        Assert.Equal(60, result.Length);
        Assert.EndsWith("…", result);
        Assert.Equal(new string('a', 59), result[..59]);
    }

    [Fact]
    public void Truncate_60Chars_Unchanged()
    {
        var s = new string('a', 60);
        Assert.Equal(s, MenuText.Truncate(s));
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePair()
    {
        var s = new string('a', 58) + "😀" + "bbb"; // pair occupies indices 58-59
        var result = MenuText.Truncate(s);
        Assert.Equal(new string('a', 58) + "…", result);
    }

    [Fact]
    public void ForMenu_TruncatesThenEscapes()
        => Assert.Equal("Q&&A", MenuText.ForMenu("Q&A"));
}
