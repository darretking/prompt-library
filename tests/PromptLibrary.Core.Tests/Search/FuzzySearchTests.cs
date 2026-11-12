using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Tests.Search;

public class FuzzySearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static Library Seed() => SeedLibrary.Create(Now);

    private static string[] Titles(IEnumerable<SearchResult> r) => r.Select(x => x.Prompt.Title).ToArray();

    private static IReadOnlyList<SearchResult> Run(Library lib, string q, Library? team = null, AppSettings? s = null)
        => FuzzySearch.Search(lib, team, s ?? new AppSettings(), q, Now);

    private static Library Small(params (string Title, string Desc, string Text, string[] Tags)[] items)
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "General" };
        return new Library
        {
            Categories = [c],
            Prompts = items.Select(i => new Prompt
            {
                Id = Guid.NewGuid(), Title = i.Title, Description = i.Desc, Text = i.Text, Tags = [.. i.Tags], CategoryId = c.Id,
            }).ToList(),
        };
    }

    [Fact]
    public void EmptyOrBlankQuery_ReturnsNothing()
    {
        Assert.Empty(Run(Seed(), ""));
        Assert.Empty(Run(Seed(), "   "));
        Assert.Empty(Run(Seed(), "#"));
        Assert.Empty(Run(Seed(), "cat:"));
    }

    [Fact]
    public void ItinTrp_FindsPlanATripItinerary_First()
    {
        var results = Run(Seed(), "itin trp");
        Assert.Equal("Plan a trip itinerary", results[0].Prompt.Title);
    }

    [Fact]
    public void Typo_Itinerery_StillFindsItinerary()
        => Assert.Contains("Plan a trip itinerary", Titles(Run(Seed(), "itinerery")));

    [Fact]
    public void Typo_NotAllowedForShortWords()
    {
        var lib = Small(("Cat guide", "", "", []));
        Assert.Empty(Run(lib, "cta"));       // 3 letters, order swapped: no typo tolerance, not a subsequence
        Assert.Single(Run(lib, "guid"));      // prefix
        Assert.Single(Run(lib, "gude"));      // 4 letters, one typo against "guide"
    }

    [Fact]
    public void Typo_TwoMistakes_DoNotMatch()
        => Assert.Empty(Run(Small(("Itinerary", "", "", [])), "itinarery2"));

    [Fact]
    public void Tag_Filter_MatchesOnlyTaggedPrompts()
    {
        var lib = Small(("A", "", "x", ["travel"]), ("B", "", "x", ["money"]), ("C", "", "x", []));
        Assert.Equal(["A"], Titles(Run(lib, "#travel")));
        Assert.Equal(["A"], Titles(Run(lib, "#TRA")));
        Assert.Empty(Run(lib, "#zzz"));
    }

    [Fact]
    public void Tag_AndWord_BothMustMatch()
    {
        var lib = Small(("Alpha plan", "", "x", ["travel"]), ("Beta plan", "", "x", ["money"]));
        Assert.Equal(["Alpha plan"], Titles(Run(lib, "plan #travel")));
    }

    [Fact]
    public void WordsAlsoMatchTags()
        => Assert.Equal(["B"], Titles(Run(Small(("A", "", "x", []), ("B", "", "x", ["budget"])), "budget")));

    [Fact]
    public void Cat_Filter_IsPrefixOfCategoryName()
    {
        var results = Run(Seed(), "cat:trav");
        Assert.Equal(3, results.Count);
        Assert.Equal(["Local tips and phrases", "Packing list", "Plan a trip itinerary"], Titles(results)); // all tie: A-Z
    }

    [Fact]
    public void Cat_Quoted_AllowsSpaces_AndCombinesWithWords()
    {
        var results = Run(Seed(), "cat:\"website design\" homepage");
        Assert.Equal(["Review my homepage"], Titles(results));
    }

    [Fact]
    public void Cat_NoMatch_GivesNothing()
        => Assert.Empty(Run(Seed(), "cat:zzz"));

    [Fact]
    public void WordOrderIsFree()
        => Assert.Contains("Plan a trip itinerary", Titles(Run(Seed(), "itinerary trip")));

    [Fact]
    public void AllWordsMustMatch()
        => Assert.Empty(Run(Small(("Trip plan", "", "", [])), "trip zebra"));

    [Fact]
    public void TitleHitOutranksDescriptionAndText()
    {
        var lib = Small(("Other", "", "plan appears in the text", []), ("Weekly thing", "a plan", "", []), ("Plan it", "", "", []));
        Assert.Equal(["Plan it", "Weekly thing", "Other"], Titles(Run(lib, "plan")));
    }

    [Fact]
    public void ExactBeatsPrefixBeatsSubstring()
    {
        var lib = Small(("Unplanned", "", "", []), ("Planning", "", "", []), ("Plan", "", "", []));
        Assert.Equal(["Plan", "Planning", "Unplanned"], Titles(Run(lib, "plan")));
    }

    [Fact]
    public void TiesAreAlphabetical()
    {
        var lib = Small(("Beta note", "", "", []), ("alpha note", "", "", []), ("Gamma note", "", "", []));
        Assert.Equal(["alpha note", "Beta note", "Gamma note"], Titles(Run(lib, "note")));
    }

    [Fact]
    public void FrecencyBreaksCloseCalls_ButNotBigScoreGaps()
    {
        var lib = Small(("Note A", "", "", []), ("Note B", "", "", []), ("Zeta", "has note", "", []));
        var s = new AppSettings();
        UsageTracker.Record(s, lib.Prompts[1].Id, Now);
        UsageTracker.Record(s, lib.Prompts[2].Id, Now); UsageTracker.Record(s, lib.Prompts[2].Id, Now);

        var titles = Titles(Run(lib, "note", s: s));
        Assert.Equal(["Note B", "Note A", "Zeta"], titles);
    }

    [Fact]
    public void TitleMatches_ForPrefix_AreTheTypedRange()
    {
        var hit = Run(Small(("Plan a trip itinerary", "", "", [])), "itin")[0];
        Assert.Equal([(12, 4)], hit.TitleMatches);
    }

    [Fact]
    public void TitleMatches_ForSubsequence_AreTheMatchedLetters()
    {
        var hit = Run(Small(("Plan a trip itinerary", "", "", [])), "trp")[0];
        Assert.Equal([(7, 2), (10, 1)], hit.TitleMatches); // "t","r" adjacent merge, then "p"
    }

    [Fact]
    public void TitleMatches_MultipleWords_AreSortedAndMerged()
    {
        var hit = Run(Small(("Plan a trip itinerary", "", "", [])), "itin trp")[0];
        Assert.Equal([(7, 2), (10, 1), (12, 4)], hit.TitleMatches);
    }

    [Fact]
    public void DescriptionMatches_AreReported()
    {
        var hit = Run(Small(("Thing", "Day by day, at your pace", "", [])), "pace")[0];
        Assert.Equal([(20, 4)], hit.DescriptionMatches);
        Assert.Empty(hit.TitleMatches);
    }

    [Fact]
    public void TypoMatch_HighlightsTheWholeWord()
    {
        var hit = Run(Small(("Itinerary", "", "", [])), "itinerery")[0];
        Assert.Equal([(0, 9)], hit.TitleMatches);
    }

    [Fact]
    public void SearchesBothLibraries_AndFlagsHiddenTeamPrompts()
    {
        var personal = Small(("Trip mine", "", "", []));
        var team = Small(("Trip team", "", "", []), ("Trip hidden", "", "", []));
        var s = new AppSettings { HiddenTeamPromptIds = [team.Prompts[1].Id] };

        var results = Run(personal, "trip", team, s);

        Assert.Equal(3, results.Count);
        Assert.False(results.Single(r => r.Prompt.Title == "Trip mine").IsTeam);
        var t = results.Single(r => r.Prompt.Title == "Trip team");
        Assert.True(t.IsTeam);
        Assert.False(t.IsHidden);
        Assert.True(results.Single(r => r.Prompt.Title == "Trip hidden").IsHidden);
    }

    [Fact]
    public void RankWins_OverLibraryOrder()
    {
        var personal = Small(("Zzz", "", "trip in text", []));
        var team = Small(("Trip", "", "", []));
        Assert.Equal(["Trip", "Zzz"], Titles(Run(personal, "trip", team)));
    }

    [Fact]
    public void ParseQuery_SplitsFiltersAndWords()
    {
        var q = FuzzySearch.ParseQuery("Cat:Trav #Tip  Hello  cat:\"Two Words\" ##x");
        Assert.Equal(["hello"], q.Words);
        Assert.Equal(["trav", "two words"], q.Categories);
        Assert.Equal(["tip", "x"], q.Tags);
        Assert.True(FuzzySearch.ParseQuery(null).IsEmpty);
    }

    [Fact]
    public void Distance_CountsASwapAsOneStep()
    {
        Assert.Equal(1, FuzzySearch.Distance("form", "from"));
        Assert.Equal(1, FuzzySearch.Distance("abcd", "abd"));
        Assert.Equal(0, FuzzySearch.Distance("abc", "abc"));
        Assert.True(FuzzySearch.Distance("abcd", "wxyz") > 1);
    }

    [Fact]
    public void ExistingPromptFilter_StillWorks()
    {
        var hits = PromptFilter.Search(Seed(), null, new AppSettings(), "itinerary");
        Assert.Contains(hits, h => h.Prompt.Title == "Plan a trip itinerary");
    }
}
