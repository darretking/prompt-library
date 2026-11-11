using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;

namespace PromptLibrary.Core.Tests.Search;

public class MatchSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static SearchResult Only(string query, string title, string text, params string[] tags)
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "General" };
        var lib = new Library
        {
            Categories = [c],
            Prompts = [new Prompt { Id = Guid.NewGuid(), Title = title, Text = text, Tags = [.. tags], CategoryId = c.Id }],
        };
        return Assert.Single(FuzzySearch.Search(lib, null, new AppSettings(), query, Now));
    }

    [Fact]
    public void TitleHit_HasNoTagsOrSnippet()
    {
        var r = Only("budget", "Make a budget", "body text", "money");
        Assert.Null(r.TagMatches);
        Assert.Null(r.TextSnippet);
    }

    [Fact]
    public void TagHit_ListsTheMatchedTagOnly()
    {
        var r = Only("sql", "Query helper", "body", "database", "sql", "postgres");
        Assert.Equal([1], r.TagMatches);
        Assert.Null(r.TextSnippet);
    }

    [Fact]
    public void TagFilter_MarksTheMatchingTags()
    {
        var r = Only("#data", "Query helper", "body", "database", "sql");
        Assert.Equal([0], r.TagMatches);
    }

    [Fact]
    public void TextHit_GivesASnippetAroundTheFirstMatch()
    {
        var text = "Please write a short note that describes the quarterly zebra migration plan for everyone on the team, thanks.";
        var r = Only("zebra", "Note writer", text);
        Assert.Null(r.TagMatches);
        var s = r.TextSnippet!;
        Assert.Equal("zebra", s.Text.Substring(s.Start, s.Length));
        Assert.InRange(s.Text.Length, 20, 64);
        Assert.StartsWith("…", s.Text);
        Assert.EndsWith("…", s.Text);
    }

    [Fact]
    public void Snippet_ShowsLineBreaksAsSpaces_AndNoEllipsisForShortText()
    {
        var r = Only("zebra", "Note writer", "first line\r\n\r\nsecond zebra line");
        var s = r.TextSnippet!;
        Assert.Equal("first line second zebra line", s.Text);
        Assert.Equal("zebra", s.Text.Substring(s.Start, s.Length));
    }
}
