using System.Globalization;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.Core.Tests.Templates;

public class PromptPreviewTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 14, 5, 0);
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    private static Dictionary<string, string> V(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private static IReadOnlyList<PreviewBlock> Build(string text, params (string, string)[] values)
        => PromptPreview.Build(text, V(values), Now, Culture);

    private static (PreviewPieceKind, string)[] Kinds(PreviewBlock block) => block.Pieces.Select(p => (p.Kind, p.Text)).ToArray();

    [Fact]
    public void RememberedValue_ShowsAsAFilledChip_AnyCase()
    {
        var block = Assert.Single(Build("Trip to {Destination} now", ("destination", "Lisbon")));

        Assert.Equal(PreviewBlockKind.Paragraph, block.Kind);
        Assert.Equal(
            [(PreviewPieceKind.Text, "Trip to "), (PreviewPieceKind.Filled, "Lisbon"), (PreviewPieceKind.Text, " now")],
            Kinds(block));
    }

    [Fact]
    public void NoValue_ShowsAMutedChipWithTheFieldName_WithoutBracesOrHint()
    {
        var block = Assert.Single(Build("Plan {number}-day trip, like {interests, e.g. food, history}", ("number", " ")));

        Assert.Equal(
            [(PreviewPieceKind.Text, "Plan "), (PreviewPieceKind.Empty, "number"), (PreviewPieceKind.Text, "-day trip, like "),
             (PreviewPieceKind.Empty, "interests")],
            Kinds(block)); // a blank remembered value counts as none
    }

    [Fact]
    public void ChoiceField_ShowsTheRememberedOption_OrAllOptions()
    {
        var empty = Assert.Single(Build("Pace: {relaxed / packed}"));
        Assert.Equal((PreviewPieceKind.Empty, "relaxed / packed"), Kinds(empty)[1]);

        var filled = Assert.Single(Build("Pace: {relaxed / packed}", ("relaxed / packed", "packed")));
        Assert.Equal((PreviewPieceKind.Filled, "packed"), Kinds(filled)[1]);
    }

    [Fact]
    public void DynamicTokens_DateAndTimeAreToday_ClipboardIsMuted_CursorIsAMarker()
    {
        var block = Assert.Single(Build("{date} {time} {clipboard}{cursor}"));

        Assert.Equal(
            [(PreviewPieceKind.Filled, "11/11/2026"), (PreviewPieceKind.Text, " "), (PreviewPieceKind.Filled, "14:05"),
             (PreviewPieceKind.Text, " "), (PreviewPieceKind.Clipboard, "clipboard"), (PreviewPieceKind.Cursor, "")],
            Kinds(block));
    }

    [Fact]
    public void EscapedBraces_AreLiteralText_NotChips()
    {
        var block = Assert.Single(Build("Use {{braces}} here"));

        Assert.Equal([(PreviewPieceKind.Text, "Use {braces} here")], Kinds(block));
    }

    [Fact]
    public void Markdown_HeadingsBoldBulletsAndBlankLines()
    {
        var blocks = Build("# Title\r\nSome **bold** text\r\n\r\n- one\r\n  - nested {x}\r\n2. two");

        Assert.Equal(
            [PreviewBlockKind.Heading, PreviewBlockKind.Paragraph, PreviewBlockKind.Blank, PreviewBlockKind.Bullet,
             PreviewBlockKind.Bullet, PreviewBlockKind.Bullet],
            blocks.Select(b => b.Kind));
        Assert.Equal("Title", blocks[0].Pieces.Single().Text);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal(
            [("Some ", false), ("bold", true), (" text", false)],
            blocks[1].Pieces.Select(p => (p.Text, p.Bold)));
        Assert.Equal(("•", 0), (blocks[3].Marker, blocks[3].Level));
        Assert.Equal(("•", 1), (blocks[4].Marker, blocks[4].Level));
        Assert.Equal(PreviewPieceKind.Empty, blocks[4].Pieces[1].Kind); // a placeholder inside a bullet
        Assert.Equal("2.", blocks[5].Marker);
    }

    [Fact]
    public void FencedCode_IsOneBlock_WithPlaceholdersFilled_AndNoMarkdown()
    {
        var blocks = Build("Run:\n```bash\nls **{dir}**\n# not a heading\n```\nDone", ("dir", "src"));

        Assert.Equal([PreviewBlockKind.Paragraph, PreviewBlockKind.Code, PreviewBlockKind.Paragraph], blocks.Select(b => b.Kind));
        var code = blocks[1];
        Assert.Equal(
            [(PreviewPieceKind.Text, "ls **"), (PreviewPieceKind.Filled, "src"), (PreviewPieceKind.Text, "**"),
             (PreviewPieceKind.Text, "\n"), (PreviewPieceKind.Text, "# not a heading")],
            Kinds(code));
        Assert.All(code.Pieces, p => Assert.False(p.Bold));
    }

    [Fact]
    public void UnclosedFence_StillShowsTheCode()
    {
        var blocks = Build("```\nfoo");

        Assert.Equal(PreviewBlockKind.Code, Assert.Single(blocks).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoText_NoBlocks(string? text) => Assert.Empty(PromptPreview.Build(text, null, Now));

    [Fact]
    public void NullValues_AreTreatedAsNone()
    {
        var block = Assert.Single(PromptPreview.Build("{a}", null, Now, Culture));

        Assert.Equal((PreviewPieceKind.Empty, "a"), Kinds(block)[0]);
    }
}
