using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.App.Team;

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

    /// <param name="join">Joins the typed input (the service's <c>JoinAsync</c>).</param>
    /// <param name="providerOf">The sync provider holding a path, or null; it may be slow, so it runs in the background.</param>
    public TeamJoinViewModel(Func<string, Task<TeamJoinResult>> join, Func<string, string?> providerOf)
    {
        _join = join;
        _providerOf = providerOf;
        JoinCommand = new AsyncRelayCommand(JoinAsync, () => CanJoin);
    }

    /// <summary>The library was joined; the dialog closes.</summary>
    public event EventHandler? Joined;

    public IAsyncRelayCommand JoinCommand { get; }

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
