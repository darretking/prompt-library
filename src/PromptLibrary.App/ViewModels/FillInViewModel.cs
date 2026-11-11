using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.ViewModels;

/// <summary>One input of the fill-in form: a text box (with a hint) or an editable drop-down.</summary>
public sealed class FillField : ObservableObject
{
    private string _value;

    public FillField(TemplateField field, string value)
    {
        Field = field;
        _value = value;
    }

    public TemplateField Field { get; }

    /// <summary>The key used to fill the text (for a choice, the whole <c>a / b</c> text).</summary>
    public string Name => Field.Name;

    public bool IsChoice => Field.Kind == TemplateFieldKind.Choice;
    public bool IsText => !IsChoice;
    public IReadOnlyList<string> Options => Field.Options;

    /// <summary>The grey hint shown in an empty text box (<c>e.g. …</c>), or empty.</summary>
    public string Hint => Field.Hint ?? "";

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }
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
        string primaryLabel = "Paste", string title = "")
    {
        _text = promptText ?? "";
        _settings = settings;
        _tokens = tokens;
        PrimaryLabel = primaryLabel;
        Title = title;

        foreach (var field in PromptTemplate.Parse(_text))
        {
            var remembered = settings.PlaceholderValues.TryGetValue(field.Name.ToLowerInvariant(), out var v) ? v : "";
            var item = new FillField(field, remembered ?? "");
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PreviewText));
            Fields.Add(item);
        }
    }

    public ObservableCollection<FillField> Fields { get; } = [];

    /// <summary>The label of the main button: <c>Paste</c>, <c>Copy</c> or <c>Send</c>.</summary>
    public string PrimaryLabel { get; }

    /// <summary>The prompt's title, shown above the fields when not empty.</summary>
    public string Title { get; }

    /// <summary>The prompt as it will come out now (unfilled blanks keep their <c>{…}</c>).</summary>
    public string PreviewText => Fill().Text;

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
