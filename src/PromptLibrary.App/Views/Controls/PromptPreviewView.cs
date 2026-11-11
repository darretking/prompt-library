using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// The prompt as it would come out, drawn from <see cref="PromptPreview"/> blocks in a read-only flow document:
/// placeholders are chips (a remembered value on BrandSoft, a muted outline chip with the field name when there is none,
/// a thin Brand caret for the cursor), headings 15 px semibold, <c>**bold**</c> semibold, bullets with a hanging indent
/// and fenced code in a CodeBlock box. Every colour is a theme token set as a resource reference, so a theme change
/// needs no rebuild.
/// </summary>
public sealed class PromptPreviewView : UserControl
{
    /// <summary>The line above the preview, so it is clear the chips show what was typed last time.</summary>
    public const string CaptionText = "Preview with your last-used values";

    private readonly FlowDocumentScrollViewer _viewer = new();

    public PromptPreviewView()
    {
        Focusable = false;
        IsTabStop = false;

        var caption = new TextBlock { Text = CaptionText, FontSize = 12, Margin = new Thickness(8, 8, 8, 4) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");

        _viewer.IsToolBarVisible = false;
        _viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _viewer.BorderThickness = new Thickness(0);
        _viewer.Background = Brushes.Transparent;
        _viewer.MinZoom = 100;
        _viewer.MaxZoom = 100;
        _viewer.SetResourceReference(FlowDocumentScrollViewer.SelectionBrushProperty, "Brush.BrandFill");
        _viewer.SelectionOpacity = 0.35;
        System.Windows.Automation.AutomationProperties.SetName(_viewer, "Prompt preview");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(caption);
        Grid.SetRow(_viewer, 1);
        grid.Children.Add(_viewer);
        Content = grid;
    }

    public new bool Focus() => _viewer.Focus();

    /// <summary>Draws <paramref name="blocks"/>, replacing what was shown.</summary>
    public void Show(IReadOnlyList<PreviewBlock> blocks)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(8, 4, 8, 8),
            FontSize = 13,
            LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity, // one column, however wide
            Background = Brushes.Transparent,
        };
        doc.SetResourceReference(FlowDocument.FontFamilyProperty, "Font.UI");
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Ink");

        var previous = PreviewBlockKind.Blank;
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case PreviewBlockKind.Blank:
                    doc.Blocks.Add(new Paragraph(new Run(" ")) { Margin = new Thickness(0), FontSize = 8, LineHeight = 8 });
                    break;
                case PreviewBlockKind.Heading:
                    var heading = new Paragraph
                    {
                        FontSize = 15, FontWeight = FontWeights.SemiBold, LineHeight = 24,
                        Margin = new Thickness(0, previous is PreviewBlockKind.Blank ? 0 : 8, 0, 4),
                    };
                    AddPieces(heading.Inlines, block.Pieces, 13);
                    doc.Blocks.Add(heading);
                    break;
                case PreviewBlockKind.Bullet:
                    doc.Blocks.Add(Bullet(block));
                    break;
                case PreviewBlockKind.Code:
                    doc.Blocks.Add(Code(block));
                    break;
                default:
                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    AddPieces(paragraph.Inlines, block.Pieces, 13);
                    doc.Blocks.Add(paragraph);
                    break;
            }
            previous = block.Kind;
        }
        _viewer.Document = doc;
    }

    /// <summary>One bullet or numbered line as its own list, so the text hangs after the marker and wraps under itself.</summary>
    private static List Bullet(PreviewBlock block)
    {
        var list = new List
        {
            Margin = new Thickness(0),
            Padding = new Thickness(20 + block.Level * 20, 0, 0, 0),
            MarkerOffset = 6,
            MarkerStyle = TextMarkerStyle.Disc,
        };
        if (block.Marker.Length > 1 && int.TryParse(block.Marker.TrimEnd('.'), out var number))
        {
            list.MarkerStyle = TextMarkerStyle.Decimal;
            list.StartIndex = number;
        }
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        AddPieces(paragraph.Inlines, block.Pieces, 13);
        list.ListItems.Add(new ListItem(paragraph));
        return list;
    }

    /// <summary>A fenced code block: the lines in a CodeBlock box with a Line frame, monospaced.</summary>
    private static BlockUIContainer Code(PreviewBlock block)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Prompt");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink");
        AddPieces(text.Inlines, block.Pieces, 12);
        var box = new Border
        {
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 4, 0, 4),
            Child = text,
        };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.CodeBlock");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        return new BlockUIContainer(box) { Margin = new Thickness(0) };
    }

    private static void AddPieces(InlineCollection inlines, IReadOnlyList<PreviewPiece> pieces, double chipSize)
    {
        foreach (var piece in pieces)
        {
            switch (piece.Kind)
            {
                case PreviewPieceKind.Text when piece.Text == "\n":
                    inlines.Add(new LineBreak());
                    break;
                case PreviewPieceKind.Text:
                    var run = new Run(piece.Text);
                    if (piece.Bold) run.FontWeight = FontWeights.SemiBold;
                    inlines.Add(run);
                    break;
                case PreviewPieceKind.Cursor:
                    inlines.Add(Caret());
                    break;
                default:
                    inlines.Add(Chip(piece, chipSize));
                    break;
            }
        }
    }

    private static InlineUIContainer Chip(PreviewPiece piece, double size)
    {
        var filled = piece.Kind == PreviewPieceKind.Filled;
        var label = new TextBlock { Text = piece.Text, FontSize = size, FontWeight = FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, filled ? "Brush.Brand" : "Brush.Muted");
        var chip = new Border
        {
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = label,
        };
        chip.SetResourceReference(Border.BackgroundProperty, filled ? "Brush.BrandSoft" : "Brush.Window");
        chip.SetResourceReference(Border.BorderBrushProperty, filled ? "Brush.BrandSoft" : "Brush.Line");
        // The tooltip says what a chip is, since a chip with a value no longer shows the field name.
        if (piece.Kind == PreviewPieceKind.Clipboard) chip.ToolTip = "Filled with the clipboard when you copy";
        else if (!filled) chip.ToolTip = "No value yet: you'll be asked when you copy";
        return new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>The <c>{cursor}</c> marker: a thin Brand caret, where the text cursor lands after pasting.</summary>
    private static InlineUIContainer Caret()
    {
        var caret = new Rectangle { Width = 2, Height = 16, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1, RadiusY = 1 };
        caret.SetResourceReference(Shape.FillProperty, "Brush.Brand");
        caret.ToolTip = "The cursor lands here after pasting";
        return new InlineUIContainer(caret) { BaselineAlignment = BaselineAlignment.Center };
    }
}
