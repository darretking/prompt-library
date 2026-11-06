using PromptLibrary.App.Team;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// Team prompts: the loaded team library. Read only unless <see cref="IsEditing"/> is on (Edit team library); a save
/// while editing goes to <see cref="TeamLibraryService.SaveAsync"/>, guarded by the hash editing started from.
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

    /// <summary>The <see cref="TeamLibraryService.CurrentHash"/> editing started from.</summary>
    public string? BaseHash { get; private set; }

    /// <summary>The save started by the last <see cref="SaveLibrary"/> while editing (its result says whether it was refused).</summary>
    public Task<TeamSaveResult>? PendingSave { get; private set; }

    /// <summary>Nothing is written while read only (false). While editing, starts the guarded save and returns true.</summary>
    public bool SaveLibrary() => Save();

    public bool SaveLibraryChange() => Save();

    /// <summary>Always false: a team save is written or refused, never left half done in memory.</summary>
    public bool LibraryUnwritten => false;

    private bool Save()
    {
        if (!_isEditing || service.Team is not { } team) return false;
        PendingSave = service.SaveAsync(team, BaseHash);
        return true;
    }
}
