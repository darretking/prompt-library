using System.Runtime.InteropServices;

namespace PromptLibrary.App.Search;

/// <summary>A rectangle in device pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Where the launcher and the toast go, as pure math in device pixels (so windows land right on mixed-DPI setups).
/// <c>work</c> is the monitor's work area in device pixels, <c>scale</c> its DPI divided by 96.
/// </summary>
public static class Placement
{
    public const double LauncherWidth = 640;
    public const double LauncherHeight = 420;
    public const double LauncherMinHeight = 240;
    private const double LauncherTopFraction = 0.25;

    /// <summary>The fill-in form may grow the launcher to this share of the work area's height before its fields scroll.</summary>
    public const double FormMaxShare = 0.8;
    public const double ToastLift = 48;

    /// <summary>The launcher: 640 x 420 DIPs (shorter on a short screen), centred, a quarter down, never below the work area.</summary>
    public static PixelRect Launcher(PixelRect work, double scale) => Launcher(work, scale, null);

    /// <summary>
    /// As the two-argument overload. With <paramref name="formHeightDips"/> (the fill-in form's height including the footer)
    /// the window is as tall as the form: at least <see cref="LauncherMinHeight"/>, at most <see cref="FormMaxShare"/> of the
    /// work area. The top edge stays where it was unless the window would pass the bottom.
    /// </summary>
    public static PixelRect Launcher(PixelRect work, double scale, double? formHeightDips)
    {
        var width = Math.Min(work.Width, (int)Math.Round(LauncherWidth * scale));
        var heightDips = formHeightDips is { } wanted
            ? Math.Clamp(wanted, LauncherMinHeight, Math.Max(LauncherMinHeight, work.Height / scale * FormMaxShare))
            : Math.Min(LauncherHeight, Math.Max(LauncherMinHeight, work.Height / scale - 32));
        var height = Math.Min(work.Height, (int)Math.Round(heightDips * scale));
        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (int)Math.Round(work.Height * LauncherTopFraction);
        if (y + height > work.Bottom) y = Math.Max(work.Y, work.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>The toast: centred at the bottom of the work area, <see cref="ToastLift"/> DIPs above its edge (above the taskbar).</summary>
    public static PixelRect Toast(PixelRect work, double scale, double widthDips, double heightDips)
    {
        var width = Math.Min(work.Width, (int)Math.Ceiling(widthDips * scale));
        var height = (int)Math.Ceiling(heightDips * scale);
        var x = work.X + (work.Width - width) / 2;
        var y = work.Bottom - (int)Math.Round(ToastLift * scale) - height;
        return new PixelRect(x, Math.Max(work.Y, y), width, height);
    }
}

/// <summary>Monitor lookups by pixel position or window: work area and DPI scale (device pixels).</summary>
internal static class Monitors
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    public static (PixelRect Work, double Scale) UnderPoint(int x, int y)
        => Describe(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    public static (PixelRect Work, double Scale) OfWindow(IntPtr window)
        => Describe(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST));

    private static (PixelRect, double) Describe(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return (new PixelRect(0, 0, 1920, 1040), 1.0);
        var scale = 1.0;
        try
        {
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        var w = info.rcWork;
        return (new PixelRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
