using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using PromptLibrary.App.Theming;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// The prompt text box of the main window: an AvalonEdit editor (placeholders, headings, fenced code and bold are
/// coloured) with a two-way bindable <see cref="Text"/>, or, in preview mode, a read-only rendering with placeholders
/// shown as chips. The text goes in and out exactly as stored (tabs, CRLF, emoji).
/// </summary>
public sealed class PromptTextEditor : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PromptTextEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(PromptTextEditor),
        new PropertyMetadata(false, (d, e) => ((PromptTextEditor)d)._editor.IsReadOnly = (bool)e.NewValue));

    public static readonly DependencyProperty IsPreviewProperty = DependencyProperty.Register(
        nameof(IsPreview), typeof(bool), typeof(PromptTextEditor),
        new PropertyMetadata(false, (d, _) => ((PromptTextEditor)d).UpdateMode()));

    /// <summary>Remembered placeholder values, shown in the preview's chips.</summary>
    public static readonly DependencyProperty PlaceholderValuesProperty = DependencyProperty.Register(
        nameof(PlaceholderValues), typeof(IDictionary<string, string>), typeof(PromptTextEditor),
        new PropertyMetadata(null, (d, _) => ((PromptTextEditor)d).RebuildPreview()));

    private readonly TextEditor _editor = new();
    private readonly ScrollViewer _previewScroll = new();
    private readonly TextBlock _preview = new();
    private readonly PromptColorizer _colorizer;
    private bool _syncing;

    public PromptTextEditor()
    {
        Focusable = false;
        IsTabStop = false;

        _editor.WordWrap = true;
        _editor.ShowLineNumbers = false;
        _editor.Background = Brushes.Transparent;
        _editor.BorderThickness = new Thickness(0);
        _editor.Padding = new Thickness(8);
        _editor.FontFamily = (FontFamily)(Application.Current?.TryFindResource("Font.Prompt")
                                           ?? new FontFamily("Cascadia Mono, Consolas"));
        _editor.FontSize = 13;
        _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.AllowToggleOverstrikeMode = false;
        _editor.SetResourceReference(Control.ForegroundProperty, "Brush.Ink");
        _editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "Brush.BrandFill");
        _editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionForegroundProperty, "Brush.OnBrand");
        _editor.TextArea.SelectionBorder = null;
        _editor.TextArea.SelectionCornerRadius = 0;
        _editor.TextArea.FocusVisualStyle = null;
        _editor.TextChanged += (_, _) => EditorTextChanged();
        _editor.PreviewKeyDown += Editor_PreviewKeyDown;

        _colorizer = new PromptColorizer();
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.Padding = new Thickness(8);
        _preview.FontSize = 13;
        _preview.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Prompt");
        _preview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink");
        _previewScroll.Content = _preview;
        _previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _previewScroll.Visibility = Visibility.Collapsed;

        var grid = new Grid();
        grid.Children.Add(_editor);
        grid.Children.Add(_previewScroll);
        Content = grid;

        Loaded += (_, _) =>
        {
            ThemeManager.Current.ThemeChanged += OnThemeChanged;
            ApplyTheme();
        };
        Unloaded += (_, _) => ThemeManager.Current.ThemeChanged -= OnThemeChanged;
        System.Windows.Automation.AutomationProperties.SetName(_editor.TextArea, "Prompt text");
    }

    /// <summary>The prompt text, exactly as in the editor.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsPreview
    {
        get => (bool)GetValue(IsPreviewProperty);
        set => SetValue(IsPreviewProperty, value);
    }

    public IDictionary<string, string>? PlaceholderValues
    {
        get => (IDictionary<string, string>?)GetValue(PlaceholderValuesProperty);
        set => SetValue(PlaceholderValuesProperty, value);
    }

    /// <summary>The wrapped editor (tests and focus handling).</summary>
    internal TextEditor Editor => _editor;


    public new bool Focus() => IsPreview ? _previewScroll.Focus() : _editor.TextArea.Focus();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PromptTextEditor)d;
        if (control._syncing) return;
        control._syncing = true;
        try
        {
            var text = (string?)e.NewValue ?? "";
            if (control._editor.Document.Text != text)
            {
                var caret = control._editor.CaretOffset;
                control._editor.Document.Text = text;
                // A new prompt (or a restore) is not something Ctrl+Z should walk back out of.
                control._editor.Document.UndoStack.ClearAll();
                control._editor.CaretOffset = Math.Min(caret, text.Length);
            }
            control.RebuildPreview();
        }
        finally { control._syncing = false; }
    }

    private void EditorTextChanged()
    {
        if (_syncing) return;
        _syncing = true;
        try { SetCurrentValue(TextProperty, _editor.Document.Text); }
        finally { _syncing = false; }
    }

    /// <summary>
    /// Tab and Shift+Tab leave the editor like any other field; Ctrl+Tab types a tab character. Everything else
    /// (Ctrl+S, Ctrl+Shift+C, ...) is left alone, so the window's shortcuts still arrive.
    /// </summary>
    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control)
        {
            if (!_editor.IsReadOnly) _editor.TextArea.PerformTextInput("\t");
        }
        else
        {
            _editor.TextArea.MoveFocus(new TraversalRequest(
                modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
        }
        e.Handled = true;
    }

    private void UpdateMode()
    {
        _editor.Visibility = IsPreview ? Visibility.Collapsed : Visibility.Visible;
        _previewScroll.Visibility = IsPreview ? Visibility.Visible : Visibility.Collapsed;
        if (IsPreview) RebuildPreview();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyTheme);

    private void ApplyTheme()
    {
        _editor.TextArea.Caret.CaretBrush = Application.Current?.TryFindResource("Brush.Ink") as Brush ?? Brushes.Black;
        _colorizer.Refresh();
        _editor.TextArea.TextView.Redraw();
        RebuildPreview();
    }

    // ---- Preview -------------------------------------------------------------------------------

    private void RebuildPreview()
    {
        if (!IsPreview) return;
        _preview.Inlines.Clear();
        var text = Text ?? "";
        var spans = PromptHighlighting.Compute(text);
        var values = PlaceholderValues;
        var chipBackground = Resource("Brush.BrandSoft");
        var chipForeground = Resource("Brush.Brand");
        var codeBackground = Resource("Brush.CodeBlock");

        var lines = SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            var (start, length) = lines[i];
            var inCode = spans.Any(s => s.Kind == HighlightKind.CodeBlock && start >= s.Start && start < s.End + 1);
            var heading = spans.Any(s => s.Kind == HighlightKind.Heading && s.Start == start);
            AppendLine(text, start, length, spans, values, chipBackground, chipForeground, codeBackground, inCode, heading);
            if (i < lines.Count - 1) _preview.Inlines.Add(new LineBreak());
        }
    }

    private void AppendLine(string text, int start, int length, IReadOnlyList<HighlightSpan> spans,
        IDictionary<string, string>? values, Brush? chipBackground, Brush? chipForeground, Brush? codeBackground,
        bool inCode, bool heading)
    {
        var end = start + length;
        var inline = spans
            .Where(s => s.Kind is HighlightKind.Placeholder or HighlightKind.Bold && s.Start >= start && s.End <= end)
            .OrderBy(s => s.Start).ThenByDescending(s => s.Length).ToList();

        void Plain(string s, bool bold)
        {
            if (s.Length == 0) return;
            var run = new Run(s);
            if (bold || heading) run.FontWeight = FontWeights.SemiBold;
            if (inCode && codeBackground is not null) run.Background = codeBackground;
            _preview.Inlines.Add(run);
        }

        var cursor = start;
        foreach (var span in inline)
        {
            if (span.Start < cursor) continue; // inside one already written
            Plain(text[cursor..span.Start], false);
            var token = text.Substring(span.Start, span.Length);
            if (span.Kind == HighlightKind.Bold)
                Plain(token[2..^2], true);
            else
                _preview.Inlines.Add(Chip(token, values, chipBackground, chipForeground));
            cursor = span.End;
        }
        Plain(text[cursor..end], false);
    }

    private static InlineUIContainer Chip(string token, IDictionary<string, string>? values, Brush? background, Brush? foreground)
    {
        var name = token.Trim('{', '}').Trim();
        var shown = values is not null && values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : token;
        var label = new TextBlock
        {
            Text = shown,
            Foreground = foreground,
            Margin = new Thickness(4, 0, 4, 0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        };
        return new InlineUIContainer(new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(4),
            Child = label,
        })
        { BaselineAlignment = BaselineAlignment.Center };
    }

    private static List<(int Start, int Length)> SplitLines(string text)
    {
        var lines = new List<(int, int)>();
        var pos = 0;
        while (true)
        {
            var end = pos;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            lines.Add((pos, end - pos));
            if (end >= text.Length) break;
            pos = end;
            if (text[pos] == '\r') pos++;
            if (pos < text.Length && text[pos] == '\n') pos++;
            if (pos >= text.Length) { lines.Add((pos, 0)); break; }
        }
        return lines;
    }

    private static Brush? Resource(string key) => Application.Current?.TryFindResource(key) as Brush;

    // ---- Colouring -----------------------------------------------------------------------------

    /// <summary>Applies <see cref="PromptHighlighting"/> to the editor's lines, with colours from the current theme.</summary>
    private sealed class PromptColorizer : DocumentColorizingTransformer
    {
        private IReadOnlyList<HighlightSpan> _spans = [];
        private string? _spansFor;
        private Brush? _chipBackground, _chipForeground, _codeBackground;

        public PromptColorizer() => Refresh();

        public void Refresh()
        {
            _chipBackground = Resource("Brush.BrandSoft");
            _chipForeground = Resource("Brush.Brand");
            _codeBackground = Resource("Brush.CodeBlock");
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.Text;
            if (!ReferenceEquals(text, _spansFor) && text != _spansFor)
            {
                _spans = PromptHighlighting.Compute(text);
                _spansFor = text;
            }

            var lineEnd = line.Offset + line.Length;
            foreach (var span in _spans)
            {
                var start = Math.Max(span.Start, line.Offset);
                var end = Math.Min(span.End, lineEnd);
                if (start >= end) continue;

                switch (span.Kind)
                {
                    case HighlightKind.Placeholder:
                        ChangeLinePart(start, end, e =>
                        {
                            if (_chipBackground is not null) e.TextRunProperties.SetBackgroundBrush(_chipBackground);
                            if (_chipForeground is not null) e.TextRunProperties.SetForegroundBrush(_chipForeground);
                        });
                        break;
                    case HighlightKind.Heading:
                    case HighlightKind.Bold:
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetTypeface(SemiBold(e.TextRunProperties.Typeface)));
                        break;
                    case HighlightKind.CodeBlock when _codeBackground is not null:
                        ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(_codeBackground));
                        break;
                }
            }
        }

        private static Typeface SemiBold(Typeface t)
            => new(t.FontFamily, t.Style, FontWeights.SemiBold, t.Stretch);
    }
}
