using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using PromptLibrary.App.Tray;
using PromptLibrary.App.ViewModels;
using PromptLibrary.App.Views;
using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Send;
using PromptLibrary.Core.Templates;
using PromptLibrary.Core.Text;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.App.Search;

/// <summary>Token values captured when the search window opened (the clipboard before our window took the focus).</summary>
internal sealed class TokenValues(string? clipboard) : ITokenValues
{
    public string? Clipboard { get; } = clipboard;
    public DateTime Now => DateTime.Now;
    public CultureInfo Culture => CultureInfo.CurrentCulture;
}

/// <summary>
/// Owns the one search window (created hidden at startup, reused), remembers which window the user was in, and does
/// what a chosen prompt asks for: copy, paste into that window, put the clipboard back, send to an AI site, open in
/// the main window. Used on the UI thread. The library data is read from <see cref="AppHost"/>'s memory, never from disk.
/// </summary>
internal sealed class SearchController : ISearchActions, IDisposable
{
    private static readonly TimeSpan CaretDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppHost _host;
    private SearchViewModel? _viewModel;
    private SearchWindow? _window;
    private IntPtr _returnTo;
    private ITokenValues _tokens = new TokenValues(null);
    private bool _disposed;

    public SearchController(AppHost host) => _host = host;

    public bool IsOpen => _window is { IsVisible: true };

    // ---- ISearchActions: data ------------------------------------------------------------------

    public Library Library => _host.Library;
    public Library? TeamLibrary => _host.Team?.Team?.Content;
    public AppSettings Settings => _host.Settings;
    public ITokenValues Tokens => _tokens;

    /// <summary>Creates the window and shows/hides it once off-screen, so the first real opening is fast.</summary>
    public void Warm()
    {
        if (_disposed) return;
        Ensure();
        _viewModel!.Reset();
        _window!.Warm();
    }

    /// <summary>
    /// Opens the window (or closes it if it is open). <paramref name="fromTray"/>: a click on the tray icon, where the
    /// window to paste into is not known, so a pick is only copied.
    /// </summary>
    public void Toggle(bool fromTray)
    {
        if (_disposed) return;
        var timer = Stopwatch.StartNew();
        if (IsOpen)
        {
            Hide();
            return;
        }

        // Both are taken before our window takes the focus.
        _returnTo = fromTray ? IntPtr.Zero : AutoPaster.ForegroundWindow();
        _tokens = new TokenValues(NeedsClipboard() ? ClipboardSnapshot.ReadText() : null);

        Ensure();
        _viewModel!.Reset();
        _window!.ShowNearMouse();
        LogOpened(timer);
    }

    public void Hide() => _window?.Hide();

    public void Dispose()
    {
        _disposed = true;
        if (_window is { } window)
        {
            _window = null;
            window.Close();
        }
    }

    private void Ensure()
    {
        if (_window is not null) return;
        _viewModel = new SearchViewModel(this);
        _window = new SearchWindow(_viewModel);
    }

    // The clipboard is only read when some prompt uses {clipboard}: faster, and no clipboard access nobody needs.
    private bool NeedsClipboard()
        => Library.Prompts.Concat(TeamLibrary?.Prompts ?? []).Any(p => p.Text.Contains("{clipboard", StringComparison.OrdinalIgnoreCase));

    [Conditional("DEBUG")]
    private void LogOpened(Stopwatch timer)
    {
        var shown = timer.ElapsedMilliseconds;
        _window!.Dispatcher.BeginInvoke(DispatcherPriority.Render,
            () => Debug.WriteLine($"[search] hotkey to shown {shown} ms, first frame {timer.ElapsedMilliseconds} ms"));
    }

    // ---- ISearchActions: what a pick does ------------------------------------------------------

    public void Close() => Hide();

    public void Paste(Prompt prompt, string text, int? caretBackSteps)
    {
        var returnTo = _returnTo;
        Hide();
        _ = PasteAsync(prompt, text, caretBackSteps, returnTo);
    }

