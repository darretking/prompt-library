using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.App.Team;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// What Settings and the setup guide show about the team library: while none is joined the two entry buttons
/// (Join a team library, Start a team library); while connected only the connected state (name, source, when it was
/// last updated or why it can't be read with Retry, and Leave team library), so a library is never replaced silently:
/// you leave first. Follows the service's changes.
/// </summary>
public sealed class TeamConnectionViewModel : ObservableObject, IDisposable
{
    private readonly TeamLibraryService? _service;

    public TeamConnectionViewModel(TeamLibraryService? service)
    {
        _service = service;
        if (_service is not null) _service.Changed += OnServiceChanged;
        RetryCommand = new AsyncRelayCommand(() => _service?.RefreshAsync() ?? Task.CompletedTask);
    }

    /// <summary>False when the team feature failed to start; the section is then left out.</summary>
    public bool IsAvailable => _service is not null;

    public bool IsConnected => _service?.Source is not null;

    /// <summary>Join and Start show only while available and not connected.</summary>
    public bool ShowActions => IsAvailable && !IsConnected;

    /// <summary>The team's name, or a plain stand-in while it hasn't been read yet (offline with no copy).</summary>
    public string Name => _service?.Team?.Name is { Length: > 0 } name ? name : "this team library";

    /// <summary>Where it lives: the folder of a file source, or the link.</summary>
    public string SourceText
    {
        get
        {
            if (_service?.Source is not { } source) return "";
            return source.Kind == TeamSourceKind.File ? Path.GetDirectoryName(source.Location) ?? source.Location : source.Location;
        }
    }

    /// <summary><c>Updated today, 08:33</c>, or why the team library can't be read right now (then Retry shows).</summary>
    public string StatusText
    {
        get
        {
            if (_service is null) return "";
            if (IsOffline) return _service.StatusMessage ?? "Can't reach the team library.";
            return _service.LastUpdated is { } when
                ? $"Updated {BackupLocation.RelativeTimeText(when.LocalDateTime, DateTime.Now, midSentence: true)}"
                : "";
        }
    }

    /// <summary>Retry beside the status while the last read failed.</summary>
    public bool ShowRetry => IsConnected && IsOffline;

    /// <summary>Reads the team library again at once.</summary>
    public IAsyncRelayCommand RetryCommand { get; }

    private bool IsOffline => _service?.Status == TeamStatus.Offline;

    public string ConnectedLine => $"Connected to {Name}";

    /// <summary>The question asked before leaving.</summary>
    public string LeaveQuestion => $"Leave {Name}? Team prompts will disappear; your copies stay.";

    /// <summary>Leaves the team library (the caller has asked first).</summary>
    public void Leave() => _service?.Leave();

    public void Dispose()
    {
        if (_service is not null) _service.Changed -= OnServiceChanged;
    }

    private void OnServiceChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
