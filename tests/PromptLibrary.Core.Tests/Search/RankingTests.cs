using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;
using PromptLibrary.Core.Seed;

namespace PromptLibrary.Core.Tests.Search;

/// <summary>The best result must come first (not merely be somewhere in the list) for each kind of query.</summary>
public class RankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<SearchResult> Run(Library lib, string q)
        => FuzzySearch.Search(lib, null, new AppSettings(), q, Now);

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
    public void Typo_itinerery_ranks_the_itinerary_prompt_first()
        => Assert.Equal("Plan a trip itinerary", Run(SeedLibrary.Create(Now), "itinerery")[0].Prompt.Title);

    [Fact]
    public void Word_order_does_not_matter_trip_plan_ranks_plan_a_trip_first()
        => Assert.Equal("Plan a trip itinerary", Run(SeedLibrary.Create(Now), "trip plan")[0].Prompt.Title);

    [Fact]
    public void Partial_word_budg_ranks_a_budget_titled_prompt_first()
    {
        var results = Run(SeedLibrary.Create(Now), "budg");
        Assert.Contains("budget", results[0].Prompt.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Partial_word_in_the_title_beats_the_same_word_only_in_the_text()
    {
        var lib = Small(("Aardvark notes", "", "set a budget first", []), ("Budget planner", "", "", []));
        Assert.Equal("Budget planner", Run(lib, "budg")[0].Prompt.Title);
    }

    [Fact]
    public void Tag_filter_ranks_the_exact_tag_before_a_longer_tag_that_only_starts_with_it()
    {
        var lib = Small(("Alpha", "", "", ["travelogue"]), ("Zulu", "", "", ["travel"]));
        Assert.Equal("Zulu", Run(lib, "#travel")[0].Prompt.Title);
    }

    [Fact]
    public void Tag_filter_on_the_seed_finds_the_tagged_prompts()
    {
        var results = Run(SeedLibrary.Create(Now), "#trip");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains(r.Prompt.Tags, t => t.StartsWith("trip", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Category_filter_with_a_word_ranks_the_best_prompt_in_that_category_first()
    {
        var results = Run(SeedLibrary.Create(Now), "cat:mon budget");
        Assert.Equal("Make a monthly budget", results[0].Prompt.Title);
    }

    [Fact]
    public void A_title_hit_beats_a_body_hit()
    {
        var lib = Small(("Aardvark facts", "", "write a summary of the article", []), ("Summary writer", "", "", []));
        Assert.Equal("Summary writer", Run(lib, "summary")[0].Prompt.Title);
    }
}
