using System.Threading;
using System.Windows;
using System.Windows.Forms.Integration;

namespace PromptLibrary.App;

/// <summary>
/// Entry point. Keeps a single running copy: a second launch signals the first copy to show its
/// main window and then exits.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\PromptLibrary.SingleInstance";
    private const string ShowEventName = @"Local\PromptLibrary.Show";

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private EventWaitHandle? _showSignal;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        // Lets the WinForms tray menu get keyboard and mouse handling inside the WPF message loop.
        WindowsFormsHost.EnableWindowsFormsInterop();

        _host = new AppHost();
        if (!_host.Start())
            return; // Start has already shown the problem and shut the app down.

        ListenForShowSignal(_showSignal, _host);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Logoff/shutdown may not run OnExit in time; remove the tray icon now so none is left behind.
        _host?.Dispose();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        if (_ownsMutex)
            _singleInstance!.ReleaseMutex();
        _singleInstance?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Waits on a background thread for a second launch and shows the main window on the UI thread.</summary>
    private void ListenForShowSignal(EventWaitHandle signal, AppHost host)
    {
        var listener = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
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
