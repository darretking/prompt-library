using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PromptLibrary.Core.Backup;

namespace PromptLibrary.App.Views;

/// <summary>
/// Lists the backups in the backup folder, newest first with the newest readable one selected, and restores
/// the chosen one after a confirm. Also opened at startup when library.json can't be read.
/// </summary>
public partial class RestoreWindow : Window
{
    private const int FolderDisplayLength = 44;

    private readonly AppHost _host;
    private readonly bool _isStartup;
    private string? _folder;

    /// <param name="isStartup">True when opened because library.json couldn't be read (Settings isn't available yet).</param>
    public RestoreWindow(AppHost host, bool isStartup)
    {
        _host = host;
        _isStartup = isStartup;
        InitializeComponent();
        Loaded += async (_, _) => await ShowBackupsAsync(host.Settings.BackupFolder);
    }

    /// <summary>The backup that was restored (set when the window closes with success).</summary>
    public BackupInfo? RestoredFrom { get; private set; }

    private async Task ShowBackupsAsync(string? folder)
    {
        _folder = folder;
        FolderText.Text = folder is null ? "No backup folder chosen" : BackupLocation.ShortenMiddle(folder, FolderDisplayLength);
        FolderText.ToolTip = folder;
        BackupList.ItemsSource = null;
        RestoreButton.IsEnabled = false;

        if (folder is null)
        {
            ShowEmpty("Backups are off, so there's nothing to restore yet.", offerSetUp: !_isStartup);
            return;
        }

        ShowEmpty("Looking for backups…");
        var (unavailable, backups) = await Task.Run(() =>
        {
            var missing = BackupLocation.IsUnavailable(folder, Directory.Exists);
            return (missing, missing ? [] : _host.ListBackups(folder));
        });
        if (folder != _folder) return; // another folder was chosen meanwhile

        if (unavailable)
            ShowEmpty(BackupLocation.PausedMessage(folder, _host.Settings.BackupProviderName));
        else if (backups.Count == 0)
            ShowEmpty("No backups yet. They'll appear here after the first backup.");
        else
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            var now = DateTime.Now;
            var rows = backups.Select(b => new BackupRow(b, BackupLocation.BackupRowText(b, now))).ToList();
            BackupList.ItemsSource = rows;
            BackupList.SelectedItem = rows.FirstOrDefault(r => r.IsReadable);
            FocusSelectedRow();
        }
    }

    private void ShowEmpty(string text, bool offerSetUp = false)
    {
        EmptyText.Text = text;
        SetUpBackupsLink.Visibility = offerSetUp ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    private void FocusSelectedRow()
        => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (BackupList.SelectedItem is { } item
                && BackupList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
                row.Focus();
        });

    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreButton.IsEnabled = BackupList.SelectedItem is BackupRow { IsReadable: true };
        ErrorText.Text = null;
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Restore();

    private void Restore_Click(object sender, RoutedEventArgs e) => Restore();

    private void SetUpBackups_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _host.OpenSettings(focusBackups: true);
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder with your backups" };
        if (_folder is not null && Directory.Exists(_folder))
            dialog.InitialDirectory = _folder;
        if (dialog.ShowDialog(this) == true)
            await ShowBackupsAsync(dialog.FolderName);
    }

    private async void Restore()
    {
        if (BackupList.SelectedItem is not BackupRow { IsReadable: true } row || _folder is null) return;

        // Unsaved edits in the main window first (they would otherwise be lost or written over the restore).
        if (!_host.ConfirmUnsavedChangesBeforeRestore()) return;

        var date = BackupLocation.RelativeTimeText(row.Info.Timestamp, DateTime.Now, midSentence: true);
        var question = $"Replace your current library with the backup from {date}? " +
                       "Your current library will be backed up first.";
        if (!ConfirmDialog.Ask(this, question, "Restore", "Cancel")) return;

        var error = _host.RestoreBackup(row.Info, _folder);
        if (error is null)
        {
            RestoredFrom = row.Info;
            DialogResult = true; // closes the window
            return;
        }

        // The safety backup may have been written even if the restore failed, so list again.
        await ShowBackupsAsync(_folder);
        ErrorText.Text = error;
    }

    /// <summary>One row: <c>Yesterday, 18:30 · 12 prompts</c> (or <c>… · Damaged — can't restore</c>).</summary>
    private sealed record BackupRow(BackupInfo Info, string Text)
    {
        public bool IsReadable => Info.PromptCount >= 0;
    }
}
