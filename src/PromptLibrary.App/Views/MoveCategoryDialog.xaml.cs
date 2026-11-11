using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views;

/// <summary>
/// Asked before deleting a category that still has prompts: move them to another category, or tick
/// <c>Also delete the N prompts in this category</c> and delete them with it (then the move-to choice is off and the
/// button says <c>Delete category and N prompts</c> in Danger style). Cancel leaves everything as it was.
/// </summary>
public partial class MoveCategoryDialog : Window
{
    private readonly int _promptCount;
    private readonly bool _canMove;

    public MoveCategoryDialog(string categoryName, int promptCount, IReadOnlyList<Category> targets)
    {
        InitializeComponent();
        _promptCount = promptCount;
        _canMove = targets.Count > 0;

        var prompts = Plural(promptCount);
        var them = promptCount == 1 ? "it" : "them";
        MessageText.Text = _canMove
            ? $"\"{categoryName}\" has {prompts}. Move {them} to another category, then \"{categoryName}\" is deleted."
            : $"\"{categoryName}\" has {prompts} and there is no other category to move {them} to. " +
              $"Create one first, or delete {them} with \"{categoryName}\".";
        TargetBox.ItemsSource = targets;
        TargetBox.SelectedIndex = _canMove ? 0 : -1; // never empty: the first other category is the default

        var label = $"Also delete the {prompts} in this category";
        DeleteBox.Content = label;
        AutomationProperties.SetName(DeleteBox, label);
        Update();

        Loaded += (_, _) => (_canMove ? (UIElement)TargetBox : DeleteBox).Focus();
    }

    private static string Plural(int count) => count == 1 ? "1 prompt" : $"{count} prompts";

    /// <summary>The chosen category, set when the dialog closes with "Move and delete"; null when the prompts are deleted.</summary>
    public Guid? SelectedCategoryId { get; private set; }

    /// <summary>True when the prompts are deleted with the category.</summary>
    public bool DeletePrompts { get; private set; }

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Update();

    private void DeleteBox_Changed(object sender, RoutedEventArgs e) => Update();

    /// <summary>True while "Move prompts to" and its label are shown.</summary>
    internal bool IsMoveChoiceShown => MovePanel.Visibility == Visibility.Visible;

    /// <summary>The helper line under the choice (empty when there is none).</summary>
    internal string HintMessage => HintText.Visibility == Visibility.Visible ? HintText.Text : "";

    internal CheckBox DeleteTickBox => DeleteBox;

    internal Category? ChosenTarget => TargetBox.SelectedItem as Category;

    /// <summary>
    /// Ticked: the move-to choice and its label are off (disabled and collapsed), the helper line says the prompts are
    /// deleted for good, in Danger text, and the button deletes. Unticked: the choice is back with a category selected.
    /// </summary>
    private void Update()
    {
        if (ActionButton is null) return; // while the XAML is still being loaded
        var delete = DeleteBox.IsChecked == true;
        TargetBox.IsEnabled = !delete;
        MovePanel.Visibility = _canMove && !delete ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = _canMove || delete ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = delete ? "These prompts will be deleted. This can't be undone." : "Prompts move to the category you pick.";
        HintText.SetResourceReference(StyleProperty, delete ? "ErrorText" : "MutedText");

        var text = delete ? $"Delete category and {Plural(_promptCount)}" : "Move and delete";
        ActionButton.Content = text;
        AutomationProperties.SetName(ActionButton, text);
        ActionButton.SetResourceReference(StyleProperty, delete ? "DangerButton" : "PrimaryButton");
        ActionButton.IsEnabled = delete || TargetBox.SelectedItem is Category;
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (DeleteBox.IsChecked == true)
        {
            DeletePrompts = true;
            SelectedCategoryId = null;
        }
        else if (TargetBox.SelectedItem is Category target)
        {
            SelectedCategoryId = target.Id;
        }
        else
        {
            return;
        }
        DialogResult = true;
    }
}
