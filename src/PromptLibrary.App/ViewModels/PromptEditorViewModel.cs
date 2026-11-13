using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// The right-hand editor: the fields of one prompt (or of a new, not yet saved prompt), whether they
/// differ from what is saved, inline validation messages and the Copy button's label.
/// </summary>
public sealed class PromptEditorViewModel : ObservableObject
{
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherTimer _copiedTimer;
    private readonly DispatcherTimer _savedTimer;

    private string _title = "";
    private string _description = "";
    private Guid? _categoryId;
    private string _text = "";
    private string? _sendTo;

    private List<string> _savedTags = [];
    private string? _savedSendTo;
    private string _savedTitle = "";
    private string _savedDescription = "";
    private Guid? _savedCategoryId;
    private string _savedText = "";

    private string? _titleError;
    private string? _textError;
    private string? _categoryError;
    private bool _showCopied;
    private bool _showSaved;
    private bool _isReadOnly;
    private bool _isPreview;
    private DateTimeOffset? _updatedAt;
    private int _uses;

    private PromptEditorViewModel(Guid? promptId)
    {
        PromptId = promptId;
        Tags.CollectionChanged += OnTagsChanged;
        _copiedTimer = new DispatcherTimer { Interval = FeedbackDuration };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            ShowCopied = false;
        };
        _savedTimer = new DispatcherTimer { Interval = FeedbackDuration };
        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            ShowSaved = false;
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

