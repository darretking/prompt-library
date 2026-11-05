using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PromptLibrary.App.Views;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.App.Updates;

/// <summary>
/// The update checker's face: checks GitHub about 30 s after start and then hourly (at most once a day, see
/// <see cref="UpdateSchedule"/>), announces a new version once (a tray balloon, or the dialog when the main window
/// is in front), keeps the "Update available" reminder in the rail and the tray menu, and runs a verified installer
/// after a clean exit. Automatic checks never show errors and never throw. Lives on the UI thread.
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    private const string AppName = "Prompt Library";
    private const string DownloadFailedText = "Couldn't download the update, so the release page will open instead.";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly AppHost _host;
    private readonly IUpdateCheckService _service;
    private readonly UpdateDownloader _downloader;
    private readonly DispatcherTimer _timer;
    private bool _dialogOpen;

    // A balloon announced a version but its dialog hasn't been seen yet: shown when the main window next opens.
    private bool _dialogOwed;

    public UpdateCoordinator(AppHost host, IUpdateCheckService service, UpdateDownloader downloader)
    {
        _host = host;
        _service = service;
        _downloader = downloader;
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TickInterval;
            await RunAutomaticCheckAsync();
        };
    }

    /// <summary>Raised when <see cref="ReminderText"/> changes.</summary>
    public event EventHandler? ReminderChanged;

    /// <summary><c>Update available: v1.0.2</c> while a newer release is known; otherwise null.</summary>
    public string? ReminderText => NewerRelease is { } r ? $"Update available: v{r.Version}" : null;

    private ReleaseInfo? NewerRelease
        => UpdateSchedule.HasNewer(_service.State, _service.Running) ? _service.State.LatestRelease : null;

    /// <summary>Shows any reminder saved from an earlier run and starts the check timer.</summary>
    public void Start()
    {
        ApplyReminder();
        _timer.Start();
    }

    /// <summary>Setup has just finished: check soon instead of waiting for the next hourly tick.</summary>
    public void CheckSoon()
    {
        _timer.Stop();
        _timer.Interval = FirstCheckDelay;
        _timer.Start();
    }

    /// <summary>The main window was just shown: show the dialog a balloon announced, if it is still owed.</summary>
    public void OnMainWindowShown()
    {
        if (!_dialogOwed || NewerRelease is null) return;
        _dialogOwed = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_host.MainWindowInFront) OpenDialog();
            else _dialogOwed = true; // another dialog of ours is open; try again next time
        });
    }

    /// <summary><c>Help → Check for updates…</c>: asks GitHub now (ignoring the daily limit) and says what it found.</summary>
    public async Task CheckNowAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await _service.CheckAsync(manual: true);
        }
        catch (Exception ex)
        {
            CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now);
            result = new UpdateCheckResult(UpdateCheckStatus.Failed, null, UpdateFailure.BadResponse);
        }
        if (_host.IsExiting) return;
        ApplyReminder();

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when NewerRelease is { } release:
                _service.MarkPrompted(release.Version);
                OpenDialog();
                break;
            case UpdateCheckStatus.UpToDate:
            case UpdateCheckStatus.UpdateAvailable: // a newer release that is somehow no longer newer
                ConfirmDialog.Inform(_host.DialogOwner,
                    $"You're up to date. Prompt Library {_service.Running} is the latest version.");
                break;
            default:
                ConfirmDialog.Inform(_host.DialogOwner, result.Failure == UpdateFailure.RateLimited
                    ? "GitHub is busy right now. Try again in a little while."
                    : "Couldn't check for updates. Check your internet connection and try again.");
                break;
        }
    }

    /// <summary>Opens the update dialog for the newest known release (rail link, tray item, balloon, manual check).</summary>
    public void OpenDialog()
    {
        if (_dialogOpen || _host.IsExiting || _host.AsksNothing || NewerRelease is not { } release) return;
        if (ComponentDispatcher.IsThreadModal)
        {
            // Another of our dialogs is open (e.g. "Save changes?" from the tray or a balloon click): never stack
            // on it; open once it has closed.
            WaitForModalToClose();
            return;
        }
        _dialogOwed = false;
        _dialogOpen = true;
        UpdateDialogResult result;
        try
        {
            result = UpdateDialog.Ask(_host.DialogOwner, release, _service.Running, async (progress, ct) =>
            {
                // Download from a fresh answer, never from the saved state: the file and its checksum must come from
                // the same live response.
                var fresh = await _service.ConfirmLatestAsync(release.Version, ct)
                            ?? throw new UpdateDownloadException("GitHub didn't confirm this release.");
                return await _downloader.DownloadAsync(fresh, Paths.UpdateDownloadFolder, progress, ct);
            });
        }
        finally
        {
            _dialogOpen = false;
        }

        switch (result.Outcome)
        {
            case UpdateDialogOutcome.NotNow:
            case UpdateDialogOutcome.Cancelled:
                _service.MarkDeclined(release.Version);
                break;
            case UpdateDialogOutcome.Failed:
                ConfirmDialog.Inform(_host.DialogOwner, DownloadFailedText);
                UpdateDialog.OpenInBrowser(release.PageUrl);
                break;
            case UpdateDialogOutcome.Downloaded:
                Install(release, result.InstallerPath!);
                break;
        }
        ApplyReminder();
    }

    public void Dispose()
    {
        _timer.Stop();
        _modalWait?.Stop();
    }

    private DispatcherTimer? _modalWait;

    private void WaitForModalToClose()
    {
        if (_modalWait is { IsEnabled: true }) return;
        _modalWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _modalWait.Tick += (_, _) =>
        {
            if (ComponentDispatcher.IsThreadModal) return;
            _modalWait!.Stop();
            OpenDialog();
        };
        _modalWait.Start();
    }

    private async Task RunAutomaticCheckAsync()
    {
        try
        {
            if (!_host.Settings.FirstRunDone || _host.IsExiting || _host.AsksNothing) return;
            var result = await _service.CheckAsync(manual: false);
            if (_host.IsExiting || result.Status == UpdateCheckStatus.NotDue) return;
            ApplyReminder();
            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && UpdateSchedule.ShouldPrompt(_service.State, _service.Running))
                Announce(_service.State.LatestRelease!);
        }
        catch (Exception ex)
        {
            // An automatic check must never take the app down; the log is enough.
            CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now);
        }
    }

    private void Announce(ReleaseInfo release)
    {
        _service.MarkPrompted(release.Version);
        if (_host.MainWindowInFront)
        {
            OpenDialog();
            return;
        }
        _dialogOwed = true;
        _host.Tray.ShowMessage(AppName, $"Prompt Library {release.Version} is available. Click to update.",
            onClick: OpenDialog);
    }

    /// <summary>Exits the app the normal way (save question, backup) and starts the installer as the last step.</summary>
    private void Install(ReleaseInfo release, string installerPath)
    {
        if (Environment.GetEnvironmentVariable("PROMPTLIBRARY_DEBUG_NO_INSTALL") == "1")
        {
            // Testing: never install over the copy that is running; show the verified file instead.
            Process.Start("explorer.exe", $"/select,\"{installerPath}\"");
            return;
        }

        // Cancel at "Save changes?" (or a close question already open) keeps the app running; the reminder stays.
        _host.TryExit(ExitReason.User, afterExit: () => StartInstaller(release, installerPath));
    }

    private static void StartInstaller(ReleaseInfo release, string installerPath)
    {
        try
        {
            if (!File.Exists(installerPath)) throw new FileNotFoundException("The installer is gone.", installerPath);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            // The app is already on its way out: the release page is the best we can still offer.
            CrashLog.Append(Paths.CrashLogFile, ex, DateTime.Now);
            UpdateDialog.OpenInBrowser(release.PageUrl);
        }
    }

    private void ApplyReminder()
    {
        var release = NewerRelease;
        _host.Tray.SetUpdateItem(release is null ? null : $"Update to {release.Version}…");
        ReminderChanged?.Invoke(this, EventArgs.Empty);
    }
}
