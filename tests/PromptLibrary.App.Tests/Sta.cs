using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace PromptLibrary.App.Tests;

/// <summary>Runs a test body on its own STA thread (WPF windows need one) and rethrows what it threw.</summary>
internal static class Sta
{
    public static void Run(Action body, int timeoutSeconds = 30)
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
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("The STA test body didn't finish within the time limit.");
        failure?.Throw();
    }

    private static readonly Lazy<Dispatcher> AppDispatcher = new(() =>
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WpfApplication tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });

    /// <summary>
    /// Runs a test body on the one long-lived STA thread that owns the shared App (the "WpfApplication" collection).
    /// WPF allows a single Application per process and <see cref="Run"/> shuts its thread's dispatcher down afterwards,
    /// which would end that Application, so the next test couldn't create one.
    /// </summary>
    public static void RunOnApp(Action body, int timeoutSeconds = 30)
    {
        var op = AppDispatcher.Value.InvokeAsync(body);
        if (op.Wait(TimeSpan.FromSeconds(timeoutSeconds)) != DispatcherOperationStatus.Completed)
            throw new TimeoutException("The STA test body didn't finish within the time limit.");
        op.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs everything queued on this thread's dispatcher (down to background priority).</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
