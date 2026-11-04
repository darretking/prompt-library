using PromptLibrary.Core.Clipboard;

namespace PromptLibrary.Core.Tests;

public class ClipboardWriterTests
{
    [Fact]
    public void TryCopy_SucceedsFirstTry_PassesCrlfText()
    {
        var written = new List<string>();
        var sleeps = new List<TimeSpan>();
        var writer = new ClipboardWriter(written.Add, sleeps.Add);

        var result = writer.TryCopy("a\nb");

        Assert.True(result);
        Assert.Equal(["a\r\nb"], written);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void TryCopy_FailsTwiceThenSucceeds_ReturnsTrue_SleptTwice()
    {
        var calls = 0;
        var written = new List<string>();
        var sleeps = new List<TimeSpan>();
        void SetText(string text)
        {
            calls++;
            if (calls <= 2) throw new InvalidOperationException("clipboard locked");
            written.Add(text);
        }
        var writer = new ClipboardWriter(SetText, sleeps.Add, delayMs: 50);

        var result = writer.TryCopy("x");

        Assert.True(result);
        Assert.Equal(3, calls);
        Assert.Equal(["x"], written);
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)], sleeps);
    }

    [Fact]
    public void TryCopy_AlwaysFails_ReturnsFalse_After5Attempts()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();
        void SetText(string text)
        {
            calls++;
            throw new System.Runtime.InteropServices.ExternalException("clipboard locked");
        }
        var writer = new ClipboardWriter(SetText, sleeps.Add);

        var result = writer.TryCopy("x");

        Assert.False(result);
        Assert.Equal(5, calls);
        Assert.Equal(4, sleeps.Count);
    }
}
