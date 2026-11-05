using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// The first-run wizard (also <c>Help → Setup guide…</c>). Esc or × skips it: the defaults stay; after step 1,
/// once a backup folder has been chosen, it asks <c>Leave setup?</c> first. Esc inside the capture box only
/// cancels the capture.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly AppHost _host;
    private bool _closingForExit;

    public OnboardingWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        ViewModel = new OnboardingViewModel(host);
        DataContext = ViewModel;
        var picker = Picker.Attach(host);
        ViewModel.AttachPicker(picker);
        picker.PropertyChanged += (_, e) =>
        {
            // The result (and Try again) sits at the bottom of the step; keep it in view when it appears.
            if (e.PropertyName == nameof(BackupLocationPickerViewModel.ResultText) && picker.ResultText is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ContentScroll.ScrollToEnd);
        };
        ViewModel.CloseRequested += (_, _) => Close();
        ViewModel.ListenRequested += (_, _) => ListenForShortcut();
        ViewModel.PropertyChanged += (_, e) =>
        {
            // A new step: the keyboard focus goes back to Next/Finish, so Enter keeps moving on.
            if (e.PropertyName is "" or nameof(OnboardingViewModel.Step) && !ShortcutField.IsListening)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PrimaryButton.Focus());
        };

        // While listening, the current shortcut is let go so pressing it reaches the box.
        ShortcutField.Shortcut = host.Settings.Hotkey;
        ShortcutField.ListeningChanged += (_, listening) =>
        {
            if (listening) _host.SuspendHotkey();
            else
            {
                _host.ResumeHotkey();
                // After the press (if any) has been handled: Esc or Tab without a new shortcut closes the box.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, ViewModel.OnCaptureEnded);
            }
        };
        ShortcutField.ShortcutPressed += (_, shortcut) =>
        {
            ViewModel.ApplyShortcut(shortcut);
            ShortcutField.Shortcut = host.Settings.Hotkey;
        };
        ShortcutField.Rejected += (_, hint) => ViewModel.ShowShortcutHint(hint);
        Closed += (_, _) => _host.ResumeHotkey();
        Loaded += (_, _) => PrimaryButton.Focus();
    }

    public OnboardingViewModel ViewModel { get; }

    /// <summary>The app is exiting: close without asking and without marking setup as done.</summary>
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // The capture box handles its own Esc (cancel listening) before this sees it.
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForExit && !ViewModel.IsFinished)
        {
            if (ViewModel.AsksBeforeLeaving && !_host.IsSessionEnding
                && !ConfirmDialog.Ask(this, "Leave setup? Your choices so far are kept.", "Leave", "Stay"))
            {
                e.Cancel = true;
                return;
            }
            ViewModel.Skip();
        }
        base.OnClosing(e);
    }

    /// <summary>Shows the capture box listening (after layout, so it can take the focus).</summary>
    private void ListenForShortcut()
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (ViewModel.IsShortcut && ViewModel.IsChangingShortcut)
                ShortcutField.StartListening();
        });
}
