using System.ComponentModel;
using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>What changed in a prompt: the changed fields on top, the text as a line diff below, and Close.</summary>
public partial class ChangesWindow : Window
{
    private readonly UserCloseTracker _closes;

    public ChangesWindow(AppHost host, ChangesViewModel model)
    {
        _closes = new UserCloseTracker(this, host);
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Another program (the installer) closing us: the app exits without asking anything instead.
        _closes.CancelIfExternal(e);
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
