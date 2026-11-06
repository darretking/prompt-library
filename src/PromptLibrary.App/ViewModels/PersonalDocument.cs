using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>Your prompts: the app's personal library, saved to library.json by the host.</summary>
public sealed class PersonalDocument(IMainHost host) : ILibraryDocument
{
    public Library Library => host.Library;

    public bool IsTeam => false;

    public bool IsReadOnly => false;

    public bool SaveLibrary() => host.SaveLibrary();

    public bool SaveLibraryChange() => host.SaveLibraryChange();

    public bool LibraryUnwritten => host.LibraryUnwritten;
}
