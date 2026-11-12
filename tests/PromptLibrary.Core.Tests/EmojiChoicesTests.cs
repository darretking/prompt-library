using System.Globalization;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests;

public class EmojiChoicesTests
{
    [Fact]
    public void Offers54DistinctSingleEmoji()
    {
        Assert.Equal(54, EmojiChoices.All.Count);
        Assert.Equal(54, EmojiChoices.All.Distinct().Count());
        Assert.All(EmojiChoices.All, e => Assert.Equal(1, new StringInfo(e).LengthInTextElements));
    }
}
