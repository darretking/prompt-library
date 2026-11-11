using System.Globalization;
using System.Windows.Input;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

public class SearchViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => "CLIP";
        public DateTime Now => new(2026, 11, 3, 9, 7, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-GB");
    }

    private sealed class FakeActions : ISearchActions
    {
        public Library Library { get; set; } = new();
        public Library? TeamLibrary { get; set; }
        public AppSettings Settings { get; } = new();
        public ITokenValues Tokens { get; } = new Tokens();
        public List<string> Calls { get; } = [];

        public void Paste(Prompt prompt, string text, int? caretBackSteps) => Calls.Add($"paste:{prompt.Title}:{text}:{caretBackSteps}");
        public void CopyOnly(Prompt prompt, string text) => Calls.Add($"copy:{prompt.Title}:{text}");
        public void Send(Prompt prompt, string text, string targetId) => Calls.Add($"send:{prompt.Title}:{text}:{targetId}");
        public void OpenInMainWindow(Prompt prompt, bool isTeam) => Calls.Add($"open:{prompt.Title}:{isTeam}");
        public void PinsChanged() => Calls.Add("pins");
        public void Close() => Calls.Add("close");
    }

    private static (Library lib, Category cat) NewLibrary()
    {
        var lib = new Library();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Coding", Emoji = "💻" };
        lib.Categories.Add(cat);
        return (lib, cat);
    }

    private static Prompt Add(Library lib, Category cat, string title, string text = "body", string description = "", string? sendTo = null)
    {
        var p = new Prompt { Id = Guid.NewGuid(), Title = title, Text = text, Description = description, CategoryId = cat.Id, SendTo = sendTo };
        lib.Prompts.Add(p);
        return p;
    }

    private static SearchViewModel Make(FakeActions host) => new(host, () => Now);

    private static List<string> Shape(SearchViewModel vm)
        => vm.Items.Select(i => i.IsHeader ? $"[{i.Header}]" : i.Title).ToList();

    [Fact]
    public void EmptyQuery_PinnedThenRecentThenAllAlphabetical_NoDuplicates() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        var zebra = Add(lib, cat, "Zebra");
        var apple = Add(lib, cat, "Apple");
        var mango = Add(lib, cat, "Mango");
        var kiwi = Add(lib, cat, "Kiwi");
        Add(lib, cat, "Banana");
        host.Settings.PinnedPromptIds.Add(zebra.Id);
        host.Settings.Usage[mango.Id] = new PromptUsage { Count = 1, LastUsedAt = Now.AddDays(-2) };
        host.Settings.Usage[kiwi.Id] = new PromptUsage { Count = 5, LastUsedAt = Now.AddDays(-1) };
        host.Settings.Usage[zebra.Id] = new PromptUsage { Count = 9, LastUsedAt = Now }; // pinned: not repeated in Recent
        host.Settings.Usage[Guid.NewGuid()] = new PromptUsage { Count = 3, LastUsedAt = Now }; // deleted prompt: ignored

        var vm = Make(host);
        vm.Reset();

        Assert.Equal(["[Pinned]", "Zebra", "[Recent]", "Kiwi", "Mango", "[All prompts]", "Apple", "Banana"], Shape(vm));
        Assert.Equal("Zebra", vm.Selected!.Title);
        Assert.Equal(["Alt+1", "Alt+2", "Alt+3", "Alt+4", "Alt+5"], vm.Items.Where(i => i.IsRow).Select(i => i.NumberHint));
        _ = apple;
    });

    [Fact]
    public void EmptyQuery_OnlyAllPrompts_HasNoHeader() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "B");
        Add(lib, cat, "A");
        var vm = Make(host);
        vm.Reset();
        Assert.Equal(["A", "B"], Shape(vm));
    });

    [Fact]
    public void RecentIsCappedAtEight() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        for (var i = 0; i < 12; i++)
        {
            var p = Add(lib, cat, $"P{i:00}");
            host.Settings.Usage[p.Id] = new PromptUsage { Count = 1, LastUsedAt = Now.AddHours(-i) };
        }
        var vm = Make(host);
        vm.Reset();
        var recent = vm.Items.TakeWhile(i => i.Header != "All prompts").Skip(1).ToList();
        Assert.Equal(8, recent.Count);
        Assert.Equal("P00", recent[0].Title);
    });

    [Fact]
    public void HiddenTeamPrompts_AreExcluded_TeamPromptsAreLabelled() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "Mine");
        var team = new Library();
        var tcat = new Category { Id = Guid.NewGuid(), Name = "Sales" };
        team.Categories.Add(tcat);
        Add(team, tcat, "Shown");
        var hidden = Add(team, tcat, "Hidden");
        host.TeamLibrary = team;
        host.Settings.HiddenTeamPromptIds.Add(hidden.Id);

        var vm = Make(host);
        vm.Reset();
        Assert.Equal(["Mine", "Shown"], Shape(vm));
        Assert.True(vm.Items.Single(i => i.Title == "Shown").IsTeam);
        Assert.False(vm.Items.Single(i => i.Title == "Mine").IsTeam);

        vm.Query = "hidden";
        Assert.Empty(vm.Items);
        Assert.Equal("No prompts match \"hidden\".", vm.EmptyText);
    });

    [Fact]
    public void Typing_RanksAndHighlightsTitleMatches() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "Explain this code", description: "Plain-English walkthrough");
        Add(lib, cat, "Write a test");
        var vm = Make(host);
        vm.Reset();

        vm.Query = "expl";

        var row = Assert.Single(vm.Items);
        Assert.Equal("Explain this code", vm.Selected!.Title);
        Assert.Contains(row.TitleSegments, s => s.Highlight && s.Text.StartsWith("Expl", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Coding · Plain-English walkthrough", row.Subtitle);
    });

    [Fact]
    public void Keys_ArrowsSkipHeaders_PageKeysJump_EndsStop() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        for (var i = 0; i < 10; i++) Add(lib, cat, $"P{i}");
        var pinned = lib.Prompts[9];
        host.Settings.PinnedPromptIds.Add(pinned.Id);
        var vm = Make(host);
        vm.Reset();

        Assert.Equal("P9", vm.Selected!.Title); // first row, under the Pinned header
        Assert.True(vm.HandleKey(Key.Down, ModifierKeys.None));
        Assert.Equal("P0", vm.Selected!.Title); // skipped the "All prompts" header
        Assert.True(vm.HandleKey(Key.Up, ModifierKeys.None));
        Assert.Equal("P9", vm.Selected!.Title);
        vm.HandleKey(Key.Up, ModifierKeys.None);
        Assert.Equal("P9", vm.Selected!.Title); // stops at the top
        vm.HandleKey(Key.PageDown, ModifierKeys.None);
        Assert.Equal("P5", vm.Selected!.Title);
        vm.HandleKey(Key.PageDown, ModifierKeys.None);
        vm.HandleKey(Key.PageDown, ModifierKeys.None);
        Assert.Equal("P8", vm.Selected!.Title); // last row
        Assert.False(vm.HandleKey(Key.A, ModifierKeys.None)); // typing is not ours
    });

    [Fact]
    public void Escape_ClearsQueryFirstThenCloses() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "A");
        var vm = Make(host);
        vm.Reset();
        vm.Query = "a";

        vm.HandleKey(Key.Escape, ModifierKeys.None);
        Assert.Equal("", vm.Query);
        Assert.DoesNotContain("close", host.Calls);

        vm.HandleKey(Key.Escape, ModifierKeys.None);
        Assert.Contains("close", host.Calls);
    });

    [Fact]
    public void Enter_PastesPlainPromptAtOnce_CtrlEnterSendsToDefaultOrChatGpt() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "Plain", text: "Hello {date}{cursor}!");
        Add(lib, cat, "ToClaude", text: "x", sendTo: "claude");
        var vm = Make(host);
        vm.Reset();

        vm.HandleKey(Key.Enter, ModifierKeys.None);
        Assert.Equal(["paste:Plain:Hello 03/11/2026!:1"], host.Calls);

        host.Calls.Clear();
        vm.HandleKey(Key.Enter, ModifierKeys.Control);
        Assert.Equal(["send:Plain:Hello 03/11/2026!:chatgpt"], host.Calls);

        host.Calls.Clear();
        vm.HandleKey(Key.Down, ModifierKeys.None);
        vm.HandleKey(Key.Enter, ModifierKeys.Control);
        Assert.Equal(["send:ToClaude:x:claude"], host.Calls);
    });

    [Fact]
    public void AltDigit_UsesTheNthResult_CtrlO_OpensMainWindow() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "A");
        Add(lib, cat, "B");
        var vm = Make(host);
        vm.Reset();

        Assert.True(vm.HandleKey(Key.D2, ModifierKeys.Alt));
        Assert.Equal(["paste:B:body:"], host.Calls);
        Assert.True(vm.HandleKey(Key.D9, ModifierKeys.Alt)); // there is no 9th: nothing happens
        Assert.Single(host.Calls);

        vm.HandleKey(Key.O, ModifierKeys.Control);
        Assert.Equal("open:B:False", host.Calls[^1]);
    });

    [Fact]
    public void PromptWithBlanks_OpensTheForm_EnterOnLastFieldPastes_ValuesAreRemembered() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "Letter", text: "Dear {name}, on {date}.");
        var vm = Make(host);
        vm.Reset();

        vm.HandleKey(Key.Enter, ModifierKeys.None);

        Assert.True(vm.IsFormOpen);
        Assert.False(vm.IsListOpen);
        Assert.Empty(host.Calls);
        Assert.False(vm.HandleKey(Key.Down, ModifierKeys.None)); // the form handles its own keys
        vm.Form!.Fields[0].Value = "Ann";
        Assert.Equal("Dear Ann, on 03/11/2026.", vm.Form.PreviewText);
        vm.Form.Complete();

        Assert.False(vm.IsFormOpen);
        Assert.Equal(["paste:Letter:Dear Ann, on 03/11/2026.:"], host.Calls);
        Assert.Equal("Ann", host.Settings.PlaceholderValues["name"]);
    });

    [Fact]
    public void FormCancel_ReturnsToTheList() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "Letter", text: "Dear {name}");
        var vm = Make(host);
        vm.Reset();
        var focusRequests = 0;
        vm.FocusSearchRequested += () => focusRequests++;

        vm.UseSelected();
        vm.Form!.Cancel();

        Assert.True(vm.IsListOpen);
        Assert.Null(vm.Form);
        Assert.Equal(1, focusRequests);
        Assert.Empty(host.Calls);
    });

    [Fact]
    public void ActionsMenu_PinTogglesAndSendToHasTargets() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        var p = Add(lib, cat, "A");
        var vm = Make(host);
        vm.Reset();

        vm.HandleKey(Key.K, ModifierKeys.Control);
        Assert.True(vm.IsActionsOpen);
        Assert.Equal(["Paste", "Copy only", "Send to", "Open in main window", "Pin"], vm.Actions.Select(a => a.Label));

        vm.HandleKey(Key.Down, ModifierKeys.None); vm.HandleKey(Key.Down, ModifierKeys.None); vm.HandleKey(Key.Down, ModifierKeys.None);
        vm.HandleKey(Key.Down, ModifierKeys.None); // Pin
        vm.HandleKey(Key.Enter, ModifierKeys.None);
        Assert.Contains(p.Id, host.Settings.PinnedPromptIds);
        Assert.Contains("pins", host.Calls);
        Assert.False(vm.IsActionsOpen);

        vm.HandleKey(Key.K, ModifierKeys.Control);
        Assert.Equal("Unpin", vm.Actions[^1].Label);

        vm.HandleKey(Key.Down, ModifierKeys.None); vm.HandleKey(Key.Down, ModifierKeys.None); // Send to
        vm.HandleKey(Key.Enter, ModifierKeys.None);
        Assert.True(vm.IsActionsOpen);
        Assert.Equal(["ChatGPT", "Claude", "Copilot", "Perplexity"], vm.Actions.Select(a => a.Label));
        vm.HandleKey(Key.Down, ModifierKeys.None);
        vm.HandleKey(Key.Enter, ModifierKeys.None);
        Assert.Equal("send:A:body:claude", host.Calls[^1]);

        vm.HandleKey(Key.K, ModifierKeys.Control);
        vm.HandleKey(Key.Escape, ModifierKeys.None);
        Assert.False(vm.IsActionsOpen);
    });

    [Fact]
    public void Preview_MarksPlaceholdersAsChips_AndCutsAtTwentyLines() => Sta.Run(() =>
    {
        var host = new FakeActions();
        var (lib, cat) = NewLibrary();
        host.Library = lib;
        Add(lib, cat, "A", text: "Hi {name}!" + string.Concat(Enumerable.Repeat("\nline", 30)));
        var vm = Make(host);
        vm.Reset();

        Assert.Equal(new TextSegment("{name}", true), vm.Preview[1]);
        var text = string.Concat(vm.Preview.Select(s => s.Text));
        Assert.Equal(20, text.Split('\n').Length - 1); // 20 lines plus the "…" line
        Assert.EndsWith("…", text);
    });
}
