using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The library the main window is showing and where its changes are saved: your prompts
/// (<see cref="PersonalDocument"/>) or the team's (<see cref="TeamDocument"/>).
/// </summary>
public interface ILibraryDocument
{
    /// <summary>The categories and prompts shown (the object can be replaced, e.g. by a restore or a refresh).</summary>
    Library Library { get; }

    /// <summary>True for the team library.</summary>
    bool IsTeam { get; }

    /// <summary>True when nothing in it can be changed from this window.</summary>
    bool IsReadOnly { get; }

    /// <summary>Saves after a prompt's Save. False when it couldn't be written (the editor keeps its changes).</summary>
    bool SaveLibrary();

    /// <summary>Saves after a change with no Save button to retry (category changes, deletions).</summary>
    bool SaveLibraryChange();

    /// <summary>True while the in-memory library has changes the file doesn't (the last write failed).</summary>
    bool LibraryUnwritten { get; }
}
