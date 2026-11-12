using System.Globalization;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.Core.Tests.Templates;

public class DynamicTokenTests
{
    private sealed class Tokens(string? clipboard = "CLIP") : ITokenValues
    {
        public string? Clipboard { get; } = clipboard;
        public DateTime Now { get; } = new(2026, 11, 3, 14, 5, 0);
        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-GB");
    }

    private static readonly Dictionary<string, string> None = [];

    [Fact]
    public void Tokens_AreNeverFormFields()
    {
        var text = "{clipboard} {DATE} {time} {cursor} {name}";
        Assert.Equal(["name"], PromptTemplate.Parse(text).Select(f => f.Name));
        Assert.False(PromptTemplate.HasFields("Today is {date}{cursor}"));
        Assert.True(PromptTemplate.HasFields("{date} {name}"));
    }

    [Fact]
    public void Fill_ReplacesClipboardDateAndTime()
    {
        var r = PromptTemplate.Fill("Fix: {clipboard} on {date} at {time}", None, new Tokens());
        Assert.Equal("Fix: CLIP on 03/11/2026 at 14:05", r.Text);
        Assert.Null(r.CaretBackSteps);
    }

    [Fact]
    public void Fill_NoClipboardText_LeavesTheTokenVisible()
        => Assert.Equal("a {clipboard} b", PromptTemplate.Fill("a {clipboard} b", None, new Tokens(null)).Text);

    [Fact]
    public void Fill_Cursor_IsRemovedAndCountsTheCharactersAfterIt()
    {
        var r = PromptTemplate.Fill("Hello {cursor}, {name}!", new Dictionary<string, string> { ["name"] = "Bo" }, new Tokens());
        Assert.Equal("Hello , Bo!", r.Text);
        Assert.Equal(5, r.CaretBackSteps); // ", Bo!"
    }

    [Fact]
    public void Fill_Cursor_CountsALineBreakAsOneStep()
        => Assert.Equal(3, PromptTemplate.Fill("x{cursor}\r\nab", None, new Tokens()).CaretBackSteps);

    [Fact]
    public void Fill_Cursor_AtTheEnd_IsZeroSteps()
        => Assert.Equal(0, PromptTemplate.Fill("x{cursor}", None, new Tokens()).CaretBackSteps);

    [Fact]
    public void Fill_ClipboardTextWithBraces_IsNotScannedAgain()
    {
        var r = PromptTemplate.Fill("{clipboard}|{cursor}", None, new Tokens("{date} {name}"));
        Assert.Equal("{date} {name}|", r.Text);
        Assert.Equal(0, r.CaretBackSteps);
    }

    [Fact]
    public void Fill_WithoutTokenValues_LeavesTokensAsWritten()
    {
        Assert.Equal("a {date} {cursor}", PromptTemplate.Fill("a {date} {cursor}", None));
        Assert.Equal("a {date}", PromptTemplate.Fill("a {date}", new Dictionary<string, string> { ["date"] = "x" }));
    }

    [Fact]
    public void Spans_IncludeTokens() => Assert.Equal(["date", "n"], PromptTemplate.Spans("{date} {n}").Select(s => s.Name));
}
