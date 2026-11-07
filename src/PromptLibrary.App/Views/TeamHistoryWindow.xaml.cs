using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Views;

/// <summary>
/// Restore an older version of the team library (from the Team header's ⋯ menu), or Review the other versions sync
/// left beside it. Lists those first ("Other version from sync · …"), then the history copies, newest first, and
/// writes the chosen one back for everyone after a confirm; the current version gets a history copy first. Restoring
/// a version from sync, or Keep current version, resolves the conflict: the copies move into the history folder.
/// </summary>
public partial class TeamHistoryWindow : Window
{
    private const string ChangedByOtherMessage =
        "Someone else changed the team library, so it's been reloaded. Check it, then choose the version again.";

    private readonly TeamLibraryService _service;
    private readonly UserCloseTracker _closes;
    private readonly bool _review;
    private bool _restoring;

    /// <param name="review">True for the conflict notice's Review: the first other version from sync is selected.</param>
    public TeamHistoryWindow(AppHost host, TeamLibraryService service, bool review)
    {
        _service = service;
        _review = review;
        _closes = new UserCloseTracker(this, host);
        InitializeComponent();
        NameText.Text = service.Team?.Name ?? "";
        Loaded += async (_, _) => await ShowVersionsAsync();
    }

    /// <summary>
    /// One row: <c>Today, 07:52 · 24 prompts</c>, or for a conflict copy
    /// <c>Other version from sync · today, 07:52 · 24 prompts</c>.
    /// </summary>
    public static string RowText(TeamHistoryItem item, DateTime now)
    {
        var count = BackupLocation.PromptCountText(item.PromptCount);
        var local = item.SavedAt.LocalDateTime;
        return item.IsConflictCopy
            ? $"Other version from sync · {BackupLocation.RelativeTimeText(local, now, midSentence: true)} · {count}"
            : $"{BackupLocation.RelativeTimeText(local, now)} · {count}";
    }

    private VersionRow? SelectedRow => VersionList.SelectedItem as VersionRow;

    protected override void OnClosing(CancelEventArgs e)
    {
        // The team file is being written; the window closes by itself when that's done.
        if (_restoring) e.Cancel = true;
        // Another program (the installer) closing us: the app exits without asking anything instead.
        else _closes.CancelIfExternal(e);
        base.OnClosing(e);
    }

    private async Task ShowVersionsAsync()
    {
        VersionList.ItemsSource = null;
        UpdateRestoreButton();
        ShowEmpty("Looking for older versions…");

        var items = await _service.ListHistoryAsync();
        var conflicts = items.Any(i => i.IsConflictCopy);
        KeepButton.Visibility = conflicts ? Visibility.Visible : Visibility.Collapsed;
        HelpText.Text = conflicts
            ? "Restore the version everyone should have, or keep the current one. Either way, nothing is deleted."
            : "Restoring keeps a copy of the current version first.";
        if (items.Count == 0)
        {
            ShowEmpty("No older versions yet. One is kept each time the team library is saved.");
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        var now = DateTime.Now;
        var rows = items.Select(i => new VersionRow(i, RowText(i, now))).ToList();
        VersionList.ItemsSource = rows;
        VersionList.SelectedItem = (_review ? rows.FirstOrDefault(r => r.Item.IsConflictCopy) : null)
                                   ?? rows.FirstOrDefault(r => !r.Item.IsConflictCopy) ?? rows[0];
        FocusSelectedRow();
    }

    private void ShowEmpty(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void FocusSelectedRow()
        => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (VersionList.SelectedItem is { } item
                && VersionList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
                row.Focus();
        });

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRestoreButton();

    private void UpdateRestoreButton()
    {
        RestoreButton.IsEnabled = SelectedRow is not null;
        ErrorText.Text = null;
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Restore();

    private void Restore_Click(object sender, RoutedEventArgs e) => Restore();

    private async void Restore()
    {
        if (_restoring || SelectedRow is not { } row) return;

        var date = BackupLocation.RelativeTimeText(row.Item.SavedAt.LocalDateTime, DateTime.Now, midSentence: true);
        var question = $"Replace the team library with the version from {date}? This changes it for everyone. " +
                       "The current version is kept first.";
        if (!ConfirmDialog.Ask(this, question, "Restore", "Cancel")) return;

        _restoring = true;
        Root.IsEnabled = false;
        TeamSaveResult result;
        try
        {
            result = await _service.RestoreAsync(row.Item);
        }
        finally
        {
            _restoring = false;
            Root.IsEnabled = true;
        }

        if (result.Status == TeamSaveStatus.Saved)
        {
            DialogResult = true; // closes the window
            return;
        }

        await ShowVersionsAsync(); // a refused or failed restore may still have kept a copy
        ErrorText.Text = result.Status == TeamSaveStatus.ChangedByOther
            ? ChangedByOtherMessage
            : result.Message ?? "Couldn't restore the team library.";
    }

    /// <summary>Keep current version: the team file stays; the other versions from sync go into its history.</summary>
    private async void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (_restoring) return;
        _restoring = true;
        Root.IsEnabled = false;
        try
        {
            await _service.KeepCurrentVersionAsync();
        }
        finally
        {
            _restoring = false;
            Root.IsEnabled = true;
        }
        DialogResult = false; // nothing was restored; the notice clears with the next read
    }

    private sealed record VersionRow(TeamHistoryItem Item, string Text);
}
