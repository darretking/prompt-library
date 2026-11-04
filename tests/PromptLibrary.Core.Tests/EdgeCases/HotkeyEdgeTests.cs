using PromptLibrary.Core.Hotkeys;

namespace PromptLibrary.Core.Tests.EdgeCases;

public class HotkeyEdgeTests
{
    [Theory]
    [InlineData("Ctrl+ſ")] // Latin long s: upper-cases to "S"
    [InlineData("Ctrl+ı")] // dotless i: upper-cases to "I"
    [InlineData("Ctrl+Ä")]
    public void TryParse_NonAsciiKey_Fails(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Null(gesture);
    }

    [Fact]
    public void TryParse_SpacesAroundParts_Ok()
    {
        Assert.True(HotkeyGesture.TryParse(" ctrl + alt + p ", out var gesture));
        Assert.Equal("Ctrl+Alt+P", gesture!.ToString());
    }

    [Fact]
    public void TryParse_LowercaseFunctionKey_Ok()
    {
        Assert.True(HotkeyGesture.TryParse("ctrl+f12", out var gesture));
        Assert.Equal("Ctrl+F12", gesture!.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Alt+Del")]
    [InlineData("Ctrl+Space")]
    public void TryParse_DisallowedKey_Fails(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }
}
