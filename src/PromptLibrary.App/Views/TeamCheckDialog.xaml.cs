using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary><c>Check the team library</c>: choosing a row records it and closes, so the main window can go there.</summary>
public partial class TeamCheckDialog : Window
{
    private readonly TeamCheckViewModel _viewModel;

    public TeamCheckDialog(TeamCheckViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TeamCheckRow row) return;
        _viewModel.Choose(row);
        DialogResult = true;
    }
}
