using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The right-hand editor: the fields of one prompt (or of a new, not yet saved prompt), whether they
/// differ from what is saved, inline validation messages and the Copy button's label.
/// </summary>
public sealed class PromptEditorViewModel : ObservableObject
{
    private static readonly TimeSpan CopiedLabelDuration = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherTimer _copiedTimer;

    private string _title = "";
    private string _description = "";
    private Guid? _categoryId;
    private string _text = "";

    private string _savedTitle = "";
    private string _savedDescription = "";
    private Guid? _savedCategoryId;
    private string _savedText = "";

    private string? _titleError;
    private string? _textError;
    private string? _categoryError;
    private bool _showCopied;

    private PromptEditorViewModel(Guid? promptId)
    {
        PromptId = promptId;
        _copiedTimer = new DispatcherTimer { Interval = CopiedLabelDuration };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            ShowCopied = false;
        };
    }

    /// <summary>An editor showing a saved prompt.</summary>
    public static PromptEditorViewModel ForPrompt(Prompt prompt)
    {
        var editor = new PromptEditorViewModel(prompt.Id);
        editor.LoadSaved(prompt);
        return editor;
    }

    /// <summary>An empty editor for a prompt that will be created in <paramref name="categoryId"/> on Save.</summary>
    public static PromptEditorViewModel ForNewPrompt(Guid categoryId)
    {
        var editor = new PromptEditorViewModel(promptId: null);
        editor._categoryId = editor._savedCategoryId = categoryId;
        return editor;
    }

    /// <summary>The saved prompt's id; null until a new prompt is first saved.</summary>
    public Guid? PromptId { get; private set; }

    public bool IsNew => PromptId is null;

    public string Title
    {
        get => _title;
        set
        {
            if (!SetProperty(ref _title, value ?? "")) return;
            TitleError = null;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value ?? ""))
                OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>
    /// The chosen category. Null from the combo box is ignored: it only happens while the list of
    /// categories is being rebuilt, never as a user choice.
    /// </summary>
    public Guid? CategoryId
    {
        get => _categoryId;
        set
        {
            if (value is null || !SetProperty(ref _categoryId, value)) return;
            CategoryError = null;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? "")) return;
            TextError = null;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(TextStats));
            OnPropertyChanged(nameof(CanCopy));
        }
    }

    /// <summary>True when any field differs from what is saved.</summary>
    public bool IsDirty =>
        _title != _savedTitle
        || _description != _savedDescription
        || _categoryId != _savedCategoryId
        || _text != _savedText;

    /// <summary>Live count above the prompt slip, e.g. <c>412 characters · 9 lines</c>.</summary>
    public string TextStats
    {
        get
        {
            // A line break counts as one character, however it is stored.
            var normalised = _text.Replace("\r\n", "\n");
            var characters = normalised.Length;
            var lines = characters == 0 ? 0 : normalised.Count(c => c == '\n') + 1;
            return $"{Plural(characters, "character")} · {Plural(lines, "line")}";
        }
    }

    public bool CanCopy => _text.Length > 0;

    public string? TitleError
    {
        get => _titleError;
        set => SetProperty(ref _titleError, value);
    }

    public string? TextError
    {
        get => _textError;
        set => SetProperty(ref _textError, value);
    }

    public string? CategoryError
    {
        get => _categoryError;
        set => SetProperty(ref _categoryError, value);
    }

    /// <summary>"Copy prompt", or "Copied" for 1.5 s after a successful copy.</summary>
    public string CopyLabel => _showCopied ? "Copied" : "Copy prompt";

    private bool ShowCopied
    {
        set
        {
            _showCopied = value;
            OnPropertyChanged(nameof(CopyLabel));
        }
    }

    /// <summary>The title to show in messages, even when the field is still blank.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(_title) ? "Untitled prompt" : _title.Trim();

    /// <summary>Checks the fields and sets the inline messages. True when everything is fine.</summary>
    public bool Validate()
    {
        TitleError = string.IsNullOrWhiteSpace(_title) ? "Give the prompt a title." : null;
        TextError = string.IsNullOrWhiteSpace(_text) ? "The prompt can't be empty." : null;
        CategoryError = _categoryId is null ? "Choose a category." : null;
        return TitleError is null && TextError is null && CategoryError is null;
    }

    /// <summary>
    /// Shows a validation failure from <c>LibraryEditor</c> under the matching field.
    /// False if the message doesn't belong to a field (the caller shows it instead).
    /// </summary>
    public bool ShowValidationError(ArgumentException error)
    {
        switch (error.ParamName)
        {
            case "title":
                TitleError = "Give the prompt a title.";
                return true;
            case "text":
                TextError = "The prompt can't be empty.";
                return true;
            case "categoryId":
                CategoryError = "Choose a category.";
                return true;
            default:
                return false;
        }
    }

    /// <summary>Records the id a new prompt got when it was added to the library.</summary>
    public void MarkCreated(Guid promptId)
    {
        PromptId = promptId;
        OnPropertyChanged(nameof(PromptId));
        OnPropertyChanged(nameof(IsNew));
    }

    /// <summary>Takes the saved prompt's values as both the fields and the "saved" baseline.</summary>
    public void LoadSaved(Prompt prompt)
    {
        _savedTitle = prompt.Title;
        _savedDescription = prompt.Description;
        _savedCategoryId = prompt.CategoryId;
        _savedText = prompt.Text;
        Revert();
    }

    /// <summary>Puts every field back to its saved value and clears the messages.</summary>
    public void Revert()
    {
        Title = _savedTitle;
        Description = _savedDescription;
        _categoryId = _savedCategoryId;
        OnPropertyChanged(nameof(CategoryId));
        Text = _savedText;
        TitleError = TextError = CategoryError = null;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// A category was deleted: wherever the editor (or its saved state) pointed at it, point at
    /// <paramref name="replacement"/> instead (null when nothing was moved, which leaves the
    /// field empty so Save asks for a category).
    /// </summary>
    public void ReplaceCategory(Guid deletedId, Guid? replacement)
    {
        if (_savedCategoryId == deletedId) _savedCategoryId = replacement;
        if (_categoryId == deletedId) _categoryId = replacement;
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Re-sends the category to the combo box after its list of choices was rebuilt.</summary>
    public void RefreshCategoryChoice() => OnPropertyChanged(nameof(CategoryId));

    public void ShowCopiedFeedback()
    {
        ShowCopied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";
}
