using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Fades the bottom 24 px of a list to transparent while it can scroll further, so a row that is cut off by the edge reads as
/// "more below" instead of as a clipped row. Set <c>ScrollFade.Enabled="True"</c> on a ListBox; the mask goes away at the end
/// of the list.
/// </summary>
public static class ScrollFade
{
    private const double FadeHeight = 24;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollFade), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= Hook;
        element.SizeChanged -= Resized;
        element.RemoveHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)Scrolled);
        if (e.NewValue is not true) { element.OpacityMask = null; return; }
        element.Loaded += Hook;
        element.SizeChanged += Resized;
        element.AddHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)Scrolled);
    }

    private static void Hook(object sender, RoutedEventArgs e) => Update((FrameworkElement)sender);

    private static void Resized(object sender, SizeChangedEventArgs e) => Update((FrameworkElement)sender);

    private static void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        if (sender is FrameworkElement element && e.OriginalSource is ScrollViewer) Update(element);
    }

    private static void Update(FrameworkElement element)
    {
        var scroller = Find(element);
        var height = element.ActualHeight;
        var more = scroller is { ScrollableHeight: > 0.5 } && scroller.VerticalOffset < scroller.ScrollableHeight - 0.5 && height > FadeHeight * 2;
        if (!more)
        {
            if (element.OpacityMask is not null) element.OpacityMask = null;
            return;
        }

        var edge = 1 - FadeHeight / height;
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, edge));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        mask.Freeze();
        element.OpacityMask = mask;
    }

    private static ScrollViewer? Find(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}
