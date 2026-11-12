using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.ViewModels;

/// <summary>One input of the fill-in form: a text box (with a hint) or an editable drop-down.</summary>
public sealed class FillField : ObservableObject
{
    private string _value;
    private bool _listOpen;
    private string? _highlighted;

    public FillField(TemplateField field, string value)
    {
        Field = field;
        _value = value;
    }

    public TemplateField Field { get; }

    /// <summary>The key used to fill the text (for a choice, the whole <c>a / b</c> text).</summary>
    public string Name => Field.Name;

    /// <summary>The label above the box: the name with its first letter upper-case ("Number", "Who's going"); the rest keeps its casing.</summary>
    public string Label => Name.Length == 0 ? Name : char.ToUpperInvariant(Name[0]) + Name[1..];

    public bool IsChoice => Field.Kind == TemplateFieldKind.Choice;
    public bool IsText => !IsChoice;
    public IReadOnlyList<string> Options => Field.Options;

    /// <summary>The grey hint shown in an empty text box (<c>e.g. …</c>), or empty.</summary>
    public string Hint => Field.Hint ?? "";

    /// <summary>The grey hint shown in an empty choice field.</summary>
    public string ChoiceHint => "Choose or type";

    /// <summary>The text. A choice field is a plain text box too: nothing but the typed text ever changes it.</summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }

    // ---- Choice fields: type freely, with the options as autocomplete and a list to pick from ----------------------

    /// <summary>True while the list of options is open under the box.</summary>
    public bool IsListOpen
    {
        get => _listOpen;
        set
        {
            if (value && Options.Count == 0) value = false;
            if (!SetProperty(ref _listOpen, value)) return;
            if (value) Highlighted = Options.FirstOrDefault(o => Same(o, _value)) ?? Options[0];
        }
    }

    /// <summary>The option the arrow keys are on while the list is open.</summary>
    public string? Highlighted
    {
        get => _highlighted;
        set => SetProperty(ref _highlighted, value);
    }

    /// <summary>
    /// The option to complete <paramref name="typed"/> to (the first that starts with it and is longer), or null. The box
    /// then shows the rest selected, so the next letter replaces it and Tab or Enter keeps it.
    /// </summary>
    public string? CompletionFor(string typed)
        => typed.Length == 0 ? null
            : Options.FirstOrDefault(o => o.Length > typed.Length && o.StartsWith(typed, StringComparison.OrdinalIgnoreCase));

    /// <summary>Down opens the list; Down and Up then move through it, stopping at the ends.</summary>
    public void MoveHighlight(int delta)
    {
        if (Options.Count == 0) return;
        if (!IsListOpen) { IsListOpen = true; return; }
        var at = _highlighted is null ? -1 : Options.ToList().IndexOf(_highlighted);
        Highlighted = Options[Math.Clamp(at + delta, 0, Options.Count - 1)];
    }

    /// <summary>Enter in an open list: takes the highlighted option and closes the list. False when no list is open.</summary>
    public bool PickHighlighted()
    {
        if (!IsListOpen || _highlighted is null) return false;
        Pick(_highlighted);
        return true;
    }

    /// <summary>Takes an option (a click, or Enter on the highlighted one) and closes the list.</summary>
    public void Pick(string option)
    {
        Value = option;
        IsListOpen = false;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The placeholder fill-in form, independent of where it is shown (the search window now, a dialog of the main window
/// later). Input: the prompt text and the settings (remembered values live in <see cref="AppSettings.PlaceholderValues"/>).
/// Output: <see cref="Completed"/> with the filled text (and, if the prompt has <c>{cursor}</c>, how many Left key
/// presses put the caret there after pasting) or <see cref="Cancelled"/>. On completion the typed values are stored in
/// the settings object; the host saves settings.
/// </summary>
public sealed class FillInViewModel : ObservableObject
{
    private readonly string _text;
    private readonly AppSettings _settings;
    private readonly ITokenValues _tokens;

    public FillInViewModel(string promptText, AppSettings settings, ITokenValues tokens,
        string primaryLabel = "Paste", string title = "", string cancelLabel = "Back")
    {
        _text = promptText ?? "";
        _settings = settings;
        _tokens = tokens;
        PrimaryLabel = primaryLabel;
        Title = title;
        CancelLabel = cancelLabel;

        foreach (var field in PromptTemplate.Parse(_text))
        {
            var remembered = settings.PlaceholderValues.TryGetValue(field.Name.ToLowerInvariant(), out var v) ? v : "";
            var item = new FillField(field, remembered ?? "");
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FillField.Value)) return;
                OnPropertyChanged(nameof(PreviewSegments));
                OnPropertyChanged(nameof(PreviewText));
                OnPropertyChanged(nameof(BlankNote));
            };
            Fields.Add(item);
        }
    }

