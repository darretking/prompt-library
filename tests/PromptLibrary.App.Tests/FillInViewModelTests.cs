using System.Globalization;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

public class FillInViewModelTests
{
    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => "from clipboard";
        public DateTime Now => new(2026, 11, 3, 14, 5, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void Fields_ArePrefilledFromRememberedValues_AndTokensAreNotFields()
    {
        var settings = new AppSettings();
        settings.PlaceholderValues["name"] = "Ann";
        settings.PlaceholderValues["tone / length"] = "short";
        var vm = new FillInViewModel("Hi {Name} {tone / length} {clipboard} {date} {city, e.g. Oslo}", settings, new Tokens());

        Assert.Equal(["Name", "tone / length", "city"], vm.Fields.Select(f => f.Name));
        Assert.Equal(["Ann", "short", ""], vm.Fields.Select(f => f.Value));
        Assert.True(vm.Fields[1].IsChoice);
        Assert.Equal("e.g. Oslo", vm.Fields[2].Hint);
    }

    [Fact]
    public void Preview_IsLive_AndKeepsBlanksVisible()
    {
        var vm = new FillInViewModel("A {x} B {y} {clipboard}", new AppSettings(), new Tokens());
        Assert.Equal("A {x} B {y} from clipboard", vm.PreviewText);

        string? changed = null;
        vm.PropertyChanged += (_, e) => changed = e.PropertyName;
        vm.Fields[0].Value = "1";

        Assert.Equal(nameof(FillInViewModel.PreviewText), changed);
        Assert.Equal("A 1 B {y} from clipboard", vm.PreviewText);
    }

    [Fact]
    public void Complete_RaisesFilledTextAndCaret_AndRemembersNonEmptyValues()
    {
        var settings = new AppSettings();
        var vm = new FillInViewModel("Dear {Name},{cursor}\r\nBye {skip}", settings, new Tokens());
        vm.Fields[0].Value = "Ann";

        string? text = null;
        int? caret = -1;
        vm.Completed += (t, c) => { text = t; caret = c; };
        vm.Complete();

        Assert.Equal("Dear Ann,\r\nBye {skip}", text);
        Assert.Equal(11, caret); // "\r\nBye {skip}" = 1 line break + 10 characters
        Assert.Equal("Ann", settings.PlaceholderValues["name"]);
        Assert.False(settings.PlaceholderValues.ContainsKey("skip"));
    }

    [Fact]
    public void Cancel_RaisesCancelled_AndStoresNothing()
    {
        var settings = new AppSettings();
        var vm = new FillInViewModel("{a}", settings, new Tokens());
        vm.Fields[0].Value = "typed";
        var cancelled = false;
        vm.Cancelled += () => cancelled = true;
        vm.Cancel();
        Assert.True(cancelled);
        Assert.Empty(settings.PlaceholderValues);
    }

    [Fact]
    public void IsFirstAndIsLast_IdentifyTheEnds()
    {
        var vm = new FillInViewModel("{a} {b} {c}", new AppSettings(), new Tokens());
        Assert.True(vm.IsFirst(vm.Fields[0]));
        Assert.False(vm.IsFirst(vm.Fields[1]));
        Assert.True(vm.IsLast(vm.Fields[2]));
        Assert.False(vm.IsLast(vm.Fields[0]));
    }
}
