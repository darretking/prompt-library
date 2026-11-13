using System.Globalization;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

public class FillInTeamValuesTests
{
    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => "clip";
        public DateTime Now => new(2026, 11, 3, 14, 5, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");
    }

    private static readonly Dictionary<string, string> Team = new() { ["name"] = "Team Ann", ["city"] = "Oslo", ["date"] = "x" };

    [Fact]
    public void Resolver_OwnThenTeamThenNone_AndTokensNeverLookedUp()
    {
        var own = new Dictionary<string, string> { ["name"] = "Mine", ["city"] = "  " };
        var d = new FillInDefaults(own, Team);

        Assert.True(d.TryGet("NAME", out var v, out var fromTeam));
        Assert.Equal("Mine", v); Assert.False(fromTeam);
        Assert.True(d.TryGet("City", out v, out fromTeam));
        Assert.Equal("Oslo", v); Assert.True(fromTeam);
        Assert.False(d.TryGet("other", out _, out _));
        Assert.False(d.TryGet("date", out _, out _));
    }

    [Fact]
    public void Form_PrefillsFromTeam_OwnWins_HintOnlyForTeam()
    {
        var settings = new AppSettings();
        settings.PlaceholderValues["name"] = "Mine";
        var vm = new FillInViewModel("{Name} {City} {Other}", settings, new Tokens(), teamValues: Team);

        Assert.Equal(["Mine", "Oslo", ""], vm.Fields.Select(f => f.Value));
        Assert.Equal([false, true, false], vm.Fields.Select(f => f.FromTeam));
    }

    [Fact]
    public void Form_ChoiceField_TakesTeamValue()
    {
        var vm = new FillInViewModel("{tone / length}", new AppSettings(), new Tokens(),
            teamValues: new Dictionary<string, string> { ["tone / length"] = "length" });
        Assert.Equal("length", vm.Fields[0].Value);
        Assert.True(vm.Fields[0].FromTeam);
    }

    [Fact]
    public void Form_NoTeam_IsUnchanged()
    {
        var vm = new FillInViewModel("{city}", new AppSettings(), new Tokens());
        Assert.Equal("", vm.Fields[0].Value);
        Assert.False(vm.Fields[0].FromTeam);
    }

    [Fact]
    public void Complete_WritesTheMembersValue()
    {
        var settings = new AppSettings();
        var vm = new FillInViewModel("{city} {name}", settings, new Tokens(), teamValues: Team);
        vm.Fields[0].Value = "Bergen";
        vm.Fields[1].Value = " ";
        vm.Complete();

        Assert.Equal("Bergen", settings.PlaceholderValues["city"]);
        Assert.False(settings.PlaceholderValues.ContainsKey("name"));
    }

    [Fact]
    public void Merge_OwnOverTeam_ForPreview()
    {
        var own = new Dictionary<string, string> { ["name"] = "Mine" };
        var merged = new FillInDefaults(own, Team).Merge();
        Assert.Equal("Mine", merged["NAME"]);
        Assert.Equal("Oslo", merged["city"]);
        Assert.False(merged.ContainsKey("date"));
    }
}
