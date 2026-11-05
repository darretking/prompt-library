using System.Text.Json.Nodes;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamFileTests
{
    private const string Cat = "11111111-1111-1111-1111-111111111111";
    private const string P1 = "22222222-2222-2222-2222-222222222222";

    private static string Json(int version = 1, string kind = "prompt-library-team", string extraRoot = "", string extraPrompt = "",
        string promptCategory = Cat, string text = "Hello")
        => $$"""
        {"kind":"{{kind}}","schemaVersion":{{version}},"name":"Sales team","updatedAt":"2026-11-05T10:00:00+00:00"{{extraRoot}},
         "categories":[{"id":"{{Cat}}","name":"Email"}],
         "prompts":[{"id":"{{P1}}","title":"Intro","description":"d","categoryId":"{{promptCategory}}","text":"{{text}}",
           "createdAt":"2026-11-05T10:00:00+00:00","updatedAt":"2026-11-05T10:00:00+00:00"{{extraPrompt}}}]}
        """;

    [Fact]
    public void Parse_ValidFile_ReadsNameCategoriesPrompts()
    {
        var team = TeamFile.Parse(Json());
        Assert.Equal("Sales team", team.Name);
        Assert.Equal(1, team.SchemaVersion);
        Assert.False(team.IsNewerSchema);
        Assert.Single(team.Content.Categories);
        Assert.Equal("Intro", Assert.Single(team.Content.Prompts).Title);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void Parse_WrongKindOrMissing_Throws(string kind)
    {
        Assert.Throws<InvalidDataException>(() => TeamFile.Parse(Json(kind: kind)));
        Assert.Throws<InvalidDataException>(() => TeamFile.Parse("""{"schemaVersion":1,"name":"x","categories":[],"prompts":[]}"""));
    }

    [Fact]
    public void Parse_BadJson_Throws()
        => Assert.Throws<InvalidDataException>(() => TeamFile.Parse("{ not json"));

    [Fact]
    public void Parse_PromptWithMissingCategory_Throws()
        => Assert.Throws<InvalidDataException>(() =>
            TeamFile.Parse(Json(promptCategory: "33333333-3333-3333-3333-333333333333")));

    [Fact]
    public void NewerSchema_IsReadOnly()
    {
        var team = TeamFile.Parse(Json(version: 2));
        Assert.True(team.IsNewerSchema);
        Assert.Equal(2, team.SchemaVersion);
        Assert.Single(team.Content.Prompts);
    }

    [Fact]
    public void Parse_ZeroOrMissingSchema_Throws()
    {
        Assert.Throws<InvalidDataException>(() => TeamFile.Parse(Json(version: 0)));
        Assert.Throws<InvalidDataException>(() => TeamFile.Parse(
            """{"kind":"prompt-library-team","name":"x","categories":[],"prompts":[]}"""));
    }

    [Fact]
    public void Save_PreservesUnknownFields()
    {
        var team = TeamFile.Parse(Json(extraRoot: ",\"x\":1", extraPrompt: ",\"tags\":[\"a\"]"));
        team.Content.Prompts[0].Text = "Edited";
        var output = JsonNode.Parse(TeamFile.Serialize(team))!;
        Assert.Equal(1, (int)output["x"]!);
        var prompt = output["prompts"]![0]!;
        Assert.Equal("a", (string)prompt["tags"]![0]!);
        Assert.Equal("Edited", (string)prompt["text"]!);
        Assert.Equal(TeamFile.Kind, (string)output["kind"]!);
    }

    [Fact]
    public void Serialize_AddedAndRemovedPrompts()
    {
        var team = TeamFile.Parse(Json());
        team.Content.Prompts.Clear();
        team.Content.Prompts.Add(new() { Id = Guid.NewGuid(), Title = "New", CategoryId = team.Content.Categories[0].Id, Text = "t" });
        var again = TeamFile.Parse(TeamFile.Serialize(team));
        Assert.Equal("New", Assert.Single(again.Content.Prompts).Title);
    }

    [Fact]
    public void Serialize_EmojiAndCrlf_RoundTrip()
    {
        var team = TeamFile.Parse(Json());
        team.Content.Prompts[0].Text = "Hi \U0001F600\r\nline";
        var text = TeamFile.Serialize(team);
        Assert.Contains("\U0001F600", text);
        Assert.DoesNotContain('\\' + "ud83d", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Hi \U0001F600\r\nline", TeamFile.Parse(text).Content.Prompts[0].Text);
    }

    [Fact]
    public void Hash_IgnoresLineEndingDifferences()
    {
        Assert.Equal(TeamFile.Hash("a\r\nb"), TeamFile.Hash("a\nb"));
        Assert.NotEqual(TeamFile.Hash("a"), TeamFile.Hash("b"));
        Assert.Equal(64, TeamFile.Hash("a").Length);
    }
}