    private async Task PasteAsync(Prompt prompt, string text, int? caretBackSteps, IntPtr returnTo)
    {
        try
        {
            // Taken now (not when the window opened): it is what the clipboard holds just before we replace it.
            var previous = _host.Settings.RestoreClipboard ? ClipboardSnapshot.TextIfOnlyText() : null;

            if (!_host.TryCopy(text))
            {
                _host.Tray.ShowMessage("Prompt Library", "Couldn't copy — clipboard busy. Try again.", isError: true);
                return;
            }
            RecordUse(prompt);

            var hasTarget = AutoPaster.IsOtherAppWindow(returnTo, out var isOurs);
            var outcome = PasteDecision.ShouldPaste(_host.Settings.PasteAfterCopy, fromShortcutMenu: returnTo != IntPtr.Zero,
                hasTarget, isOurs)
                ? await AutoPaster.PasteAsync(returnTo)
                : PasteOutcome.NotAttempted;

            if (outcome == PasteOutcome.Pasted)
            {
                if (caretBackSteps is { } steps)
                {
                    await Task.Delay(CaretDelay); // let the app take the pasted text first
                    AutoPaster.PressLeft(steps);
                }
                if (previous is not null)
                {
                    await Task.Delay(RestoreDelay);
                    if (!_disposed) ClipboardSnapshot.RestoreIfUnchanged(_host, previous, text);
                }
            }

            if (!_disposed && PasteDecision.CopiedMessage(prompt.Title, outcome) is { } message)
                _host.Tray.ShowMessage("Prompt Library", message);
        }
        catch (Exception ex)
        {
            Core.Storage.CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now);
        }
    }

    public void CopyOnly(Prompt prompt, string text)
    {
        Hide();
        if (_host.CopyText(text, prompt.Title))
            RecordUse(prompt);
    }

    public void Send(Prompt prompt, string text, string targetId)
    {
        Hide();
        var target = SendTargets.Resolve(targetId);
        var copied = _host.TryCopy(text);
        try
        {
            Process.Start(new ProcessStartInfo(target.BuildUrl(text)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _host.Tray.ShowMessage("Prompt Library",
                copied ? $"Couldn't open {target.Name}. The prompt is on your clipboard." : $"Couldn't open {target.Name}.",
                isError: true);
            return;
        }

        RecordUse(prompt);
        var message = !copied ? $"Opened {target.Name}. Couldn't copy the prompt — clipboard busy."
            : target.IsTooLong(text) ? $"Opened {target.Name}. The prompt is on your clipboard — paste it there."
            : $"Opened {target.Name}. The prompt is also on your clipboard.";
        _host.Tray.ShowMessage("Prompt Library", message, isError: !copied);
    }

    public void OpenInMainWindow(Prompt prompt, bool isTeam)
    {
        Hide();
        _host.OpenPromptInMainWindow(prompt, isTeam);
    }

    public void PinsChanged()
    {
        _host.SaveSettings();
        _host.RefreshQuickMenu();
    }

    private void RecordUse(Prompt prompt)
    {
        UsageTracker.Record(_host.Settings, prompt.Id, DateTimeOffset.Now);
        _host.SaveSettings();
    }
}

/// <summary>Reads and restores clipboard text around a paste.</summary>
internal static class ClipboardSnapshot
{
    /// <summary>The clipboard's text, or null when it has none or is busy.</summary>
    public static string? ReadText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The clipboard's text if it holds nothing but plain-text formats; otherwise null (nothing to put back).</summary>
    public static string? TextIfOnlyText()
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data is null || !ClipboardFormats.IsTextOnly(data.GetFormats(false))) return null;
            return data.GetData(DataFormats.UnicodeText, false) as string is { Length: > 0 } text ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Puts <paramref name="previous"/> back, but only if the clipboard still holds what we put there (a newer copy by the
    /// user is never overwritten).
    /// </summary>
    public static void RestoreIfUnchanged(AppHost host, string previous, string ours)
    {
        var current = ReadText();
        if (current is null || LineEndings.ToCrlf(current) != LineEndings.ToCrlf(ours)) return;
        host.TryCopy(previous);
    }
}
