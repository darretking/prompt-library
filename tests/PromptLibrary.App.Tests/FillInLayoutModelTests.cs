using System.Globalization;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

public class FillInLayoutModelTests
{
    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => null;
        public DateTime Now => new(2026, 11, 3, 14, 5, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");
    }

    private static FillField Choice(string text = "{relaxed / packed / mixed}", string? remembered = null)
    {
        var settings = new AppSettings();
        if (remembered is not null) settings.PlaceholderValues["relaxed / packed / mixed"] = remembered;
        return new FillInViewModel(text, settings, new Tokens()).Fields[0];
    }

    [Fact]
    public void Choice_IsAPlainTextValue_NothingElseEverChangesIt()
    {
        var field = Choice(remembered: "Packed");
        Assert.True(field.IsChoice);
        Assert.Equal("Packed", field.Value); // the remembered text exactly
        field.Value = "something else";
        Assert.Equal("something else", field.Value);
        field.IsListOpen = true;
        Assert.Equal("something else", field.Value); // opening the list does not touch the text
    }

    [Theory]
    [InlineData("p", "packed")]
    [InlineData("PA", "packed")]
    [InlineData("m", "mixed")]
    [InlineData("packed", null)]   // already whole
    [InlineData("x", null)]
    [InlineData("", null)]
    public void CompletionFor_IsThePrefixMatchingOption(string typed, string? expected)
        => Assert.Equal(expected, Choice().CompletionFor(typed));

    [Fact]
    public void List_OpensOnTheMatchingOption_ArrowsMoveAndStopAtTheEnds()
    {
        var field = Choice(remembered: "packed");
        field.MoveHighlight(+1);                       // Down opens the list
        Assert.True(field.IsListOpen);
        Assert.Equal("packed", field.Highlighted);
        field.MoveHighlight(+1);
        field.MoveHighlight(+1);
        Assert.Equal("mixed", field.Highlighted);
        field.MoveHighlight(-5);
        Assert.Equal("relaxed", field.Highlighted);
    }

    [Fact]
    public void Enter_InTheOpenList_TakesTheHighlightedOption_ElseIsNotHandled()
    {
        var field = Choice();
        Assert.False(field.PickHighlighted());          // closed: Enter moves on as usual
        field.MoveHighlight(+1);
        field.MoveHighlight(+1);
        Assert.True(field.PickHighlighted());
        Assert.Equal("packed", field.Value);
        Assert.False(field.IsListOpen);
    }

    [Fact]
    public void PickingAnOption_ClosesTheList()
    {
        var field = Choice();
        field.IsListOpen = true;
        field.Pick("mixed");
        Assert.Equal("mixed", field.Value);
        Assert.False(field.IsListOpen);
    }

    [Fact]
    public void TextField_NeverOpensAList()
    {
        var vm = new FillInViewModel("{name}", new AppSettings(), new Tokens());
        vm.Fields[0].IsListOpen = true;
        Assert.False(vm.Fields[0].IsListOpen);
    }

    [Fact]
    public void Preview_UpdatesOnlyWhenAValueChanges()
    {
        var vm = new FillInViewModel("A {relaxed / packed} trip", new AppSettings(), new Tokens());
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FillInViewModel.PreviewText)) raised++; };
        vm.Fields[0].IsListOpen = true;
        Assert.Equal(0, raised);
        vm.Fields[0].Value = "packed";
        Assert.Equal(1, raised);
        Assert.Equal("A packed trip", vm.PreviewText);
    }
}
