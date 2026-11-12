using System.Globalization;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

/// <summary>The third muted line of a result row (tags or a text snippet) and the context-sensitive footer.</summary>
public class SearchRowLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => null;
        public DateTime Now => new(2026, 11, 3, 9, 7, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-GB");
    }

    private sealed class Host : ISearchActions
    {
        public Library Library { get; } = new();
        public Library? TeamLibrary => null;
        public AppSettings Settings { get; } = new();
        public ITokenValues Tokens { get; } = new Tokens();
        public void Paste(Prompt prompt, string text, int? caretBackSteps) { }
        public void CopyOnly(Prompt prompt, string text) { }
        public void Send(Prompt prompt, string text, string targetId) { }
        public void OpenInMainWindow(Prompt prompt, bool isTeam) { }
        public void PinsChanged() { }
        public void Close() { }
    }

    private static (SearchViewModel Vm, Host Host) Make(string title, string text, params string[] tags)
    {
        var host = new Host();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Coding" };
        host.Library.Categories.Add(cat);
        host.Library.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = title, Text = text, Tags = [.. tags], CategoryId = cat.Id });
        var vm = new SearchViewModel(host, () => Now);
        vm.Reset();
        return (vm, host);
    }

    [Fact]
    public void TitleMatch_HasNoThirdLine() => Sta.Run(() =>
    {
        var (vm, _) = Make("Write a budget", "body", "money");
        vm.Query = "budget";
        var row = vm.Items.Single(i => i.IsRow);
        Assert.False(row.HasTagLine);
        Assert.False(row.HasSnippetLine);
    });

    [Fact]
    public void TagMatch_ShowsTagChips_MatchedOneFirstAndMarked() => Sta.Run(() =>
    {
        var (vm, _) = Make("Query helper", "body", "database", "sql", "postgres");
        vm.Query = "sql";
        var row = vm.Items.Single(i => i.IsRow);
        Assert.True(row.HasTagLine);
        Assert.False(row.HasSnippetLine);
        Assert.Equal([new TagChip("#sql", true), new TagChip("#database", false), new TagChip("#postgres", false)], row.TagChips);
    });

    [Fact]
    public void TextMatch_ShowsASnippetWithTheMatchHighlighted() => Sta.Run(() =>
    {
        var (vm, _) = Make("Note writer", "Please write a short note about the quarterly zebra migration plan for everyone.");
        vm.Query = "zebra";
        var row = vm.Items.Single(i => i.IsRow);
        Assert.True(row.HasSnippetLine);
        Assert.False(row.HasTagLine);
        var marked = row.SnippetSegments.Single(s => s.Highlight);
        Assert.Equal("zebra", marked.Text);
        Assert.InRange(string.Concat(row.SnippetSegments.Select(s => s.Text)).Length, 20, 64);
    });

    [Fact]
    public void EmptyQuery_RowsHaveNoThirdLine() => Sta.Run(() =>
    {
        var (vm, _) = Make("Query helper", "body", "sql");
        Assert.All(vm.Items.Where(i => i.IsRow), r => Assert.False(r.HasTagLine || r.HasSnippetLine));
    });

    [Fact]
    public void Footer_FollowsTheState_ListActionsForm() => Sta.Run(() =>
    {
        var (vm, _) = Make("Greeting", "Hello {name}");
        Assert.Equal("Enter paste · Ctrl+Enter send to ChatGPT · Ctrl+K more options · Esc close", vm.FooterText);
        Assert.Equal(["Enter", "Ctrl+Enter", "Ctrl+K", "Esc"], vm.Footer.Select(h => h.Key));

        vm.OpenActions();
        Assert.Equal("Enter choose · Esc back", vm.FooterText);

        vm.CloseActions();
        vm.UseSelected(); // the prompt has a blank: the form opens
        Assert.True(vm.IsFormOpen);
        Assert.Equal("Tab next field · Enter paste · Esc back", vm.FooterText);
    });

    [Fact]
    public void Footer_WithNoResults_OffersOnlyClose() => Sta.Run(() =>
    {
        var (vm, _) = Make("Greeting", "Hello {name}");
        vm.Query = "zzzz-nothing";
        Assert.False(vm.HasResults);
        Assert.Equal("Esc close", vm.FooterText);
        vm.Query = "";
        Assert.Equal("Enter paste · Ctrl+Enter send to ChatGPT · Ctrl+K more options · Esc close", vm.FooterText);
    });
}
