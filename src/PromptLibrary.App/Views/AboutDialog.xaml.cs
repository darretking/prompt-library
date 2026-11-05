using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace PromptLibrary.App.Views;

/// <summary><c>Help → About Prompt Library</c>: name, version, and a link that opens the data folder in Explorer.</summary>
public partial class AboutDialog : Window
{
    private readonly string _dataFolder;

    public AboutDialog(string version, string dataFolder)
    {
        InitializeComponent();
        _dataFolder = dataFolder;
        VersionText.Text = $"Version {version}";
        OpenFolderLink.ToolTip = dataFolder;
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> when it is on screen, else centred on the screen.</summary>
    public static void Open(Window? owner, string version, string dataFolder)
    {
        var dialog = new AboutDialog(version, dataFolder);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }
        dialog.ShowDialog();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", $"\"{_dataFolder}\"");
        }
        catch (Win32Exception)
        {
            // Explorer couldn't be started; the folder is still in the link's tooltip.
        }
    }
}
