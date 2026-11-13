using System.Threading;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.App;

/// <summary>
/// Entry point. Keeps a single running copy: a second launch signals the first copy to show its
/// main window and then exits.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\PromptLibrary.SingleInstance";
    private const string ShowEventName = @"Local\PromptLibrary.Show";
    private static readonly TimeSpan CrashCleanupTimeout = TimeSpan.FromSeconds(3);

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private EventWaitHandle? _showSignal;
    private AppHost? _host;
    private readonly CancellationTokenSource _pipeStop = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Views.DropAlignment.KeepLeftAligned();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + Paths.InstanceSuffix);
        _singleInstance = new Mutex(initiallyOwned: true, MutexName + Paths.InstanceSuffix, out _ownsMutex);
        var joinText = PromptLibrary.App.Startup.JoinArguments.Parse(e.Args);
        if (!_ownsMutex)
        {
            // Hand a team file or link to the running copy; if that fails, at least bring it up as before.
            if (joinText is null || !PromptLibrary.App.Startup.JoinPipe.Send(joinText, PromptLibrary.App.Startup.JoinPipe.Name, TimeSpan.FromSeconds(2)))
                _showSignal.Set();
            Shutdown();
            return;
        }

        // Lets the WinForms tray menu get keyboard and mouse handling inside the WPF message loop.
        WindowsFormsHost.EnableWindowsFormsInterop();

        // Windows 11 window chrome; AppHost.Start applies the saved Theme setting (light/dark palette) once settings are loaded.
        Theming.WindowBackdrop.Register();

        _host = new AppHost();
        HandleCrashes(_host);
        if (!_host.Start())
            return; // Start has already shown the problem and shut the app down.

        ListenForShowSignal(_showSignal, _host);
        PromptLibrary.App.Startup.JoinPipe.Listen(PromptLibrary.App.Startup.JoinPipe.Name, text => Dispatcher.BeginInvoke(() => _host.ShowJoinPrefilled(text)),
            _pipeStop.Token);

        // A first launch with a file or link opens Join with it once start-up is done.
        if (joinText is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _host.ShowJoinPrefilled(joinText));
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Logoff/shutdown may not run OnExit in time; remove the tray icon now so none is left behind.
        // From here on nothing asks questions (unsaved edits stay unsaved rather than blocking logoff).
        _host?.OnSessionEnding();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeStop.Cancel();
        _host?.Dispose();
        if (_ownsMutex)
            _singleInstance!.ReleaseMutex();
        _singleInstance?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// An exception nothing caught: <see cref="AppHost.OnCrash"/> logs it, keeps unsaved work and removes the tray
    /// icon, then the app still dies (nothing is marked handled: its state can't be trusted any more). A task whose
    /// exception nobody looked at is only logged.
    /// </summary>
    private void HandleCrashes(AppHost host)
    {
        DispatcherUnhandledException += (_, e) => host.OnCrash(e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception ?? new Exception($"Unknown error: {e.ExceptionObject}");
            if (Dispatcher.CheckAccess())
            {
                host.OnCrash(exception);
                return;
            }

            // A background thread: the clean-up belongs on the UI thread, if it still answers in time.
            try
            {
                Dispatcher.Invoke(() => host.OnCrash(exception), DispatcherPriority.Send, CancellationToken.None,
                    CrashCleanupTimeout);
            }
            catch (Exception)
            {
                // The UI thread is gone or failing too; at least log it below.
            }
            if (!host.HasCrashed)
                CrashLog.Append(Paths.CrashLogFile, exception, DateTime.Now);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Append(Paths.CrashLogFile, e.Exception, DateTime.Now);
            e.SetObserved();
        };
    }

    /// <summary>Waits on a background thread for a second launch and shows the main window on the UI thread.</summary>
    private void ListenForShowSignal(EventWaitHandle signal, AppHost host)
    {
        var listener = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    signal.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return; // OnExit disposed the handle: the app is closing, so stop listening.
                }
                Dispatcher.BeginInvoke(host.ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "PromptLibrary.ShowSignal",
        };
        listener.Start();
    }
}
