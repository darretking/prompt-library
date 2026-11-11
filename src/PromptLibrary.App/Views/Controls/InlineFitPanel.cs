using System.Windows;
using System.Windows.Controls;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Lays out two children on one line: the first (a description that may be trimmed) and the second (tag chips) after it
/// with a gap, but only when both fit at their natural width. When they don't, the second is hidden (it takes no room)
/// and the first gets the whole line.
/// </summary>
public sealed class InlineFitPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(InlineFitPanel),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The space between the two children.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private bool _showSecond;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count == 0) return default;
        var first = InternalChildren[0];
        var second = InternalChildren.Count > 1 ? InternalChildren[1] : null;

        first.Measure(new Size(double.PositiveInfinity, available.Height));
        var firstWidth = first.DesiredSize.Width;
        var height = first.DesiredSize.Height;

        _showSecond = false;
        var secondWidth = 0.0;
        if (second is not null)
        {
            second.Measure(new Size(double.PositiveInfinity, available.Height));
            secondWidth = second.DesiredSize.Width;
            _showSecond = secondWidth > 0 && firstWidth + Gap + secondWidth <= available.Width;
            height = Math.Max(height, second.DesiredSize.Height);
            second.Opacity = _showSecond ? 1 : 0;
            second.IsHitTestVisible = _showSecond;
        }

        first.Measure(new Size(
            double.IsInfinity(available.Width) ? firstWidth : Math.Max(0, available.Width - (_showSecond ? Gap + secondWidth : 0)),
            available.Height));
        var width = _showSecond ? firstWidth + Gap + secondWidth : Math.Min(firstWidth, available.Width);
        return new Size(double.IsInfinity(width) ? 0 : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        var first = InternalChildren[0];
        var firstWidth = Math.Min(first.DesiredSize.Width, finalSize.Width);
        first.Arrange(new Rect(0, 0, firstWidth, finalSize.Height));

        if (InternalChildren.Count > 1)
        {
            var second = InternalChildren[1];
            second.Arrange(_showSecond
                ? new Rect(firstWidth + Gap, 0, second.DesiredSize.Width, finalSize.Height)
                : new Rect(0, 0, 0, 0));
        }
        return finalSize;
    }
}
