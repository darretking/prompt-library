using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.Core.Backup;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The shared "where do backups go" picker (Settings and the onboarding wizard). One card per detected cloud
/// folder, plus the folder in use if it is somewhere else. Activating a card (or choosing a folder) backs up
/// there straight away; only if that works does it become the backup folder. Results show inline.
/// In select-only mode (the wizard) activating a card only selects it; the folder in use, else the first detected
/// one, starts selected, and <see cref="BackUpSelectedAsync"/> (the wizard's Next) backs up there.
/// </summary>
public sealed class BackupLocationPickerViewModel : ObservableObject
{
    private const int PathDisplayLength = 56;
    private const string CustomFolderName = "Folder";
    private const string LocalFolderNote = "This folder is on this PC, so it won't help if the PC fails.";
    private static readonly TimeSpan DetectingMessageDelay = TimeSpan.FromMilliseconds(300);

    private readonly AppHost _host;
    private readonly Func<string?, string?> _chooseFolder;
    private readonly bool _selectOnly;

    private IReadOnlyList<CloudFolder> _detected = [];
    private bool _detectedOnce;
    private bool _isDetecting;
    private bool _showDetecting;
    private bool _isBusy;
    private string? _progressText;
    private string? _resultText;
    private bool _resultIsError;
    private string? _noteText;
    private Func<Task>? _retry;
    private string? _selectedFolder;
    private bool _preselected;

    /// <param name="chooseFolder">Shows a folder dialog starting at the given folder; returns the choice or null.</param>
    /// <param name="selectOnly">Activating a card selects it instead of backing up there.</param>
    public BackupLocationPickerViewModel(AppHost host, Func<string?, string?> chooseFolder, bool selectOnly = false)
    {
        _host = host;
        _chooseFolder = chooseFolder;
        _selectOnly = selectOnly;

        PickCommand = new AsyncRelayCommand<BackupFolderCard?>(card =>
        {
            if (card is null) return Task.CompletedTask;
            if (!_selectOnly) return BackUpToAsync(card.Folder, card.ProviderName, card.Name);
            Select(card.Folder);
            return Task.CompletedTask;
        });
        ChooseFolderCommand = new AsyncRelayCommand(ChooseFolderAsync, () => !_isBusy);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(showProgress: true));
        // Not while a timed-out copy to the folder is still running.
        TryAgainCommand = new AsyncRelayCommand(() => _retry?.Invoke() ?? Task.CompletedTask,
            () => _host.SlowPickedFolderBackup is null);
        RestoreCommand = new RelayCommand(_host.OpenRestore);

