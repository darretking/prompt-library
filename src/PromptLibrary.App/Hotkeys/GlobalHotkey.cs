using System.Runtime.InteropServices;
using System.Windows.Interop;
using PromptLibrary.Core.Hotkeys;

namespace PromptLibrary.App.Hotkeys;

/// <summary>
/// One system-wide shortcut, registered with RegisterHotKey on a hidden message-only window.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 1;
    private const int ProbeId = 2; // only for IsFree; never left registered
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _window;
    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey()
    {
        _window = new HwndSource(new HwndSourceParameters("PromptLibrary.Hotkey")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    /// <summary>Replaces any shortcut registered earlier. False if another app already owns it.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        var modifiers = (uint)gesture.Modifiers | MOD_NOREPEAT;
        _registered = RegisterHotKey(_window.Handle, HotkeyId, modifiers, VirtualKey(gesture.Key));
        return _registered;
    }

    /// <summary>True if no app (this one included) owns the shortcut now: registers it on a spare id and lets it go.</summary>
    public bool IsFree(HotkeyGesture gesture)
    {
        var modifiers = (uint)gesture.Modifiers | MOD_NOREPEAT;
        if (!RegisterHotKey(_window.Handle, ProbeId, modifiers, VirtualKey(gesture.Key)))
            return false;
        UnregisterHotKey(_window.Handle, ProbeId);
        return true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    /// <summary>Key is "A"–"Z", "0"–"9" (virtual key = the character) or "F1"–"F24" (VK_F1 = 0x70).</summary>
    private static uint VirtualKey(string key)
        => key.Length > 1 && key[0] == 'F'
            ? 0x70u + uint.Parse(key[1..]) - 1
            : key[0];

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
