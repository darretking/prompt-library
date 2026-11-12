using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Draws the fill-in form's live preview into a TextBlock: plain text as it is, a blank still to fill as a Brand chip
/// (its label on BrandSoft), a value the user typed in Ink on a faint BrandSoft chip. Bind <c>Segments</c> to
/// <see cref="FillInViewModel.PreviewSegments"/>; it is rebuilt on every change, so the preview follows each keystroke.
/// Colours are theme tokens set as resource references.
/// </summary>
public static class FillPreview
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IReadOnlyList<FillSegment>), typeof(FillPreview), new PropertyMetadata(null, OnSegmentsChanged));

    public static IReadOnlyList<FillSegment>? GetSegments(DependencyObject d) => (IReadOnlyList<FillSegment>?)d.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject d, IReadOnlyList<FillSegment>? value) => d.SetValue(SegmentsProperty, value);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<FillSegment> segments) return;
        foreach (var segment in segments)
        {
            if (segment.Kind == FillSegmentKind.Text)
                block.Inlines.Add(new System.Windows.Documents.Run(segment.Text));
            else
                block.Inlines.Add(Chip(segment));
        }
    }

    private static System.Windows.Documents.InlineUIContainer Chip(FillSegment segment)
    {
        var empty = segment.Kind == FillSegmentKind.Empty;
        var label = new TextBlock { Text = segment.Text, FontWeight = empty ? FontWeights.SemiBold : FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, empty ? "Brush.Brand" : "Brush.Ink");
        var chip = new Border
        {
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(4),
            Child = label,
        };
        chip.SetResourceReference(Border.BackgroundProperty, empty ? "Brush.BrandSoft" : "Brush.Hover");
        return new System.Windows.Documents.InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }
}
