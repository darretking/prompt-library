using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary><c>Note for everyone</c>: sets or removes the team note. Closes with true when Save note or Remove note was chosen.</summary>
public partial class TeamNoteDialog : Window
{
    private readonly TeamNoteViewModel _viewModel;

    public TeamNoteDialog(TeamNoteViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            NoteBox.Focus();
            NoteBox.CaretIndex = NoteBox.Text.Length;
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Save()) DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Remove()) DialogResult = true;
    }
}
