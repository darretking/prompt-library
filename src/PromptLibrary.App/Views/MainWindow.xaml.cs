using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PromptLibrary.App.Team;
using PromptLibrary.App.ViewModels;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views;

/// <summary>
/// The three-column main window. Created once by <see cref="AppHost"/>; closing it only hides it
/// (after offering to save unsaved changes) and the app stays in the tray.
/// </summary>
public partial class MainWindow : Window, IDialogs
{
    private readonly AppHost _host;
    private readonly UserCloseTracker _closes;
    private readonly CloseQuestion _closeQuestion;
    private bool _exiting;

    public MainWindow(AppHost host)
    {
        _host = host;
        _closes = new UserCloseTracker(this, host);
        _closeQuestion = new CloseQuestion(this);
        InitializeComponent();
        ViewModel = new MainViewModel(host, this, new PromptHistoryStore(Paths.HistoryFolder, Paths.CrashLogFile));
        ViewModel.TitleFocusRequested += (_, _) => FocusLater(TitleBox);
        ViewModel.ToastRequested += (_, text) => ShowToast(text);
        ViewModel.HistoryDialog = model => new HistoryWindow(_host, model) { Owner = this }.ShowDialog() == true;
        DataContext = ViewModel;

        // Coming back to the window re-checks a paused backup folder (e.g. Google Drive signed in again).
        Activated += (_, _) => _host.RecheckBackups();

        // An empty library: the one thing to do is "New prompt", so it starts with the focus.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && ViewModel.ShowNoPromptsYet)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => EmptyNewPromptButton.Focus());
        };
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Really closes the window; used when the app exits.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    /// <summary>True while "Save changes?" for closing the window is pending or open.</summary>
    public bool IsAskingToClose => _closeQuestion.IsAsking;

    /// <summary>Brings the open "Save changes?" question to the front.</summary>
    public void ActivateCloseQuestion() => _closeQuestion.Activate();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Another program (the installer) closing us: the app exits without asking anything instead.
        if (_closes.CancelIfExternal(e))
        {
            base.OnClosing(e);
            return;
        }

