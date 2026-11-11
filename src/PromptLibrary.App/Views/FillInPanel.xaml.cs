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
        DataContextChanged += (_, _) => FocusFirstField();
        Loaded += (_, _) => FocusFirstField();
    }

    private FillInViewModel? ViewModel => DataContext as FillInViewModel;

    /// <summary>Moves the keyboard to the first field.</summary>
    public void FocusFirstField()
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));

    private void Complete_Click(object sender, RoutedEventArgs e) => ViewModel?.Complete();

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel?.Cancel();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var field = (e.OriginalSource as FrameworkElement)?.DataContext as FillField;
        var combo = FindCombo(e.OriginalSource as DependencyObject);

        switch (e.Key)
        {
            case Key.Escape:
                if (combo is { IsDropDownOpen: true }) combo.IsDropDownOpen = false;
                else vm.Cancel();
                e.Handled = true;
                break;

            case Key.Back when field is not null && vm.IsFirst(field) && e.OriginalSource is TextBox { Text.Length: 0 }:
                vm.Cancel();
                e.Handled = true;
                break;

            case Key.Enter when e.OriginalSource is not Button && combo is not { IsDropDownOpen: true }:
                if (field is null || vm.IsLast(field) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    vm.Complete();
                else
                    (e.OriginalSource as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
        }
    }

    private static ComboBox? FindCombo(DependencyObject? from)
    {
        while (from is not null)
        {
            if (from is ComboBox combo) return combo;
            from = from is FrameworkElement { TemplatedParent: { } parent } ? parent : System.Windows.Media.VisualTreeHelper.GetParent(from);
        }
        return null;
    }
}
