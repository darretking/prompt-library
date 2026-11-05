using System.Diagnostics;
using System.Runtime.InteropServices;
using PromptLibrary.Core.Clipboard;

namespace PromptLibrary.App.Tray;

/// <summary>
/// Pastes (Ctrl+V) into the window the user was typing in before the shortcut menu opened. The prompt is already on
/// the clipboard. The keys are only sent when that window is back in front, no modifier key is still held, and the
/// app isn't running as administrator (Windows blocks input to it then). Use on the UI thread.
/// </summary>
public static class AutoPaster
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan ForegroundTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>True for a live window of another process (we never send keys to our own windows).</summary>
    public static bool IsOtherAppWindow(IntPtr window, out bool isOurs)
    {
        isOurs = false;
        if (window == IntPtr.Zero || !IsWindow(window)) return false;
        GetWindowThreadProcessId(window, out var pid);
        isOurs = pid == (uint)Environment.ProcessId;
        return true;
    }

    /// <summary>
    /// Sends Ctrl+V to <paramref name="target"/>. Waits up to 800 ms for Ctrl, Alt, Shift and Win to be let go (the
    /// user may still hold the shortcut), then up to 300 ms for the window to be in front (asking for it again if
    /// needed). If a modifier is still held or the window isn't in front, nothing is sent (Failed).
    /// </summary>
    public static async Task<PasteOutcome> PasteAsync(IntPtr target)
    {
        if (IsElevated(target) && !IsCurrentProcessElevated())
            return PasteOutcome.Elevated;

        var waited = Stopwatch.StartNew();
        while (AnyModifierDown() && waited.Elapsed < ModifierReleaseTimeout)
            await Task.Delay(PollInterval);
        var modifiersStillDown = AnyModifierDown();

        var foregroundMatched = false;
        if (!modifiersStillDown)
        {
            waited.Restart();
            while (!(foregroundMatched = GetForegroundWindow() == target) && waited.Elapsed < ForegroundTimeout)
            {
                SetForegroundWindow(target); // same window the tray gives focus back to, so the two never disagree
                await Task.Delay(PollInterval);
            }
        }

        if (PasteDecision.OutcomeAfterWait(modifiersStillDown, foregroundMatched) is { } notSent)
            return notSent;

        INPUT[] keys =
        [
            Key(VK_CONTROL, up: false),
            Key(VK_V, up: false),
            Key(VK_V, up: true),
            Key(VK_CONTROL, up: true),
        ];
        var accepted = SendInput((uint)keys.Length, keys, Marshal.SizeOf<INPUT>());
        return PasteDecision.OutcomeOfSend(keys.Length, (int)accepted);
    }

    private static bool AnyModifierDown()
        => IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_SHIFT) || IsDown(VK_LWIN) || IsDown(VK_RWIN);

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static INPUT Key(ushort virtualKey, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    /// <summary>True if the window's process runs elevated, or if we aren't even allowed to ask (treated the same).</summary>
    private static bool IsElevated(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return true;
        try
        {
            return IsProcessElevated(process) ?? true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsCurrentProcessElevated() => IsProcessElevated(GetCurrentProcess()) ?? false;

    private static bool? IsProcessElevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _)
                ? elevated != 0
                : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // The union must include MOUSEINPUT (its largest member) so INPUT has the size SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
}
