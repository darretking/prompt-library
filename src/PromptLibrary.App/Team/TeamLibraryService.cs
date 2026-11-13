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
/// hourly, on a file-change notice and on request, and makes guarded saves for editors. All file and network access
/// runs in background tasks; results, settings changes and <see cref="Changed"/> happen on the UI thread only. No
/// background failure escapes: each is logged and the last good copy stays.
/// </summary>
public sealed class TeamLibraryService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);
    private const string InvalidInputMessage = "That isn't a folder, file or https link.";

    /// <summary>What users see when a write to the shared folder fails; the raw detail goes to the log only.</summary>
    internal const string WriteFailedMessage = "Couldn't save the team library. Check that you can save files in that folder.";
    internal const string StartFailedMessage = "Couldn't start the team library. Check that you can save files in that folder.";
    internal const string UnreadableExistingMessage = "This folder already has a team file that can't be read.";

    // Turns typed input into a source; it touches the file system, so it only runs off the UI thread.
    private readonly Func<string, TeamSource?> _parse;
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

    private FileSystemWatcher? _watcher;
    private string? _json;
    private bool _held;
    private bool _deferred;
    private bool _disposed;

    // Bumped by every read and every change of the loaded copy; only the newest read may apply.
    private int _generation;

    public TeamLibraryService(HttpClient http, string appVersion, string cachePath, AppSettings settings,
        Func<Library> personal, Func<bool> saveSettings, Func<DateTimeOffset> now, Action<Exception> log,
        TimeSpan? debounce = null, Func<string, TeamSource?>? parseInput = null)
    {
        _parse = parseInput ?? (input => TeamSource.FromInput(input));
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

    /// <summary>Hash of the file content <see cref="Team"/> was loaded from; the editor captures it when editing starts and passes it to <see cref="SaveAsync"/>.</summary>
    public string? CurrentHash { get; private set; }

    /// <summary>When the team library itself was last updated (from its file), or null when not loaded.</summary>
    public DateTimeOffset? LastUpdated => Team?.UpdatedAt;

    /// <summary>When the source was last read successfully, or null.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    public TeamStatus Status { get; private set; } = TeamStatus.NotConnected;

    /// <summary>Why the last read failed, while <see cref="Status"/> is <see cref="TeamStatus.Offline"/>.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>How the last read that was applied went (null before one is back): the offline header says why from it.</summary>
    public TeamReadStatus? LastReadStatus { get; private set; }

    /// <summary>True when this PC can edit: a file source it can write to, loaded, and not made by a newer app.</summary>
    public bool CanEdit { get; private set; }

    /// <summary>True when the write probe passed: a file source this PC can open for writing (whatever its schema).</summary>
    public bool CanWrite { get; private set; }

    /// <summary>True while an automatic refresh was asked for during a hold; releasing the hold runs it.</summary>
    public bool HasHeldRefresh => _deferred;

    /// <summary>Other <c>team-prompts*.json</c> files next to the team file (sync conflict copies).</summary>
    public IReadOnlyList<string> ConflictCopies { get; private set; } = [];

    /// <summary>Raised on the UI thread when anything the team UI shows may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Loads the offline copy at once (status Offline until a read works), starts the hourly timer and refreshes in
    /// the background. The file watcher starts with that first read, once the folder is known to exist.
    /// </summary>
    public void Start()
    {
        try
        {
            if (_settings.TeamSource is { Length: > 0 } saved && saved.StartsWith("https://", StringComparison.OrdinalIgnoreCase) is var isUrl)
            {
                // The saved value is already normalised, so no file-system call (an unreachable share would hang startup).
                var source = new TeamSource(isUrl ? TeamSourceKind.Url : TeamSourceKind.File, saved);
                Source = source;
                Status = TeamStatus.Offline;
                if (TeamCache.Load(_cachePath) is { } cached && cached.Source == source)
                {
                    Team = WithoutClashes(cached.Team);
                    _json = cached.Json;
                    CurrentHash = TeamFile.Hash(cached.Json);
                    LastChecked = cached.FetchedAt;
                }
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

    /// <summary>Reads the source now (Retry, or a reload the user is waiting for). Never throws. Ignores a hold.</summary>
    public Task RefreshAsync() => RunRefreshAsync(force: true);

    /// <summary>An automatic refresh (timer or file watcher): deferred while held, otherwise a normal read.</summary>
    public Task RequestRefreshAsync()
    {
        if (_held)
        {
            _deferred = true;
            return Task.CompletedTask;
        }
        return RunRefreshAsync(force: false);
    }

    /// <summary>
    /// Holds automatic refreshes while the team library is being edited (a read still in flight then only records
    /// that a refresh is owed); releasing runs one if any were deferred.
    /// </summary>
    public void HoldRefresh(bool hold)
    {
        _held = hold;
        if (hold || !_deferred) return;
        _deferred = false;
        _ = RunRefreshAsync(force: false);
    }

    /// <summary>
    /// Joins a team library. The source is saved only after a successful first read of a valid team file. Joining a
    /// different source first clears the old library's pins, hides, markers and offline copy.
    /// </summary>
    public async Task<TeamJoinResult> JoinAsync(string input)
    {
        try
        {
            if (await Task.Run(() => _parse(input)) is not { } source) return new TeamJoinResult(false, InvalidInputMessage);

            var snap = await ReadSnapshotAsync(source, needWatcher: true);
            return await OnUi(() => ApplyJoin(source, snap));
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
            _generation++; // reads still in flight are discarded
            StopWatching();
            var team = Team?.Content ?? new Library();
            TeamUserState.Prune(_settings, _personal(), team);
            TeamUserState.ClearTeam(_settings, team);
            _settings.TeamSource = null;
            TeamCache.Delete(_cachePath);
            Team = null;
            Source = null;
            _json = null;
            CurrentHash = null;
            LastChecked = null;
            StatusMessage = null;
            LastReadStatus = null;
            Status = TeamStatus.NotConnected;
            CanEdit = false;
            CanWrite = false;
            ConflictCopies = [];
            _deferred = false;
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
    public async Task<TeamCreateResult> StartAsync(string folder, string name, IReadOnlyCollection<Guid> seedCategoryIds,
        bool addBusinessExamples = false)
    {
        try
        {
            var personal = _personal();
            var created = await Task.Run(() => _writer.Create(folder, name, personal, seedCategoryIds, addBusinessExamples));
            if (!created.Created)
            {
                // A refusal because the folder already has a library is plain copy (said plainly also when that file
                // can't be read); any other failure carries raw OS text.
                if (created.ExistingName is not null) return created;
                if (created.AlreadyExists) return created with { Message = UnreadableExistingMessage };
                _log(new IOException(created.Message ?? "Start failed"));
                return created with { Message = StartFailedMessage };
            }
            var joined = await JoinAsync(created.FilePath);
            return joined.Success ? created : created with { Created = false, Message = joined.Message };
        }
        catch (Exception ex)
        {
            _log(ex);
            return new TeamCreateResult(false, Path.Combine(folder, TeamFile.FileName), null, "Couldn't start the team library.");
        }
    }

    /// <summary>
    /// Saves an edited team library over the shared file, only if the file still has <paramref name="baseHash"/>, the
    /// <see cref="CurrentHash"/> the editor started editing from (so a refresh that landed during the edit can't
    /// hide someone else's change). When it differs, the library is re-read at once (even while refreshes are held)
    /// and the refusal is returned.
    /// </summary>
    public async Task<TeamSaveResult> SaveAsync(TeamLibrary edited, string? baseHash)
    {
        try
        {
            if (Source is not { Kind: TeamSourceKind.File } source)
                return new TeamSaveResult(TeamSaveStatus.Failed, null, "The team library can't be saved here.");

            var (result, json) = await Task.Run(() =>
            {
                var saved = _writer.Save(source.Location, edited, baseHash);
                string? text = null;
                if (saved.Status == TeamSaveStatus.Saved)
                {
                    try { text = File.ReadAllText(source.Location); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return (saved, text);
            });
            result = ScrubFailure(result);

            if (result.Status == TeamSaveStatus.Saved)
                await OnUi(() => AfterOwnSave(source, edited, json, result.NewHash));
            else if (result.Status == TeamSaveStatus.ChangedByOther)
                await RunRefreshAsync(force: true);
            return result;
        }
        catch (Exception ex)
        {
            _log(ex);
            return new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't save the team library.");
        }
    }

    /// <summary>
    /// Puts the loaded copy back after a save failed, so changes that weren't written don't stay in
    /// <see cref="Team"/> (where a later save, the shortcut menu or the window would pick them up).
    /// </summary>
    public void RevertToLoaded()
    {
        if (_json is null) return;
        try
        {
            Team = WithoutClashes(TeamFile.Parse(_json));
        }
        catch (InvalidDataException ex)
        {
            _log(ex);
            return;
        }
        RaiseChanged();
    }

    /// <summary>
    /// The versions an editor can restore (see <see cref="TeamWriter.ListHistory"/>): sync conflict copies first, then
    /// the history copies, newest first. Read in the background; empty for a link source or on any failure.
    /// </summary>
    public async Task<IReadOnlyList<TeamHistoryItem>> ListHistoryAsync()
    {
        try
        {
            if (Source is not { Kind: TeamSourceKind.File } source) return [];
            return await Task.Run(() => _writer.ListHistory(source.Location));
        }
        catch (Exception ex)
        {
            _log(ex);
            return [];
        }
    }

    /// <summary>
    /// Writes <paramref name="item"/> back over the team file (<see cref="TeamWriter.Restore"/>: the current version
    /// gets a history copy first), guarded by <see cref="CurrentHash"/> like <see cref="SaveAsync"/>. A restore is the
    /// editor's own save, so it marks nothing New or Updated on this PC. Restoring a sync conflict copy resolves the
    /// conflict: the copies then move into the history folder, as with <see cref="KeepCurrentVersionAsync"/>. When the
    /// file changed meanwhile, the library is re-read and the refusal is returned.
    /// </summary>
    public async Task<TeamSaveResult> RestoreAsync(TeamHistoryItem item)
    {
        try
        {
            if (Source is not { Kind: TeamSourceKind.File } source)
                return new TeamSaveResult(TeamSaveStatus.Failed, null, "The team library can't be saved here.");

            var expected = CurrentHash;
            var (result, json, restored) = await Task.Run(() =>
            {
                var saved = _writer.Restore(source.Location, item, expected);
                string? text = null;
                TeamLibrary? team = null;
                if (saved.Status == TeamSaveStatus.Saved)
                {
                    if (item.IsConflictCopy) _writer.MoveConflictCopiesToHistory(source.Location);
                    try
                    {
                        text = File.ReadAllText(source.Location);
                        team = TeamFile.Parse(text);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
                }
                return (saved, text, team);
            });
            result = ScrubFailure(result);

            if (result.Status == TeamSaveStatus.Saved)
            {
                if (restored is not null) await OnUi(() => AfterOwnSave(source, restored, json, result.NewHash));
                // Read again when the restored file couldn't be, or when the conflict copies have gone (the notice clears).
                if (restored is null || item.IsConflictCopy) await RunRefreshAsync(force: true);
            }
            else if (result.Status == TeamSaveStatus.ChangedByOther)
            {
                await RunRefreshAsync(force: true);
            }
            return result;
        }
        catch (Exception ex)
        {
            _log(ex);
            return new TeamSaveResult(TeamSaveStatus.Failed, null, "Couldn't restore the team library.");
        }
    }

    /// <summary>
    /// Review → Keep current version: the team file stays as it is, and the sync conflict copies move into its history
    /// folder (<see cref="TeamWriter.MoveConflictCopiesToHistory"/>; never deleted), so the notice clears. Then reads the
    /// file again. Never throws.
    /// </summary>
    public async Task KeepCurrentVersionAsync()
    {
        try
        {
            if (Source is not { Kind: TeamSourceKind.File } source) return;
            await Task.Run(() => _writer.MoveConflictCopiesToHistory(source.Location));
            await RunRefreshAsync(force: true);
        }
        catch (Exception ex)
        {
            _log(ex);
        }
    }

    // A failed write carries raw OS text ("Access to the path ... is denied"): it is logged, and the user gets plain copy.
    private TeamSaveResult ScrubFailure(TeamSaveResult result)
    {
        if (result.Status != TeamSaveStatus.Failed) return result;
        _log(new IOException(result.Message ?? "Save failed"));
        return result with { Message = WriteFailedMessage };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hourly.Stop();
        _debounce.Stop();
        StopWatching();
    }

    // What one background pass found: the read, whether the file is writable, the conflict copies, and (when asked
    // for) a watcher already created. All file-system access happens here, off the UI thread.
    private sealed record Snapshot(TeamReadResult Read, bool CanWrite, IReadOnlyList<string> Conflicts, FileSystemWatcher? Watcher);

    private Task<Snapshot> ReadSnapshotAsync(TeamSource source, bool needWatcher)
        => Task.Run(async () =>
        {
            var read = await _reader.ReadAsync(source, CancellationToken.None);
            if (source.Kind != TeamSourceKind.File) return new Snapshot(read, false, [], null);
            return new Snapshot(read, TeamWriter.CanWrite(source.Location), TeamWriter.FindConflictCopies(source.Location),
                needWatcher ? CreateWatcher(source) : null);
        });

    private async Task RunRefreshAsync(bool force)
    {
        try
        {
            if (Source is not { } source) return;
            var generation = ++_generation;
            var snap = await ReadSnapshotAsync(source, needWatcher: _watcher is null);
            await OnUi(() => Apply(source, snap, generation, force));
        }
        catch (Exception ex)
        {
            _log(ex);
        }
    }

    // Runs on the UI thread (directly when already there).
    private Task OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> OnUi<T>(Func<T> func)
    {
        if (_dispatcher.CheckAccess()) return Task.FromResult(func());
        return _dispatcher.InvokeAsync(func).Task;
    }

    private TeamJoinResult ApplyJoin(TeamSource source, Snapshot snap)
    {
        var result = snap.Read;
        if (result.Status != TeamReadStatus.Ok || result.Team is null || result.Json is null)
        {
            snap.Watcher?.Dispose();
            return new TeamJoinResult(false, result.Message ?? "Can't read that team library.");
        }

        var generation = ++_generation; // reads of the old source still in flight are discarded
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
        StopWatching();
        Apply(source, snap, generation, force: true); // caches, takes the baseline, prunes, saves settings, raises Changed
        return new TeamJoinResult(true, null);
    }

    private void AfterOwnSave(TeamSource source, TeamLibrary edited, string? json, string? newHash)
    {
        if (Source != source) return;
        _generation++; // an older read still in flight must not roll the saved copy back

        // Compare with the copy before the save (re-parsed, so in-place edits of Team are seen). Ids are unique after
        // WithoutClashes, but the indexer (not ToDictionary) makes sure a repeated one can never fail a save that was written.
        WithoutClashes(edited);
        Dictionary<Guid, string>? before = null;
        if (_json is not null)
        {
            before = new Dictionary<Guid, string>();
            foreach (var p in WithoutClashes(TeamFile.Parse(_json)).Content.Prompts) before[p.Id] = TeamUserState.Fingerprint(p);
        }
        foreach (var p in edited.Content.Prompts)
            if (before is null || !before.TryGetValue(p.Id, out var fp) || fp != TeamUserState.Fingerprint(p))
                TeamUserState.MarkSeen(_settings, p);

        if (json is null || TeamFile.Hash(json) != newHash) json = TeamFile.Serialize(edited);

        Team = edited;
        _json = json;
        CurrentHash = newHash;
        LastChecked = _now();
        Status = TeamStatus.Connected;
        StatusMessage = null;
        CanEdit = !edited.IsNewerSchema;
        CanWrite = true;
        try { TeamCache.Save(_cachePath, source, json, _now()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        TeamUserState.Prune(_settings, _personal(), edited.Content);
        _saveSettings();
        RaiseChanged();
    }

    // Applies one read result on the UI thread. Dropped when the source changed or a newer read has started; while
    // held (and not forced) it only records that a refresh is owed. A failed read keeps the last good copy.
    private void Apply(TeamSource source, Snapshot snap, int generation, bool force)
    {
        if (_disposed || Source != source || generation != _generation)
        {
            snap.Watcher?.Dispose();
            return;
        }

        if (snap.Watcher is not null)
        {
            if (_watcher is null) _watcher = snap.Watcher;
            else snap.Watcher.Dispose();
        }

        if (_held && !force)
        {
            _deferred = true;
            return;
        }

        var result = snap.Read;
        var before = (Status, CurrentHash, StatusMessage, LastReadStatus, CanEdit, CanWrite, string.Join('|', ConflictCopies));
        LastReadStatus = result.Status;
        if (result.Status == TeamReadStatus.Ok && result.Team is not null && result.Json is not null)
        {
            TeamRefresh.Apply(result, _cachePath, source, _now());
            Team = WithoutClashes(result.Team);
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
            if (_settings.TeamSource != source.Location)
            {
                _settings.TeamSource = source.Location;
                settingsChanged = true;
            }
            if (settingsChanged || before.CurrentHash != result.Hash) _saveSettings();
        }
        else
        {
            Status = TeamStatus.Offline;
            StatusMessage = result.Message ?? "Can't reach the team library.";
        }

        CanWrite = source.Kind == TeamSourceKind.File && snap.CanWrite;
        CanEdit = CanWrite && Team is not null && !Team.IsNewerSchema;
        ConflictCopies = snap.Conflicts;
        var after = (Status, CurrentHash, StatusMessage, LastReadStatus, CanEdit, CanWrite, string.Join('|', ConflictCopies));
        if (before != after) RaiseChanged();
    }

    // A team prompt or category whose id is also one of your own gets a new id (see TeamIds), so the shortcut menu,
    // pins and Leave never mix the two up. Runs on the UI thread, where the personal library lives.
    private TeamLibrary WithoutClashes(TeamLibrary team)
    {
        var personal = _personal();
        TeamIds.ResolveClashes(team.Content, personal.Prompts.Select(p => p.Id).ToHashSet(),
            personal.Categories.Select(c => c.Id).ToHashSet());
        return team;
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

    // Created off the UI thread (it touches the folder). Null when the folder isn't there or can't be watched; the
    // hourly refresh still runs, and a later read tries again.
    private FileSystemWatcher? CreateWatcher(TeamSource source)
    {
        try
        {
            var folder = Path.GetDirectoryName(source.Location);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

            var watcher = new FileSystemWatcher(folder, "team-prompts*.json");
            FileSystemEventHandler onChange = (_, _) => ScheduleRefresh();
            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += (_, _) => ScheduleRefresh();
            // Events can be lost (buffer overflow) or the watch can break (network drop): drop this watcher, so the
            // next successful read creates a new one, and read now in case a change was missed.
            watcher.Error += (_, _) => OnWatcherError(watcher);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Called on a watcher thread.
    private void OnWatcherError(FileSystemWatcher watcher)
    {
        try
        {
            if (_disposed) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || !ReferenceEquals(_watcher, watcher)) return;
                StopWatching();
                _debounce.Stop();
                _debounce.Start();
            });
        }
        catch (Exception)
        {
            // Dispatcher shut down; nothing to do.
        }
    }

    /// <summary>True while a file watcher is running (tests).</summary>
    internal bool IsWatching => _watcher is not null;

    /// <summary>Raises the running watcher's error handling, as a broken watch would (tests).</summary>
    internal void SimulateWatcherError()
    {
        if (_watcher is { } watcher) OnWatcherError(watcher);
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
