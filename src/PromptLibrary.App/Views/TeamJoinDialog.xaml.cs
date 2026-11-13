using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// <c>Join a team library</c>: one box for a shared folder, file or link. It stays open (and can't be dismissed)
/// while the read runs, shows errors inline, and closes with true once joined.
/// </summary>
public partial class TeamJoinDialog : Window
{
    private readonly TeamJoinViewModel _viewModel;

    public TeamJoinDialog(TeamJoinViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Joined += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
            // A box prefilled from a command-line argument must not join on a stray Enter: Join becomes the
            // default button only once the user edits the text.
            if (InputBox.Text.Length == 0) JoinButton.IsDefault = true;
            else InputBox.TextChanged += (_, _) => JoinButton.IsDefault = true;
            _ = viewModel.FindAsync();
        };
        Closed += (_, _) => viewModel.CancelFind();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the team's shared folder" };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Input = dialog.FolderName;
            InputBox.CaretIndex = InputBox.Text.Length;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // A read in progress would still join afterwards, so it is waited for (it ends within seconds).
        if (_viewModel.IsChecking && DialogResult != true) e.Cancel = true;
        base.OnClosing(e);
    }
}
