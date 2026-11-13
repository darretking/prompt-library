using PromptLibrary.Core.Clipboard;

namespace PromptLibrary.Core.Tests;

public class PasteWaitTests
{
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 999, true)]
    [InlineData(false, 1000, false)]
    [InlineData(true, 0, false)]
    public void KeepWaitingForForeground_StopsOnMatchOrAfterOneSecond(bool matched, int ms, bool expected)
        => Assert.Equal(expected, PasteDecision.KeepWaitingForForeground(matched, TimeSpan.FromMilliseconds(ms)));

    [Theory]
    [InlineData(100, true, false)]   // idle but too early
    [InlineData(399, true, false)]
    [InlineData(400, true, true)]    // minimum passed and idle
    [InlineData(800, false, false)]  // busy target: keep waiting
    [InlineData(1499, false, false)]
    [InlineData(1500, false, true)]  // give up waiting for idle
    public void ClipboardRestoreReady_NeedsMinimumAndIdle_ButNeverForever(int ms, bool idle, bool expected)
        => Assert.Equal(expected, PasteDecision.ClipboardRestoreReady(TimeSpan.FromMilliseconds(ms), idle));

    [Fact]
    public void ClipboardStillOurs_IgnoresLineEndingStyle_AndNull()
    {
        Assert.True(PasteDecision.ClipboardStillOurs("a\r\nb", "a\nb"));
        Assert.False(PasteDecision.ClipboardStillOurs("something new", "a\nb"));
        Assert.False(PasteDecision.ClipboardStillOurs(null, "a"));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void CanSendKeysTo_BlocksElevatedTargetsWhenWeAreNot(bool target, bool us, bool expected)
        => Assert.Equal(expected, PasteDecision.CanSendKeysTo(target, us));

    [Fact]
    public void ToastFor_PerOutcome()
    {
        Assert.Equal(("Pasted: X", ToastKind.Success), PasteDecision.ToastFor("X", PasteOutcome.Pasted));
        Assert.Equal(("Copied: X", ToastKind.Success), PasteDecision.ToastFor("X", PasteOutcome.NotAttempted));
        Assert.Equal(("Couldn't paste into that app. The prompt is on your clipboard: press Ctrl+V to paste it.", ToastKind.Error),
            PasteDecision.ToastFor("X", PasteOutcome.Failed));
        Assert.Equal(PasteDecision.CouldntPasteText, PasteDecision.ToastFor("X", PasteOutcome.Elevated).Text);
    }
}
