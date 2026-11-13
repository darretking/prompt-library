using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// The category emoji picker: a 32 px button showing <see cref="SelectedEmoji"/> (a muted smiley when none) that opens a
/// 6 x 10 grid of curated emoji (<see cref="EmojiChoices"/>) and <c>No emoji</c>. <see cref="Picked"/> is raised when the
/// user chooses.
/// </summary>
public partial class EmojiButton : UserControl
{
    public static readonly DependencyProperty SelectedEmojiProperty = DependencyProperty.Register(
        nameof(SelectedEmoji), typeof(string), typeof(EmojiButton),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedChanged));

    public EmojiButton()
    {
        InitializeComponent();
        Cells.ItemsSource = EmojiChoices.All;
        Show(null);
    }

    /// <summary>The chosen emoji, or null for none.</summary>
    public string? SelectedEmoji
    {
        get => (string?)GetValue(SelectedEmojiProperty);
        set => SetValue(SelectedEmojiProperty, value);
    }

    /// <summary>Raised after the user picked an emoji (or "No emoji"); <see cref="SelectedEmoji"/> already has it.</summary>
    public event EventHandler? Picked;

    /// <summary>Opens the picker.</summary>
    public void Open() => Picker.IsOpen = true;

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmojiButton)d).Show(e.NewValue as string);

    private void Show(string? emoji)
    {
        EmojiText.Text = emoji ?? "";
        EmojiText.Visibility = emoji is null ? Visibility.Collapsed : Visibility.Visible;
        Smiley.Visibility = emoji is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Trigger_Click(object sender, RoutedEventArgs e) => Picker.IsOpen = !Picker.IsOpen;

    private void Cell_Click(object sender, RoutedEventArgs e) => Pick(((FrameworkElement)sender).DataContext as string);

    private void None_Click(object sender, RoutedEventArgs e) => Pick(null);

    private void Pick(string? emoji)
    {
        SelectedEmoji = emoji;
        Picker.IsOpen = false;
        Picked?.Invoke(this, EventArgs.Empty);
    }

    // Keyboard users land on the first emoji; Esc closes the grid (and nothing else: the name box behind keeps editing).
    private void Picker_Opened(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Cells.ItemContainerGenerator.ContainerFromIndex(0) is { } first
                && FindButton(first) is { } button)
                button.Focus();
        });

    private void Picker_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Picker.IsOpen = false;
        Trigger.Focus();
        e.Handled = true;
    }

    private static Button? FindButton(DependencyObject from)
    {
        if (from is Button button) return button;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(from); i++)
            if (FindButton(System.Windows.Media.VisualTreeHelper.GetChild(from, i)) is { } found)
                return found;
        return null;
    }
}
