using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;

namespace PromptLibrary.Core.Tests.Team;

public class CombinedSearchTests
{
    private static Library Lib(params (string title, string text)[] ps)
    {
        var lib = new Library();
        var c = Guid.NewGuid();
        lib.Categories.Add(new Category { Id = c, Name = "C" });
        foreach (var (t, x) in ps) lib.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = t, Text = x, CategoryId = c });
        return lib;
    }

    [Fact]
    public void Search_BothLibraries_LabelsTeam_IncludesHiddenGreyed()
    {
        var mine = Lib(("zeta mail", ""), ("alpha mail", ""), ("unrelated", ""));
        var team = Lib(("beta", "send mail"), ("gamma mail", ""), ("nope", ""));
        var hidden = team.Prompts[1].Id;
        var s = new AppSettings { HiddenTeamPromptIds = [hidden] };

        var hits = PromptFilter.Search(mine, team, s, "  MAIL ");

        Assert.Equal(new[] { "alpha mail", "zeta mail", "beta", "gamma mail" }, hits.Select(h => h.Prompt.Title));
        Assert.Equal(new[] { false, false, true, true }, hits.Select(h => h.IsTeam));
        Assert.Equal(new[] { false, false, false, true }, hits.Select(h => h.IsHidden));
    }

    [Fact]
    public void Search_EmptyOrWhitespaceQuery_ReturnsEmpty()
    {
        var mine = Lib(("a", ""));
        Assert.Empty(PromptFilter.Search(mine, null, new AppSettings(), ""));
        Assert.Empty(PromptFilter.Search(mine, null, new AppSettings(), "   "));
    }

    [Fact]
    public void Search_NoTeam_OnlyYours()
    {
        var hits = PromptFilter.Search(Lib(("a", "")), null, new AppSettings(), "a");
        Assert.Single(hits);
        Assert.False(hits[0].IsTeam);
    }
}
