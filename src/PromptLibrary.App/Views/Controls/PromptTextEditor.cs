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
/// coloured) with a two-way bindable <see cref="Text"/>, or, in preview mode, a read-only rendering of the result (see <see cref="PromptPreviewView"/>), placeholders
/// shown as chips. The text goes in and out exactly as stored (tabs, CRLF, emoji).
/// </summary>
public sealed class PromptTextEditor : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PromptTextEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(PromptTextEditor),
        new PropertyMetadata(false, (d, e) =>
        {
            var control = (PromptTextEditor)d;
            control._editor.IsReadOnly = (bool)e.NewValue;
            control.UpdateHint();
        }));

    public static readonly DependencyProperty IsPreviewProperty = DependencyProperty.Register(
        nameof(IsPreview), typeof(bool), typeof(PromptTextEditor),
        new PropertyMetadata(false, (d, _) => ((PromptTextEditor)d).UpdateMode()));

    /// <summary>Remembered placeholder values, shown in the preview's chips.</summary>
    public static readonly DependencyProperty PlaceholderValuesProperty = DependencyProperty.Register(
        nameof(PlaceholderValues), typeof(IDictionary<string, string>), typeof(PromptTextEditor),
        new PropertyMetadata(null, (d, _) => ((PromptTextEditor)d).RebuildPreview()));

    private readonly TextEditor _editor = new();
    private readonly PromptPreviewView _preview = new();
    private readonly TextBlock _hint = new()
    {
        Text = "Type or paste your prompt. Use {name} where something changes each time.",
        IsHitTestVisible = false,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Margin = new Thickness(9, 8, 9, 0),
        VerticalAlignment = VerticalAlignment.Top,
    };
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
        _editor.FontSize = 14;
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

        _preview.Visibility = Visibility.Collapsed;

        var grid = new Grid();
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        _hint.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Prompt");
        grid.Children.Add(_editor);
        grid.Children.Add(_hint);
        grid.Children.Add(_preview);
        _editor.TextChanged += (_, _) => UpdateHint();
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

    /// <summary>
    /// Asks for the height of its content (the text, or the rendered preview), up to the room it is given, instead of
    /// all of it: the slip around it (<see cref="SlipFrame"/>) is then as tall as the prompt, and a long prompt scrolls.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (Content is not UIElement inner) return base.MeasureOverride(constraint);
        inner.Measure(new Size(constraint.Width, double.PositiveInfinity));
        var wanted = inner.DesiredSize.Height;
        var height = double.IsInfinity(constraint.Height) ? wanted : Math.Min(wanted, constraint.Height);
        return base.MeasureOverride(new Size(constraint.Width, height));
    }

    /// <summary>The wrapped editor (tests and focus handling).</summary>
    internal TextEditor Editor => _editor;


    public new bool Focus() => IsPreview ? _preview.Focus() : _editor.TextArea.Focus();

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
        _preview.Visibility = IsPreview ? Visibility.Visible : Visibility.Collapsed;
        UpdateHint();
        if (IsPreview) RebuildPreview();
    }

    /// <summary>The faint line in an empty prompt box, so a new prompt's page isn't a blank void.</summary>
    private void UpdateHint()
        => _hint.Visibility = !IsPreview && !IsReadOnly && string.IsNullOrEmpty(_editor.Document.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        _preview.ShowCaption = PromptTemplate.HasFields(Text);
        _preview.Show(PromptPreview.Build(Text, PlaceholderValues, DateTime.Now));
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
