using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PromptLibrary.App.Team;
using PromptLibrary.App.ViewModels;
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
        ViewModel = new MainViewModel(host, this);
        ViewModel.TitleFocusRequested += (_, _) => FocusLater(TitleBox);
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

    /// <summary>
    /// Tab and Shift+Tab leave the prompt box like any other field; Ctrl+Tab types a tab character
    /// (replacing any selected text).
    /// </summary>
    private void PromptBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.Control || PromptBox.IsReadOnly) return;

        var start = PromptBox.SelectionStart;
        PromptBox.SelectedText = "\t";
        PromptBox.Select(start + 1, 0);
        e.Handled = true;
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

    /// <summary>
    /// The editor's two button groups share one line when they fit; near the minimum width (the Team side's row is
    /// the wider one) the right group, ending in Copy prompt, moves to a second line, still right-aligned. The slip
    /// and the gap above the buttons then give up that line's height, so nothing is cut off at the minimum size.
    /// </summary>
    private void ButtonRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        var stacked = LeftButtons.ActualWidth + RightButtons.ActualWidth + 8 > ButtonRow.ActualWidth;
        Grid.SetRow(RightButtons, stacked ? 1 : 0);
        RightButtons.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        ButtonRow.Margin = new Thickness(0, stacked ? 8 : 16, 0, 0);
        SlipRow.MinHeight = stacked ? 40 : 64;
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