        RebuildCards();
    }

    public ObservableCollection<BackupFolderCard> Cards { get; } = new();

    /// <summary>Select-only mode: the card the wizard's Next backs up to, or null.</summary>
    public BackupFolderCard? SelectedCard => Cards.FirstOrDefault(c => c.IsSelected);

    /// <summary>A backup is running (cards and "Choose another folder…" are disabled).</summary>
    public bool IsBusy => _isBusy;

    /// <summary>The "Restore from a backup…" link; not in the wizard (there is nothing to restore yet).</summary>
    public bool ShowRestoreLink => !_selectOnly;

    public IAsyncRelayCommand<BackupFolderCard?> PickCommand { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand TryAgainCommand { get; }
    public IRelayCommand RestoreCommand { get; }

    /// <summary>"Looking for cloud folders…" — only when detection takes longer than 300 ms.</summary>
    public bool ShowDetecting
    {
        get => _showDetecting;
        private set => SetProperty(ref _showDetecting, value);
    }

    /// <summary>Detection finished and found nothing: the "No cloud folder found…" line and Refresh show.</summary>
    public bool NothingFound => _detectedOnce && !_isDetecting && _detected.Count == 0;

    /// <summary>"Backing up…" while a chosen folder (one without a card) is being backed up.</summary>
    public string? ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary><c>Backed up to …</c> or <c>Backup failed: …</c> after a pick; null before.</summary>
    public string? ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public bool ResultIsError
    {
        get => _resultIsError;
        private set
        {
            if (SetProperty(ref _resultIsError, value))
                OnPropertyChanged(nameof(ShowTryAgain));
        }
    }

    public bool ShowTryAgain => _resultIsError && _retry is not null;

    /// <summary>A muted note after a successful backup, e.g. that the folder is on this PC.</summary>
    public string? NoteText
    {
        get => _noteText;
        private set => SetProperty(ref _noteText, value);
    }

    /// <summary><c>Last backup: Today, 09:05</c> when backups are on and one has been made; otherwise null.</summary>
    public string? LastBackupText
        => _host.Settings is { BackupFolder: not null, LastBackupAt: { } at }
            ? $"Last backup: {BackupLocation.RelativeTimeText(at.LocalDateTime, DateTime.Now)}"
            : null;

    /// <summary>
    /// Looks for cloud folders off the UI thread, then rebuilds the cards. Skipped while a detection or a
    /// backup is running. <paramref name="showProgress"/> shows "Looking for cloud folders…" after 300 ms.
    /// </summary>
    public async Task RefreshAsync(bool showProgress)
    {
        if (_isDetecting || _isBusy) return;
        _isDetecting = true;
        OnPropertyChanged(nameof(NothingFound));
        try
        {
            var detect = Task.Run(AppHost.DetectCloudFolders);
            if (showProgress && await Task.WhenAny(detect, Task.Delay(DetectingMessageDelay)) != detect)
                ShowDetecting = true;
            _detected = await detect;
        }
        catch (Exception)
        {
            // Detection is best effort; keep what was found before. "Choose another folder…" still works.
        }
        finally
        {
            _isDetecting = false;
            _detectedOnce = true;
            ShowDetecting = false;
        }

        if (!_isBusy)
            RebuildCards();
    }

    /// <summary>
    /// Select-only mode: backs up to the selected card's folder unless it is already the one in use. True when
    /// backups now go there (or nothing needed doing); false when the backup failed (the failure shows inline).
    /// </summary>
    public async Task<bool> BackUpSelectedAsync()
    {
        if (SelectedCard is not { } card || card.IsInUse) return true;
        return await BackUpToAsync(card.Folder, card.ProviderName, card.Name);
    }

    private void Select(string? folder)
    {
        _selectedFolder = folder;
        foreach (var card in Cards)
            card.IsSelected = folder is not null && BackupLocation.SameFolder(card.Folder, folder);
        OnPropertyChanged(nameof(SelectedCard));
    }

    private async Task ChooseFolderAsync()
    {
        var folder = _chooseFolder(_host.Settings.BackupFolder);
        if (folder is null) return;
        await BackUpToAsync(folder, BackupLocation.ProviderFor(folder, _detected), folder);
    }

    /// <summary>
    /// Backs up to the folder; on success it becomes the backup folder (and, in select-only mode, the selection),
    /// on failure nothing changes. True on success.
    /// </summary>
    private async Task<bool> BackUpToAsync(string folder, string? providerName, string where)
    {
        if (_isBusy) return false;
        SetBusy(true);
        ResultText = null;
        NoteText = null;
        _retry = null;
        ResultIsError = false;

        var card = Cards.FirstOrDefault(c => BackupLocation.SameFolder(c.Folder, folder));
        if (card is not null)
            card.IsBackingUp = true;
        else
            ProgressText = "Backing up…";

        BackupResult result;
        try
        {
            result = await _host.BackUpToNewFolderAsync(folder, providerName);
        }
        finally
        {
            if (card is not null) card.IsBackingUp = false;
            ProgressText = null;
            SetBusy(false);
        }

        if (result.Success && _selectOnly)
            _selectedFolder = folder;
        RebuildCards();
        if (result.Success)
        {
            ResultText = $"Backed up to {where}.";
            if (card is null && IsOnSystemDrive(folder))
                NoteText = LocalFolderNote;
        }
        else
        {
            _retry = () => BackUpToAsync(folder, providerName, where);
            ResultText = $"Backup failed: {result.Error}";
            ResultIsError = true;
            if (_host.SlowPickedFolderBackup is { } stillRunning)
                _ = EnableTryAgainWhenDoneAsync(stillRunning);
        }
        OnPropertyChanged(nameof(ShowTryAgain));
        OnPropertyChanged(nameof(LastBackupText));
        return result.Success;
    }

    /// <summary>Try again stays disabled until the copy that timed out has really finished.</summary>
    private async Task EnableTryAgainWhenDoneAsync(Task stillRunning)
    {
        TryAgainCommand.NotifyCanExecuteChanged();
        try
        {
            await stillRunning;
        }
        catch (Exception)
        {
            // Only its end matters here.
        }
        TryAgainCommand.NotifyCanExecuteChanged();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        foreach (var card in Cards)
            card.IsEnabled = !busy;
        ChooseFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsBusy));
    }

    private void RebuildCards()
    {
        var current = _host.Settings.BackupFolder;

        var wanted = _detected
            .Select(cloud => new BackupFolderCard(cloud.DisplayName, CloudFolderDetector.BackupFolderFor(cloud), cloud.DisplayName, _selectOnly))
            .ToList();

        // The folder in use is always shown, even when it isn't (or is no longer) a detected cloud folder.
        if (current is not null && !wanted.Any(c => BackupLocation.SameFolder(c.Folder, current)))
        {
            var provider = _host.Settings.BackupProviderName ?? BackupLocation.ProviderFor(current, _detected);
            wanted.Add(new BackupFolderCard(provider ?? CustomFolderName, current, provider, _selectOnly));
        }

        // Only replace the cards when they changed, so re-detecting on activation doesn't take the keyboard focus away.
        var same = wanted.Count == Cards.Count
                   && wanted.Zip(Cards).All(p => p.First.Name == p.Second.Name && p.First.Folder == p.Second.Folder);
        if (!same)
        {
            Cards.Clear();
            foreach (var card in wanted) Cards.Add(card);
        }

        foreach (var card in Cards)
            card.IsInUse = BackupLocation.SameFolder(card.Folder, current);

        if (_selectOnly)
        {
            // The folder in use starts selected, else the first detected one (once detection has answered).
            if (!_preselected && (current is not null || _detectedOnce))
            {
                _preselected = true;
                _selectedFolder = current ?? Cards.FirstOrDefault()?.Folder;
            }
            Select(_selectedFolder);
        }

        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(LastBackupText));
    }

    /// <summary>True when the folder is on the same drive as the library itself (so it's no help if the PC fails).</summary>
    private static bool IsOnSystemDrive(string folder)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.Equals(Path.GetPathRoot(folder), Path.GetPathRoot(appData), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One card: provider name, the folder backups go to (middle-shortened), and its state.</summary>
    /// <param name="selectOnly">The wizard's mode: the highlight shows the selection rather than the folder in use.</param>
    public sealed class BackupFolderCard(string name, string folder, string? providerName, bool selectOnly = false)
        : ObservableObject
    {
        private bool _isInUse;
        private bool _isSelected;
        private bool _isBackingUp;
        private bool _isEnabled = true;

        public string Name { get; } = name;
        public string Folder { get; } = folder;
        public string? ProviderName { get; } = providerName;
        public string FolderText { get; } = BackupLocation.ShortenMiddle(folder, PathDisplayLength);
        public string AutomationName
            => $"Back up to {Name}, {Folder}" + (IsHighlighted ? $", {BadgeText.ToLowerInvariant()}" : "");

        /// <summary>Backups go here now: BrandSoft fill, Brand edge and "✓ In use" (outside select-only mode).</summary>
        public bool IsInUse
        {
            get => _isInUse;
            set
            {
                if (SetProperty(ref _isInUse, value))
                    OnStateChanged();
            }
        }

        /// <summary>Select-only mode: the wizard's Next backs up here.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    OnStateChanged();
            }
        }

        /// <summary>Shows the highlight (BrandSoft fill, Brand edge, ✓ and <see cref="BadgeText"/>).</summary>
        public bool IsHighlighted => selectOnly ? _isSelected : _isInUse;

        /// <summary><c>In use</c>, or <c>Selected</c> for a selected card that backups don't go to yet.</summary>
        public string BadgeText => _isInUse ? "In use" : "Selected";

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsHighlighted));
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(AutomationName));
        }

        /// <summary>A backup to this card's folder is running ("Backing up…").</summary>
        public bool IsBackingUp
        {
            get => _isBackingUp;
            set => SetProperty(ref _isBackingUp, value);
        }

        /// <summary>False while a backup to any folder is running.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }
}
