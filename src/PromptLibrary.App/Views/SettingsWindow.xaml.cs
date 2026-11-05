using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PromptLibrary.App.Views;

/// <summary>
/// Settings: shortcut, backup location, how many backups to keep and start with Windows. Every setting applies
/// (and is saved) as soon as it changes; the only button is Close.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string ShortcutIdle = "Opens your prompt menu from any app.";
    private const string ShortcutInUse = "That shortcut is in use by another app.";
    private static readonly int[] KeepChoices = [10, 30, 90, 365];

    private readonly AppHost _host;
    private readonly UserCloseTracker _closes;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _closes = new UserCloseTracker(this, host);
        InitializeComponent();

        ShortcutField.Shortcut = host.Settings.Hotkey;
        if (host.HotkeyRegistered)
            SetShortcutStatus(ShortcutIdle, isError: false);
        else
            SetShortcutStatus(ShortcutInUse, isError: true);

        // While listening, the current shortcut is let go so pressing it reaches the box.
        ShortcutField.ListeningChanged += (_, listening) =>
        {
            if (listening) _host.SuspendHotkey();
            else _host.ResumeHotkey();
        };
        ShortcutField.ShortcutPressed += (_, shortcut) => ApplyShortcut(shortcut);
        ShortcutField.Rejected += (_, hint) => SetShortcutStatus(hint, isError: false);
        Closed += (_, _) => _host.ResumeHotkey();

        var keep = host.Settings.BackupsToKeep;
        KeepBox.ItemsSource = KeepChoices.Contains(keep) ? KeepChoices : KeepChoices.Append(keep).Order().ToArray();
        KeepBox.SelectedItem = keep;
        KeepBox.SelectionChanged += KeepBox_SelectionChanged;

        PasteBox.IsChecked = host.Settings.PasteAfterCopy;
        PasteBox.Checked += (_, _) => ApplyPasteAfterCopy(true);
        PasteBox.Unchecked += (_, _) => ApplyPasteAfterCopy(false);

        StartupBox.IsChecked = host.Settings.StartWithWindows;
        StartupBox.Checked += (_, _) => ApplyStartWithWindows(true);
        StartupBox.Unchecked += (_, _) => ApplyStartWithWindows(false);

        Picker.Attach(host);
    }

    /// <summary>Moves the focus to the backup folders (for "Set up backups").</summary>
    public void FocusBackups() => Picker.FocusFirstCard();

    private void ChangeShortcut_Click(object sender, RoutedEventArgs e) => ShortcutField.StartListening();

    /// <summary>Registers the pressed shortcut at once; if another app has it, the old one stays.</summary>
    private void ApplyShortcut(string shortcut)
    {
        var registered = _host.ApplyHotkey(shortcut);
        ShortcutField.Shortcut = _host.Settings.Hotkey;
        if (!registered)
        {
            SetShortcutStatus(ShortcutInUse, isError: true);
            return;
        }

        _host.SaveSettings(); // a failure to write is reported by the host; the shortcut works for this run
        SetShortcutStatus($"Saved. Press {_host.Settings.Hotkey} in any app to try it.", isError: false);
    }

    private void SetShortcutStatus(string text, bool isError)
    {
        ShortcutStatus.Text = text;
        if (isError)
            ShortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        else
            ShortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
    }

    private void KeepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeepBox.SelectedItem is not int keep) return;
        _host.Settings.BackupsToKeep = keep;
        _host.SaveSettings(); // older backups are pruned at the next backup
    }

    private void ApplyPasteAfterCopy(bool enabled)
    {
        _host.Settings.PasteAfterCopy = enabled;
        _host.SaveSettings();
    }

    private void ApplyStartWithWindows(bool enabled)
    {
        _host.Settings.StartWithWindows = enabled;
        _host.ApplyStartupRegistration();
        _host.SaveSettings();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _closes.MarkUserClose();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Another program (the installer) closing us: the app exits without asking anything instead.
        _closes.CancelIfExternal(e);
        base.OnClosing(e);
    }
}
