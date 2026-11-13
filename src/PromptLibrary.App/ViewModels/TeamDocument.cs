using PromptLibrary.App.Team;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// Team prompts: the loaded team library. Read only unless <see cref="IsEditing"/> is on (Edit team library); a save
/// while editing goes to <see cref="TeamLibraryService.SaveAsync"/>, guarded by <see cref="BaseHash"/>. A team save
/// never blocks: <see cref="SaveLibrary"/> only starts it, and <see cref="PendingSave"/> says how it went.
/// </summary>
public sealed class TeamDocument(TeamLibraryService service) : ILibraryDocument
{
    // Shown while no team library is loaded, so callers never see null.
    private readonly Library _empty = new();
    private bool _isEditing;

    public TeamLibraryService Service => service;

    /// <summary>The team library's own name, e.g. <c>Acme best practice</c>; empty when none is loaded.</summary>
    public string Name => service.Team?.Name ?? "";

    public Library Library => service.Team?.Content ?? _empty;

    public bool IsTeam => true;

    /// <summary>
    /// True while the team library is being edited. Turning it on takes <see cref="BaseHash"/>, the version saves
    /// must still find on disk.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (value && !_isEditing) BaseHash = service.CurrentHash;
            _isEditing = value;
        }
    }

    public bool IsReadOnly => !_isEditing;

    /// <summary>
    /// The <see cref="TeamLibraryService.CurrentHash"/> of the copy being edited. Every change is saved at once, so
    /// when the loaded copy is replaced (an own save, or a reload after a refused save) <see cref="Rebase"/> moves it on.
    /// </summary>
    public string? BaseHash { get; private set; }

    /// <summary>The save started by the last <see cref="SaveLibrary"/> or <see cref="SaveLibraryChange"/>; null when none was started.</summary>
    public Task<TeamSaveResult>? PendingSave { get; private set; }

    /// <summary>True while <see cref="PendingSave"/> is still writing.</summary>
    public bool IsSaving => PendingSave is { IsCompleted: false };

    /// <summary>
    /// While editing, starts the guarded save (<see cref="PendingSave"/>). Always false: nothing is written yet when
    /// it returns, so the caller keeps the editor unsaved until <see cref="PendingSave"/> reports
    /// <see cref="TeamSaveStatus.Saved"/>. Nothing is started while read only.
    /// </summary>
    public bool SaveLibrary() => Start();

    /// <inheritdoc cref="SaveLibrary"/>
    public bool SaveLibraryChange() => Start();

    /// <summary>True while a save is writing; a team save is otherwise written or refused, never left half done.</summary>
    public bool LibraryUnwritten => IsSaving;

    /// <summary>The loaded copy was replaced (see <see cref="BaseHash"/>): saves now guard against its hash.</summary>
    public void Rebase()
    {
        if (_isEditing) BaseHash = service.CurrentHash;
    }

    /// <summary>
    /// Starts a guarded save of the loaded copy without edit mode (a prompt copied in from Your prompts): it must still
    /// find the version being edited (<see cref="BaseHash"/>) or, when not editing, the one loaded now.
    /// </summary>
    public void SaveWithoutEditing()
        => PendingSave = service.Team is { } team ? service.SaveAsync(team, _isEditing ? BaseHash : service.CurrentHash) : null;

    private bool Start()
    {
        PendingSave = _isEditing && service.Team is { } team ? service.SaveAsync(team, BaseHash) : null;
        return false;
    }
}
