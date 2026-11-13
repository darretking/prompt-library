using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The Join a team library dialog: one box for a shared folder, file or link. Join reads it (the box and buttons
/// stay put, "Checking…" and a progress bar show); a failure shows inline in Danger text with the service's exact
/// copy, plus the provider hint for a folder that can't be reached. Nothing here touches files or the network on
/// the UI thread: both the join and the provider lookup run in the background.
/// </summary>
public sealed class TeamJoinViewModel : ObservableObject
{
    private readonly Func<string, Task<TeamJoinResult>> _join;
    private readonly Func<string, string?> _providerOf;
    private string _input = "";
    private bool _isChecking;
    private string? _error;
    private bool _isLooking;
    private CancellationTokenSource? _findCts;
    private readonly Func<CancellationToken, Task<IReadOnlyList<FoundTeamFile>>>? _find;
    private readonly TimeSpan _slowAfter;

    /// <param name="join">Joins the typed input (the service's <c>JoinAsync</c>).</param>
    /// <param name="providerOf">The sync provider holding a path, or null; it may be slow, so it runs in the background.</param>
    /// <param name="find">Looks for team files on this PC (cloud roots); null shows no list. Runs off the UI thread.</param>
    /// <param name="initialInput">A path or link to prefill (from the command line); the user still presses Join.</param>
    /// <param name="slowAfter">How long the search runs before "Looking for team libraries…" shows (500 ms).</param>
    public TeamJoinViewModel(Func<string, Task<TeamJoinResult>> join, Func<string, string?> providerOf,
        Func<CancellationToken, Task<IReadOnlyList<FoundTeamFile>>>? find = null, string? initialInput = null,
        TimeSpan? slowAfter = null)
    {
        _join = join;
        _providerOf = providerOf;
        _find = find;
        _slowAfter = slowAfter ?? TimeSpan.FromMilliseconds(500);
        _input = initialInput ?? "";
        PickFoundCommand = new RelayCommand<FoundTeamFile>(file =>
        {
            if (file is not null && CanEdit) Input = file.Path;
        });
        JoinCommand = new AsyncRelayCommand(JoinAsync, () => CanJoin);
    }

    /// <summary>The library was joined; the dialog closes.</summary>
    public event EventHandler? Joined;

    public IAsyncRelayCommand JoinCommand { get; }

    /// <summary>Team files found on this PC; the list shows only when it has one.</summary>
    public ObservableCollection<FoundTeamFile> Found { get; } = [];

    public bool HasFound => Found.Count > 0;

    /// <summary>The search has run longer than half a second; a short one shows nothing.</summary>
    public bool IsLooking
    {
        get => _isLooking;
        private set => SetProperty(ref _isLooking, value);
    }

    /// <summary>Clicking a found library fills the box.</summary>
    public IRelayCommand<FoundTeamFile> PickFoundCommand { get; }

    /// <summary>Runs the search once when the dialog opens. Never throws; a cancelled search adds nothing.</summary>
    public async Task FindAsync()
    {
        if (_find is null || _findCts is not null) return;
        var cts = _findCts = new CancellationTokenSource();
        var slow = ShowLookingAfterDelayAsync(cts.Token);
        try
        {
            var hits = await Task.Run(() => _find(cts.Token), CancellationToken.None);
            if (cts.IsCancellationRequested) return;
            foreach (var hit in hits) Found.Add(hit);
            OnPropertyChanged(nameof(HasFound));
        }
        catch (Exception)
        {
            // The list is a convenience: no list is fine.
        }
        finally
        {
            cts.Cancel();
            await slow;
            IsLooking = false;
        }
    }

    private async Task ShowLookingAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_slowAfter, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // The delay can finish just as the search does; a cancelled token means the search is over.
        if (token.IsCancellationRequested) return;
        IsLooking = true;
    }

    /// <summary>The dialog closed: stop looking.</summary>
    public void CancelFind() => _findCts?.Cancel();

    public string Input
    {
        get => _input;
        set
        {
            if (!SetProperty(ref _input, value)) return;
            Error = null;
            JoinCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!SetProperty(ref _isChecking, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            JoinCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The box and Browse… are locked while the read runs.</summary>
    public bool CanEdit => !_isChecking;

    public bool CanJoin => !_isChecking && !string.IsNullOrWhiteSpace(_input);

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    /// <summary>Runs the join; on success raises <see cref="Joined"/>, otherwise sets <see cref="Error"/>.</summary>
    public async Task JoinAsync()
    {
        if (!CanJoin) return;
        var input = _input.Trim().Trim('"');
        Error = null;
        IsChecking = true;
        try
        {
            var result = await _join(input);
            if (result.Success)
            {
                Joined?.Invoke(this, EventArgs.Empty);
                return;
            }

            var message = result.Message ?? "Couldn't join that team library.";
            var provider = message == TeamDialogText.CantReachFolder ? await Task.Run(() => _providerOf(input)) : null;
            Error = TeamDialogText.Describe(message, provider);
        }
        catch (Exception)
        {
            Error = "Couldn't join that team library.";
        }
        finally
        {
            IsChecking = false;
        }
    }
}
