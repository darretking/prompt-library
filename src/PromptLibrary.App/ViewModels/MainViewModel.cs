using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// State and actions of the main window: category rail, prompt list with search, and the editor.
/// Every successful change is saved through <see cref="AppHost.SaveLibrary"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const string AppTitle = "Prompt Library";
    private static readonly StringComparer NameOrder = StringComparer.CurrentCultureIgnoreCase;
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(6);

    private readonly AppHost _host;
    private readonly IDialogs _dialogs;
    private readonly DispatcherTimer _statusTimer;
    private string? _statusText;

    // The user answered Cancel to "Save changes?" while the search was hiding the open prompt: the editor stays
    // and the search doesn't ask again until the editor changes or the search is cleared.
    private bool _searchKeepsEditor;

    private CategoryItem? _selectedCategory;
    private Prompt? _selectedPrompt;
    private string _searchText = "";
    private PromptEditorViewModel? _editor;
    private bool _isAddingCategory;
    private string _newCategoryName = "";
    private string? _newCategoryError;

    // True while the lists are being rebuilt; the list boxes push null selections then, which are ignored.
    private bool _refreshing;

    // True while a deferred "Save changes?" is on screen.
    private bool _asking;

    // The editor's prompt as it is on disk, while a change to it is in memory but failed to write.
    // "Don't save" restores it, so the change is never written later by another save.
    private PromptSnapshot? _unwritten;

    public MainViewModel(AppHost host, IDialogs dialogs)
    {
        _host = host;
        _dialogs = dialogs;

        NewCategoryCommand = new RelayCommand(StartNewCategory);
        RenameCategoryCommand = new RelayCommand<CategoryItem?>(item => StartRename(item ?? SelectedCategory));
        DeleteCategoryCommand = new RelayCommand<CategoryItem?>(item => DeleteCategory(item ?? SelectedCategory));
        NewPromptCommand = new RelayCommand(NewPrompt);
        SaveCommand = new RelayCommand(() => Save());
        CopyCommand = new RelayCommand(Copy);
        DeleteCommand = new RelayCommand(Delete);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        BackupNowCommand = new RelayCommand(() => _ = _host.RunBackupAsync(BackupTrigger.Manual));
        RestoreCommand = new RelayCommand(_host.OpenRestore);
        OpenSettingsCommand = new RelayCommand(() => _host.OpenSettings());
        SetUpBackupsCommand = new RelayCommand(() => _host.OpenSettings(focusBackups: true));
        ExitCommand = new RelayCommand(_host.ExitApp);
        SetupGuideCommand = new RelayCommand(_host.OpenOnboarding);
        AboutCommand = new RelayCommand(_host.ShowAbout);

        _host.BackupStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BackupWarning));
            OnPropertyChanged(nameof(BackupWarningDetail));
            OnPropertyChanged(nameof(ShowBackupWarning));
        };

        _statusTimer = new DispatcherTimer { Interval = StatusDuration };
        _statusTimer.Tick += (_, _) => StatusText = null;

        RefreshAll();
    }

    /// <summary>Raised after a new prompt is started, so the view can put the cursor in Title.</summary>
    public event EventHandler? TitleFocusRequested;

    public ObservableCollection<CategoryItem> Categories { get; } = new();

    /// <summary>The categories the editor's Category box offers (A–Z, no "All prompts").</summary>
    public ObservableCollection<Category> CategoryChoices { get; } = new();

    public ObservableCollection<Prompt> Prompts { get; } = new();

    public IRelayCommand NewCategoryCommand { get; }
    public IRelayCommand<CategoryItem?> RenameCategoryCommand { get; }
    public IRelayCommand<CategoryItem?> DeleteCategoryCommand { get; }
    public IRelayCommand NewPromptCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand BackupNowCommand { get; }
    public IRelayCommand RestoreCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand SetUpBackupsCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand SetupGuideCommand { get; }
    public IRelayCommand AboutCommand { get; }

    public CategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_refreshing || value is null || ReferenceEquals(value, _selectedCategory)) return;
            if (EditorSurvivesCategory(value.Id) || _editor is not { IsDirty: true })
                ShowCategory(value.Id);
            else
                AfterAskingToSave(() => ShowCategory(value.Id), nameof(SelectedCategory));
        }
    }

    public Prompt? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            if (_refreshing || ReferenceEquals(value, _selectedPrompt)) return;

            // Clearing the selection, or re-selecting what the editor already shows, keeps the editor.
            if (value is null || _editor?.PromptId == value.Id)
            {
                _selectedPrompt = value;
                OnPropertyChanged();
                return;
            }

            if (_editor is { IsDirty: true })
                AfterAskingToSave(() => OpenPrompt(value), nameof(SelectedPrompt));
            else
                OpenPrompt(value);
        }
    }

    /// <summary>Filters the list by title, description and text within the selected category.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? "")) return;
            RefreshPrompts();
            OnPropertyChanged(nameof(HasSearchText));
            FollowSearch();
        }
    }

    /// <summary>Shows the search box's clear (×) button.</summary>
    public bool HasSearchText => _searchText.Length > 0;

    public PromptEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (ReferenceEquals(value, _editor)) return;
            if (_editor is not null)
            {
                _editor.PropertyChanged -= OnEditorChanged;
                _editor.StopTimers();
            }
            _editor = value;
            _searchKeepsEditor = false;
            if (_editor is not null) _editor.PropertyChanged += OnEditorChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEditor));
            OnPropertyChanged(nameof(HasNoEditor));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool HasEditor => _editor is not null;
    public bool HasNoEditor => _editor is null;

    /// <summary>"Prompt Library", with a "• " prefix while the editor has unsaved changes.</summary>
    public string WindowTitle => _editor is { IsDirty: true } ? "• " + AppTitle : AppTitle;

    public bool ShowNoPromptsYet => Prompts.Count == 0 && string.IsNullOrWhiteSpace(_searchText);

    /// <summary>The list's "+ New prompt" footer; hidden while the empty state shows its own New prompt button.</summary>
    public bool ShowNewPromptFooter => !ShowNoPromptsYet;

    public bool ShowNoMatches => Prompts.Count == 0 && !string.IsNullOrWhiteSpace(_searchText);
    public string NoMatchesText => $"No prompts match \"{_searchText.Trim()}\".";

    /// <summary>What the editor area says when no prompt is open.</summary>
    public string NoEditorText => _host.Library.Prompts.Count == 0
        ? "No prompts yet. Press Ctrl+N or choose New prompt."
        : "Select a prompt, or create one.";

    /// <summary>A short muted confirmation under the categories, e.g. <c>Moved 2 prompts to Code review.</c>; null when none.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The warning bar's text ("Backups are off…" or "Backups paused…"), or null when backups are fine.</summary>
    public string? BackupWarning => _host.BackupWarning;

    public bool ShowBackupWarning => _host.BackupWarning is not null;

    /// <summary>The full backup folder while paused (the bar's tooltip).</summary>
    public string? BackupWarningDetail => _host.BackupWarningDetail;

    public bool IsAddingCategory
    {
        get => _isAddingCategory;
        private set => SetProperty(ref _isAddingCategory, value);
    }

    public string NewCategoryName
    {
        get => _newCategoryName;
        set
        {
            if (SetProperty(ref _newCategoryName, value ?? ""))
                NewCategoryError = null;
        }
    }

    public string? NewCategoryError
    {
        get => _newCategoryError;
        private set => SetProperty(ref _newCategoryError, value);
    }

    /// <summary>
    /// If the editor has unsaved changes, asks <c>Save changes to "title"?</c>: Save saves (false if that fails),
    /// Don't save throws the changes away, Cancel returns false. True when it's fine to move on.
    /// </summary>
    public bool ConfirmDiscardOrSave()
    {
        if (_editor is not { IsDirty: true } editor) return true;

        switch (_dialogs.AskSaveChanges(editor.DisplayTitle))
        {
            case true:
                return Save();
            case false:
                DiscardChanges(editor);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Rebuilds rail, list and choices from the library, keeping selections where they still exist.</summary>
    public void RefreshAll()
    {
        // A prompt removed from the library (e.g. by a restore) can't stay open.
        if (_editor is { PromptId: { } id } && _host.Library.Prompts.All(p => p.Id != id))
            Editor = null;

        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
        OnPropertyChanged(nameof(BackupWarning));
        OnPropertyChanged(nameof(ShowBackupWarning));
    }

    /// <summary>
    /// Before a restore replaces the library: offers to save unsaved edits, then, if a change is still only in
    /// memory because writing it failed, warns that the restore will lose it. False if the user cancelled.
    /// </summary>
    public bool ConfirmBeforeRestore()
    {
        if (!ConfirmDiscardOrSave()) return false;
        return _unwritten is null || _dialogs.ConfirmDestructive(
            "Some of your changes couldn't be saved to disk. Restoring now will lose them.", "Restore anyway");
    }

    /// <summary>
    /// The library was replaced by a restore: close the editor (its saved values are out of date; any unsaved
    /// edits were offered for saving before the restore) and rebuild everything.
    /// </summary>
    public void ReloadAfterRestore()
    {
        _unwritten = null;
        Editor = null;
        RefreshAll();
    }

    // ---- Categories ----------------------------------------------------------------------------

    public void StartNewCategory()
    {
        foreach (var item in Categories) item.IsRenaming = false;
        NewCategoryName = "";
        NewCategoryError = null;
        IsAddingCategory = true;
    }

    public void CancelNewCategory()
    {
        IsAddingCategory = false;
        NewCategoryError = null;
    }

    /// <summary>Adds the typed category and selects it. False (with an inline message) if the name isn't valid.</summary>
    public bool CommitNewCategory()
    {
        var name = _newCategoryName.Trim();
        NewCategoryError = CategoryNameError(name, exceptId: null);
        if (NewCategoryError is not null) return false;

        Category category;
        try
        {
            category = NewLibraryEditor().AddCategory(name);
        }
        catch (ArgumentException)
        {
            NewCategoryError = CategoryNameError(name, exceptId: null) ?? "Give the category a name.";
            return false;
        }

        IsAddingCategory = false;
        _host.SaveLibrary();
        RebuildCategories(_selectedCategory?.Id);
        SelectCategoryNow(category.Id);
        return true;
    }

    public void StartRename(CategoryItem? item)
    {
        if (item is null || item.IsAll) return;
        CancelNewCategory();
        foreach (var other in Categories) other.IsRenaming = false;
        item.EditName = item.Name;
        item.Error = null;
        item.IsRenaming = true;
    }

    public void CancelRename(CategoryItem item)
    {
        item.IsRenaming = false;
        item.Error = null;
    }

    /// <summary>Renames the category to the typed name. False (with an inline message) if the name isn't valid.</summary>
    public bool CommitRename(CategoryItem item)
    {
        if (!item.IsRenaming || item.Id is not { } id) return true;

        var name = item.EditName.Trim();
        if (name == item.Name)
        {
            CancelRename(item);
            return true;
        }

        item.Error = CategoryNameError(name, exceptId: id);
        if (item.Error is not null) return false;

        try
        {
            NewLibraryEditor().RenameCategory(id, name);
        }
        catch (ArgumentException ex)
        {
            item.Error = CategoryNameError(name, exceptId: id) ?? ex.Message;
            return false;
        }

        item.IsRenaming = false;
        _host.SaveLibrary();
        RebuildCategories(_selectedCategory?.Id);
        return true;
    }

    public void DeleteCategory(CategoryItem? item)
    {
        if (item?.Id is not { } id) return;

        var promptCount = NewLibraryEditor().PromptCount(id);
        Guid? moveTo = null;

        if (promptCount == 0)
        {
            if (!_dialogs.ConfirmDestructive($"Delete the category \"{item.Name}\"?", "Delete")) return;
        }
        else
        {
            var targets = _host.Library.Categories
                .Where(c => c.Id != id)
                .OrderBy(c => c.Name, NameOrder)
                .ToList();
            if (targets.Count == 0)
            {
                _dialogs.ShowMessage(
                    $"\"{item.Name}\" still has {Count(promptCount, "prompt")}. " +
                    "Create another category first so they have somewhere to go.");
                return;
            }

            moveTo = _dialogs.ChooseMoveTarget(item.Name, promptCount, targets);
            if (moveTo is null) return;
        }

        try
        {
            NewLibraryEditor().DeleteCategory(id, moveTo);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        _editor?.ReplaceCategory(id, moveTo);
        _host.SaveLibrary();

        // Show where the prompts went; if the deleted category was empty, fall back to All prompts.
        var selectId = _selectedCategory?.Id == id ? moveTo : _selectedCategory?.Id;
        RebuildCategories(selectId);
        RefreshPrompts();

        if (moveTo is { } targetId && _host.Library.Categories.FirstOrDefault(c => c.Id == targetId) is { } target)
            ShowStatus($"Moved {Count(promptCount, "prompt")} to {target.Name}.");
    }

    // ---- Prompts -------------------------------------------------------------------------------

    /// <summary>
    /// Starts a new prompt in the selected category (the first category A–Z under "All prompts").
    /// It joins the library when it is saved.
    /// </summary>
    public void NewPrompt()
    {
        if (!ConfirmDiscardOrSave()) return;

        var categoryId = _selectedCategory?.Id
            ?? _host.Library.Categories.OrderBy(c => c.Name, NameOrder).FirstOrDefault()?.Id;
        if (categoryId is null)
        {
            _dialogs.ShowMessage("Create a category first, then add prompts to it.");
            StartNewCategory();
            return;
        }

        Editor = PromptEditorViewModel.ForNewPrompt(categoryId.Value);
        _selectedPrompt = null;
        OnPropertyChanged(nameof(SelectedPrompt));
        TitleFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves the editor. False if there's nothing valid to save or the file couldn't be written
    /// (the editor then keeps its changes so the user can try again).
    /// </summary>
    public bool Save()
    {
        var editor = _editor;
        if (editor is null || !editor.IsDirty) return true;
        if (!editor.Validate()) return false;

        Guid promptId;
        PromptSnapshot onDisk;
        try
        {
            var library = NewLibraryEditor();
            if (editor.PromptId is { } existingId)
            {
                // After an earlier failed write, the earlier snapshot is still what's on disk.
                onDisk = _unwritten ?? PromptSnapshot.Capture(_host.Library, existingId);
                library.UpdatePrompt(existingId, editor.Title, editor.Description, editor.CategoryId!.Value, editor.Text);
                promptId = existingId;
            }
            else
            {
                promptId = library.AddPrompt(editor.CategoryId!.Value, editor.Title, editor.Description, editor.Text).Id;
                onDisk = PromptSnapshot.ForNewPrompt(promptId);
                editor.MarkCreated(promptId); // a retry after a failed write must update, not add again
            }
        }
        catch (ArgumentException ex)
        {
            if (!editor.ShowValidationError(ex))
                _dialogs.ShowMessage(ex.Message);
            return false;
        }

        var written = _host.SaveLibrary();
        _unwritten = written ? null : onDisk;
        if (written)
        {
            editor.LoadSaved(_host.Library.Prompts.First(p => p.Id == promptId));
            editor.ShowSavedFeedback();
        }

        // If the prompt moved out of the category being shown, follow it to its new category.
        var showCategory = _selectedCategory?.Id is { } shown && shown != editor.CategoryId
            ? editor.CategoryId
            : _selectedCategory?.Id;
        RebuildCategories(showCategory);
        RefreshPrompts();
        return written;
    }

    /// <summary>Copies what is in the editor now, unsaved edits included.</summary>
    public void Copy()
    {
        var editor = _editor;
        if (editor is null || !editor.CanCopy) return;
        if (_host.CopyText(editor.Text, editor.DisplayTitle))
            editor.ShowCopiedFeedback();
    }

    /// <summary>Deletes the prompt in the editor after asking (a new, unsaved prompt is just dropped).</summary>
    public void Delete()
    {
        var editor = _editor;
        if (editor is null) return;

        if (editor.PromptId is not { } id)
        {
            if (!editor.IsDirty || _dialogs.ConfirmDestructive($"Delete \"{editor.DisplayTitle}\"?", "Delete"))
                Editor = null;
            return;
        }

        var title = _host.Library.Prompts.FirstOrDefault(p => p.Id == id)?.Title ?? editor.DisplayTitle;
        if (!_dialogs.ConfirmDestructive($"Delete \"{title}\"?", "Delete")) return;

        try
        {
            NewLibraryEditor().DeletePrompt(id);
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowMessage(ex.Message);
            return;
        }

        if (_unwritten?.PromptId == id) _unwritten = null;
        Editor = null;
        _host.SaveLibrary();
        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>
    /// "Don't save": the editor goes back to what is saved. If a change to this prompt reached
    /// memory but not the disk, the library is rolled back too (a new prompt is removed).
    /// </summary>
    private void DiscardChanges(PromptEditorViewModel editor)
    {
        if (_unwritten is not { } snapshot || snapshot.PromptId != editor.PromptId)
        {
            if (editor.IsNew)
                Editor = null;
            else
                editor.Revert();
            return;
        }

        snapshot.Restore(_host.Library);
        _unwritten = null;
        _host.RefreshQuickMenu();

        var restored = _host.Library.Prompts.FirstOrDefault(p => p.Id == snapshot.PromptId);
        if (restored is null)
            Editor = null;
        else
            editor.LoadSaved(restored);

        RebuildCategories(_selectedCategory?.Id);
        RefreshPrompts();
    }

    // The host's Library object can be replaced (restore), so make an editor for the current one.
    private LibraryEditor NewLibraryEditor() => new(_host.Library, () => DateTimeOffset.Now);

    private string? CategoryNameError(string name, Guid? exceptId)
    {
        if (name.Length == 0) return "Give the category a name.";
        var clash = _host.Library.Categories.Any(c =>
            c.Id != exceptId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return clash ? $"There's already a category called \"{name}\"." : null;
    }

    /// <summary>
    /// Runs <paramref name="change"/> once the user has answered "Save changes?". Deferred so the
    /// dialog is not shown from inside the list box's selection change; meanwhile the list snaps
    /// back to the current selection, which stays if the user cancels.
    /// </summary>
    private void AfterAskingToSave(Action change, string selectionProperty)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            SnapBackSelection(selectionProperty);
            if (_asking) return; // already asking; one question at a time

            _asking = true;
            try
            {
                if (ConfirmDiscardOrSave())
                    change();
            }
            finally
            {
                _asking = false;
            }
        });
    }

    /// <summary>
    /// After the search text changed: if the search hides the open prompt, open the first match instead (or close
    /// the editor when nothing matches). Unsaved changes are offered for saving first; if the user cancels, the
    /// editor and the search text stay as they are.
    /// </summary>
    private void FollowSearch()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            _searchKeepsEditor = false;
            return;
        }
        if (!SearchHidesEditor()) return;

        if (_editor is not { IsDirty: true })
        {
            OpenFirstMatch();
            return;
        }
        if (_searchKeepsEditor || _asking) return;

        // Deferred so the question isn't shown from inside the search box's text change.
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (_asking || !SearchHidesEditor()) return;
            _asking = true;
            try
            {
                if (!ConfirmDiscardOrSave())
                    _searchKeepsEditor = true;
                else if (SearchHidesEditor()) // saving may have made the prompt match
                    OpenFirstMatch();
            }
            finally
            {
                _asking = false;
            }
        });
    }

    /// <summary>True when the list shows search results that don't include the editor's prompt (or there's no editor).</summary>
    private bool SearchHidesEditor()
        => !string.IsNullOrWhiteSpace(_searchText)
           && (_editor is null ? Prompts.Count > 0 : _editor.PromptId is not { } id || Prompts.All(p => p.Id != id));

    private void OpenFirstMatch()
    {
        if (Prompts.FirstOrDefault() is { } first)
        {
            OpenPrompt(first);
            return;
        }
        Editor = null;
        _selectedPrompt = null;
        OnPropertyChanged(nameof(SelectedPrompt));
    }

    private void ShowStatus(string text)
    {
        StatusText = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    /// <summary>
    /// Puts the list box back on the current selection after the user clicked another row.
    /// Re-raising PropertyChanged with an unchanged value is ignored by the binding, so the
    /// selection is cleared and restored to force the update.
    /// </summary>
    private void SnapBackSelection(string selectionProperty)
    {
        _refreshing = true;
        try
        {
            if (selectionProperty == nameof(SelectedPrompt))
            {
                var keep = _selectedPrompt;
                _selectedPrompt = null;
                OnPropertyChanged(nameof(SelectedPrompt));
                _selectedPrompt = keep;
            }
            else
            {
                var keep = _selectedCategory;
                _selectedCategory = null;
                OnPropertyChanged(nameof(SelectedCategory));
                _selectedCategory = keep;
            }
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(selectionProperty);
    }

    /// <summary>Selects a category from code (e.g. after adding it), asking about unsaved changes if needed.</summary>
    private void SelectCategoryNow(Guid? id)
    {
        if (!EditorSurvivesCategory(id) && !ConfirmDiscardOrSave())
            return;
        ShowCategory(id);
    }

    /// <summary>True if switching to category <paramref name="id"/> can keep the editor open.</summary>
    private bool EditorSurvivesCategory(Guid? id)
        => _editor is null || id is null || _editor.CategoryId == id;

    private void ShowCategory(Guid? id)
    {
        _selectedCategory = Categories.FirstOrDefault(c => c.Id == id) ?? Categories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
        if (!EditorSurvivesCategory(_selectedCategory?.Id))
            Editor = null;
        RefreshPrompts();
    }

    private void OpenPrompt(Prompt prompt)
    {
        if (!_host.Library.Prompts.Contains(prompt)) return; // deleted meanwhile
        Editor = PromptEditorViewModel.ForPrompt(prompt);
        _selectedPrompt = Prompts.Contains(prompt) ? prompt : null;
        OnPropertyChanged(nameof(SelectedPrompt));
    }

    private void RebuildCategories(Guid? selectId)
    {
        var library = _host.Library;
        var sorted = library.Categories.OrderBy(c => c.Name, NameOrder).ToList();

        _refreshing = true;
        try
        {
            Categories.Clear();
            Categories.Add(new CategoryItem(null, "All prompts", library.Prompts.Count));
            foreach (var category in sorted)
                Categories.Add(new CategoryItem(category.Id, category.Name,
                    library.Prompts.Count(p => p.CategoryId == category.Id)));

            CategoryChoices.Clear();
            foreach (var category in sorted)
                CategoryChoices.Add(category);

            _selectedCategory = Categories.FirstOrDefault(c => c.Id == selectId) ?? Categories[0];
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedCategory));
        _editor?.RefreshCategoryChoice();
    }

    private void RefreshPrompts()
    {
        var keepId = _editor?.PromptId ?? _selectedPrompt?.Id;

        _refreshing = true;
        try
        {
            Prompts.Clear();
            foreach (var prompt in PromptFilter.Apply(_host.Library, _selectedCategory?.Id, _searchText))
                Prompts.Add(prompt);

            // While the items were replaced the list box pushed null selections, and the binding may have left
            // its SelectedItem at the kept prompt with nothing actually selected. Re-raising an unchanged value is
            // ignored, so clear it first (as SnapBackSelection does) to make the final update stick.
            _selectedPrompt = null;
            OnPropertyChanged(nameof(SelectedPrompt));
            _selectedPrompt = Prompts.FirstOrDefault(p => p.Id == keepId);
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(SelectedPrompt));
        OnPropertyChanged(nameof(ShowNoPromptsYet));
        OnPropertyChanged(nameof(ShowNewPromptFooter));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(NoEditorText));
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PromptEditorViewModel.IsDirty))
            OnPropertyChanged(nameof(WindowTitle));
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
