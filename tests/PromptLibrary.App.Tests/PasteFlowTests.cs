using System.Globalization;
using PromptLibrary.App.Search;
using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Tests;

/// <summary>The fill, copy, paste, caret and clipboard-restore flow the search window and the shortcut menu share.</summary>
public class PasteFlowTests
{
    private static readonly IntPtr Target = new(1234);

    private sealed class Tokens : ITokenValues
    {
        public string? Clipboard => "CLIP";
        public DateTime Now => new(2026, 11, 3, 9, 7, 0);
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-GB");
    }

    private sealed class Steps : IPasteSteps
    {
        public List<string> Calls { get; } = [];
        public bool CopyWorks { get; set; } = true;
        public string? Snapshot { get; set; } = "OLD";
        public PasteOutcome PasteResult { get; set; } = PasteOutcome.Pasted;
        public bool HasTarget { get; set; } = true;
        public bool IsDisposed { get; set; }

        public bool TryCopy(string text) { Calls.Add($"copy:{text}"); return CopyWorks; }
        public string? SnapshotText() => Snapshot;
        public bool IsOtherAppWindow(IntPtr window, out bool isOurs) { isOurs = false; return HasTarget; }
        public Task<PasteOutcome> PasteAsync(IntPtr target) { Calls.Add("paste"); return Task.FromResult(PasteResult); }
        public Task WaitForTargetToReadClipboardAsync(IntPtr target) => Task.CompletedTask;
        public void PressLeft(int count) => Calls.Add($"left:{count}");
        public void RestoreIfUnchanged(string previous, string ours) => Calls.Add($"restore:{previous}");
    }

    private static Task<bool> Run(Steps steps, AppSettings settings, string text = "hi", int? caret = null,
        IntPtr? returnTo = null, List<string>? log = null)
        => PasteFlow.RunAsync(steps, settings, text, caret, returnTo ?? Target,
            onCopied: () => log?.Add("used"), onOutcome: o => log?.Add($"outcome:{o}"));

    // ---- Resolve ----

    [Fact]
    public void Resolve_TemplatePrompt_AsksTheForm_AndReturnsItsAnswer()
    {
        var asked = new List<string>();
        var result = PasteFlow.Resolve("Hello {name}", new Tokens(), text =>
        {
            asked.Add(text);
            return new FillResult("Hello Ann", 3);
        });

        Assert.Equal(["Hello {name}"], asked);
        Assert.Equal("Hello Ann", result!.Text);
        Assert.Equal(3, result.CaretBackSteps);
    }

    [Fact]
    public void Resolve_CancelledForm_ReturnsNull()
        => Assert.Null(PasteFlow.Resolve("Hello {name}", new Tokens(), _ => null));

    [Fact]
    public void Resolve_NoFields_SkipsTheForm_AndFillsTokensAndCursor()
    {
        var result = PasteFlow.Resolve("On {date}: {clipboard}{cursor}!", new Tokens(), _ => throw new InvalidOperationException("form shown"));

        Assert.StartsWith("On ", result!.Text);
        Assert.Contains("CLIP", result.Text);
        Assert.DoesNotContain("{", result.Text);
        Assert.Equal(1, result.CaretBackSteps);
    }

    // ---- RunAsync ----

    [Fact]
    public async Task Run_Paste_ThenCaret_ThenRestore_InThatOrder()
    {
        var steps = new Steps();
        var log = new List<string>();

        Assert.True(await Run(steps, new AppSettings { PasteAfterCopy = true, RestoreClipboard = true }, "hi", caret: 4, log: log));

        Assert.Equal(["copy:hi", "paste", "left:4", "restore:OLD"], steps.Calls);
        Assert.Equal(["used", "outcome:Pasted"], log);
    }

    [Fact]
    public async Task Run_RestoreOff_DoesNotRestore()
    {
        var steps = new Steps();

        await Run(steps, new AppSettings { PasteAfterCopy = true, RestoreClipboard = false });

        Assert.Equal(["copy:hi", "paste"], steps.Calls);
    }

    [Fact]
    public async Task Run_NoPaste_NeverRestoresTheClipboard()
    {
        var steps = new Steps();

        await Run(steps, new AppSettings { PasteAfterCopy = false, RestoreClipboard = true }, caret: 2);

        Assert.Equal(["copy:hi"], steps.Calls); // plain copy: the prompt stays on the clipboard, no caret keys
    }

    [Fact]
    public async Task Run_NoTargetWindow_OnlyCopies()
    {
        var steps = new Steps { HasTarget = false };

        await Run(steps, new AppSettings { PasteAfterCopy = true, RestoreClipboard = true }, returnTo: IntPtr.Zero);

        Assert.Equal(["copy:hi"], steps.Calls);
    }

    [Fact]
    public async Task Run_PasteFailed_DoesNotMoveCaretOrRestore()
    {
        var steps = new Steps { PasteResult = PasteOutcome.Failed };

        await Run(steps, new AppSettings { PasteAfterCopy = true, RestoreClipboard = true }, caret: 2);

        Assert.Equal(["copy:hi", "paste"], steps.Calls);
    }

    [Fact]
    public async Task Run_Disposed_StillMovesCaret_ButSkipsRestore()
    {
        var steps = new Steps { IsDisposed = true };

        await Run(steps, new AppSettings { PasteAfterCopy = true, RestoreClipboard = true }, caret: 2);

        Assert.Equal(["copy:hi", "paste", "left:2"], steps.Calls);
    }

    [Fact]
    public async Task Run_BusyClipboard_ReturnsFalse_RecordsNoUse_AndDoesNotPaste()
    {
        var steps = new Steps { CopyWorks = false };
        var log = new List<string>();

        Assert.False(await Run(steps, new AppSettings { PasteAfterCopy = true }, log: log));

        Assert.Equal(["copy:hi"], steps.Calls);
        Assert.Empty(log);
    }
}
