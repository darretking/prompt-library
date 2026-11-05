using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>The questions <see cref="MainViewModel"/> asks the user; the main window shows them.</summary>
public interface IDialogs
{
    /// <summary><c>Save changes to "title"?</c> — true = Save, false = Don't save, null = Cancel.</summary>
    bool? AskSaveChanges(string title);

    /// <summary>
    /// A question about something that can't be undone, e.g. <c>Delete "title"?</c> with a red
    /// <paramref name="action"/> button (e.g. <c>Delete</c>) and Cancel. True means the action was chosen.
    /// </summary>
    bool ConfirmDestructive(string message, string action);

    /// <summary>
    /// Asks where the prompts of a category that is being deleted should go.
    /// Returns the chosen category's id, or null if the user cancelled.
    /// </summary>
    Guid? ChooseMoveTarget(string categoryName, int promptCount, IReadOnlyList<Category> targets);

    void ShowMessage(string message);
}
