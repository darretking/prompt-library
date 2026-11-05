using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    private bool _exiting;

    public MainWindow(AppHost host)
    {
        _host = host;
        _closes = new UserCloseTracker(this, host);
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
            e.Cancel = true;
            if (ViewModel.ConfirmDiscardOrSave())
            {
                Hide();
                _host.OnMainWindowHidden(); // says once that the app is still running in the tray
            }
        }
        base.OnClosing(e);
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
        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.Control) return;

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
