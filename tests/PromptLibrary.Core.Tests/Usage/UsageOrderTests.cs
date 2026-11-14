using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Tests.Usage;

public class UsageOrderTests
{
    private static Prompt P(string title) => new() { Id = Guid.NewGuid(), Title = title };

    private static string[] Titles(IEnumerable<Prompt> prompts) => prompts.Select(p => p.Title).ToArray();

    [Fact]
    public void Prompts_NoUsage_IsAToZ()
    {
        var s = new AppSettings();
        Assert.Equal(["A", "b", "c"], Titles(UsageOrder.Prompts([P("b"), P("A"), P("c")], s)));
    }

    [Fact]
    public void Prompts_ByUse_MostUsedFirst_TiesAToZ()
    {
        var s = new AppSettings();
        var (b, a, c) = (P("b"), P("A"), P("c"));
        s.Usage[b.Id] = new PromptUsage { Count = 3 };
        s.Usage[c.Id] = new PromptUsage { Count = 3 };
        Assert.Equal(["b", "c", "A"], Titles(UsageOrder.Prompts([c, a, b], s)));
    }

    [Fact]
    public void Prompts_ByName_IgnoresUsage()
    {
        var s = new AppSettings { PromptSort = UsageOrder.ByName };
        var (b, a, c) = (P("b"), P("A"), P("c"));
        s.Usage[b.Id] = new PromptUsage { Count = 5 };
        Assert.Equal(["A", "b", "c"], Titles(UsageOrder.Prompts([b, a, c], s)));
    }

    [Fact]
    public void Uses_MissingOrNullEntry_IsZero()
    {
        var s = new AppSettings();
        var id = Guid.NewGuid();
        Assert.Equal(0, UsageOrder.Uses(s, id));
        s.Usage[id] = null!;
        Assert.Equal(0, UsageOrder.Uses(s, id));
    }

    [Fact]
    public void SortsByUse_UnknownValue_IsTrue()
    {
        Assert.True(UsageOrder.SortsByUse(new AppSettings { PromptSort = "foo" }));
        Assert.False(UsageOrder.SortsByUse(new AppSettings { PromptSort = UsageOrder.ByName }));
    }

    [Fact]
    public void Prompts_CaseOnlyDifferentTitles_StableOrder()
    {
        var s = new AppSettings();
        var (lower, upper) = (P("a"), P("A"));
        Assert.Equal([lower, upper], UsageOrder.Prompts([lower, upper], s));
        Assert.Equal([upper, lower], UsageOrder.Prompts([upper, lower], s));
    }
}
