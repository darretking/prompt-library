using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Tests;

public class CategoryItemTests
{
    [Fact]
    public void RailEmoji_IsColourOnEveryRow()
    {
        Assert.Equal(CategoryItem.AllEmoji, new CategoryItem(null, "All prompts", 3).RailEmoji);
        Assert.Equal(CategoryItem.RecentEmoji, new CategoryItem(CategoryItem.RecentId, "Recent", 2).RailEmoji);
        Assert.Equal(CategoryItem.DefaultEmoji, new CategoryItem(Guid.NewGuid(), "Coding", 1).RailEmoji);
        Assert.Equal(CategoryItem.DefaultEmoji, new CategoryItem(Guid.NewGuid(), "Coding", 1, emoji: " ").RailEmoji);
        Assert.Equal("\U0001F4BC", new CategoryItem(Guid.NewGuid(), "Business", 1, emoji: "\U0001F4BC").RailEmoji);
    }

    [Fact]
    public void CategoryItem_PinnedRow_IsNotCategory_HidesEmoji()
    {
        var item = new CategoryItem(CategoryItem.PinnedId, "Pinned", 2);
        Assert.True(item.IsPinnedRow);
        Assert.False(item.IsCategory);
        Assert.False(item.ShowEmoji);
        Assert.Equal("", item.RailEmoji);
        Assert.True(new CategoryItem(Guid.NewGuid(), "Coding", 1).ShowEmoji);
    }

    [Fact]
    public void RailEmoji_DefaultDoesNotBecomeTheCategorysOwn()
    {
        var item = new CategoryItem(Guid.NewGuid(), "Coding", 1);
        Assert.Null(item.Emoji);
        Assert.Equal("Coding", item.DisplayName);
    }
}

public class AppRepositoryTests
{
    [Fact]
    public void ChangelogUrl_PointsAtThisRepositorysChangelog()
        => Assert.Equal("https://github.com/darretking/prompt-library/blob/main/CHANGELOG.md", AppRepository.ChangelogUrl);
}
