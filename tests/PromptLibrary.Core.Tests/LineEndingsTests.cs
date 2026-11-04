using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Tests;

public class LineEndingsTests
{
    [Theory]
    [InlineData("a\nb", "a\r\nb")]
    [InlineData("a\r\nb", "a\r\nb")]
    [InlineData("a\rb", "a\r\nb")]
    [InlineData("a\n\n\nb", "a\r\n\r\n\r\nb")]
    [InlineData("  tab\there  \n", "  tab\there  \r\n")]
    [InlineData("", "")]
    public void ToCrlf_NormalisesAllLineEndings_KeepsEverythingElse(string input, string expected)
        => Assert.Equal(expected, LineEndings.ToCrlf(input));

    [Fact]
    public void ToCrlf_KeepsEmojiQuotesBraces()
        => Assert.Equal("\"{x}\" 🙂\r\n'y'", LineEndings.ToCrlf("\"{x}\" 🙂\n'y'"));
}
