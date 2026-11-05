using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PromptLibrary.Core.Backup;

namespace PromptLibrary.App.Views;

/// <summary>
/// Lists the backups in the backup folder, newest first with the newest readable one selected, and restores
/// the chosen one after a confirm. When there are safety copies (made before earlier restores), a collapsed
/// "Safety copies" group lists them too. Also opened at startup when library.json can't be read.
/// </summary>
public partial class RestoreWindow : Window
{
    private const int FolderDisplayLength = 44;

    private readonly AppHost _host;
    private readonly UserCloseTracker _closes;
    private readonly bool _isStartup;
    private string? _folder;
    private bool _restoring;

    /// <param name="isStartup">True when opened because library.json couldn't be read (Settings isn't available yet).</param>
    public RestoreWindow(AppHost host, bool isStartup)
    {
        _host = host;
        _isStartup = isStartup;
        _closes = new UserCloseTracker(this, host);
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var safetyCopies = ShowSafetyCopiesAsync();
            await ShowBackupsAsync(host.Settings.BackupFolder);
            await safetyCopies;
        };
    }

    /// <summary>The backup that was restored (set when the window closes with success).</summary>
    public BackupInfo? RestoredFrom { get; private set; }

    private BackupRow? SelectedRow => (BackupList.SelectedItem ?? SafetyList.SelectedItem) as BackupRow;

    protected override void OnClosing(CancelEventArgs e)
    {
        // The library file is being replaced; the window closes by itself when that's done.
        if (_restoring) e.Cancel = true;
        // Another program (the installer) closing us: the app exits without asking anything instead.
        else _closes.CancelIfExternal(e);
        base.OnClosing(e);
    }

    private async Task ShowBackupsAsync(string? folder)
    {
        _folder = folder;
        FolderText.Text = folder is null ? "No backup folder chosen" : BackupLocation.ShortenMiddle(folder, FolderDisplayLength);
        FolderText.ToolTip = folder;
        BackupList.ItemsSource = null;
        UpdateRestoreButton();

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
            var rows = ToRows(backups);
            BackupList.ItemsSource = rows;
            // The newest ordinary backup, not the copy a restore just made of the library it replaced.
            BackupList.SelectedItem = rows.FirstOrDefault(r => r is { IsReadable: true, Note: null })
                                      ?? rows.FirstOrDefault(r => r.IsReadable);
            FocusSelectedRow();
        }
    }

    /// <summary>Lists <c>%APPDATA%\PromptLibrary\Safety backups</c>; the group shows only when it has copies.</summary>
    private async Task ShowSafetyCopiesAsync()
    {
        var copies = await Task.Run(() => _host.ListBackups(Paths.SafetyBackupsFolder));
        SafetyList.ItemsSource = ToRows(copies, labelPreRestoreCopies: false); // the group says what they are
        SafetyToggle.Visibility = copies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (copies.Count == 0) SafetyToggle.IsChecked = false;
    }

    private List<BackupRow> ToRows(IReadOnlyList<BackupInfo> backups, bool labelPreRestoreCopies = true)
    {
        var now = DateTime.Now;
        return backups.Select(b => new BackupRow(b, BackupLocation.BackupRowText(b, now),
                BackupLocation.BackupRowNote(labelPreRestoreCopies && BackupLocation.IsPreRestoreCopy(_host.Settings, b.Path))))
            .ToList();
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

    // One selection across both lists: picking a row in one clears the other.
    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedItem is not null) SafetyList.SelectedItem = null;
        UpdateRestoreButton();
    }

    private void SafetyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SafetyList.SelectedItem is not null) BackupList.SelectedItem = null;
        UpdateRestoreButton();
    }

    private void UpdateRestoreButton()
    {
        RestoreButton.IsEnabled = SelectedRow is { IsReadable: true };
        ErrorText.Text = null;
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Restore();

    private void Restore_Click(object sender, RoutedEventArgs e) => Restore();

    private void SetUpBackups_Click(object sender, RoutedEventArgs e)
    {
        _closes.MarkUserClose();
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
        if (_restoring || SelectedRow is not { IsReadable: true } row) return;

        // Unsaved edits in the main window first (they would otherwise be lost or written over the restore).
        if (!_host.ConfirmUnsavedChangesBeforeRestore()) return;

        var date = BackupLocation.RelativeTimeText(row.Info.Timestamp, DateTime.Now, midSentence: true);
        var question = $"Replace your current library with the backup from {date}? " +
                       "Your current library will be backed up first.";
        if (!ConfirmDialog.Ask(this, question, "Restore", "Cancel")) return;

        _restoring = true;
        Root.IsEnabled = false;
        string? error;
        try
        {
            error = await _host.RestoreBackupAsync(row.Info);
        }
        finally
        {
            _restoring = false;
            Root.IsEnabled = true;
        }

        if (error is null)
        {
            RestoredFrom = row.Info;
            DialogResult = true; // closes the window
            return;
        }

        // The safety backup may have been written even if the restore failed, so list again.
        await Task.WhenAll(ShowBackupsAsync(_folder), ShowSafetyCopiesAsync());
        ErrorText.Text = error;
    }

    /// <summary>
    /// One row: <c>Yesterday, 18:30 · 12 prompts</c> (or <c>… · Damaged — can't restore</c>), with the muted
    /// <c>Made automatically before a restore</c> under a copy a restore made first.
    /// </summary>
    private sealed record BackupRow(BackupInfo Info, string Text, string? Note)
    {
        public bool IsReadable => Info.PromptCount >= 0;
        public string AutomationName => Note is null ? Text : $"{Text}, {Note.ToLowerInvariant()}";
    }
}