    public ObservableCollection<FillField> Fields { get; } = [];

    /// <summary>The label of the main button: <c>Paste</c>, <c>Copy</c> or <c>Send</c>.</summary>
    public string PrimaryLabel { get; }

    /// <summary>The prompt's title, shown above the fields when not empty.</summary>
    public string Title { get; }

    /// <summary>The label of the other button: <c>Back</c> in the search window, <c>Cancel</c> in a dialog.</summary>
    public string CancelLabel { get; }

    /// <summary>
    /// Under the preview, before Paste / Copy: <c>2 fields are blank: they stay as {…} in the prompt.</c> Empty when every
    /// field is filled.
    /// </summary>
    public string BlankNote
    {
        get
        {
            var blank = Fields.Count(f => string.IsNullOrWhiteSpace(f.Value));
            return blank switch
            {
                0 => "",
                1 => "1 field is blank: it stays as {…} in the prompt.",
                _ => $"{blank} fields are blank: they stay as {{…}} in the prompt.",
            };
        }
    }

    /// <summary>The prompt as it will come out now (unfilled blanks keep their <c>{…}</c>).</summary>
    public string PreviewText => Fill().Text;

    /// <summary>
    /// The same text as <see cref="PreviewText"/>, cut into pieces for drawing: plain text, a value the user filled in, and
    /// a blank still to fill (shown as a chip with the field's label). Rebuilt on every keystroke; raising
    /// <c>PreviewSegments</c> is what makes the view redraw.
    /// </summary>
    public IReadOnlyList<FillSegment> PreviewSegments
    {
        get
        {
            // Every field is filled with a private-use marker so one pass of the real fill (escapes, tokens, code spans)
            // decides where the fields land; the markers are then swapped for the value or the blank.
            var markers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Fields.Count; i++)
                markers[Fields[i].Name] = Open.ToString() + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + Close;

            var text = PromptTemplate.Fill(Clean(_text), markers, _tokens).Text;
            var segments = new List<FillSegment>();
            var at = 0;
            while (at < text.Length)
            {
                var open = text.IndexOf(Open, at);
                var close = open < 0 ? -1 : text.IndexOf(Close, open);
                if (open < 0 || close < 0) break;
                if (open > at) segments.Add(new FillSegment(FillSegmentKind.Text, text[at..open]));
                var slot = int.TryParse(text.AsSpan(open + 1, close - open - 1), out var index) && index < Fields.Count ? Fields[index] : null;
                if (slot is not null)
                    segments.Add(string.IsNullOrWhiteSpace(slot.Value)
                        ? new FillSegment(FillSegmentKind.Empty, slot.Label)
                        : new FillSegment(FillSegmentKind.Filled, slot.Value));
                at = close + 1;
            }
            if (at < text.Length) segments.Add(new FillSegment(FillSegmentKind.Text, text[at..]));
            return segments;
        }
    }

    private const char Open = '', Close = '';

    private static string Clean(string text) => text.Replace(Open.ToString(), "").Replace(Close.ToString(), "");

    /// <summary>True when <paramref name="field"/> is the last one (Enter there completes the form).</summary>
    public bool IsLast(FillField field) => Fields.Count > 0 && ReferenceEquals(Fields[^1], field);

    /// <summary>True when <paramref name="field"/> is the first one (Backspace in it, when empty, goes back).</summary>
    public bool IsFirst(FillField field) => Fields.Count > 0 && ReferenceEquals(Fields[0], field);

    public event Action<string, int?>? Completed;
    public event Action? Cancelled;

    /// <summary>Fills the prompt, remembers the values and raises <see cref="Completed"/>.</summary>
    public void Complete()
    {
        foreach (var field in Fields)
            if (!string.IsNullOrWhiteSpace(field.Value))
                _settings.PlaceholderValues[field.Name.ToLowerInvariant()] = field.Value;

        var result = Fill();
        Completed?.Invoke(result.Text, result.CaretBackSteps);
    }

    public void Cancel() => Cancelled?.Invoke();

    private FillResult Fill()
        => PromptTemplate.Fill(_text, Fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase), _tokens);
}

/// <summary>How a piece of the form's live preview is drawn.</summary>
public enum FillSegmentKind
{
    /// <summary>Plain prompt text.</summary>
    Text,

    /// <summary>What the user typed (or a remembered value): Ink on a faint BrandSoft chip.</summary>
    Filled,

    /// <summary>A blank still to fill: a Brand chip with the field's label.</summary>
    Empty,
}

/// <summary>One piece of the live preview.</summary>
public sealed record FillSegment(FillSegmentKind Kind, string Text);
