using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Tags as chips: type a tag and press Enter or comma to add it (lower-cased and trimmed by <see cref="TagRules"/>),
/// Backspace in the empty box removes the last chip, × removes one. Up to <see cref="TagRules.MaxTags"/>.
/// </summary>
public partial class TagBox : UserControl
{
    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(
        nameof(Tags), typeof(ObservableCollection<string>), typeof(TagBox),
        new PropertyMetadata(null, (d, e) => ((TagBox)d).OnTagsChanged(e)));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(TagBox), new PropertyMetadata(false, (d, _) => ((TagBox)d).Refresh()));

    public TagBox() => InitializeComponent();

    public ObservableCollection<string>? Tags
    {
        get => (ObservableCollection<string>?)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Visible while the chips can be removed.</summary>
    public Visibility RemoveVisibility => IsReadOnly ? Visibility.Collapsed : Visibility.Visible;

    private void OnTagsChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObservableCollection<string> old)
            old.CollectionChanged -= TagsCollectionChanged;
        if (e.NewValue is ObservableCollection<string> tags)
            tags.CollectionChanged += TagsCollectionChanged;
        Chips.ItemsSource = e.NewValue as ObservableCollection<string>;
        Refresh();
    }

    private void TagsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Refresh();

    /// <summary>Shows the typing box only while more tags fit and the field is editable; the hint only while empty.</summary>
    private void Refresh()
    {
        var count = Tags?.Count ?? 0;
        InputHost.Visibility = IsReadOnly || count >= TagRules.MaxTags ? Visibility.Collapsed : Visibility.Visible;
        Placeholder.Visibility = count == 0 && Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Re-evaluate the × buttons' visibility.
        Chips.Items.Refresh();
    }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (InputHost.IsVisible) Input.Focus();
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
        => Placeholder.Visibility = (Tags?.Count ?? 0) == 0 && Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ",")
        {
            Commit();
            e.Handled = true;
        }
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true; // Enter adds a tag; it never reaches a default button
        }
        else if (e.Key == Key.Back && Input.Text.Length == 0 && Tags is { Count: > 0 } tags)
        {
            tags.RemoveAt(tags.Count - 1);
            e.Handled = true;
        }
    }

    private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    /// <summary>Adds what is typed as a tag (nothing for a blank or repeated one) and clears the box.</summary>
    private void Commit()
    {
        var typed = Input.Text;
        if (typed.Length == 0) return;
        Input.Clear();
        if (Tags is not { } tags || IsReadOnly) return;

        var tag = TagRules.Normalize([typed]).FirstOrDefault();
        if (tag is null || tags.Contains(tag) || tags.Count >= TagRules.MaxTags) return;
        tags.Add(tag);
    }

    private void RemoveChip_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is string tag && !IsReadOnly)
            Tags?.Remove(tag);
        if (InputHost.IsVisible) Input.Focus();
    }
}
