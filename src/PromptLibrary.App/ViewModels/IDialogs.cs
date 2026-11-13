using PromptLibrary.App.Team;
using PromptLibrary.Core.Exchange;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// What the user chose when deleting a category that has prompts: move them to <see cref="MoveTo"/>, or delete them
/// with the category (<see cref="DeletePrompts"/>, then <see cref="MoveTo"/> is null).
/// </summary>
public sealed record CategoryDeletion(Guid? MoveTo, bool DeletePrompts);

/// <summary>Where an import reads from, or an export writes to: a file, or a folder.</summary>
public sealed record ExchangePick(string Path, bool IsFolder);

/// <summary>The questions <see cref="MainViewModel"/> asks the user; the main window shows them.</summary>
public interface IDialogs
{
    /// <summary><paramref name="question"/> (e.g. <c>Save changes to "title"?</c>) — true = Save, false = Don't save, null = Cancel.</summary>
    bool? AskSaveChanges(string question);

    /// <summary>
    /// A question about something that can't be undone, e.g. <c>Delete "title"?</c> with a red
    /// <paramref name="action"/> button (e.g. <c>Delete</c>) and Cancel. True means the action was chosen.
    /// </summary>
    bool ConfirmDestructive(string message, string action);

    /// <summary>
    /// Asks what to do with the prompts of a category that is being deleted: move them to another category (one of
    /// <paramref name="targets"/>, which may be empty) or delete them too. Null if the user cancelled.
    /// </summary>
    CategoryDeletion? ChooseCategoryDeletion(string categoryName, int promptCount, IReadOnlyList<Category> targets) => null;

    /// <summary>Asks for the file or folder to import prompts from (file / folder choice, then the Windows picker); null on cancel.</summary>
    ExchangePick? ChooseImportSource() => null;

    /// <summary>Asks for the file or folder to export prompts to (format choice, then the Windows picker); null on cancel.</summary>
    ExchangePick? ChooseExportTarget() => null;

    void ShowMessage(string message);

    /// <summary>Says what an import did: counts and, for files that failed, a list of file and reason.</summary>
    void ShowImportResults(ImportResult result, bool intoTeam = false) => ShowMessage(result.Summary);

    /// <summary>
    /// Restore an older version of the team library (or, with <paramref name="review"/>, review the other versions sync
    /// left beside it). True when a version was restored.
    /// </summary>
    bool ShowTeamHistory(TeamLibraryService service, bool review);
}
