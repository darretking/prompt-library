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

    private static string Show(FillInViewModel vm)
        => string.Concat(vm.PreviewSegments.Select(s => s.Kind switch
        {
            FillSegmentKind.Empty => $"[{s.Text}]",
            FillSegmentKind.Filled => $"<{s.Text}>",
            _ => s.Text,
        }));

    [Fact]
    public void PreviewSegments_ShowEmptyBlanksAsChipsAndFilledValuesAsValues()
    {
        var vm = new FillInViewModel("Trip of {number} days for {who's going}.", new AppSettings(), new Tokens());
        Assert.Equal("Trip of [Number] days for [Who's going].", Show(vm));
        Assert.Equal(FillSegmentKind.Empty, vm.PreviewSegments[1].Kind);
    }

    [Fact]
    public void PreviewSegments_UpdateOnEveryKeystroke_AndRaiseTheChangeNotification()
    {
        var vm = new FillInViewModel("Trip of {number} days for {who's going}.", new AppSettings(), new Tokens());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var seen = new List<string>();
        foreach (var typed in new[] { "5", "50", "5", "" })
        {
            vm.Fields[0].Value = typed;
            seen.Add(Show(vm));
        }

        Assert.Equal(["Trip of <5> days for [Who's going].", "Trip of <50> days for [Who's going].",
            "Trip of <5> days for [Who's going].", "Trip of [Number] days for [Who's going]."], seen);
        Assert.Equal(4, raised.Count(n => n == nameof(FillInViewModel.PreviewSegments)));
    }

    [Fact]
    public void PreviewSegments_FillTokensAndEscapesLikeThePastedText()
    {
        var vm = new FillInViewModel("{{x}} {a} on {date}{cursor}", new AppSettings(), new Tokens());
        vm.Fields[0].Value = "Z";
        Assert.Equal("{x} <Z> on 11/3/2026", Show(vm));
        Assert.Equal(vm.PreviewText, string.Concat(vm.PreviewSegments.Select(s => s.Text)));
    }

    [Fact]
    public void Labels_StartWithACapital_AndKeepTheRestAsWritten()
    {
        var vm = new FillInViewModel("{number} {who's going} {iPhone model} {city, e.g. Oslo}", new AppSettings(), new Tokens());
        Assert.Equal(["Number", "Who's going", "IPhone model", "City"], vm.Fields.Select(f => f.Label));
        Assert.Equal("number", vm.Fields[0].Name); // the key is unchanged
    }
}
