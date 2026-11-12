using PromptLibrary.Core.Send;

namespace PromptLibrary.Core.Tests.Send;

public class SendTargetsTests
{
    [Fact]
    public void HasTheFourTargets_InOrder()
        => Assert.Equal(["chatgpt", "claude", "copilot", "perplexity"], SendTargets.All.Select(t => t.Id));

    [Fact]
    public void DisplayNames()
        => Assert.Equal(["ChatGPT", "Claude", "Copilot", "Perplexity"], SendTargets.All.Select(t => t.Name));

    [Theory]
    [InlineData("chatgpt", "https://chatgpt.com/?q=Hello%20world%3F")]
    [InlineData("claude", "https://claude.ai/new?q=Hello%20world%3F")]
    [InlineData("copilot", "https://copilot.microsoft.com/?q=Hello%20world%3F")]
    [InlineData("perplexity", "https://www.perplexity.ai/search?q=Hello%20world%3F")]
    public void BuildUrl_EscapesTheText(string id, string expected)
        => Assert.Equal(expected, SendTargets.Find(id)!.BuildUrl("Hello world?"));

    [Fact]
    public void BuildUrl_EscapesNewlinesAndUnicode()
        => Assert.Equal("https://chatgpt.com/?q=a%0D%0Ab%20%C3%A9", SendTargets.Find("chatgpt")!.BuildUrl("a\r\nb é"));

    [Fact]
    public void BuildUrl_TooLong_FallsBackToBaseUrl()
    {
        var t = SendTargets.Find("claude")!;
        var text = new string('a', 6000);
        Assert.Equal("https://claude.ai/new", t.BuildUrl(text));
        Assert.True(t.IsTooLong(text));
    }

    [Fact]
    public void BuildUrl_JustUnderTheLimit_KeepsTheText()
    {
        var t = SendTargets.Find("chatgpt")!;
        var text = new string('a', SendTargets.MaxUrlLength - t.QueryUrl.Length);
        var url = t.BuildUrl(text);
        Assert.Equal(SendTargets.MaxUrlLength, url.Length);
        Assert.False(t.IsTooLong(text));
    }

    [Fact]
    public void BuildUrl_EncodingCountsTowardsTheLimit()
        => Assert.Equal("https://chatgpt.com/", SendTargets.Find("chatgpt")!.BuildUrl(new string('é', 1500)));

    [Fact]
    public void BuildUrl_EmptyText_OpensTheSite()
        => Assert.Equal("https://chatgpt.com/", SendTargets.Find("chatgpt")!.BuildUrl(""));

    [Fact]
    public void Find_IgnoresCase_AndUnknownIsNull()
    {
        Assert.Equal("claude", SendTargets.Find("CLAUDE")!.Id);
        Assert.Null(SendTargets.Find("nope"));
        Assert.Null(SendTargets.Find(null));
    }

    [Fact]
    public void Resolve_FallsBackToChatGpt()
    {
        Assert.Equal("chatgpt", SendTargets.Resolve(null).Id);
        Assert.Equal("chatgpt", SendTargets.Resolve("gone").Id);
        Assert.Equal("copilot", SendTargets.Resolve("copilot").Id);
    }
}