        // When Windows is ending the session, close without asking: a question would block logoff (the host has
        // already saved or recovered unsaved edits).
        if (!_exiting && !_host.IsSessionEnding)
        {
            // Closing only hides the window, after the question (asked once this close has been cancelled). It also ends
            // editing the team library. When a team save has to finish first, the window hides once it has gone through.
            _closeQuestion.CancelAndAsk(e,
                ask: () => !_exiting && IsVisible && ViewModel.ConfirmLeave(HideLater) && !_exiting && !_host.IsExiting,
                onYes: HideToTray);
        }
        base.OnClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        _host.OnMainWindowHidden(); // says once that the app is still running in the tray
    }

    private void HideLater()
    {
        if (!_exiting && !_host.IsExiting && IsVisible) HideToTray();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ViewModel.SendCommand.Execute(null); // Send to the prompt's default AI app
            e.Handled = true;
        }
        else if (e.Key == Key.F6 && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            CycleFocusPane(backwards: Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // ---- IDialogs ------------------------------------------------------------------------------

    public bool? AskSaveChanges(string question) => ConfirmDialog.AskSaveChanges(this, question);

    public bool ConfirmDestructive(string message, string action)
        => ConfirmDialog.Ask(this, message, action, "Cancel", danger: true);

    public Guid? ChooseMoveTarget(string categoryName, int promptCount, IReadOnlyList<Category> targets)
    {
        var dialog = new MoveCategoryDialog(categoryName, promptCount, targets) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.SelectedCategoryId : null;
    }

    public void ShowMessage(string message)
    {
        if (!_host.AsksNothing)
            ConfirmDialog.Inform(this, message);
    }

    public bool ShowTeamHistory(TeamLibraryService service, bool review)
    {
        var window = new TeamHistoryWindow(_host, service, review) { Owner = this };
        return window.ShowDialog() == true;
    }

    // ---- Category rail -------------------------------------------------------------------------

    private void RailList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return; // keys typed into the rename box

        if (e.Key == Key.F2)
        {
            ViewModel.StartRename(RailList.SelectedItem as CategoryItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && RailList.SelectedItem is CategoryItem { IsAll: false } item)
        {
            ViewModel.DeleteCategory(item);
            e.Handled = true;
        }
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
        => ViewModel.StartRename(((FrameworkElement)sender).DataContext as CategoryItem);

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
        => ViewModel.DeleteCategory(((FrameworkElement)sender).DataContext as CategoryItem);

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not CategoryItem item) return;

        if (e.Key == Key.Enter)
        {
            if (ViewModel.CommitRename(item))
                FocusSelectedCategory();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRename(item);
            FocusSelectedCategory();
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicking away keeps a valid new name (like Explorer); an invalid one is dropped.
        // Deferred so the click that moved focus finishes first: committing rebuilds the rail, which
        // would otherwise swallow that click.
        if (((FrameworkElement)sender).DataContext is not CategoryItem { IsRenaming: true } item) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (item.IsRenaming && !ViewModel.CommitRename(item))
                ViewModel.CancelRename(item);
        });
    }

    private void NewCategoryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel.CommitNewCategory())
                FocusSelectedCategory();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelNewCategory();
            FocusSelectedCategory();
            e.Handled = true;
        }
    }

    private void NewCategoryBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Leaving an empty box closes it; a typed name stays so it isn't lost.
        if (ViewModel.IsAddingCategory && string.IsNullOrWhiteSpace(ViewModel.NewCategoryName))
            ViewModel.CancelNewCategory();
    }

    /// <summary>Inline name boxes take the cursor as soon as they appear.</summary>
    private void InlineBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
            FocusLater(box, selectAll: true);
    }

    // ---- Prompt list ---------------------------------------------------------------------------

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            ViewModel.SearchText = "";
            e.Handled = true;
        }
    }

    private void PromptList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && PromptList.SelectedItem is not null)
        {
            ViewModel.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The row menu's Pin / Unpin.</summary>
    private void PinPrompt_Click(object sender, RoutedEventArgs e)
        => ViewModel.PinCommand.Execute(((FrameworkElement)sender).DataContext as PromptRow);

    /// <summary>The row menu's Hide from menu / Unhide (team prompts only).</summary>
    private void HidePrompt_Click(object sender, RoutedEventArgs e)
        => ViewModel.HideCommand.Execute(((FrameworkElement)sender).DataContext as PromptRow);

    /// <summary>The Team header's ⋯: opens its menu under the button.</summary>
    private void TeamMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- Editor buttons ------------------------------------------------------------------------

    /// <summary>The bottom bar's ... button: opens its menu above the button.</summary>
    private void Overflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    /// <summary>
    /// The Send to arrow: the AI apps (a check on the prompt's default), then <c>Set as default for this prompt</c>
    /// with the same apps as a submenu. Built when opened, so the check is always current.
    /// </summary>
    private void SendMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var item = (Style)FindResource("FlyoutItem");
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("FlyoutMenu"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
        };

        var choices = ViewModel.SendChoices;
        foreach (var choice in choices)
        {
            var id = choice.Id;
            var entry = new MenuItem { Style = item, Header = choice.Name, IsChecked = choice.IsDefault };
            entry.Click += (_, _) => ViewModel.SendCommand.Execute(id);
            menu.Items.Add(entry);
        }

        if (ViewModel.Editor is { IsEditable: true })
        {
            menu.Items.Add(new Separator { Style = (Style)FindResource("FlyoutSeparator") });
            var setDefault = new MenuItem { Style = item, Header = "Set as default for this prompt" };
            foreach (var choice in choices)
            {
                var id = choice.Id;
                var entry = new MenuItem { Style = item, Header = choice.Name, IsChecked = choice.IsDefault };
                entry.Click += (_, _) => ViewModel.SetDefaultSendCommand.Execute(id);
                setDefault.Items.Add(entry);
            }
            menu.Items.Add(setDefault);
        }

        menu.IsOpen = true;
    }

    // ---- Toast ---------------------------------------------------------------------------------

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _toastTimerHooked;

    private static readonly Duration ToastFade = new(TimeSpan.FromMilliseconds(167));

    /// <summary>
    /// A small pill at the bottom of the editor: fades in over 167 ms (Fluent enter curve), stays 1.5 s, fades out
    /// (exit curve). With animations off in Windows it just appears and disappears.
    /// </summary>
    private void ShowToast(string text)
    {
        if (!_toastTimerHooked)
        {
            _toastTimerHooked = true;
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                FadeToast(show: false);
            };
        }

        ToastText.Text = text;
        System.Windows.Automation.AutomationProperties.SetName(Toast, text);
        Toast.Visibility = Visibility.Visible;
        FadeToast(show: true);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void FadeToast(bool show)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Toast.BeginAnimation(OpacityProperty, null);
            Toast.Opacity = show ? 1 : 0;
            if (!show) Toast.Visibility = Visibility.Collapsed;
            return;
        }

        // Fluent: enter cubic-bezier(0,0,0,1), exit (1,0,1,1).
        var spline = show ? new System.Windows.Media.Animation.KeySpline(0, 0, 0, 1) : new System.Windows.Media.Animation.KeySpline(1, 0, 1, 1);
        var animation = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = ToastFade };
        animation.KeyFrames.Add(new System.Windows.Media.Animation.SplineDoubleKeyFrame(
            show ? 1 : 0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(ToastFade.TimeSpan), spline));
        if (!show)
            animation.Completed += (_, _) =>
            {
                if (!_toastTimer.IsEnabled) Toast.Visibility = Visibility.Collapsed;
            };
        Toast.BeginAnimation(OpacityProperty, animation);
    }

    // ---- F6 ------------------------------------------------------------------------------------

    /// <summary>F6 / Shift+F6: moves the focus rail → prompt list → editor → rail.</summary>
    private void CycleFocusPane(bool backwards)
    {
        // 0 = rail, 1 = list, 2 = editor; anything else (search box, menu) counts as before the rail.
        var current = RailList.IsKeyboardFocusWithin ? 0
            : PromptList.IsKeyboardFocusWithin ? 1
            : PromptBox.IsKeyboardFocusWithin || TitleBox.IsKeyboardFocusWithin ? 2
            : -1;
        var next = backwards ? (current <= 0 ? 2 : current - 1) : (current + 1) % 3;
        if (next == 2 && !ViewModel.HasEditor) next = backwards ? 1 : 0;

        switch (next)
        {
            case 0:
                if (RailList.ItemContainerGenerator.ContainerFromItem(RailList.SelectedItem) is ListBoxItem railRow) railRow.Focus();
                else RailList.Focus();
                break;
            case 1:
                if (PromptList.ItemContainerGenerator.ContainerFromItem(PromptList.SelectedItem) is ListBoxItem listRow) listRow.Focus();
                else PromptList.Focus();
                break;
            default:
                PromptBox.Focus();
                break;
        }
    }

    // ---- Window size ---------------------------------------------------------------------------

    /// <summary>The minimum height before any team bar: the size the three columns are laid out for (style guide).</summary>
    private const double BaseMinHeight = 520;

    /// <summary>
    /// The switch and the team notice bar sit above the three columns, so the minimum window height grows by their
    /// height while they show: the editor keeps the room it has at 860×520 without them, and its buttons are never cut
    /// off (the Team side's edit bar and a new prompt's heading would otherwise push them out of the window).
    /// </summary>
    private void TeamBars_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) UpdateMinHeight();
    }

    private void TeamBars_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMinHeight();

    private void UpdateMinHeight()
    {
        static double HeightOf(FrameworkElement bar) => bar.IsVisible ? bar.ActualHeight : 0;
        // Never above the work area, or the window couldn't fit on a small or highly scaled screen.
        MinHeight = WindowFit.Height(BaseMinHeight + HeightOf(SwitchBar) + HeightOf(TeamBar), WindowFit.WorkAreaHeight);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private void FocusSelectedCategory()
    {
        if (RailList.ItemContainerGenerator.ContainerFromItem(RailList.SelectedItem) is ListBoxItem row)
            row.Focus();
    }

    /// <summary>Focuses after layout, for controls that are only just becoming visible.</summary>
    private void FocusLater(TextBox box, bool selectAll = false)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            if (selectAll) box.SelectAll();
        });
}
