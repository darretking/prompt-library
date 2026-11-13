using PromptLibrary.Core.Menu;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests;

public class QuickMenuBuilderTests
{
    [Fact]
    public void Build_SortsCategoriesAndPrompts_CaseInsensitive()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var lib = new Library();
        var names = new[] { "beta", "Alpha", "gamma" };
        for (var i = 0; i < 3; i++)
        {
            lib.Categories.Add(new Category { Id = ids[i], Name = names[i] });
            lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "z", CategoryId = ids[i] });
        }
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "b", CategoryId = ids[0] });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "A", CategoryId = ids[0] });

        var menu = QuickMenuBuilder.Build(lib);

        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, menu.Select(c => c.Name));
        var beta = menu.Single(c => c.Name == "beta");
        Assert.Equal(new[] { "A", "b", "z" }, beta.Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_CategoryWithEmoji_ShowsEmojiThenName_ButSortsByName()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var lib = new Library();
        lib.Categories.Add(new Category { Id = a, Name = "Beta", Emoji = "\U0001F4A1" });
        lib.Categories.Add(new Category { Id = b, Name = "Alpha" });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "x", CategoryId = a });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "y", CategoryId = b });

        var menu = QuickMenuBuilder.Build(lib);

        Assert.Equal(new[] { "Alpha", "\U0001F4A1 Beta" }, menu.Select(c => c.Name));
    }

    [Fact]
    public void Build_OmitsEmptyCategories()
    {
        var full = Guid.NewGuid();
        var lib = new Library();
        lib.Categories.Add(new Category { Id = full, Name = "Full" });
        lib.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Empty" });
        lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "p", CategoryId = full });

        var menu = QuickMenuBuilder.Build(lib);

        Assert.Equal(new[] { "Full" }, menu.Select(c => c.Name));
    }

    [Fact]
    public void Build_EmptyLibrary_ReturnsEmpty()
        => Assert.Empty(QuickMenuBuilder.Build(new Library()));

    private static (Library lib, Guid cat) Lib(string catName, params string[] titles)
    {
        var lib = new Library();
        var cat = Guid.NewGuid();
        lib.Categories.Add(new Category { Id = cat, Name = catName });
        foreach (var t in titles) lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = t, CategoryId = cat });
        return (lib, cat);
    }

    [Fact]
    public void Build_NoTeamNoPins_SameAsBefore()
    {
        var (lib, _) = Lib("Cat", "b", "a");
        var menu = QuickMenuBuilder.Build(lib, null, new AppSettings());

        Assert.Empty(menu.Pinned);
        Assert.Empty(menu.Team);
        Assert.False(menu.TeamCollapsed);
        Assert.Equal(QuickMenuBuilder.Build(lib), menu.Yours, QuickMenuEq.Instance);
    }

    [Fact]
    public void Build_PinnedFirstInPinOrder_TeamFlagged()
    {
        var (mine, _) = Lib("Mine", "m1");
        var (team, _) = Lib("Team", "t1", "t2");
        var m1 = mine.Prompts[0].Id;
        var t2 = team.Prompts[1].Id;
        var s = new AppSettings { PinnedPromptIds = [t2, Guid.NewGuid(), m1] };

        var menu = QuickMenuBuilder.Build(mine, team, s);

        Assert.Equal(new[] { t2, m1 }, menu.Pinned.Select(p => p.Id));
        Assert.True(menu.Pinned[0].IsTeam);
        Assert.False(menu.Pinned[1].IsTeam);
        Assert.Contains(menu.Team.Single().Prompts, p => p.Id == t2 && p.IsTeam);
        Assert.Contains(menu.Yours.Single().Prompts, p => p.Id == m1 && !p.IsTeam);
    }

    [Fact]
    public void Build_HiddenTeamPromptsLeftOut()
    {
        var (mine, _) = Lib("Mine", "m1");
        var (team, _) = Lib("Team", "t1", "t2");
        var (team2, _) = Lib("Gone", "g");
        team.Categories.AddRange(team2.Categories);
        team.Prompts.AddRange(team2.Prompts);
        var t1 = team.Prompts[0].Id;
        var s = new AppSettings
        {
            HiddenTeamPromptIds = [t1, team2.Prompts[0].Id],
            PinnedPromptIds = [t1],
        };

        var menu = QuickMenuBuilder.Build(mine, team, s);

        Assert.Empty(menu.Pinned);
        Assert.Equal(new[] { "Team" }, menu.Team.Select(c => c.Name));
        Assert.Equal(new[] { "t2" }, menu.Team[0].Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_Featured_LibraryOrder_HiddenAndPinnedLeftOut()
    {
        var (mine, _) = Lib("Mine", "m1");
        var (team, _) = Lib("Team", "t1", "t2", "t3", "t4", "t5");
        foreach (var p in team.Prompts.Take(4)) p.Featured = true;
        var s = new AppSettings
        {
            HiddenTeamPromptIds = [team.Prompts[1].Id],
            PinnedPromptIds = [team.Prompts[2].Id],
        };

        var menu = QuickMenuBuilder.Build(mine, team, s);

        Assert.Equal(new[] { "t1", "t4" }, menu.Featured.Select(p => p.Title)); // t2 hidden, t3 only in Pinned
        Assert.All(menu.Featured, p => Assert.True(p.IsTeam));
        Assert.Equal(new[] { "t3" }, menu.Pinned.Select(p => p.Title));
    }

    [Fact]
    public void Build_NoFeatured_BlockAbsent()
    {
        var (mine, _) = Lib("Mine", "m1");
        var (team, _) = Lib("Team", "t1");
        mine.Prompts[0].Featured = true; // only team prompts count

        Assert.Empty(QuickMenuBuilder.Build(mine, team, new AppSettings()).Featured);
        Assert.Empty(QuickMenuBuilder.Build(mine, null, new AppSettings()).Featured);
    }

    [Fact]
    public void Build_SameNamedCategories_StaySeparate()
    {
        var (mine, _) = Lib("Same", "m1");
        var (team, _) = Lib("Same", "t1");

        var menu = QuickMenuBuilder.Build(mine, team, new AppSettings());

        Assert.Equal("Same", menu.Yours.Single().Name);
        Assert.Equal("Same", menu.Team.Single().Name);
        Assert.Equal(new[] { "m1" }, menu.Yours[0].Prompts.Select(p => p.Title));
        Assert.Equal(new[] { "t1" }, menu.Team[0].Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_TeamCollapsedOnlyAboveSixCategories()
    {
        var team = new Library();
        for (var i = 0; i < 7; i++)
        {
            var c = Guid.NewGuid();
            team.Categories.Add(new Category { Id = c, Name = "C" + i });
            team.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "p", CategoryId = c });
        }
        Assert.True(QuickMenuBuilder.Build(new Library(), team, new AppSettings()).TeamCollapsed);

        team.Prompts.RemoveAt(6);
        Assert.False(QuickMenuBuilder.Build(new Library(), team, new AppSettings()).TeamCollapsed);
    }

    private sealed class QuickMenuEq : IEqualityComparer<QuickMenuCategory>
    {
        public static readonly QuickMenuEq Instance = new();
        public bool Equals(QuickMenuCategory? a, QuickMenuCategory? b)
            => a!.Name == b!.Name && a.Prompts.SequenceEqual(b.Prompts);
        public int GetHashCode(QuickMenuCategory o) => o.Name.GetHashCode();
    }
}
