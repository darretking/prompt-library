using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary><c>Suggest for the team</c>: closes with true when Send suggestion was chosen.</summary>
public partial class SuggestDialog : Window
{
    private readonly SuggestViewModel _viewModel;

    public SuggestDialog(SuggestViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => NoteBox.Focus();
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Send()) DialogResult = true;
    }
}
