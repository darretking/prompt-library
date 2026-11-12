using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views;

/// <summary>
/// Asked before deleting a category that still has prompts (titled <c>Delete "Travel"?</c>): two choices, <c>Move the
/// N prompts to</c> a category (picked right under it) or <c>Delete the N prompts too</c> (then the category box is greyed and
/// the button says <c>Delete category and N prompts</c> in Danger style). Cancel leaves everything as it was.
/// </summary>
public partial class MoveCategoryDialog : Window
{
    private readonly int _promptCount;
    private readonly bool _canMove;
    private readonly string _categoryName;

    public MoveCategoryDialog(string categoryName, int promptCount, IReadOnlyList<Category> targets)
    {
        InitializeComponent();
        _promptCount = promptCount;
        _canMove = targets.Count > 0;

        _categoryName = categoryName;
        TargetBox.ItemsSource = targets;
        TargetBox.SelectedIndex = _canMove ? 0 : -1; // never empty: the first other category is the default

        // A group of its own: WPF shares a GroupName across windows that are not shown yet.
        var group = "PromptsGo-" + Guid.NewGuid().ToString("N");
        MoveRadio.GroupName = group;
        DeleteBox.GroupName = group;
        MoveRadio.IsChecked = _canMove;
        Title = $"Delete \"{categoryName}\"?";
        AutomationProperties.SetName(this, Title);
        var move = $"Move the {Plural(promptCount)} to";
        MoveRadio.Content = move;
        AutomationProperties.SetName(MoveRadio, move);
        var label = $"Delete the {Plural(promptCount)} too";
        DeleteBox.Content = label;
        AutomationProperties.SetName(DeleteBox, label);
        if (!_canMove) DeleteBox.IsChecked = true;
        Update();

        Loaded += (_, _) => (_canMove ? (UIElement)TargetBox : DeleteBox).Focus();
    }

    private static string Plural(int count) => count == 1 ? "1 prompt" : $"{count} prompts";

    /// <summary>The chosen category, set when the dialog closes with "Move and delete"; null when the prompts are deleted.</summary>
    public Guid? SelectedCategoryId { get; private set; }

    /// <summary>True when the prompts are deleted with the category.</summary>
    public bool DeletePrompts { get; private set; }

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Update();

    private void Choice_Changed(object sender, RoutedEventArgs e)
    {
        // Unticking "Delete" (from code) goes back to moving, so one choice is always made.
        if (DeleteBox is not null && MoveRadio is not null && _canMove
            && DeleteBox.IsChecked != true && MoveRadio.IsChecked != true)
            MoveRadio.IsChecked = true;
        Update();
    }

    /// <summary>True while the category to move the prompts to can be picked (it is greyed while Delete is chosen).</summary>
    internal bool IsMoveChoiceShown => MovePanel.Visibility == Visibility.Visible && TargetBox.IsEnabled;

    /// <summary>The helper line under the choice (empty when there is none).</summary>
    /// <summary>The question at the top, which follows the tick box.</summary>
    internal string Message => MessageText.Text;

    internal string HintMessage => DeleteWarning.Visibility == Visibility.Visible ? DeleteWarning.Text
        : HintText.Visibility == Visibility.Visible ? HintText.Text : "";

    internal System.Windows.Controls.Primitives.ToggleButton DeleteTickBox => DeleteBox;

    internal Category? ChosenTarget => TargetBox.SelectedItem as Category;

    /// <summary>
    /// Delete chosen: the category box is greyed, the helper line says the prompts are deleted for good, in Danger text,
    /// and the button deletes. Move chosen: the box is back with a category selected.
    /// </summary>
    private void Update()
    {
        if (ActionButton is null) return; // while the XAML is still being loaded
        var delete = DeleteBox.IsChecked == true;
        var prompts = Plural(_promptCount);
        var them = _promptCount == 1 ? "it" : "them";
        MessageText.Text = delete
            ? $"\"{_categoryName}\" and its {prompts} will be deleted."
            : _canMove
                ? $"\"{_categoryName}\" has {prompts}. Move {them} to another category, then \"{_categoryName}\" is deleted."
                : $"\"{_categoryName}\" has {prompts} and there is no other category to move {them} to. " +
                  $"Create one first, or delete {them} with \"{_categoryName}\".";
        TargetBox.IsEnabled = !delete;
        MovePanel.Visibility = _canMove ? Visibility.Visible : Visibility.Collapsed;
        // The box stays (greyed) while Delete is chosen, so "Move the 3 prompts to" never reads as a cut-off sentence.
        HintText.Visibility = _canMove && !delete ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = "Prompts move to the category you pick.";
        DeleteWarning.Visibility = delete ? Visibility.Visible : Visibility.Collapsed;

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