    /// <summary>
    /// True for a team prompt while the team library isn't being edited: the fields show the saved text as normal,
    /// selectable text and can't be changed.
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (!SetProperty(ref _isReadOnly, value)) return;
            OnPropertyChanged(nameof(IsEditable));
        }
    }


    /// <summary>The opposite of <see cref="IsReadOnly"/> (for bindings).</summary>
    public bool IsEditable => !_isReadOnly;

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

    /// <summary>True while the prompt box shows the read-only preview (placeholders as chips) instead of the editor.</summary>
    public bool IsPreview
    {
        get => _isPreview;
        set
        {
            if (!SetProperty(ref _isPreview, value)) return;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(ShowViewToggle));
        }
    }

    /// <summary>The opposite of <see cref="IsPreview"/> (for the Edit side of the Edit | Preview switch).</summary>
    public bool IsEditMode
    {
        get => !_isPreview;
        set => IsPreview = !value;
    }

    /// <summary>The prompt's tags, in order (kept normalised by the tag box; <see cref="TagRules"/> on save).</summary>
    public ObservableCollection<string> Tags { get; } = [];

    /// <summary>The prompt's own default Send to target (an id from <c>SendTargets</c>), or null for the app default.</summary>
    public string? SendTo
    {
        get => _sendTo;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value;
            if (!SetProperty(ref _sendTo, value)) return;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    private void OnTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_loadingTags) OnPropertyChanged(nameof(IsDirty));
    }

    private bool _loadingTags;

    private void SetTags(IEnumerable<string> tags)
    {
        _loadingTags = true;
        try
        {
            Tags.Clear();
            foreach (var tag in tags) Tags.Add(tag);
        }
        finally { _loadingTags = false; }
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
            OnPropertyChanged(nameof(ShowViewToggle));
        }
    }

    /// <summary>True when any field differs from what is saved.</summary>
    public bool IsDirty =>
        _title != _savedTitle
        || _description != _savedDescription
        || _categoryId != _savedCategoryId
        || _text != _savedText
        || _sendTo != _savedSendTo
        || !Tags.SequenceEqual(_savedTags);

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

    /// <summary>
    /// The Edit | Preview switch is only offered when there is something to preview: the prompt has {fields} or markdown
    /// (it stays while Preview is on, so the way back is never hidden).
    /// </summary>
    public bool ShowViewToggle => _isPreview || PromptTemplate.HasFields(_text) || MarkdownHint.Looks(_text);

    /// <summary>How many times the prompt was copied or sent (for <see cref="MetaText"/>); set by the main view model.</summary>
    public int Uses
    {
        get => _uses;
        set
        {
            if (SetProperty(ref _uses, value)) OnPropertyChanged(nameof(MetaText));
        }
    }

    /// <summary>The quiet line under the prompt, e.g. <c>Updated 2 days ago · Used 5 times</c>; empty for a new prompt.</summary>
    public string MetaText
    {
        get
        {
            if (_updatedAt is not { } updated || updated == default) return "";
            var text = "Updated " + HistoryViewModel.RelativeAge(updated, DateTimeOffset.Now);
            return _uses > 0 ? $"{text} · Used {(_uses == 1 ? "once" : _uses + " times")}" : text;
        }
    }

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

    /// <summary>"Save", or "Saved" for 1.5 s after a successful save.</summary>
    public string SaveLabel => IsShowingSaved ? "Saved" : "Save";

    /// <summary>True while the Save button says "Saved" (it is shown at full strength although it is disabled).</summary>
    public bool IsShowingSaved => _showSaved && !IsDirty;

    private bool ShowSaved
    {
        set
        {
            _showSaved = value;
            OnPropertyChanged(nameof(SaveLabel));
            OnPropertyChanged(nameof(IsShowingSaved));
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

    /// <summary>
    /// The saved prompt is gone (another editor deleted it) while the fields hold unsaved changes: they stay, as a new
    /// prompt that Save adds in <paramref name="categoryId"/> (null when there's no category left; Save then asks for one).
    /// </summary>
    public void BecomeNew(Guid? categoryId)
    {
        PromptId = null;
        _savedTitle = _savedDescription = _savedText = "";
        _savedTags = [];
        _savedSendTo = null;
        _savedCategoryId = categoryId;
        _categoryId = categoryId;
        OnPropertyChanged(nameof(PromptId));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Takes the saved prompt's values as both the fields and the "saved" baseline.</summary>
    public void LoadSaved(Prompt prompt)
    {
        _savedTitle = prompt.Title;
        _savedDescription = prompt.Description;
        _savedCategoryId = prompt.CategoryId;
        _savedText = prompt.Text;
        _savedTags = [.. prompt.Tags ?? []];
        _savedSendTo = prompt.SendTo;
        _updatedAt = prompt.UpdatedAt;
        OnPropertyChanged(nameof(MetaText));
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
        SetTags(_savedTags);
        SendTo = _savedSendTo;
        TitleError = TextError = CategoryError = null;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// Version history's "Restore this version": the fields take the old version's values as unsaved changes
    /// (the category and default send target stay as they are).
    /// </summary>
    public void ApplyVersion(PromptVersion version)
    {
        Title = version.Title;
        Description = version.Description;
        Text = version.Text;
        SetTags(TagRules.Normalize(version.Tags));
        TitleError = TextError = null;
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

    /// <summary>Stops the "Copied" timer; called when this editor is replaced or closed.</summary>
    public void StopTimers()
    {
        _copiedTimer.Stop();
        _savedTimer.Stop();
    }

    public void ShowSavedFeedback()
    {
        ShowSaved = true;
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    public void ShowCopiedFeedback()
    {
        ShowCopied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    /// <summary>
    /// What the always-visible Save button says: "Save" while there is something to save (or the prompt is new), else
    /// "Saved", so the header always tells the user whether their edits are kept.
    /// </summary>
    public string SaveStateText => IsDirty || IsNew ? "Save" : "Saved";

    /// <summary>True when everything is saved: the Save button shows a check and "Saved".</summary>
    public bool IsAllSaved => !IsDirty && !IsNew;

    /// <summary>The opposite of <see cref="IsAllSaved"/>: the Save button is enabled and shows the unsaved dot.</summary>
    public bool HasSomethingToSave => !IsAllSaved;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsDirty) or nameof(IsNew))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SaveStateText)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsAllSaved)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasSomethingToSave)));
        }
        // An edit straight after saving turns "Saved" back into "Save".
        if (e.PropertyName == nameof(IsDirty) && _showSaved)
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SaveLabel)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsShowingSaved)));
        }
    }

    private static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";
}
