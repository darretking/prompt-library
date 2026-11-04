using PromptLibrary.Core.Hotkeys;

namespace PromptLibrary.Core.Tests;

public class HotkeyGestureTests
{
    [Fact]
    public void TryParse_CtrlAltP_Ok()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+P", out var g));
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, g!.Modifiers);
        Assert.Equal("P", g.Key);
    }

    [Fact]
    public void TryParse_IsCaseAndOrderInsensitive()
    {
        Assert.True(HotkeyGesture.TryParse(" alt + ctrl + p ", out var g));
        Assert.Equal("Ctrl+Alt+P", g!.ToString());
    }

    [Fact]
    public void TryParse_NoModifier_Fails()
    {
        Assert.False(HotkeyGesture.TryParse("P", out var g));
        Assert.Null(g);
    }

    [Fact]
    public void TryParse_ShiftOnly_Fails()
    {
        Assert.False(HotkeyGesture.TryParse("Shift+P", out _));
    }

    [Fact]
    public void TryParse_F12WithWin_Ok()
    {
        Assert.True(HotkeyGesture.TryParse("Win+F12", out var g));
        Assert.Equal(HotkeyModifiers.Win, g!.Modifiers);
        Assert.Equal("F12", g.Key);
        Assert.Equal("Win+F12", g.ToString());
    }

    [Theory]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+PP")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Ctrl+P")]
    [InlineData("Ctrl+P+Q")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+;")]
    public void TryParse_Garbage_Fails(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var g));
        Assert.Null(g);
    }

    [Theory]
    [InlineData("control+7", "Ctrl+7")]
    [InlineData("Windows+Shift+f24", "Shift+Win+F24")]
    [InlineData("Ctrl+Alt+Shift+Win+a", "Ctrl+Alt+Shift+Win+A")]
    public void TryParse_AcceptsAliasesAndCanonicalises(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(expected, g!.ToString());
    }
}
