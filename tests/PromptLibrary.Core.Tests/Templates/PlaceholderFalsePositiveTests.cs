using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.Core.Tests.Templates;

/// <summary>Braces in real coding prompts are not placeholders; escapes and code spans behave.</summary>
public class PlaceholderFalsePositiveTests
{
    private static Dictionary<string, string> V(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    public static TheoryData<string> CodeSamples => new()
    {
        "public int Add(int a, int b) { return a + b; }",
        "Fix this:\npublic void Run()\n{\n    var x = 1;\n}\n",
        "const add = (a, b) => { return a + b };",
        "const f = x => ({ id: x });",
        "Parse {\"name\": \"Ann\", \"age\": 3} into a record",
        "{\"items\": [1, 2, 3]}",
        "Match the regex a{2,3} and \\d{4}-\\d{2}",
        "Use [a-z]{3} for the code",
        "body { color: red; margin: 0 }",
        ".card{padding:8px}",
        "print(f\"{x}\")",
        "name = f'{first} {last}'",
        "Console.WriteLine($\"Hello {name}\");",
        "println!(\"Hi {name}\");",
        "The fraction is \\frac{a}{b} and x^{2}",
        "Template literal: `Hello ${name}` in JS",
        "{ get; set; }",
        "if (ok) { run() }",
        "Use `{name}` as the key",
        "Example:\n```json\n{ \"a\": 1 }\n```\nDone",
        "```\nfunction f() { return {a}; }\n```",
        "String.Format(\"{0} is {1}\", a, b)",
        "x = {1, 2, 3}",
        "Ruby says \"hi #{name}\"",
    };

    [Theory]
    [MemberData(nameof(CodeSamples))]
    public void CodingPrompts_HaveNoFields(string text)
    {
        Assert.False(PromptTemplate.HasFields(text), text);
        Assert.Empty(PromptTemplate.Parse(text));
    }

    [Theory]
    [InlineData("{1name;}")]
    [InlineData("{a=b}")]
    [InlineData("{f(x)}")]
    [InlineData("{a[0]}")]
    [InlineData("{a<b>}")]
    [InlineData("{'a'}")]
    [InlineData("{key: value}")]
    [InlineData("{#tag}")]
    [InlineData("{ name }")]
    [InlineData("{2,3}")]
    [InlineData("{`x`}")]
    public void ContentThatIsNotALabel_IsNotAPlaceholder(string text)
        => Assert.False(PromptTemplate.HasFields(text), text);

    [Fact]
    public void LongContent_IsNotAPlaceholder()
        => Assert.False(PromptTemplate.HasFields("{" + new string('a', 61) + "}"));

    [Theory]
    [InlineData("Hello {name}")]
    [InlineData("Plan {5} dinners")]
    [InlineData("Write {3 words, e.g. calm, premium}")]
    [InlineData("Be {friendly / formal}")]
    [InlineData("{child's name} is {area or \"not booked yet\"}")]
    [InlineData("Rewrite this: \"{text}\"")]
    [InlineData("Fix this:\n```\n{paste code}\n```")]
    public void Labels_AreStillPlaceholders(string text)
        => Assert.True(PromptTemplate.HasFields(text), text);

    [Fact]
    public void FencedCode_WithAPlaceholderLine_StillFillsThatLine()
    {
        var text = "Explain:\n```\n{paste code}\n```\nand {name}";
        Assert.Equal(["paste code", "name"], PromptTemplate.Parse(text).Select(f => f.Name));
        Assert.Equal("Explain:\n```\nx = 1\n```\nand Bo", PromptTemplate.Fill(text, V(("paste code", "x = 1"), ("name", "Bo"))));
    }

    [Fact]
    public void BracesInCode_AreKeptOnFill_AndFieldsOutsideAreFilled()
    {
        var text = "Use `{name}` and {name} in\n```\n{{ x }} {name}\n```";
        Assert.Equal("Use `{name}` and Bo in\n```\n{{ x }} {name}\n```", PromptTemplate.Fill(text, V(("name", "Bo"))));
    }

    [Theory]
    [InlineData("a {{ b }} c", "a { b } c")]
    [InlineData("{{name}}", "{name}")]
    [InlineData("no braces", "no braces")]
    public void DoubledBraces_BecomeLiteral_WhenThereAreNoFields(string text, string expected)
    {
        Assert.False(PromptTemplate.HasFields(text));
        Assert.Equal(expected, PromptTemplate.Fill(text, V()));
        Assert.Equal(expected, PromptTemplate.Fill(text, V(), null).Text);
    }

    [Fact]
    public void Spans_IgnoreBracesInCode()
        => Assert.Empty(PromptTemplate.Spans("`{a}` and ```{b}``` and f\"{c}\""));

    [Fact]
    public void SeedPlanATrip_StillHasSevenFields_IncludingAreaWithQuotes()
    {
        var text = SeedLibrary.Create(DateTimeOffset.UnixEpoch).Prompts.Single(p => p.Title == "Plan a trip itinerary").Text;
        Assert.Equal(7, PromptTemplate.Parse(text).Count);
    }

    // Field counts of the shipped seed prompts before the placeholder rules were tightened; they must not change.
    private static readonly Dictionary<string, int> SeedFieldCounts = new()
    {
        ["Social media content calendar"] = 5,
        ["Ad copy in five angles"] = 3,
        ["Ideal customer profile"] = 1,
        ["Plan a landing page"] = 3,
        ["Review my homepage"] = 1,
        ["Colours and fonts"] = 3,
        ["Plan my week"] = 3,
        ["Break down a big task"] = 2,
        ["Turn notes into actions"] = 2,
        ["Party games for kids"] = 5,
        ["Rainy-day activities"] = 1,
        ["Personalised bedtime story"] = 5,
        ["Cook with what I have"] = 4,
        ["Weekly meal plan"] = 5,
        ["Adapt a recipe"] = 1,
        ["Test a business idea"] = 3,
        ["SWOT analysis"] = 3,
        ["Set my prices"] = 4,
        ["Reply to an email"] = 3,
        ["Make this sound better"] = 2,
        ["Summarise this"] = 1,
        ["Explain it simply"] = 2,
        ["Make me a study plan"] = 5,
        ["Quiz me"] = 2,
        ["Beginner workout plan"] = 7,
        ["Build a healthy habit"] = 2,
        ["Desk stretch routine"] = 2,
        ["Plan a trip itinerary"] = 7,
        ["Packing list"] = 6,
        ["Local tips and phrases"] = 1,
        ["Make a monthly budget"] = 4,
        ["Savings goal plan"] = 4,
        ["Help me decide on a purchase"] = 6,
        ["Tailor my CV to a job"] = 3,
        ["Interview practice"] = 3,
        ["Ask for a raise"] = 5,
        ["Explain this code"] = 1,
        ["Fix this error"] = 1,
        ["Review my code"] = 1,
    };

    [Fact]
    public void EverySeedPrompt_KeepsItsFieldCount()
    {
        var prompts = SeedLibrary.Create(DateTimeOffset.UnixEpoch).Prompts;
        Assert.Equal(SeedFieldCounts.Count, prompts.Count);
        foreach (var p in prompts)
            Assert.Equal(SeedFieldCounts[p.Title], PromptTemplate.Parse(p.Text).Count);
    }
}
