using System.Windows;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views;

/// <summary>
/// Asked before deleting a category that still has prompts: which category should they move to?
/// Cancel leaves everything as it was.
/// </summary>
public partial class MoveCategoryDialog : Window
{
    public MoveCategoryDialog(string categoryName, int promptCount, IReadOnlyList<Category> targets)
    {
        InitializeComponent();

        var prompts = promptCount == 1 ? "1 prompt" : $"{promptCount} prompts";
        MessageText.Text = $"\"{categoryName}\" has {prompts}. Choose where to move them; then the category is deleted.";
        TargetBox.ItemsSource = targets;
        TargetBox.SelectedIndex = 0;

        Loaded += (_, _) => TargetBox.Focus();
    }

    /// <summary>The chosen category, set when the dialog closes with "Move and delete".</summary>
    public Guid? SelectedCategoryId { get; private set; }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (TargetBox.SelectedItem is not Category target) return;
        SelectedCategoryId = target.Id;
        DialogResult = true;
    }
}
