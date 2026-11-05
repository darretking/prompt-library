using System.Windows;
using System.Windows.Automation;

namespace PromptLibrary.App.Views;

/// <summary>
/// The app's one styled question box (used instead of system message boxes): a message, a filled primary button
/// (Brand, or Danger for deletions), a cancel button, and optionally a separate alternative button (e.g.
/// <c>Don't save</c>) and a link (e.g. <c>Show file</c>) that doesn't close the dialog. Every button is a normal
/// WPF button, so UI Automation can invoke it.
/// </summary>
public partial class ConfirmDialog : Window
{
    private readonly Action? _linkAction;
    private Choice _choice = Choice.Cancel;

    private enum Choice { Cancel, Primary, Alternative }

    private ConfirmDialog(string message, string primary, string? cancel, bool danger,
        string? alternative, string? linkText, Action? linkAction)
    {
        InitializeComponent();
        MessageText.Text = message;
        AutomationProperties.SetName(this, message);

        PrimaryButton.Content = primary;
        AutomationProperties.SetName(PrimaryButton, primary.TrimEnd('…'));
        if (danger)
            PrimaryButton.SetResourceReference(StyleProperty, "DangerButton");

        if (cancel is null)
        {
            // A message with only "OK": Esc closes it too.
            CancelButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
        }
        else
        {
            CancelButton.Content = cancel;
            AutomationProperties.SetName(CancelButton, cancel);
        }

        if (alternative is not null)
        {
            AlternativeButton.Content = alternative;
            AutomationProperties.SetName(AlternativeButton, alternative);
            AlternativeButton.Visibility = Visibility.Visible;
        }

        _linkAction = linkAction;
        if (linkText is not null && linkAction is not null)
        {
            LinkButton.Content = linkText;
            AutomationProperties.SetName(LinkButton, linkText);
            LinkButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Shows the question; true when the user chose the primary action (Esc and × are cancel).
    /// <paramref name="danger"/> makes the primary button red (for deletions).
    /// </summary>
    public static bool Ask(Window? owner, string message, string primary, string cancel,
        string? linkText = null, Action? linkAction = null, bool danger = false)
        => Show(new ConfirmDialog(message, primary, cancel, danger, alternative: null, linkText, linkAction), owner)
           == Choice.Primary;

    /// <summary>
    /// <paramref name="question"/> (<c>Save changes to "title"?</c>) with Save / Don't save / Cancel: true = save,
    /// false = don't save, null = cancel (also Esc and ×).
    /// </summary>
    public static bool? AskSaveChanges(Window? owner, string question)
        => Show(new ConfirmDialog(question, "Save", "Cancel", danger: false,
                alternative: "Don't save", linkText: null, linkAction: null), owner) switch
        {
            Choice.Primary => true,
            Choice.Alternative => false,
            _ => null,
        };

    /// <summary>A message with a single OK button.</summary>
    public static void Inform(Window? owner, string message)
        => Show(new ConfirmDialog(message, "OK", cancel: null, danger: false, alternative: null, linkText: null,
            linkAction: null), owner);

    private static Choice Show(ConfirmDialog dialog, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true; // nothing else of ours may be on screen (startup, tray)
        }
        dialog.ShowDialog();
        return dialog._choice;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        _choice = Choice.Primary;
        if (!PrimaryButton.IsCancel) // an "OK"-only message is closed by IsCancel itself
            DialogResult = true;
    }

    private void Alternative_Click(object sender, RoutedEventArgs e)
    {
        _choice = Choice.Alternative;
        DialogResult = true;
    }

    private void Link_Click(object sender, RoutedEventArgs e) => _linkAction?.Invoke();
}
