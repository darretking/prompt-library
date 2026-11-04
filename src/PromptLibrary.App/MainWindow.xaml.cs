using System.ComponentModel;
using System.Windows;

namespace PromptLibrary.App;

/// <summary>Placeholder main window (built out in Task 9). Closing it only hides it; the app stays in the tray.</summary>
public partial class MainWindow : Window
{
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Really closes the window; used when the app exits.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
