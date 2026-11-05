using System.Runtime.InteropServices;

namespace PromptLibrary.App.Hotkeys;

/// <summary>
/// A low-level keyboard hook (WH_KEYBOARD_LL) used only while the shortcut box is listening. A combination that
/// another app has registered as its hotkey never reaches our window as a key press, so the box couldn't see it;
/// the hook can. It only looks at keys while our window is the foreground window, and only takes
/// Ctrl/Alt + letter, digit or F-key presses (reported as text such as <c>Ctrl+Alt+K</c> and not passed on, so the
/// other app's hotkey doesn't fire). Everything else goes through untouched. Install on the UI thread; dispose to
/// remove it.
/// </summary>
public sealed class ShortcutKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly IntPtr _window;
    private readonly Action<string> _pressed;
    private readonly HookProc _callback; // referenced here so the delegate isn't collected while installed
    private IntPtr _hook;

    /// <param name="window">Our window; keys are only looked at while it is in the foreground.</param>
    /// <param name="pressed">Called (inside the hook, so keep it short) with the combination's text.</param>
    public ShortcutKeyHook(IntPtr window, Action<string> pressed)
    {
        _window = window;
        _pressed = pressed;
        _callback = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
    }

    /// <summary>False if Windows refused the hook; the box then works as before (it just can't see taken shortcuts).</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0
            && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
            && GetForegroundWindow() == _window
            && Combination(Marshal.ReadInt32(data)) is { } text) // vkCode is KBDLLHOOKSTRUCT's first field
        {
            _pressed(text);
            return 1; // swallowed: neither our window nor the app owning the hotkey gets it
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary><c>Ctrl+Alt+K</c>-style text while Ctrl or Alt is held with a letter, digit or F1–F24; else null.</summary>
    private static string? Combination(int virtualKey)
    {
        var ctrl = IsDown(VK_CONTROL);
        var alt = IsDown(VK_MENU);
        if (!ctrl && !alt) return null;

        string? key = virtualKey switch
        {
            >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F), // VK_F1..VK_F24
            _ => null,
        };
        if (key is null) return null;

        return (ctrl ? "Ctrl+" : "")
               + (alt ? "Alt+" : "")
               + (IsDown(VK_SHIFT) ? "Shift+" : "")
               + (IsDown(VK_LWIN) || IsDown(VK_RWIN) ? "Win+" : "")
               + key;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
