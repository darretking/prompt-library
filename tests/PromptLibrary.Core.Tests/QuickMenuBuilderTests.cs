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

        // Pinned follow the sort setting (no usage here, so A to Z: m1, t2)
        Assert.Equal(new[] { m1, t2 }, menu.Pinned.Select(p => p.Id));
        Assert.False(menu.Pinned[0].IsTeam);
        Assert.True(menu.Pinned[1].IsTeam);
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

    private static AppSettings Uses(params (Prompt p, int n)[] counts)
    {
        var s = new AppSettings();
        foreach (var (p, n) in counts) s.Usage[p.Id] = new PromptUsage { Count = n };
        return s;
    }

    [Fact]
    public void Build_ByUse_PromptsInCategoryMostUsedFirst()
    {
        var (lib, _) = Lib("Cat", "a", "b", "c", "d");
        var s = Uses((lib.Prompts[2], 5), (lib.Prompts[1], 2), (lib.Prompts[3], 2));

        var menu = QuickMenuBuilder.Build(lib, null, s);

        Assert.Equal(new[] { "c", "b", "d", "a" }, menu.Yours.Single().Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_ByUse_CategoriesBySumOfUses()
    {
        var (lib, _) = Lib("Alpha", "a");
        var (zed, _) = Lib("Zed", "z1", "z2");
        lib.Categories.AddRange(zed.Categories);
        lib.Prompts.AddRange(zed.Prompts);
        var s = Uses((zed.Prompts[0], 2), (zed.Prompts[1], 3));

        var menu = QuickMenuBuilder.Build(lib, null, s);

        Assert.Equal(new[] { "Zed", "Alpha" }, menu.Yours.Select(c => c.Name));
    }

    [Fact]
    public void Build_ByUse_TeamCategoriesSorted_HiddenNotCounted()
    {
        var (team, _) = Lib("Alpha", "a");
        var (mid, _) = Lib("Mid", "m1");
        var (zed, _) = Lib("Zed", "z1", "z2");
        foreach (var l in new[] { mid, zed })
        {
            team.Categories.AddRange(l.Categories);
            team.Prompts.AddRange(l.Prompts);
        }
        var s = Uses((mid.Prompts[0], 1), (zed.Prompts[0], 10));
        s.HiddenTeamPromptIds = [zed.Prompts[0].Id]; // its 10 uses must not lift Zed above Mid

        var menu = QuickMenuBuilder.Build(new Library(), team, s);

        Assert.Equal(new[] { "Mid", "Alpha", "Zed" }, menu.Team.Select(c => c.Name));
    }

    [Fact]
    public void Build_ByUse_PinnedMostUsedFirst()
    {
        var (lib, _) = Lib("Cat", "A", "B");
        var s = Uses((lib.Prompts[1], 4));
        s.PinnedPromptIds = [lib.Prompts[0].Id, lib.Prompts[1].Id];

        Assert.Equal(new[] { "B", "A" }, QuickMenuBuilder.Build(lib, null, s).Pinned.Select(p => p.Title));
    }

    [Fact]
    public void Build_ByName_PinnedAToZ()
    {
        var (lib, _) = Lib("Cat", "A", "b");
        var s = Uses((lib.Prompts[1], 9));
        s.PromptSort = "name";
        s.PinnedPromptIds = [lib.Prompts[1].Id, lib.Prompts[0].Id];

        Assert.Equal(new[] { "A", "b" }, QuickMenuBuilder.Build(lib, null, s).Pinned.Select(p => p.Title));
    }

    [Fact]
    public void Build_UsageForDeletedPrompt_Ignored()
    {
        var (lib, _) = Lib("Cat", "b", "a");
        var s = new AppSettings();
        s.Usage[Guid.NewGuid()] = new PromptUsage { Count = 99 };

        var menu = QuickMenuBuilder.Build(lib, null, s);

        Assert.Equal(new[] { "a", "b" }, menu.Yours.Single().Prompts.Select(p => p.Title));
    }

    [Fact]
    public void Build_FeaturedKeepsLibraryOrder()
    {
        var (mine, _) = Lib("Mine", "m1");
        var (team, _) = Lib("Team", "t1", "t2", "t3");
        foreach (var p in team.Prompts) p.Featured = true;
        var s = Uses((team.Prompts[2], 9), (team.Prompts[1], 3));

        var menu = QuickMenuBuilder.Build(mine, team, s);

        Assert.Equal(new[] { "t1", "t2", "t3" }, menu.Featured.Select(p => p.Title));
    }

    private sealed class QuickMenuEq : IEqualityComparer<QuickMenuCategory>
    {
        public static readonly QuickMenuEq Instance = new();
        public bool Equals(QuickMenuCategory? a, QuickMenuCategory? b)
            => a!.Name == b!.Name && a.Prompts.SequenceEqual(b.Prompts);
        public int GetHashCode(QuickMenuCategory o) => o.Name.GetHashCode();
    }
}
