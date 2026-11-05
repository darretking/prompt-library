using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace PromptLibrary.App.Views;

/// <summary>
/// Tells a close the user asked for (×, Alt+F4, the taskbar's Close, Esc or a Close button) from a WM_CLOSE sent by
/// another program, such as the installer closing the app before an upgrade. Such an external close must never ask
/// anything: the window stays open and the app exits without questions instead (<see cref="ExitReason.External"/>).
/// </summary>
public sealed class UserCloseTracker
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    private readonly Window _window;
    private readonly AppHost _host;
    private bool _userClose;

    public UserCloseTracker(Window window, AppHost host)
    {
        _window = window;
        _host = host;
        // ×, Alt+F4 and the taskbar's Close all arrive as SC_CLOSE before the WM_CLOSE.
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(WndProc);
    }

    /// <summary>Call just before the window's own code closes it (Esc, a Close button, a finished step).</summary>
    public void MarkUserClose() => _userClose = true;

    /// <summary>
    /// Call first in OnClosing. True for an external close: the close is cancelled and the app's prompt-free exit is
    /// started (first caller wins). Closes while the app is already exiting or Windows is ending the session, and
    /// closes from a modal dialog's result (DialogResult set), are left alone.
    /// </summary>
    public bool CancelIfExternal(CancelEventArgs e)
    {
        var userClose = _userClose || _window.DialogResult.HasValue;
        _userClose = false;
        if (userClose || _host.IsExiting || _host.IsSessionEnding || !_host.IsRunning)
            return false;

        e.Cancel = true;
        // Leave the close handler first: the exit closes this window itself.
        _window.Dispatcher.BeginInvoke(() => _host.ExitApp(ExitReason.External));
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_CLOSE)
            _userClose = true;
        return IntPtr.Zero;
    }
}
