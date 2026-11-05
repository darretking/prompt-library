using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamSeedLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Categories =
    [
        "Sales", "Customer support", "Marketing", "Meetings & management", "HR & people", "Operations & finance",
    ];

    [Fact]
    public void TeamSeed_24Prompts_6Categories_4Each()
    {
        var lib = TeamSeedLibrary.Create(Now);

        Assert.Equal(24, lib.Prompts.Count);
        Assert.Equal(Categories, lib.Categories.Select(c => c.Name).ToArray());
        Assert.All(lib.Categories, c => Assert.Equal(4, lib.Prompts.Count(p => p.CategoryId == c.Id)));
        Assert.All(lib.Prompts, p => Assert.Equal(Now, p.CreatedAt));
        Assert.All(lib.Prompts, p => Assert.Equal(Now, p.UpdatedAt));
    }

    [Fact]
    public void TeamSeed_UniqueTitles_PlaceholdersPresent_TitlesUnder40()
    {
        var lib = TeamSeedLibrary.Create(Now);

        Assert.Equal(24, lib.Prompts.Select(p => p.Title).Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
        Assert.Equal(24, lib.Prompts.Select(p => p.Id).Distinct().Count());
        Assert.All(lib.Prompts, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        Assert.All(lib.Prompts, p => Assert.Matches(@"\{[^{}]+\}", p.Text));
        Assert.All(lib.Prompts, p => Assert.True(p.Title.Length <= 40, $"\"{p.Title}\" is too long for the menu"));
    }

    [Fact]
    public void TeamSeed_HrFinancePolicyPrompts_CarryCheckNote()
    {
        var lib = TeamSeedLibrary.Create(Now);

        foreach (var name in new[] { "HR & people", "Operations & finance" })
        {
            var id = lib.Categories.Single(c => c.Name == name).Id;
            Assert.All(lib.Prompts.Where(p => p.CategoryId == id),
                p => Assert.Contains("check with your", p.Text, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TeamSeed_NamedPromptsExist()
    {
        var titles = TeamSeedLibrary.Create(Now).Prompts.Select(p => p.Title).ToList();

        Assert.Contains("Follow-up email after a meeting", titles);
    }
}
