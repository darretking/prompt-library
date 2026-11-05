using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace PromptLibrary.App.Tests;

/// <summary>Runs a test body on its own STA thread (WPF windows need one) and rethrows what it threw.</summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("The STA test body didn't finish within 30 s.");
        failure?.Throw();
    }

    /// <summary>Runs everything queued on this thread's dispatcher (down to background priority).</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
