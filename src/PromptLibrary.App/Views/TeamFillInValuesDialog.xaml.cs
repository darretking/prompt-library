using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary><c>Fill-in values for everyone</c>: closes with true when the rows pass the limits and Save was chosen.</summary>
public partial class TeamFillInValuesDialog : Window
{
    private readonly TeamFillInValuesViewModel _viewModel;

    public TeamFillInValuesDialog(TeamFillInValuesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddField();
        RowList.UpdateLayout();
        if (RowList.ItemContainerGenerator.ContainerFromIndex(_viewModel.Rows.Count - 1) is not FrameworkElement container) return;
        container.BringIntoView();
        FindFirstBox(container)?.Focus();
    }

    private static TextBox? FindFirstBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox { IsVisible: true } box) return box;
            if (FindFirstBox(child) is { } found) return found;
        }
        return null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TrySave()) DialogResult = true;
    }
}
