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

    [Theory]
    [InlineData("{a}", true)]
    [InlineData("{a} {b} {c}", true)]
    [InlineData("{a} {b} {c} {d}", false)]
    public void PreviewStartsOpen_OnlyForThreeFieldsOrFewer(string text, bool open)
        => Assert.Equal(open, new FillInViewModel(text, new AppSettings(), new Tokens()).PreviewStartsOpen);

    [Fact]
    public void Choice_HasTheRememberedOptionSelected_AndFreeTextSelectsNone()
    {
        var settings = new AppSettings();
        settings.PlaceholderValues["tone / length"] = "Length";
        var vm = new FillInViewModel("{tone / length}", settings, new Tokens());
        var field = vm.Fields[0];

        Assert.True(field.IsChoice);
        Assert.Equal("length", field.SelectedOption); // the option as written in the prompt
        field.Value = "something else";
        Assert.Null(field.SelectedOption);
        field.SelectedOption = "tone";
        Assert.Equal("tone", field.Value);
    }
}
