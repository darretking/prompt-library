using System.Windows.Interop;
using System.Windows.Threading;

namespace PromptLibrary.App;

/// <summary>
/// A hidden top-level window that turns a WM_CLOSE from outside the app (the installer closing the running copy
/// before an upgrade or uninstall) into a normal exit. The tray app has no other window that would answer it.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class CloseRequestWindow : IDisposable
{
    private const int WM_CLOSE = 0x0010;

    private readonly HwndSource _window;

    public event EventHandler? CloseRequested;

    public CloseRequestWindow()
    {
        // Top-level (no parent) rather than message-only, so EnumWindows finds it; no WS_VISIBLE, so it never shows.
        _window = new HwndSource(new HwndSourceParameters("Prompt Library")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
    }

    public void Dispose()
    {
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLOSE)
        {
            // Keep the window (the exit disposes it) and leave the message handler before exiting.
            handled = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        }
        return IntPtr.Zero;
    }
}
