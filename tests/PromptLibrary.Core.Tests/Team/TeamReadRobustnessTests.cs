using System.Globalization;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

/// <summary>Team files read the same under any culture, and clashing ids never break a read or a save.</summary>
public class TeamReadRobustnessTests : IDisposable
{
    private const string Cat = "11111111-1111-1111-1111-111111111111";
    private const string Cat2 = "33333333-3333-3333-3333-333333333333";
    private const string P1 = "22222222-2222-2222-2222-222222222222";
    private const string P2 = "44444444-4444-4444-4444-444444444444";

    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Prompt(string id, string title, string category = Cat)
        => $$"""{"id":"{{id}}","title":"{{title}}","description":"","categoryId":"{{category}}","text":"t","createdAt":"2026-11-05T10:00:00+00:00","updatedAt":"2026-11-05T10:00:00+00:00"}""";

    private static string Json(string prompts, string categories = $$"""{"id":"{{Cat}}","name":"Email"}""")
        => $$"""
        {"kind":"prompt-library-team","schemaVersion":1,"name":"Sales","updatedAt":"2026-11-05T10:00:00+00:00",
         "categories":[{{categories}}],"prompts":[{{prompts}}]}
        """;

    private static T InCulture<T>(string name, Func<T> body)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        try { return body(); }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void Parse_UpdatedAt_SameUnderNonGregorianCulture(string culture)
    {
        var team = InCulture(culture, () => TeamFile.Parse(Json(Prompt(P1, "A"))));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), team.UpdatedAt);
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void Cache_FetchedAt_RoundTripsUnderNonGregorianCulture(string culture)
    {
        var path = Path.Combine(_dir, "cache.json");
        var at = new DateTimeOffset(2026, 11, 5, 12, 30, 0, TimeSpan.FromHours(-6));
        var loaded = InCulture(culture, () =>
        {
            TeamCache.Save(path, new TeamSource(TeamSourceKind.File, @"C:\t\team-prompts.json"), Json(Prompt(P1, "A")), at);
            return TeamCache.Load(path);
        });
        Assert.NotNull(loaded);
        Assert.Equal(at, loaded.Value.FetchedAt);
    }

    [Fact]
    public void Parse_DuplicatePromptId_SecondGetsStableFreshId()
    {
        var json = Json(Prompt(P1, "First") + "," + Prompt(P1, "Second"));

        var a = TeamFile.Parse(json).Content.Prompts;
        var b = TeamFile.Parse(json).Content.Prompts;

        Assert.Equal(Guid.Parse(P1), a[0].Id);
        Assert.NotEqual(a[0].Id, a[1].Id);
        Assert.Equal(a[1].Id, b[1].Id); // the same on every read, so pins and markers stay
        Assert.Equal(2, a.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Parse_DuplicateCategoryId_SecondGetsFreshId_PromptsStayWithFirst()
    {
        var json = Json(Prompt(P1, "A"), $$"""{"id":"{{Cat}}","name":"Email"},{"id":"{{Cat}}","name":"Copy"}""");

        var content = TeamFile.Parse(json).Content;

        Assert.Equal(Guid.Parse(Cat), content.Categories[0].Id);
        Assert.NotEqual(content.Categories[0].Id, content.Categories[1].Id);
        Assert.Equal(Guid.Parse(Cat), content.Prompts[0].CategoryId);
    }

    [Fact]
    public void ResolveClashes_PersonalIds_TeamCopyGetsStableFreshIds_PromptsFollowCategory()
    {
        var personalPrompts = new HashSet<Guid> { Guid.Parse(P1) };
        var personalCategories = new HashSet<Guid> { Guid.Parse(Cat) };
        var json = Json(Prompt(P1, "A") + "," + Prompt(P2, "B", Cat2),
            $$"""{"id":"{{Cat}}","name":"Email"},{"id":"{{Cat2}}","name":"Other"}""");

        var first = TeamFile.Parse(json).Content;
        Assert.True(TeamIds.ResolveClashes(first, personalPrompts, personalCategories));
        var again = TeamFile.Parse(json).Content;
        TeamIds.ResolveClashes(again, personalPrompts, personalCategories);

        Assert.DoesNotContain(first.Prompts, p => personalPrompts.Contains(p.Id));
        Assert.DoesNotContain(first.Categories, c => personalCategories.Contains(c.Id));
        Assert.Equal(Guid.Parse(P2), first.Prompts[1].Id); // no clash, no change
        Assert.Equal(Guid.Parse(Cat2), first.Categories[1].Id);
        Assert.Equal(first.Categories[0].Id, first.Prompts[0].CategoryId);
        Assert.Equal(first.Prompts.Select(p => p.Id), again.Prompts.Select(p => p.Id));
        Assert.Equal(first.Categories.Select(c => c.Id), again.Categories.Select(c => c.Id));
        Assert.False(TeamIds.ResolveClashes(first, personalPrompts, personalCategories)); // nothing left to fix
    }

    [Fact]
    public void Serialize_AfterDuplicateIds_WritesDistinctIds_KeepingFirstNodesFields()
    {
        var first = Prompt(P1, "First").Replace("\"text\":\"t\"", "\"text\":\"t\",\"future\":1");
        var team = TeamFile.Parse(Json(first + "," + Prompt(P1, "Second")));

        var back = TeamFile.Parse(TeamFile.Serialize(team));

        Assert.Equal(["First", "Second"], back.Content.Prompts.Select(p => p.Title));
        Assert.Equal(2, back.Content.Prompts.Select(p => p.Id).Distinct().Count());
        Assert.Contains("\"future\"", TeamFile.Serialize(team));
    }
}
