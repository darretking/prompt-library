using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>
/// The placeholder form (see <see cref="FillInViewModel"/>). Host it anywhere by setting its DataContext; it raises
/// the view model's <c>Completed</c> / <c>Cancelled</c>. The first field takes the focus whenever the DataContext changes.
/// </summary>
public partial class FillInPanel : UserControl
{
    public FillInPanel()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        DataContextChanged += (_, _) =>
        {
            FocusFirstField();
        };
        Loaded += (_, _) => FocusFirstField();
    }

    /// <summary>Shows the "Esc back" hint beside the buttons. The search window turns it off: its footer already says it.</summary>
    public bool ShowEscHint
    {
        get => EscHint.Visibility == Visibility.Visible;
        set => EscHint.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private FillInViewModel? ViewModel => DataContext as FillInViewModel;

    /// <summary>Moves the keyboard to the first field.</summary>
    public void FocusFirstField()
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));

    private void Complete_Click(object sender, RoutedEventArgs e) => ViewModel?.Complete();

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel?.Cancel();

    private bool _completing;

    // Typing in a choice field: the first option that starts with the text fills in after the caret, selected, so the
    // next letter replaces it and Tab or Enter keeps it. Only a single typed letter at the end of the text does this
    // (not Backspace, not a paste, not text the program sets).
    private void ChoiceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_completing || sender is not TextBox { DataContext: FillField field } box) return;
        if (e.Changes.Count != 1 || e.Changes.Single() is not { AddedLength: 1 } change) return;
        if (change.Offset + 1 != box.Text.Length) return; // the letter went in at the end

        var typedLength = box.Text.Length;
        if (field.CompletionFor(box.Text) is not { } completion) return;
        _completing = true;
        try
        {
            box.Text = completion;
            box.Select(typedLength, completion.Length - typedLength);
        }
        finally
        {
            _completing = false;
        }
    }

    private void ChoiceBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: FillField field }) field.IsListOpen = false;
    }

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FillField field } button) return;
        field.IsListOpen = !field.IsListOpen;
        if (button.Parent is Panel panel && panel.Children.OfType<TextBox>().FirstOrDefault() is { } box)
            Keyboard.Focus(box);
    }

    private void Option_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string option }) return;
        var field = FieldOfOption(sender as DependencyObject);
        if (field is null) return;
        field.Pick(option);
        e.Handled = true;
    }

    // The option rows sit in a popup whose DataContext is the option text; the field is the popup's owner's context.
    private static FillField? FieldOfOption(DependencyObject? from)
    {
        while (from is not null)
        {
            if (from is System.Windows.Controls.Primitives.Popup { DataContext: FillField field }) return field;
            if (from is FrameworkElement { DataContext: FillField direct }) return direct;
            from = from is FrameworkElement { Parent: { } logical } ? logical : System.Windows.Media.VisualTreeHelper.GetParent(from);
        }
        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var field = (e.OriginalSource as FrameworkElement)?.DataContext as FillField;
        var choice = field is { IsChoice: true } && e.OriginalSource is TextBox ? field : null;

        switch (e.Key)
        {
            case Key.Escape:
                if (choice is { IsListOpen: true }) choice.IsListOpen = false;
                else vm.Cancel();
                e.Handled = true;
                break;

            case Key.Down or Key.Up when choice is not null:
                choice.MoveHighlight(e.Key == Key.Down ? +1 : -1);
                e.Handled = true;
                break;

            case Key.Back when field is not null && vm.IsFirst(field) && e.OriginalSource is TextBox { Text.Length: 0 }:
                vm.Cancel();
                e.Handled = true;
                break;

            case Key.Enter when choice is { IsListOpen: true } && choice.PickHighlighted():
                (e.OriginalSource as TextBox)?.Select(((TextBox)e.OriginalSource).Text.Length, 0);
                e.Handled = true;
                break;

            case Key.Enter when e.OriginalSource is not Button:
                if (field is null || vm.IsLast(field) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    vm.Complete();
                else
                    (e.OriginalSource as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
        }
    }
}
