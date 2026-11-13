using System.Globalization;
using System.Windows.Input;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

/// <summary>Prompts with no fields paste at once (tokens still filled); screen-reader names of rows, actions and hints.</summary>
public class SearchNoFieldAndNamesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => "CLIP";
        public DateTime Now => new(2026, 11, 3, 9, 7, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-GB");
    }

    private sealed class Host : ISearchActions
    {
        public Library Library { get; } = new();
        public Library? TeamLibrary => null;
        public AppSettings Settings { get; } = new();
        public ITokenValues Tokens { get; } = new Tokens();
        public List<string> Calls { get; } = [];
        public void Paste(Prompt prompt, string text, int? caretBackSteps) => Calls.Add($"paste:{text}:{caretBackSteps}");
        public void CopyOnly(Prompt prompt, string text) => Calls.Add($"copy:{text}");
        public void Send(Prompt prompt, string text, string targetId) => Calls.Add($"send:{text}:{targetId}");
        public void OpenInMainWindow(Prompt prompt, bool isTeam) { }
        public void PinsChanged() { }
        public void Close() => Calls.Add("close");
    }

    private static (SearchViewModel Vm, Host Host) Make(string text, bool pinned = false, string title = "P")
    {
        var host = new Host();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Travel", Emoji = "✈️" };
        host.Library.Categories.Add(cat);
        var prompt = new Prompt { Id = Guid.NewGuid(), Title = title, Text = text, CategoryId = cat.Id };
        host.Library.Prompts.Add(prompt);
        if (pinned) host.Settings.PinnedPromptIds.Add(prompt.Id);
        var vm = new SearchViewModel(host, () => Now);
        vm.Reset();
        return (vm, host);
    }

    [Fact]
    public void Enter_OnAPromptWithNoFields_PastesAtOnce_NoForm() => Sta.Run(() =>
    {
        var (vm, host) = Make("public int Add(int a, int b) { return a + b; }");
        Assert.True(vm.HandleKey(Key.Enter, ModifierKeys.None));
        Assert.Null(vm.Form);
        Assert.Equal(["paste:public int Add(int a, int b) { return a + b; }:"], host.Calls);
    });

    [Fact]
    public void Enter_NoFields_StillFillsTokens_AndTurnsDoubledBracesLiteral() => Sta.Run(() =>
    {
        var (vm, host) = Make("On {date} at {time}: {{literal}} {clipboard}{cursor}!");
        vm.UseSelected();
        Assert.Null(vm.Form);
        Assert.Equal(["paste:On 03/11/2026 at 09:07: {literal} CLIP!:1"], host.Calls);
    });

    [Fact]
    public void AltN_OnANoFieldPrompt_PastesAtOnce() => Sta.Run(() =>
    {
        var (vm, host) = Make("plain text");
        Assert.True(vm.HandleKey(Key.D1, ModifierKeys.Alt));
        Assert.Null(vm.Form);
        Assert.Equal(["paste:plain text:"], host.Calls);
    });

    [Fact]
    public void AltN_OnAFieldPrompt_OpensTheForm() => Sta.Run(() =>
    {
        var (vm, host) = Make("Hello {name}");
        vm.HandleKey(Key.D1, ModifierKeys.Alt);
        Assert.NotNull(vm.Form);
        Assert.Empty(host.Calls);
    });

    [Fact]
    public void CtrlK_Paste_OnANoFieldPrompt_PastesAtOnce() => Sta.Run(() =>
    {
        var (vm, host) = Make("var x = new { a = 1 };");
        vm.HandleKey(Key.K, ModifierKeys.Control);
        Assert.Equal("Paste", vm.SelectedAction!.Label);
        vm.RunSelectedAction();
        Assert.Null(vm.Form);
        Assert.Equal(["paste:var x = new { a = 1 };:"], host.Calls);
    });

    [Fact]
    public void CtrlEnter_OnANoFieldPrompt_SendsAtOnce() => Sta.Run(() =>
    {
        var (vm, host) = Make("plain");
        vm.HandleKey(Key.Enter, ModifierKeys.Control);
        Assert.Null(vm.Form);
        Assert.Single(host.Calls);
        Assert.StartsWith("send:plain:", host.Calls[0]);
    });

    [Theory]
    [InlineData("public string Name { get; set; }")]
    [InlineData("{\"a\": 1}")]
    public void CodeLikePrompt_EnterPastes_CtrlEnterSends_NoForm_TextUnchanged(string code) => Sta.Run(() =>
    {
        var (vm, host) = Make(code);
        vm.HandleKey(Key.Enter, ModifierKeys.None);
        Assert.Null(vm.Form);
        vm.HandleKey(Key.Enter, ModifierKeys.Control);
        Assert.Null(vm.Form);
        Assert.Equal(2, host.Calls.Count);
        Assert.Equal($"paste:{code}:", host.Calls[0]);
        Assert.StartsWith($"send:{code}:", host.Calls[1]);
    });

    [Fact]
    public void RowAutomationName_HasTitleCategoryAndState() => Sta.Run(() =>
    {
        var (vm, _) = Make("x", pinned: true, title: "Plan a trip itinerary");
        var row = vm.Items.Single(i => i.IsRow);
        Assert.Equal("Plan a trip itinerary, Travel, pinned", row.AutomationName);
        Assert.Equal(row.AutomationName, row.ToString());
        Assert.Equal("✈️", row.Emoji);
        Assert.Equal("Travel", row.CategoryName);
    });

    [Fact]
    public void ActionAndFooterNames_ReadAsWords() => Sta.Run(() =>
    {
        var (vm, _) = Make("x");
        vm.OpenActions();
        Assert.Equal(["Paste", "Copy only", "Send to, opens a list", "Open in main window", "Pin"],
            vm.Actions.Select(a => a.ToString()));
        Assert.Equal("Enter: choose", vm.Footer[0].ToString());
        Assert.Equal("Esc: back", vm.Footer[1].AutomationName);
    });

    [Fact]
    public void EmptyResults_SayWhatToTry() => Sta.Run(() =>
    {
        var (vm, _) = Make("x");
        vm.Query = "zzzz";
        Assert.False(vm.HasResults);
        Assert.Equal("No prompts match \"zzzz\".", vm.EmptyText);
        Assert.Equal("Try fewer words, or a tag like #email", vm.EmptyHint);
        Assert.Empty(vm.Preview); // the last prompt's text must not linger beside "No prompts match"
    });
}
