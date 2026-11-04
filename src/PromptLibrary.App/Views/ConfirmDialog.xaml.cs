using System.Windows;
using System.Windows.Automation;

namespace PromptLibrary.App.Views;

/// <summary>
/// A styled two-button question (used instead of a system message box): a message, a filled primary button,
/// a cancel button and an optional link (e.g. <c>Show file</c>) that doesn't close the dialog.
/// </summary>
public partial class ConfirmDialog : Window
{
    private readonly Action? _linkAction;

    private ConfirmDialog(string message, string primary, string cancel, string? linkText, Action? linkAction)
    {
        InitializeComponent();
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        CancelButton.Content = cancel;
        AutomationProperties.SetName(PrimaryButton, primary.TrimEnd('…'));
        AutomationProperties.SetName(CancelButton, cancel);
        AutomationProperties.SetName(this, message);

        _linkAction = linkAction;
        if (linkText is not null && linkAction is not null)
        {
            LinkButton.Content = linkText;
            AutomationProperties.SetName(LinkButton, linkText);
            LinkButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Shows the question; true when the user chose the primary action (Esc and × are cancel).</summary>
    public static bool Ask(Window? owner, string message, string primary, string cancel,
        string? linkText = null, Action? linkAction = null)
    {
        var dialog = new ConfirmDialog(message, primary, cancel, linkText, linkAction);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true; // nothing else of ours may be on screen (startup)
        }
        return dialog.ShowDialog() == true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Link_Click(object sender, RoutedEventArgs e) => _linkAction?.Invoke();
}
