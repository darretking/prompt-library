using System.ComponentModel;
using System.Windows;

namespace PromptLibrary.App.Views;

/// <summary>
/// Asks a window's close question ("Save changes?", "Leave setup?") after its Closing event instead of inside it.
/// While Closing runs, WPF marks the window as closing and Show() or Close() on it throws; a question asked there
/// keeps that state for as long as it is open, while its message loop still runs the tray, the hotkey and a second
/// launch, which may show or close the window. So the close is cancelled at once and the question runs next.
/// </summary>
public sealed class CloseQuestion
{
    private readonly Window _window;

    public CloseQuestion(Window window) => _window = window;

    /// <summary>True from the close until the question has been answered.</summary>
    public bool IsAsking { get; private set; }

    /// <summary>
    /// Call from OnClosing: cancels the close and, once the Closing event has finished, runs <paramref name="ask"/>.
    /// When it returns true, <paramref name="onYes"/> runs (hide or really close the window). A close while the
    /// question is already pending or open only cancels.
    /// </summary>
    public void CancelAndAsk(CancelEventArgs e, Func<bool> ask, Action onYes)
    {
        e.Cancel = true;
        if (IsAsking) return;
        IsAsking = true;
        _window.Dispatcher.BeginInvoke(() =>
        {
            bool yes;
            try
            {
                yes = ask();
            }
            finally
            {
                IsAsking = false;
            }
            if (yes) onYes();
        });
    }

    /// <summary>Brings the open question (or, before it is shown, the window) to the front.</summary>
    public void Activate()
    {
        var question = _window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);
        (question ?? _window).Activate();
    }
}
