using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>How a highlighted piece of an <see cref="InlineText"/> looks.</summary>
public enum InlineHighlight
{
    /// <summary>Brand-coloured text (matched search letters).</summary>
    Match,

    /// <summary>Brand text on a soft brand fill (a placeholder in the preview).</summary>
    Chip,
}

/// <summary>
/// Attached properties that fill a TextBlock from <see cref="TextSegment"/>s, so matched letters and placeholder chips
/// can be shown with theme brushes (DynamicResource) from a view model.
/// </summary>
public static class InlineText
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IEnumerable<TextSegment>), typeof(InlineText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight", typeof(InlineHighlight), typeof(InlineText), new PropertyMetadata(InlineHighlight.Match, OnChanged));

    public static IEnumerable<TextSegment>? GetSegments(DependencyObject d) => (IEnumerable<TextSegment>?)d.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject d, IEnumerable<TextSegment>? value) => d.SetValue(SegmentsProperty, value);

    public static InlineHighlight GetHighlight(DependencyObject d) => (InlineHighlight)d.GetValue(HighlightProperty);

    public static void SetHighlight(DependencyObject d, InlineHighlight value) => d.SetValue(HighlightProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        if (GetSegments(block) is not { } segments) return;

        var chip = GetHighlight(block) == InlineHighlight.Chip;
        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.Highlight)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Brand");
                if (chip)
                {
                    run.SetResourceReference(TextElement.BackgroundProperty, "Brush.BrandSoft");
                    run.FontWeight = FontWeights.SemiBold;
                }
            }
            block.Inlines.Add(run);
        }
    }
}
