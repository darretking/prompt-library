using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;

namespace PromptLibrary.Core.Tests;

public class PromptFilterTests
{
    private static readonly Guid CatA = Guid.NewGuid();
    private static readonly Guid CatB = Guid.NewGuid();

    private static Library Sample() => new()
    {
        Categories =
        {
            new Category { Id = CatA, Name = "A" },
            new Category { Id = CatB, Name = "B" },
        },
        Prompts =
        {
            new Prompt { Id = Guid.NewGuid(), Title = "charlie", CategoryId = CatA, Text = "uses xUnit here" },
            new Prompt { Id = Guid.NewGuid(), Title = "Alpha", CategoryId = CatB, Text = "plain" },
            new Prompt { Id = Guid.NewGuid(), Title = "bravo", CategoryId = CatA, Description = "desc", Text = "plain" },
        },
    };

    [Fact]
    public void Filter_AllCategories_BlankQuery_ReturnsAllSorted()
    {
        var result = PromptFilter.Apply(Sample(), null, "  ");
        Assert.Equal(new[] { "Alpha", "bravo", "charlie" }, result.Select(p => p.Title));
    }

    [Fact]
    public void Filter_ByCategory_OnlyThatCategory()
    {
        var result = PromptFilter.Apply(Sample(), CatA, null);
        Assert.Equal(new[] { "bravo", "charlie" }, result.Select(p => p.Title));
    }

    [Fact]
    public void Filter_QueryMatchesTextOnly_CaseInsensitive()
    {
        var result = PromptFilter.Apply(Sample(), null, " XUNIT ");
        Assert.Equal(new[] { "charlie" }, result.Select(p => p.Title));
    }

    [Fact]
    public void Filter_QueryMatchesDescription()
    {
        var result = PromptFilter.Apply(Sample(), null, "DESC");
        Assert.Equal(new[] { "bravo" }, result.Select(p => p.Title));
    }
}
