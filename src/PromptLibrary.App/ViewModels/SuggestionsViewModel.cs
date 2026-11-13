using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>A category choice: the shown text and the name it stands for ("new: Sales" creates Sales).</summary>
public sealed record SuggestionCategoryOption(string Display, string Name);

/// <summary>One row of the suggestions list.</summary>
public sealed class SuggestionItem(Suggestion suggestion, string subtitle)
{
    public Suggestion Suggestion { get; } = suggestion;
    public string Title => Suggestion.Title;
    public string Subtitle { get; } = subtitle;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Suggestion.Description);
    public bool HasNote => !string.IsNullOrWhiteSpace(Suggestion.Note);
}

/// <summary>
/// The editors' Suggestions window: the list, the selected suggestion's text, description and note, a team category
/// (the suggested name when it exists, else "new: name") and Add to team prompts / Dismiss.
/// </summary>
public sealed class SuggestionsViewModel : ObservableObject
{
    private readonly string _teamFilePath;
    private readonly IReadOnlyList<string> _teamCategoryNames;
    private readonly Action<Suggestion, string, Action> _add;
    private SuggestionItem? _selected;
    private SuggestionCategoryOption? _category;

    /// <param name="add">Adds the suggestion to the team under the category name and calls the last argument when saved.</param>
    public SuggestionsViewModel(string teamFilePath, IEnumerable<Suggestion> suggestions,
        IEnumerable<string> teamCategoryNames, DateTimeOffset now, Action<Suggestion, string, Action> add)
    {
        _teamFilePath = teamFilePath;
        _teamCategoryNames = teamCategoryNames.ToList();
        _add = add;
        foreach (var s in suggestions) Items.Add(new SuggestionItem(s, Subtitle(s, now)));
        AddCommand = new RelayCommand(Add, () => _selected is not null);
        DismissCommand = new RelayCommand(Dismiss, () => _selected is not null);
        Selected = Items.FirstOrDefault();
    }

    public ObservableCollection<SuggestionItem> Items { get; } = [];

    public IRelayCommand AddCommand { get; }
    public IRelayCommand DismissCommand { get; }

    public IReadOnlyList<SuggestionCategoryOption> CategoryOptions { get; private set; } = [];

    public bool IsEmpty => Items.Count == 0;
    public bool HasSelection => _selected is not null;

    /// <summary>The count of suggestions left (the caller refreshes its own count when the window closes).</summary>
    public int Remaining => Items.Count;

    public SuggestionItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            CategoryOptions = value is null ? [] : OptionsFor(value.Suggestion.CategoryName, _teamCategoryNames);
            Category = CategoryOptions.FirstOrDefault();
            OnPropertyChanged(nameof(CategoryOptions));
            OnPropertyChanged(nameof(HasSelection));
            AddCommand.NotifyCanExecuteChanged();
            DismissCommand.NotifyCanExecuteChanged();
        }
    }

    public SuggestionCategoryOption? Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    /// <summary>The existing team categories, then the suggested name as "new: name" when none matches; the default comes first.</summary>
    public static IReadOnlyList<SuggestionCategoryOption> OptionsFor(string suggested, IEnumerable<string> teamCategoryNames)
    {
        var name = suggested.Trim();
        var names = teamCategoryNames.Select(n => n.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = names.Select(n => new SuggestionCategoryOption(n, n)).ToList();
        var match = options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            options.Remove(match);
            options.Insert(0, match);
        }
        else if (name.Length > 0)
        {
            options.Insert(0, new SuggestionCategoryOption("new: " + name, name));
        }
        return options;
    }

    private static string Subtitle(Suggestion s, DateTimeOffset now)
    {
        var age = HistoryViewModel.RelativeAge(s.CreatedAt, now);
        return string.IsNullOrWhiteSpace(s.From) ? age : s.From + " · " + age;
    }

    private void Add()
    {
        if (_selected is not { } item || _category is not { } category) return;
        _add(item.Suggestion, category.Name, () => Remove(item));
    }

    private void Dismiss()
    {
        if (_selected is { } item) Remove(item);
    }

    private void Remove(SuggestionItem item)
    {
        TeamSuggestions.MarkDone(_teamFilePath, item.Suggestion.Id);
        var index = Items.IndexOf(item);
        if (index < 0) return;
        Items.RemoveAt(index);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Remaining));
        if (ReferenceEquals(_selected, item))
            Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }
}
