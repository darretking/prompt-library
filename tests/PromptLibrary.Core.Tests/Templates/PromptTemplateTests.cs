using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.Core.Tests.Templates;

public class PromptTemplateTests
{
    private static Dictionary<string, string> V(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void Parse_PlainName_IsTextField()
    {
        var f = Assert.Single(PromptTemplate.Parse("Hello {name}!"));
        Assert.Equal("name", f.Name);
        Assert.Equal(TemplateFieldKind.Text, f.Kind);
        Assert.Empty(f.Options);
        Assert.Null(f.Hint);
    }

    [Fact]
    public void Parse_Choice_SplitsOnSlash()
    {
        var f = Assert.Single(PromptTemplate.Parse("A {relaxed / packed / mixed} trip"));
        Assert.Equal(TemplateFieldKind.Choice, f.Kind);
        Assert.Equal(["relaxed", "packed", "mixed"], f.Options);
    }

    [Fact]
    public void Parse_SlashWithoutSpaces_IsNotAChoice()
    {
        var f = Assert.Single(PromptTemplate.Parse("{and/or}"));
        Assert.Equal(TemplateFieldKind.Text, f.Kind);
        Assert.Equal("and/or", f.Name);
    }

    [Fact]
    public void Parse_LabelWithHint()
    {
        var f = Assert.Single(PromptTemplate.Parse("{platforms, e.g. Instagram and LinkedIn}"));
        Assert.Equal("platforms", f.Name);
        Assert.Equal("e.g. Instagram and LinkedIn", f.Hint);
    }

    [Fact]
    public void Parse_CommaWithoutEg_IsPartOfTheName()
        => Assert.Equal("Dear, friend", Assert.Single(PromptTemplate.Parse("{Dear, friend}")).Name);

    [Fact]
    public void Parse_DoubledBraces_AreLiteral()
    {
        Assert.Empty(PromptTemplate.Parse("function {{ return 1; }}"));
        Assert.False(PromptTemplate.HasFields("{{not a field}}"));
    }

    [Fact]
    public void Parse_DuplicateNames_MergeCaseInsensitively()
    {
        var fields = PromptTemplate.Parse("{City} then {city} then {CITY, e.g. Rome} and {other}");
        Assert.Equal(["City", "other"], fields.Select(f => f.Name));
        Assert.Equal("e.g. Rome", fields[0].Hint);
    }

    [Theory]
    [InlineData("open { never closed")]
    [InlineData("a } stray")]
    [InlineData("{}")]
    [InlineData("{   }")]
    [InlineData("{ unclosed\nnext line }")]
    [InlineData("no braces at all")]
    [InlineData("")]
    public void Parse_NoPlaceholders(string text)
    {
        Assert.Empty(PromptTemplate.Parse(text));
        Assert.False(PromptTemplate.HasFields(text));
        Assert.Equal(text, PromptTemplate.Fill(text, V()));
    }

    [Fact]
    public void Parse_NullText_IsEmpty()
    {
        Assert.Empty(PromptTemplate.Parse(null));
        Assert.Empty(PromptTemplate.Spans(null));
    }

    [Fact]
    public void Parse_UnclosedBraceThenRealPlaceholder_FirstIsLiteral()
    {
        var text = "if { then {name}";
        Assert.Equal("name", Assert.Single(PromptTemplate.Parse(text)).Name);
        Assert.Equal("if { then Ann", PromptTemplate.Fill(text, V(("name", "Ann"))));
    }

    [Fact]
    public void Fill_ReplacesEveryOccurrence_AndIgnoresCase()
        => Assert.Equal("Rome, Rome", PromptTemplate.Fill("{City}, {city}", V(("CITY", "Rome"))));

    [Fact]
    public void Fill_EmptyOrMissingValue_LeavesPlaceholder()
    {
        Assert.Equal("Hi {name} and Bo", PromptTemplate.Fill("Hi {name} and {other}", V(("name", ""), ("other", "Bo"))));
        Assert.Equal("Hi {name}", PromptTemplate.Fill("Hi {name}", V()));
        Assert.Equal("Hi {name}", PromptTemplate.Fill("Hi {name}", V(("name", "  "))));
    }

    [Fact]
    public void Fill_Choice_UsesTheWholeChoiceTextAsKey()
        => Assert.Equal("a packed pace", PromptTemplate.Fill("a {relaxed / packed} pace", V(("relaxed / packed", "packed"))));

    [Fact]
    public void Fill_HintedField_UsesLabelAsKey()
        => Assert.Equal("on Instagram", PromptTemplate.Fill("on {platforms, e.g. Instagram}", V(("platforms", "Instagram"))));

    [Fact]
    public void Fill_UnescapesDoubledBraces_WhenThereAreFields()
        => Assert.Equal("{\"a\": Bo}", PromptTemplate.Fill("{{\"a\": {x}}}", V(("x", "Bo"))));

    [Fact]
    public void Fill_ValueContainingBraces_IsNotReprocessed()
        => Assert.Equal("{x} z", PromptTemplate.Fill("{a} z", V(("a", "{x}"))));

    [Fact]
    public void Spans_GiveStartLengthAndName_ExcludingEscapes()
    {
        var spans = PromptTemplate.Spans("ab {{x}} {name} {a / b}");
        Assert.Equal(2, spans.Count);
        Assert.Equal((9, 6, "name"), spans[0]);
        Assert.Equal((16, 7, "a / b"), spans[1]);
    }

    [Fact]
    public void Spans_PointAtTheBracesInTheOriginalText()
    {
        const string text = "x {first} y {second, e.g. z}";
        foreach (var (start, length, _) in PromptTemplate.Spans(text))
        {
            Assert.Equal('{', text[start]);
            Assert.Equal('}', text[start + length - 1]);
        }
    }

    [Fact]
    public void SeedPlanATrip_ParsesAsExpected()
    {
        var text = SeedLibrary.Create(DateTimeOffset.UnixEpoch).Prompts.Single(p => p.Title == "Plan a trip itinerary").Text;
        var fields = PromptTemplate.Parse(text);

        Assert.Equal(["number", "destination", "who's going", "interests", "relaxed / packed", "budget", "area or \"not booked yet\""],
            fields.Select(f => f.Name));
        Assert.Equal("e.g. food, history, hiking", fields.Single(f => f.Name == "interests").Hint);
        Assert.Equal(TemplateFieldKind.Choice, fields.Single(f => f.Name == "relaxed / packed").Kind);

        var filled = PromptTemplate.Fill(text, V(("number", "5"), ("destination", "Lisbon"), ("relaxed / packed", "relaxed")));
        Assert.Contains("Plan a 5-day trip to Lisbon for {who's going}", filled);
        Assert.Contains("prefer a relaxed pace", filled);
    }

    [Fact]
    public void EverySeedPrompt_HasFields_AndSpansMatchParse()
    {
        foreach (var p in SeedLibrary.Create(DateTimeOffset.UnixEpoch).Prompts)
        {
            Assert.True(PromptTemplate.HasFields(p.Text), p.Title);
            Assert.NotEmpty(PromptTemplate.Spans(p.Text));
        }
    }
}
