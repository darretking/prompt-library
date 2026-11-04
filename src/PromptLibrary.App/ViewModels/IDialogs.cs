using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>The questions <see cref="MainViewModel"/> asks the user; the main window shows them.</summary>
public interface IDialogs
{
    /// <summary>"Save changes?" — true = save, false = don't save, null = cancel.</summary>
    bool? AskSaveChanges();

    /// <summary>A yes/no question, e.g. <c>Delete "title"?</c>. True means yes.</summary>
    bool Confirm(string message);

    /// <summary>
    /// Asks where the prompts of a category that is being deleted should go.
    /// Returns the chosen category's id, or null if the user cancelled.
    /// </summary>
    Guid? ChooseMoveTarget(string categoryName, int promptCount, IReadOnlyList<Category> targets);

    void ShowMessage(string message);
}
