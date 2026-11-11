using System.Windows;
using System.Windows.Controls;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Holds the prompt slip. It is as tall as its content wants, but never shorter than <see cref="MinContentHeight"/> (about
/// eight lines) and never taller than the room it is given: a short prompt gets a modest page, a long one fills the card.
/// </summary>
public sealed class SlipFrame : Decorator
{
    public static readonly DependencyProperty MinContentHeightProperty = DependencyProperty.Register(
        nameof(MinContentHeight), typeof(double), typeof(SlipFrame),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The least height (when the room allows): roughly eight lines of text plus the slip's padding.</summary>
    public double MinContentHeight
    {
        get => (double)GetValue(MinContentHeightProperty);
        set => SetValue(MinContentHeightProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Child is null) return default;
        Child.Measure(available); // the child sees the room there is, so a long text scrolls instead of growing past it
        var height = Math.Max(Child.DesiredSize.Height, MinContentHeight);
        if (!double.IsInfinity(available.Height)) height = Math.Min(height, available.Height);
        return new Size(Child.DesiredSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
