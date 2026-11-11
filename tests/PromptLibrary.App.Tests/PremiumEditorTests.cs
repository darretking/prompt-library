using System.IO;
using PromptLibrary.App.Team;
using PromptLibrary.App.Updates;
using PromptLibrary.App.ViewModels;
using PromptLibrary.App.Views.Controls;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Tests;

/// <summary>The premium editor: text round trip, highlighting, tags and send target in the editor, history, send.</summary>
public sealed class PremiumEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pl-premium-" + Guid.NewGuid().ToString("N"));

    public PremiumEditorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ---- The prompt text control ---------------------------------------------------------------

    [Theory]
    [InlineData("plain")]
    [InlineData("tab\tseparated\tvalues")]
    [InlineData("crlf\r\nlines\r\nhere\r\n")]
    [InlineData("lf only\nlines\n")]
    [InlineData("mixed\r\nand\nold\rmac")]
    [InlineData("emoji 😀 and 👩‍💻 and é ñ 日本語")]
    [InlineData("  leading and trailing spaces  \t\r\n")]
    [InlineData("")]
    public void PromptTextEditor_TextRoundTripsByteExact(string text) => Sta.Run(() =>
    {
        var control = new PromptTextEditor { Text = text };

        Assert.Equal(text, control.Editor.Text);
        Assert.Equal(text, control.Text);

        // Typing in the editor flows back to Text, again exactly.
        control.Editor.Document.Insert(0, "\t😀\r\n");
        Assert.Equal("\t😀\r\n" + text, control.Text);
    });

    [Fact]
    public void PromptTextEditor_ReadOnlyFollowsTheProperty() => Sta.Run(() =>
    {
        var control = new PromptTextEditor { IsReadOnly = true };
        Assert.True(control.Editor.IsReadOnly);
        control.IsReadOnly = false;
        Assert.False(control.Editor.IsReadOnly);
    });

    [Fact]
    public void PromptTextEditor_SettingTextFromTheModelDoesNotKeepUndoHistory() => Sta.Run(() =>
    {
        var control = new PromptTextEditor { Text = "first" };
        control.Text = "second";

        Assert.False(control.Editor.CanUndo);
    });

    // ---- Highlighting --------------------------------------------------------------------------

    [Fact]
    public void Highlighting_FindsPlaceholdersTokensHeadingsBoldAndFences()
    {
        const string text = "# Title\r\nHello {name}, **bold** {date}\r\n```\r\n# not a heading {x}\r\n```\r\nend";
        var spans = PromptHighlighting.Compute(text);

        string Of(HighlightSpan s) => text.Substring(s.Start, s.Length);
        Assert.Contains(spans, s => s.Kind == HighlightKind.Heading && Of(s) == "# Title");
        Assert.Contains(spans, s => s.Kind == HighlightKind.Placeholder && Of(s) == "{name}");
        Assert.Contains(spans, s => s.Kind == HighlightKind.Placeholder && Of(s) == "{date}");
        Assert.Contains(spans, s => s.Kind == HighlightKind.Bold && Of(s) == "**bold**");
        Assert.Contains(spans, s => s.Kind == HighlightKind.CodeBlock && Of(s).StartsWith("```") && Of(s).EndsWith("```"));
        // Inside the fence a "#" line is not a heading and bold isn't applied, but placeholders still are.
        Assert.Single(spans, s => s.Kind == HighlightKind.Heading);
        Assert.Contains(spans, s => s.Kind == HighlightKind.Placeholder && Of(s) == "{x}");
    }

    [Fact]
    public void Highlighting_UnclosedFenceRunsToTheEnd()
    {
        const string text = "a\n```\ncode\nmore";
        var fence = Assert.Single(PromptHighlighting.Compute(text), s => s.Kind == HighlightKind.CodeBlock);
        Assert.Equal(text.Length, fence.End);
    }

    // ---- Editor view model ---------------------------------------------------------------------

    [Fact]
    public void Editor_TagsAndSendToCountAsChanges() => Sta.Run(() =>
    {
        var editor = PromptEditorViewModel.ForPrompt(NewPrompt(tags: ["a"], sendTo: "claude"));
        Assert.False(editor.IsDirty);

        editor.Tags.Add("b");
        Assert.True(editor.IsDirty);
        editor.Tags.RemoveAt(1);
        Assert.False(editor.IsDirty);

        editor.SendTo = "chatgpt";
        Assert.True(editor.IsDirty);
        editor.Revert();
        Assert.False(editor.IsDirty);
        Assert.Equal("claude", editor.SendTo);
        Assert.Equal(["a"], editor.Tags);
    });

    [Fact]
    public void Editor_ApplyVersionLoadsAllFieldsAsUnsavedChanges() => Sta.Run(() =>
    {
        var editor = PromptEditorViewModel.ForPrompt(NewPrompt(tags: ["a"]));
        editor.ApplyVersion(new PromptVersion { Title = "Old", Description = "d", Text = "old text", Tags = ["X ", "y"] });

        Assert.True(editor.IsDirty);
        Assert.Equal("Old", editor.Title);
        Assert.Equal("old text", editor.Text);
        Assert.Equal(["x", "y"], editor.Tags);
    });

    // ---- Main view model -----------------------------------------------------------------------

    [Fact]
    public void Save_WritesTagsAndSendTo_AndKeepsThePreviousVersion() => Sta.Run(() =>
    {
        var (vm, host, history, prompt) = Open();
        vm.Editor!.Tags.Add("review");
        vm.Editor.SendTo = "claude";
        vm.Editor.Text = "New text";

        Assert.True(vm.Save());

        Assert.Equal(["review"], prompt.Tags);
        Assert.Equal("claude", prompt.SendTo);
        var versions = history.List(prompt.Id);
        var old = Assert.Single(versions);
        Assert.Equal("Original text", old.Text);
        Assert.Equal([], old.Tags);
        Assert.False(vm.Editor.IsDirty);
        Assert.Equal(1, host.LibrarySaves);
    });

    [Fact]
    public void Save_ASendToOnlyChangeKeepsNoHistory() => Sta.Run(() =>
    {
        var (vm, _, history, prompt) = Open();
        vm.Editor!.SendTo = "claude";

        Assert.True(vm.Save());

        Assert.Equal("claude", prompt.SendTo);
        Assert.Empty(history.List(prompt.Id));
    });

    [Fact]
    public void Delete_RemovesHistoryPinAndUsage() => Sta.Run(() =>
    {
        var (vm, host, history, prompt) = Open();
        vm.Editor!.Text = "Changed";
        vm.Save();
        host.Settings.PinnedPromptIds.Add(prompt.Id);
        host.Settings.Usage[prompt.Id] = new PromptUsage { Count = 3 };
        Assert.NotEmpty(history.List(prompt.Id));

        vm.DeleteCommand.Execute(null);

        Assert.Empty(history.List(prompt.Id));
        Assert.DoesNotContain(prompt.Id, host.Settings.PinnedPromptIds);
        Assert.False(host.Settings.Usage.ContainsKey(prompt.Id));
    });

    [Fact]
    public void Copy_RecordsUsage_AndSendsTheRawTextThroughTheFillInHook() => Sta.Run(() =>
    {
        var (vm, host, _, prompt) = Open();
        vm.Editor!.Text = "Hello {name}";
        var resolved = new List<string>();
        vm.FillInResolver = t => { resolved.Add(t); return t.Replace("{name}", "Ann"); };

        vm.CopyCommand.Execute(null);

        Assert.Equal(["Hello {name}"], resolved);
        Assert.Equal("Hello Ann", host.Copied.Single());
        Assert.Equal(1, host.Settings.Usage[prompt.Id].Count);
    });

    [Fact]
    public void Copy_CancelledFillIn_CopiesNothing() => Sta.Run(() =>
    {
        var (vm, host, _, _) = Open();
        vm.Editor!.Text = "Hello {name}";
        vm.FillInResolver = _ => null;

        vm.CopyCommand.Execute(null);

        Assert.Empty(host.Copied);
    });

    [Fact]
    public void Send_CopiesOpensTheDefaultAppAndSaysSo() => Sta.Run(() =>
    {
        var (vm, host, _, prompt) = Open(sendTo: "claude");
        var toasts = new List<string>();
        vm.ToastRequested += (_, t) => toasts.Add(t);

        vm.SendCommand.Execute(null);

        Assert.Equal("Original text", host.Copied.Single());
        Assert.Equal("https://claude.ai/new?q=Original%20text", host.Opened.Single());
        Assert.Equal("Opened Claude. The prompt is also on your clipboard.", vm.StatusText);
        Assert.Equal(["Sent to Claude"], toasts);
        Assert.Equal(1, host.Settings.Usage[prompt.Id].Count);
    });

    [Fact]
    public void Send_ToAnotherAppAndALongPrompt_OpensTheHomePage() => Sta.Run(() =>
    {
        var (vm, host, _, _) = Open();
        vm.Editor!.Text = new string('x', 7000);

        vm.SendCommand.Execute("perplexity");

        Assert.Equal("https://www.perplexity.ai/", host.Opened.Single());
        Assert.StartsWith("Opened Perplexity. The prompt is also on your clipboard", vm.StatusText);
    });

    [Fact]
    public void SendChoices_CheckTheDefault_AndSetDefaultMakesTheEditorDirty() => Sta.Run(() =>
    {
        var (vm, _, _, _) = Open();
        Assert.Equal("ChatGPT", vm.SendChoices.Single(c => c.IsDefault).Name);

        vm.SetDefaultSendCommand.Execute("copilot");

        Assert.Equal("Copilot", vm.SendChoices.Single(c => c.IsDefault).Name);
        Assert.True(vm.Editor!.IsDirty);
    });

    [Fact]
    public void UnsavedEdit_CarriesTheTags() => Sta.Run(() =>
    {
        var (vm, _, _, _) = Open();
        vm.Editor!.Tags.Add("keep");

        var edit = vm.CurrentUnsavedEdit();

        Assert.Equal(["keep"], edit!.Tags);
    });

    [Fact]
    public void SaveForExit_SavesTheTagsToo() => Sta.Run(() =>
    {
        var (vm, _, _, prompt) = Open();
        vm.Editor!.Tags.Add("exit");

        Assert.Null(vm.SaveForExit());

        Assert.Equal(["exit"], prompt.Tags);
    });

    [Fact]
    public void ShowHistory_OpensThePromptsVersionsNewestFirst() => Sta.Run(() =>
    {
        var (vm, _, _, _) = Open();
        vm.Editor!.Text = "Second";
        vm.Save();
        vm.Editor.Text = "Third";
        vm.Save();
        HistoryViewModel? shown = null;
        vm.HistoryDialog = m => { shown = m; return false; };

        Assert.True(vm.CanShowHistory);
        vm.ShowHistoryCommand.Execute(null);

        Assert.Equal(["Second", "Original text"], shown!.Items.Select(i => i.Version.Text));
    });

    // ---- History view model --------------------------------------------------------------------

    [Fact]
    public void HistoryViewModel_DiffsTheChosenVersionAgainstTheEditorText()
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var applied = new List<PromptVersion>();
        var versions = new[]
        {
            new PromptVersion { SavedAt = now.AddHours(-2), Text = "one\ntwo", Title = "T" },
        };
        var model = new HistoryViewModel(versions, "one\nthree", now, applied.Add);

        Assert.Equal("2 hours ago", model.Items[0].When);
        Assert.Equal("one", model.Items[0].FirstLine);
        Assert.Equal(
            [(DiffKind.Same, "one"), (DiffKind.Removed, "two"), (DiffKind.Added, "three")],
            model.Diff.Select(d => (d.Kind, d.Text)));

        Assert.True(model.Restore());
        Assert.Same(versions[0], applied.Single());
    }

    [Fact]
    public void HistoryViewModel_NoVersions_IsEmptyAndCannotRestore()
    {
        var model = new HistoryViewModel([], "text", DateTimeOffset.Now, _ => { });

        Assert.True(model.IsEmpty);
        Assert.False(model.CanRestore);
        Assert.False(model.Restore());
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(300, "5 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(86400 + 3600, "yesterday")]
    [InlineData(86400 * 3 + 3600, "3 days ago")]
    public void HistoryViewModel_RelativeAge(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, HistoryViewModel.RelativeAge(now.AddSeconds(-secondsAgo), now));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static Prompt NewPrompt(IEnumerable<string>? tags = null, string? sendTo = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Title", Description = "", CategoryId = Guid.NewGuid(), Text = "Original text",
        Tags = [.. tags ?? []], SendTo = sendTo,
    };

    private (MainViewModel Vm, FakeHost Host, PromptHistoryStore History, Prompt Prompt) Open(string? sendTo = null)
    {
        var host = new FakeHost();
        var editor = new LibraryEditor(host.Library, () => DateTimeOffset.Now);
        var category = editor.AddCategory("Coding");
        var prompt = editor.AddPrompt(category.Id, "Review", "desc", "Original text", [], sendTo);
        var history = new PromptHistoryStore(Path.Combine(_root, "History"));
        var vm = new MainViewModel(host, new FakeDialogs(), history, host.Opened.Add);
        vm.SelectedPrompt = vm.Prompts.First(r => r.Id == prompt.Id);
        Assert.NotNull(vm.Editor);
        return (vm, host, history, prompt);
    }

    private sealed class FakeHost : IMainHost
    {
        public UpdateCoordinator? Updates => null;
        public TeamLibraryService? Team => null;
        public Library Library { get; } = new();
        public AppSettings Settings { get; } = new();
        public string? BackupWarning => null;
        public string? BackupWarningDetail => null;
        public bool LibraryUnwritten => false;
        public int LibrarySaves { get; private set; }
        public List<string> Copied { get; } = [];
        public List<string> Opened { get; } = [];

        public event EventHandler? BackupStateChanged { add { } remove { } }

        public bool SaveLibrary() { LibrarySaves++; return true; }
        public bool SaveLibraryChange() { LibrarySaves++; return true; }
        public bool SaveSettings() => true;
        public void RefreshQuickMenu() { }
        public bool CopyText(string text, string title) { Copied.Add(text); return true; }
        public Task<BackupResult?> RunBackupAsync(BackupTrigger trigger, bool showInTray = true) => Task.FromResult<BackupResult?>(null);
        public void OpenRestore() { }
        public void OpenSettings(bool focusBackups = false) { }
        public void OpenOnboarding() { }
        public void ShowAbout() { }
        public void ExitApp(ExitReason reason = ExitReason.User) { }
    }

    private sealed class FakeDialogs : IDialogs
    {
        public bool? AskSaveChanges(string question) => null;
        public bool ConfirmDestructive(string message, string action) => true;
        public void ShowMessage(string message) { }
        public bool ShowTeamHistory(TeamLibraryService service, bool review) => false;
    }
}
