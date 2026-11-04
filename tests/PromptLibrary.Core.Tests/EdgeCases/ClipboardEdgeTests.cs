using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Tests.EdgeCases;

public class ClipboardEdgeTests
{
    [Fact]
    public void TryCopy_AlreadyCrlf_Unchanged()
    {
        var written = new List<string>();
        var writer = new ClipboardWriter(written.Add, _ => { });

        Assert.True(writer.TryCopy("a\r\nb\r\n\r\nc"));

        Assert.Equal(["a\r\nb\r\n\r\nc"], written);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(5, -1)]
    public void Ctor_ZeroAttempts_Throws(int attempts, int delayMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClipboardWriter(_ => { }, _ => { }, attempts, delayMs));
    }

    [Fact]
    public void TryCopy_CustomAttemptsAndDelay()
    {
        var calls = 0;
        var sleeps = new List<TimeSpan>();
        var writer = new ClipboardWriter(_ => { calls++; throw new InvalidOperationException("busy"); }, sleeps.Add,
            attempts: 3, delayMs: 10);

        Assert.False(writer.TryCopy("x"));

        Assert.Equal(3, calls);
        Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)], sleeps);
    }

    [Fact]
    public void ToCrlf_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => LineEndings.ToCrlf(null!));
    }
}
