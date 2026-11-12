using System.Windows;
using System.Windows.Controls;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// A scroll viewer for the editor card's body. When the room is enough, its <see cref="HeightGate"/> content is as tall as
/// it wants (a short prompt gives a short card) or, when the content is taller than the room, exactly the room, so the
/// prompt slip takes what is left. Only when even the slip's least height does not fit does the body scroll, so nothing is
/// ever clipped at the minimum window size or at 200 % scaling.
/// </summary>
public sealed class FitScroll : ScrollViewer
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (Content is HeightGate gate && gate.Limit != constraint.Height)
        {
            gate.Limit = constraint.Height;
            gate.InvalidateMeasure();
        }
        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// The content of a <see cref="FitScroll"/>: a panel whose last <see cref="SlipFrame"/> is the part that gives way. Inside a
/// scroll viewer it is measured with unlimited height, so it applies the room (<see cref="Limit"/>) itself.
/// </summary>
public sealed class HeightGate : Decorator
{
    /// <summary>The height the scroll viewer has; set by <see cref="FitScroll"/>.</summary>
    public double Limit { get; set; } = double.PositiveInfinity;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not UIElement child) return default;
        child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        var wanted = child.DesiredSize.Height;
        if (wanted <= Limit || double.IsInfinity(Limit))
            return new Size(child.DesiredSize.Width, wanted);

        // The least height: everything else as it is, the slip at its floor.
        var least = wanted;
        if (child is Panel panel && panel.Children.OfType<SlipFrame>().LastOrDefault() is { } slip)
            least = wanted - slip.DesiredSize.Height + slip.MinContentHeight;

        var height = Math.Max(Limit, least);
        child.Measure(new Size(constraint.Width, height));
        return new Size(child.DesiredSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
