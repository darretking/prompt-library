using System.ComponentModel;
using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// Version history of a personal prompt: earlier versions on the left, a line diff against the editor's text on the
/// right. "Restore this version" puts the version in the editor as an unsaved change and closes the window.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly UserCloseTracker _closes;
    private readonly HistoryViewModel _model;

    public HistoryWindow(AppHost host, HistoryViewModel model)
    {
        _closes = new UserCloseTracker(this, host);
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Another program (the installer) closing us: the app exits without asking anything instead.
        _closes.CancelIfExternal(e);
        base.OnClosing(e);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Restore()) DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
