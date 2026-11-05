using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Team;

/// <summary>Whether the team library is current on this PC.</summary>
public enum TeamStatus
{
    /// <summary>The last read of the source worked.</summary>
    Connected,

    /// <summary>A source is set but the last read failed; the last good (offline) copy is shown.</summary>
    Offline,

    /// <summary>No team library is joined.</summary>
    NotConnected,
}

/// <summary>The outcome of <see cref="TeamLibraryService.JoinAsync"/>; <see cref="Message"/> says why it failed.</summary>
public sealed record TeamJoinResult(bool Success, string? Message);

/// <summary>
/// Owns the team library on this PC: joins, leaves and starts one, keeps the offline copy, refreshes it at start,
/// hourly, on a file-change notice and on request, and makes guarded saves for editors. Reads never block the UI
/// thread (except the short local-file re-read after a refused save). Lives on the UI thread; <see cref="Changed"/>
/// is raised there. No background failure escapes: each is logged and the last good copy stays.
/// </summary>
public sealed class TeamLibraryService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);
    private const string InvalidInputMessage = "That isn't a folder, file or https link.";

    private readonly TeamReader _reader;
    private readonly string _cachePath;
    private readonly AppSettings _settings;
    private readonly Func<Library> _personal;
    private readonly Func<bool> _saveSettings;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Exception> _log;
    private readonly TeamWriter _writer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _hourly;
    private readonly DispatcherTimer _debounce;
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;
    private string? _json;
    private bool _held;
    private bool _deferred;
    private bool _disposed;

    public TeamLibraryService(HttpClient http, string appVersion, string cachePath, AppSettings settings,
        Func<Library> personal, Func<bool> saveSettings, Func<DateTimeOffset> now, Action<Exception> log,
        TimeSpan? debounce = null)
    {
        _reader = new TeamReader(http, appVersion);
        _cachePath = cachePath;
        _settings = settings;
        _personal = personal;
        _saveSettings = saveSettings;
        _now = now;
        _log = log;
        _writer = new TeamWriter(now);

        _hourly = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = RefreshInterval };
        _hourly.Tick += (_, _) => _ = RequestRefreshAsync();
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = debounce ?? DefaultDebounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RequestRefreshAsync();
        };
    }

    /// <summary>The loaded team library (the last good copy), or null when none is joined or nothing is loaded yet.</summary>
    public TeamLibrary? Team { get; private set; }

    /// <summary>Where the team library comes from, or null when none is joined.</summary>
    public TeamSource? Source { get; private set; }

    /// <summary>Hash of the file content <see cref="Team"/> was loaded from; the guard for <see cref="Save"/>.</summary>
    public string? CurrentHash { get; private set; }

    /// <summary>When the team library itself was last updated (from its file), or null when not loaded.</summary>
    public DateTimeOffset? LastUpdated => Team?.UpdatedAt;

    /// <summary>When the source was last read successfully, or null.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    public TeamStatus Status { get; private set; } = TeamStatus.NotConnected;

    /// <summary>Why the last read failed, while <see cref="Status"/> is <see cref="TeamStatus.Offline"/>.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>True when this PC can edit: a file source it can write to, loaded, and not made by a newer app.</summary>
    public bool CanEdit { get; private set; }

    /// <summary>Other <c>team-prompts*.json</c> files next to the team file (sync conflict copies).</summary>
    public IReadOnlyList<string> ConflictCopies { get; private set; } = [];

    /// <summary>Raised on the UI thread when anything the team UI shows may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Loads the offline copy at once (status Offline until a read works), starts the hourly timer and the file
    /// watcher, and refreshes in the background.
    /// </summary>
    public void Start()
    {
        try
        {
            if (_settings.TeamSource is { Length: > 0 } saved && TeamSource.FromInput(saved) is { } source)
            {
                Source = source;
                Status = TeamStatus.Offline;
                if (TeamCache.Load(_cachePath) is { } cached && cached.Source == source)
                {
                    Team = cached.Team;
                    _json = cached.Json;
                    CurrentHash = TeamFile.Hash(cached.Json);
                    LastChecked = cached.FetchedAt;
                }
                UpdateEditing();
                StartWatching();
                RaiseChanged();
            }
            _hourly.Start();
        }
        catch (Exception ex)
        {
            _log(ex);
        }

        if (Source is not null) _ = RequestRefreshAsync();
    }

    /// <summary>Reads the source now (Retry). Never throws. Ignores a hold.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            if (Source is not { } source) return;
            var result = await _reader.ReadAsync(source, CancellationToken.None);
            Apply(source, result);
        }
        catch (Exception ex)
        {
            _log(ex);
        }
    }

    /// <summary>An automatic refresh (timer or file watcher): deferred while held, otherwise as <see cref="RefreshAsync"/>.</summary>
    public Task RequestRefreshAsync()
    {
        if (_held)
        {
            _deferred = true;
            return Task.CompletedTask;
        }
        return RefreshAsync();
    }

    /// <summary>Holds automatic refreshes while the team library is being edited; releasing runs one if any were deferred.</summary>
    public void HoldRefresh(bool hold)
    {
        _held = hold;
        if (hold || !_deferred) return;
        _deferred = false;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Joins a team library. The source is saved only after a successful first read of a valid team file. Joining a
    /// different source first clears the old library's pins, hides, markers and offline copy.
    /// </summary>
    public async Task<TeamJoinResult> JoinAsync(string input)
    {
        try
        {
            if (TeamSource.FromInput(input) is not { } source) return new TeamJoinResult(false, InvalidInputMessage);

            var result = await _reader.ReadAsync(source, CancellationToken.None);
            if (result.Status != TeamReadStatus.Ok || result.Team is null || result.Json is null)
                return new TeamJoinResult(false, result.Message ?? "Can't read that team library.");

            if (Source is not null && Source != source)
            {
                var old = Team?.Content ?? new Library();
                TeamUserState.Prune(_settings, _personal(), old);
                TeamUserState.ClearTeam(_settings, old);
                TeamCache.Delete(_cachePath);
                Team = null;
                _json = null;
                CurrentHash = null;
            }

            _settings.TeamSource = source.Location;
            Source = source;
            StartWatching();
            Apply(source, result); // caches, takes the baseline, prunes, saves settings and raises Changed
            return new TeamJoinResult(true, null);
        }
        catch (Exception ex)
        {
            _log(ex);
            return new TeamJoinResult(false, "Couldn't join that team library.");
        }
    }

    /// <summary>Leaves the team library: removes the source, offline copy, team pins, hides and markers.</summary>
    public void Leave()
    {
        try
        {
            StopWatching();
            var team = Team?.Content ?? new Library();
            TeamUserState.Prune(_settings, _personal(), team);
            TeamUserState.ClearTeam(_settings, team);
            _settings.TeamSource = null;
            TeamCache.Delete(_cachePath);
            lock (_sync)
            {
                Team = null;
                Source = null;
                _json = null;
                CurrentHash = null;
                LastChecked = null;
                StatusMessage = null;
                Status = TeamStatus.NotConnected;
                CanEdit = false;
                ConflictCopies = [];
            }
            _saveSettings();
        }
        catch (Exception ex)
        {
            _log(ex);
        }
        RaiseChanged();
    }

    /// <summary>
    /// Starts a team library in <paramref name="folder"/> and, when it was created, joins it. When the folder already
    /// has one, nothing is joined and the result carries its name so the caller can offer to join it.
    /// </summary>
    public TeamCreateResult Start(string folder, string name, IReadOnlyCollection<Guid> seedCategoryIds,
        bool addBusinessExamples = false)
    {
        var created = _writer.Create(folder, name, _personal(), seedCategoryIds, addBusinessExamples);
        if (created.Created)
        {
            var joined = Task.Run(() => JoinAsync(created.FilePath)).GetAwaiter().GetResult();
            if (!joined.Success) return created with { Created = false, Message = joined.Message };
        }
        return created;
    }

    /// <summary>
    /// Saves an edited team library over the shared file, only if the file is unchanged since it was loaded. When
    /// someone else changed it, it is re-read at once (even while refreshes are held) and the refusal is returned.
    /// </summary>
    public TeamSaveResult Save(TeamLibrary edited)
    {
        if (Source is not { Kind: TeamSourceKind.File } source)
            return new TeamSaveResult(TeamSaveStatus.Failed, null, "The team library can't be saved here.");
        try
        {
            var result = _writer.Save(source.Location, edited, CurrentHash);
            if (result.Status == TeamSaveStatus.Saved)
            {
                AfterOwnSave(source, edited, result.NewHash);
            }
            else if (result.Status == TeamSaveStatus.ChangedByOther)
            {
                var read = Task.Run(() => _reader.ReadAsync(source, CancellationToken.None)).GetAwaiter().GetResult();
                Apply(source, read);
            }
            return result;
        }
        catch (Exception ex)
        {
            _log(ex);
            return new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't save the team library.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hourly.Stop();
        _debounce.Stop();
        StopWatching();
    }

    private void AfterOwnSave(TeamSource source, TeamLibrary edited, string? newHash)
    {
        // Compare with the copy before the save (re-parsed, so in-place edits of Team are seen).
        var before = _json is null ? null : TeamFile.Parse(_json).Content.Prompts.ToDictionary(p => p.Id, TeamUserState.Fingerprint);
        foreach (var p in edited.Content.Prompts)
            if (before is null || !before.TryGetValue(p.Id, out var fp) || fp != TeamUserState.Fingerprint(p))
                TeamUserState.MarkSeen(_settings, p);

        string? json = null;
        try { json = File.ReadAllText(source.Location); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (json is null || TeamFile.Hash(json) != newHash) json = TeamFile.Serialize(edited);

        lock (_sync)
        {
            Team = edited;
            _json = json;
            CurrentHash = newHash;
            LastChecked = _now();
            Status = TeamStatus.Connected;
            StatusMessage = null;
            try { TeamCache.Save(_cachePath, source, json, _now()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            TeamUserState.Prune(_settings, _personal(), edited.Content);
            UpdateEditing();
        }
        _saveSettings();
        RaiseChanged();
    }

    // Applies one read result (ignored if the source has changed meanwhile). A failed read keeps the last good copy.
    private void Apply(TeamSource source, TeamReadResult result)
    {
        bool raise;
        lock (_sync)
        {
            if (Source != source) return;
            var before = (Status, CurrentHash, StatusMessage, CanEdit, string.Join('|', ConflictCopies));
            if (result.Status == TeamReadStatus.Ok && result.Team is not null && result.Json is not null)
            {
                TeamRefresh.Apply(result, _cachePath, source, _now());
                Team = result.Team;
                _json = result.Json;
                CurrentHash = result.Hash;
                LastChecked = _now();
                Status = TeamStatus.Connected;
                StatusMessage = null;

                var settingsChanged = false;
                if (!_settings.TeamBaselineTaken)
                {
                    TeamUserState.TakeBaseline(_settings, result.Team.Content);
                    settingsChanged = true;
                }
                settingsChanged |= TeamUserState.Prune(_settings, _personal(), result.Team.Content);
                if (settingsChanged || before.CurrentHash != result.Hash || _settings.TeamSource != source.Location)
                {
                    _settings.TeamSource = source.Location;
                    settingsChanged = true;
                }
                if (settingsChanged) _saveSettings();
            }
            else
            {
                Status = TeamStatus.Offline;
                StatusMessage = result.Message ?? "Can't reach the team library.";
            }

            UpdateEditing();
            var after = (Status, CurrentHash, StatusMessage, CanEdit, string.Join('|', ConflictCopies));
            raise = before != after;
        }
        if (raise) RaiseChanged();
    }

    private void UpdateEditing()
    {
        if (Source is { Kind: TeamSourceKind.File } source)
        {
            CanEdit = Team is not null && !Team.IsNewerSchema && TeamWriter.CanWrite(source.Location);
            ConflictCopies = TeamWriter.FindConflictCopies(source.Location);
        }
        else
        {
            CanEdit = false;
            ConflictCopies = [];
        }
    }

    private void RaiseChanged()
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) Notify();
        else _dispatcher.BeginInvoke(Notify);
    }

    private void Notify()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _log(ex); }
    }

    private void StartWatching()
    {
        StopWatching();
        try
        {
            if (Source is not { Kind: TeamSourceKind.File } source) return;
            var folder = Path.GetDirectoryName(source.Location);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            var watcher = new FileSystemWatcher(folder, "team-prompts*.json");
            FileSystemEventHandler onChange = (_, _) => ScheduleRefresh();
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += (_, _) => ScheduleRefresh();
            watcher.Error += (_, _) => { }; // the hourly refresh still runs
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception)
        {
            StopWatching(); // fall back to the hourly refresh
        }
    }

    private void StopWatching()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null) return;
        try { watcher.Dispose(); } catch (Exception) { }
    }

    // Called on a watcher thread: restarts the debounce on the UI thread.
    private void ScheduleRefresh()
    {
        try
        {
            if (_disposed) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                _debounce.Stop();
                _debounce.Start();
            });
        }
        catch (Exception)
        {
            // Dispatcher shut down; nothing to do.
        }
    }
}
