using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.App.Views;

/// <summary>How the update dialog ended.</summary>
public enum UpdateDialogOutcome
{
    /// <summary>Not now (also Esc and ×).</summary>
    NotNow,

    /// <summary>The installer was downloaded and verified; <see cref="UpdateDialogResult.InstallerPath"/> is set.</summary>
    Downloaded,

    /// <summary>The download failed or couldn't be verified.</summary>
    Failed,

    /// <summary>The user cancelled the download.</summary>
    Cancelled,
}

public sealed record UpdateDialogResult(UpdateDialogOutcome Outcome, string? InstallerPath = null);

/// <summary>
/// "Prompt Library X is available. You have Y." with <c>Update now</c> / <c>Not now</c>. Update now downloads the
/// installer here, with progress and Cancel, and closes when the download ends; the caller then runs it.
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly ReleaseInfo _release;
    private readonly Func<IProgress<double>, CancellationToken, Task<string>> _download;
    private CancellationTokenSource? _downloading;
    private UpdateDialogResult _result = new(UpdateDialogOutcome.NotNow);

    private UpdateDialog(ReleaseInfo release, AppVersion running,
        Func<IProgress<double>, CancellationToken, Task<string>> download)
    {
        InitializeComponent();
        _release = release;
        _download = download;
        MessageText.Text = $"Prompt Library {release.Version} is available. You have {running}.";
    }

    /// <summary>
    /// Asks; on Update now runs <paramref name="download"/> (which returns the verified installer's path and throws
    /// <see cref="UpdateDownloadException"/> or <see cref="OperationCanceledException"/>).
    /// </summary>
    public static UpdateDialogResult Ask(Window? owner, ReleaseInfo release, AppVersion running,
        Func<IProgress<double>, CancellationToken, Task<string>> download)
    {
        var dialog = new UpdateDialog(release, running, download);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true; // from the tray or a balloon: nothing else of ours may be on screen
            dialog.Topmost = true;
            dialog.ContentRendered += (_, _) => dialog.Topmost = false; // in front once, then a normal window
        }
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>Opens <paramref name="url"/> in the default browser; best effort.</summary>
    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No browser registered: nothing more we can do.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Esc or × while downloading means Cancel: stop the download; the window closes when it has stopped.
        if (_downloading is { } cts)
        {
            e.Cancel = true;
            cts.Cancel();
            return;
        }
        base.OnClosing(e);
    }

    private void WhatsNew_Click(object sender, RoutedEventArgs e) => OpenInBrowser(_release.PageUrl);

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading is not null) return;
        _downloading = new CancellationTokenSource();

        UpdateButton.Visibility = Visibility.Collapsed;
        NotNowButton.Content = "Cancel";
        System.Windows.Automation.AutomationProperties.SetName(NotNowButton, "Cancel");
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<double>(fraction =>
        {
            Progress.Value = Math.Clamp(fraction, 0, 1);
            ProgressText.Text = string.Format(CultureInfo.CurrentCulture, "Downloading update… {0:0}%",
                Math.Clamp(fraction, 0, 1) * 100);
        });

        try
        {
            var path = await _download(progress, _downloading.Token);
            _result = new UpdateDialogResult(UpdateDialogOutcome.Downloaded, path);
        }
        catch (OperationCanceledException)
        {
            _result = new UpdateDialogResult(UpdateDialogOutcome.Cancelled);
        }
        catch (UpdateDownloadException)
        {
            _result = new UpdateDialogResult(UpdateDialogOutcome.Failed);
        }
        finally
        {
            _downloading.Dispose();
            _downloading = null;
        }
        Close();
    }
}
