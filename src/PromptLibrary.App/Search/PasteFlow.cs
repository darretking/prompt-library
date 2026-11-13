using PromptLibrary.App.Tray;
using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Search;

/// <summary>The things the paste flow does to the outside world, so the flow itself can be tested with a fake.</summary>
internal interface IPasteSteps
{
    bool TryCopy(string text);

    /// <summary>The clipboard's text if it holds only plain text (what to put back later); otherwise null.</summary>
    string? SnapshotText();

    bool IsOtherAppWindow(IntPtr window, out bool isOurs);
    Task<PasteOutcome> PasteAsync(IntPtr target);
    Task WaitForTargetToReadClipboardAsync(IntPtr target);
    void PressLeft(int count);
    void RestoreIfUnchanged(string previous, string ours);
    bool IsDisposed { get; }
}

/// <summary>The real thing: the clipboard, the paster and the keyboard.</summary>
internal sealed class WindowsPasteSteps(AppHost host, Func<bool> isDisposed) : IPasteSteps
{
    public bool TryCopy(string text) => host.TryCopy(text);
    public string? SnapshotText() => ClipboardSnapshot.TextIfOnlyText();
    public bool IsOtherAppWindow(IntPtr window, out bool isOurs) => AutoPaster.IsOtherAppWindow(window, out isOurs);
    public Task<PasteOutcome> PasteAsync(IntPtr target) => AutoPaster.PasteAsync(target);
    public Task WaitForTargetToReadClipboardAsync(IntPtr target) => AutoPaster.WaitForTargetToReadClipboardAsync(target);
    public void PressLeft(int count) => AutoPaster.PressLeft(count);
    public void RestoreIfUnchanged(string previous, string ours) => ClipboardSnapshot.RestoreIfUnchanged(host, previous, ours);
    public bool IsDisposed => isDisposed();
}

/// <summary>
/// What using a prompt means, shared by the search window and the classic shortcut menu: fill it in (<see cref="Resolve"/>),
/// then copy, paste, move the caret for <c>{cursor}</c> and put the old clipboard back (<see cref="RunAsync"/>).
/// </summary>
internal static class PasteFlow
{
    /// <summary>
    /// The text to use: a prompt with placeholders goes through <paramref name="ask"/> (the form; null = cancelled, so
    /// null is returned); one without never shows a form, only its dynamic tokens are filled.
    /// </summary>
    public static FillResult? Resolve(string promptText, ITokenValues tokens, Func<string, FillResult?> ask)
        => PromptTemplate.HasFields(promptText)
            ? ask(promptText)
            : PromptTemplate.Fill(promptText, new Dictionary<string, string>(), tokens);

    /// <summary>
    /// Copies <paramref name="text"/>, then pastes it into <paramref name="returnTo"/> if the settings and the target allow.
    /// <paramref name="onCopied"/> runs once the copy worked (record the use); <paramref name="onOutcome"/> gets what the
    /// paste did (show the toast), before the caret move and the clipboard restore. False if the clipboard stayed busy.
    /// </summary>
    public static async Task<bool> RunAsync(IPasteSteps steps, AppSettings settings, string text, int? caretBackSteps,
        IntPtr returnTo, Action onCopied, Action<PasteOutcome> onOutcome)
    {
        // Taken now (not when the window opened): it is what the clipboard holds just before we replace it.
        var previous = settings.RestoreClipboard ? steps.SnapshotText() : null;

        if (!steps.TryCopy(text)) return false;
        onCopied();

        var hasTarget = steps.IsOtherAppWindow(returnTo, out var isOurs);
        var outcome = PasteDecision.ShouldPaste(settings.PasteAfterCopy, fromShortcutMenu: returnTo != IntPtr.Zero,
            hasTarget, isOurs)
            ? await steps.PasteAsync(returnTo)
            : PasteOutcome.NotAttempted;

        onOutcome(outcome);

        if (outcome == PasteOutcome.Pasted && (caretBackSteps is not null || previous is not null))
        {
            // Not a fixed delay: until the app has read the clipboard and gone idle (400 ms at least, 1.5 s at most).
            await steps.WaitForTargetToReadClipboardAsync(returnTo);
            if (caretBackSteps is { } n) steps.PressLeft(n);
            if (previous is not null && !steps.IsDisposed) steps.RestoreIfUnchanged(previous, text);
        }
        return true;
    }
}
