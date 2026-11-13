using System.Windows;
using System.Windows.Controls;
using PromptLibrary.Core.Editing;

namespace PromptLibrary.App.Views;

/// <summary>
/// Asks before Settings deletes every personal prompt: says how many prompts and categories go, that team prompts stay
/// and that a safety copy is made, and only enables "Delete all my prompts" once DELETE is typed (any case).
/// </summary>
public partial class DeleteAllDialog : Window
{
    public DeleteAllDialog(int promptCount, int categoryCount)
    {
        InitializeComponent();
        MessageText.Text = Message(promptCount, categoryCount);
        Loaded += (_, _) => ConfirmBox.Focus();
    }

    /// <summary><c>This deletes 39 prompts and 13 categories from Your prompts.</c></summary>
    internal static string Message(int prompts, int categories)
    {
        var p = prompts == 1 ? "1 prompt" : $"{prompts} prompts";
        var c = categories == 1 ? "1 category" : $"{categories} categories";
        var what = (prompts, categories) switch
        {
            (0, _) => c,
            (_, 0) => p,
            _ => $"{p} and {c}",
        };
        return $"This deletes {what} from Your prompts.";
    }

    /// <summary>True once the typed text confirms (also what enables the button).</summary>
    internal bool IsConfirmed => LibraryReset.IsConfirmed(ConfirmBox.Text);

    internal TextBox Box => ConfirmBox;

    internal Button DeleteActionButton => DeleteButton;

    private void ConfirmBox_TextChanged(object sender, TextChangedEventArgs e) => DeleteButton.IsEnabled = IsConfirmed;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!IsConfirmed) return; // Enter before the word is typed does nothing
        DialogResult = true;
    }
}
