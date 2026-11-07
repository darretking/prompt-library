using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// <c>Start a team library</c>: folder, name, optional categories to copy in, and the business examples option.
/// It can't be dismissed while the library is being written, and closes with true once started (or joined).
/// </summary>
public partial class TeamStartDialog : Window
{
    private readonly TeamStartViewModel _viewModel;

    public TeamStartDialog(TeamStartViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Finished += (_, _) => DialogResult = true;
        Loaded += (_, _) => FolderBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a shared folder for the team library" };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Folder = dialog.FolderName;
            FolderBox.CaretIndex = FolderBox.Text.Length;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsWorking && DialogResult != true) e.Cancel = true;
        base.OnClosing(e);
    }
}
