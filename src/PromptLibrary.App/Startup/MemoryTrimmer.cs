using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PromptLibrary.App.Startup;

/// <summary>
/// Keeps the tray app small while it sits idle. A few seconds after start-up warm-up and after a window closes (and while
/// no window of ours is showing) it compacts the managed heap once and gives the pages the process isn't using back to
/// Windows (<c>EmptyWorkingSet</c>): they come back on demand, so the next opening costs a few soft page faults, not a
/// reload. Use on the UI thread.
/// </summary>
internal static class MemoryTrimmer
{
    /// <summary>How long the app must stay quiet before it trims.</summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    private static DispatcherTimer? _timer;

    /// <summary>Trims after <see cref="IdleDelay"/>; a second request restarts the wait. Does nothing while a window is open.</summary>
    public static void Schedule()
    {
        if (Application.Current is not { } app) return;
        _timer ??= CreateTimer(app.Dispatcher);
        _timer.Stop();
        _timer.Start();
    }

    private static DispatcherTimer CreateTimer(Dispatcher dispatcher)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = IdleDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (AnyWindowShowing()) { timer.Start(); return; } // try again once it has closed
            TrimNow();
        };
        return timer;
    }

    private static bool AnyWindowShowing()
        => Application.Current.Windows.OfType<Window>().Any(w => w.IsVisible && w.Left > -5000);

    /// <summary>Collects and compacts the heap once, then returns the idle pages to Windows. Returns the working set afterwards (bytes).</summary>
    public static long TrimNow()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var self = Process.GetCurrentProcess();
            EmptyWorkingSet(self.Handle);
            self.Refresh();
            LogTrimmed(self.WorkingSet64, self.PrivateMemorySize64);
            return self.WorkingSet64;
        }
        catch (Exception)
        {
            return 0; // cosmetic: a failure leaves the memory as it was
        }
    }

    [Conditional("DEBUG")]
    private static void LogTrimmed(long workingSet, long privateBytes)
        => Debug.WriteLine($"[memory] trimmed: working set {workingSet / 1048576.0:0.0} MB, private {privateBytes / 1048576.0:0.0} MB");

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
