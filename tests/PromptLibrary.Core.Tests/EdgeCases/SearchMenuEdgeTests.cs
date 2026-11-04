using PromptLibrary.Core.Menu;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;

namespace PromptLibrary.Core.Tests.EdgeCases;

public class SearchMenuEdgeTests
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
            new Prompt { Id = Guid.NewGuid(), Title = "Review code", CategoryId = CatA, Text = "Look for bugs." },
            new Prompt { Id = Guid.NewGuid(), Title = "Summarise", CategoryId = CatA, Text = "Summarise the code (briefly)." },
            new Prompt { Id = Guid.NewGuid(), Title = "Regex helper", CategoryId = CatB, Text = "Match .* and [a-z] in code" },
            new Prompt { Id = Guid.NewGuid(), Title = "Party", CategoryId = CatB, Text = "Add 🎉 to the message" },
        },
    };

    private static string[] Titles(IEnumerable<Prompt> prompts) => prompts.Select(p => p.Title).ToArray();

    [Fact]
    public void Filter_CategoryAndQuery_Combined()
    {
        var result = PromptFilter.Apply(Sample(), CatA, "code");

        Assert.Equal(["Review code", "Summarise"], Titles(result));
    }

    [Fact]
    public void Filter_TitleMatch()
    {
        var result = PromptFilter.Apply(Sample(), null, "REVIEW");

        Assert.Equal(["Review code"], Titles(result));
    }

    [Theory]
    [InlineData(".*", "Regex helper")]
    [InlineData("[", "Regex helper")]
    [InlineData("(", "Summarise")]
    public void Filter_RegexCharsAreLiteral(string query, string expectedTitle)
    {
        var result = PromptFilter.Apply(Sample(), null, query);

        Assert.Equal([expectedTitle], Titles(result));
    }

    [Fact]
    public void Filter_EmojiQuery()
    {
        Assert.Equal(["Party"], Titles(PromptFilter.Apply(Sample(), null, "🎉")));
        Assert.Empty(PromptFilter.Apply(Sample(), null, "🙂"));
    }

    [Fact]
    public void Build_EqualTitles_StableOrder()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Cat" };
        var first = new Prompt { Id = Guid.NewGuid(), Title = "Same", CategoryId = category.Id };
        var second = new Prompt { Id = Guid.NewGuid(), Title = "same", CategoryId = category.Id };
        var third = new Prompt { Id = Guid.NewGuid(), Title = "SAME", CategoryId = category.Id };
        var lib = new Library { Categories = { category }, Prompts = { first, second, third } };

        var ids = QuickMenuBuilder.Build(lib).Single().Prompts.Select(p => p.Id);

        Assert.Equal([first.Id, second.Id, third.Id], ids);
    }

    [Fact]
    public void ForMenu_TruncatesThenEscapes()
    {
        var input = new string('a', 58) + "&&&";

        Assert.Equal(new string('a', 58) + "&&…", MenuText.ForMenu(input));
    }

    [Fact]
    public void ForMenu_OnlyAmpersand()
    {
        Assert.Equal("&&", MenuText.ForMenu("&"));
    }

    [Fact]
    public void Truncate_Exactly60_Unchanged()
    {
        var input = new string('b', 60);

        Assert.Equal(input, MenuText.Truncate(input));
    }
}
