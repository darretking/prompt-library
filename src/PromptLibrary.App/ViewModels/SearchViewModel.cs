using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Search;
using PromptLibrary.Core.Send;
using PromptLibrary.Core.Templates;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.App.ViewModels;

/// <summary>A piece of text; <see cref="Highlight"/> marks a matched search letter or a placeholder chip.</summary>
public readonly record struct TextSegment(string Text, bool Highlight);

/// <summary>What the search window needs from the app. <c>AppHost</c> (through the search controller) implements it.</summary>
public interface ISearchActions
{
    Library Library { get; }

    /// <summary>The team prompts (hidden ones included; the window filters them), or null without a team library.</summary>
    Library? TeamLibrary { get; }

    AppSettings Settings { get; }

    /// <summary>Values for <c>{clipboard}</c>, <c>{date}</c> and <c>{time}</c>, captured when the window opened.</summary>
    ITokenValues Tokens { get; }

    /// <summary>Hide the window, copy, paste into the app that was in front, record the use.</summary>
    void Paste(Prompt prompt, string text, int? caretBackSteps);

    /// <summary>Hide the window and only copy.</summary>
    void CopyOnly(Prompt prompt, string text);

    /// <summary>Hide the window, open the AI site with the text and put it on the clipboard too.</summary>
    void Send(Prompt prompt, string text, string targetId);

    void OpenInMainWindow(Prompt prompt, bool isTeam);

    /// <summary>Pins changed: save settings and refresh what shows them.</summary>
    void PinsChanged();

    /// <summary>Hide the window.</summary>
    void Close();
}

/// <summary>A <c>#tag</c> chip in a result row; <see cref="IsMatch"/> when the search matched it (shown bold).</summary>
public sealed record TagChip(string Text, bool IsMatch);

/// <summary>One key and what it does, for the footer ("Enter" "paste").</summary>
public readonly record struct FooterHint(string Key, string Label)
{
    /// <summary>What a screen reader says: <c>Enter: paste</c>.</summary>
    public string AutomationName => $"{Key}: {Label}";

    public override string ToString() => AutomationName;
}

/// <summary>One line of the result list: a group header or a prompt.</summary>
public sealed class SearchItem
{
    public bool IsHeader { get; init; }
    public bool IsRow => !IsHeader;
    public string Header { get; init; } = "";

    public Prompt? Prompt { get; init; }
    public bool IsTeam { get; init; }
    public bool IsPinned { get; init; }
    public string Title => Prompt?.Title ?? "";

    /// <summary>The title split into matched and unmatched pieces.</summary>
    public IReadOnlyList<TextSegment> TitleSegments { get; init; } = [];

    /// <summary>Category name and description on one muted line.</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>The category's name (no emoji), or empty.</summary>
    public string CategoryName { get; init; } = "";

    /// <summary>The category's emoji for the leading glyph column, or empty (the view then shows a document or team icon).</summary>
    public string Emoji { get; init; } = "";

    public bool HasEmoji => Emoji.Length > 0;
    public bool ShowsTeamGlyph => !HasEmoji && IsTeam;
    public bool ShowsDocumentGlyph => !HasEmoji && !IsTeam;

    /// <summary>What a screen reader says for the row: <c>Plan a trip itinerary, Travel, team, pinned</c>.</summary>
    public string AutomationName => IsHeader
        ? Header
        : string.Join(", ", new[] { Title, CategoryName, IsTeam ? "team" : "", IsPinned ? "pinned" : "" }
            .Where(s => s.Length > 0));

    public override string ToString() => AutomationName;

    /// <summary>
    /// Third line, when the match came from the tags: the prompt's tags as <c>#tag</c> chips, matched ones first and bold.
    /// </summary>
    public IReadOnlyList<TagChip> TagChips { get; init; } = [];

    /// <summary>Third line, when the match came from the prompt text: about 60 characters around it, the match highlighted.</summary>
    public IReadOnlyList<TextSegment> SnippetSegments { get; init; } = [];

    public bool HasTagLine => TagChips.Count > 0;
    public bool HasSnippetLine => SnippetSegments.Count > 0;

    /// <summary><c>Alt+1</c>… for the first nine rows, else empty.</summary>
    public string NumberHint { get; set; } = "";
}

/// <summary>One entry of the Ctrl+K menu.</summary>
public sealed record SearchAction(string Label, Action Run, bool OpensSubmenu = false)
{
    /// <summary>What a screen reader says: the label, and that it opens another list.</summary>
    public string AutomationName => OpensSubmenu ? $"{Label}, opens a list" : Label;

    public override string ToString() => AutomationName;
}

/// <summary>
/// The launcher: ranked or grouped results, the preview, the keys, the fill-in form and the Ctrl+K menu. The window
/// is created once and reused; <see cref="Reset"/> readies it for the next opening from the in-memory libraries.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    public const int RecentCount = 8;
    public const int MaxResults = 100;
    private const int PageSize = 6;
    private const int PreviewLines = 20;

    private enum Intent { Paste, Send, Copy }

    private readonly ISearchActions _host;
    private readonly Func<DateTimeOffset> _now;
    private string _query = "";
    private SearchItem? _selected;
    private IReadOnlyList<TextSegment> _preview = [];
    private FillInViewModel? _form;
    private bool _actionsOpen;
    private SearchAction? _selectedAction;
    private bool _inSendMenu;

    public SearchViewModel(ISearchActions host, Func<DateTimeOffset>? now = null)
    {
        _host = host;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public ObservableCollection<SearchItem> Items { get; } = [];
    public ObservableCollection<SearchAction> Actions { get; } = [];

    /// <summary>Raised when the search box should get the keyboard (after the form closes, after a reset).</summary>
    public event Action? FocusSearchRequested;

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? "")) return;
            CloseActions();
            Rebuild();
        }
    }

    public SearchItem? Selected
    {
        get => _selected;
        set
        {
            if (value is { IsHeader: true } || !SetProperty(ref _selected, value)) return;
            Preview = BuildPreview(value?.Prompt);
        }
    }

    /// <summary>The first lines of the selected prompt; placeholders are marked as chips.</summary>
    public IReadOnlyList<TextSegment> Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>The fill-in form that replaces the list, or null.</summary>
    public FillInViewModel? Form
    {
        get => _form;
        private set
        {
            if (!SetProperty(ref _form, value)) return;
            OnPropertyChanged(nameof(IsFormOpen));
            OnPropertyChanged(nameof(IsListOpen));
            OnPropertyChanged(nameof(Footer));
            OnPropertyChanged(nameof(FooterText));
        }
    }

    public bool IsFormOpen => _form is not null;
    public bool IsListOpen => _form is null;

    public bool IsActionsOpen
    {
        get => _actionsOpen;
        private set
        {
            if (!SetProperty(ref _actionsOpen, value)) return;
            OnPropertyChanged(nameof(Footer));
            OnPropertyChanged(nameof(FooterText));
        }
    }

    public SearchAction? SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    public bool HasResults => Items.Count > 0;

    public string EmptyText => string.IsNullOrWhiteSpace(_query) ? "No prompts yet." : $"No prompts match \"{_query.Trim()}\".";

    /// <summary>The line under <see cref="EmptyText"/>: how to widen the search (nothing while the library itself is empty).</summary>
    public string EmptyHint => string.IsNullOrWhiteSpace(_query) ? "" : "Try fewer words, or #tag / cat:name";

    /// <summary>The keys that work right now, for the footer: the list, the actions popup or the fill-in form.</summary>
    public IReadOnlyList<FooterHint> Footer => IsFormOpen
        ? [new("Tab", "next field"), new("Enter", _form!.PrimaryLabel.ToLowerInvariant()), new("Esc", "back")]
        : IsActionsOpen
            ? [new("Enter", "choose"), new("Esc", "back")]
            : [new("Enter", "paste"), new("Ctrl+Enter", "send"), new("Ctrl+K", "actions"), new("Esc", "close")];

    /// <summary>The footer as plain text (for screen readers and tests).</summary>
    public string FooterText => string.Join(" · ", Footer.Select(h => $"{h.Key} {h.Label}"));

    /// <summary>Clears the query and form and rebuilds the list from the current libraries (the window is about to show).</summary>
    public void Reset()
    {
        _query = "";
        OnPropertyChanged(nameof(Query));
        CloseActions();
        Form = null;
        Rebuild();
        FocusSearchRequested?.Invoke();
    }

    // ---- Keys ----------------------------------------------------------------------------------

    /// <summary>
    /// Handles a key of the list view. <paramref name="key"/> is the real key (for Alt combinations, the system key).
    /// False when the key is not ours (typing goes to the search box) or the form is open (it handles its own keys).
    /// </summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (IsFormOpen) return false;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);

        if (IsActionsOpen)
        {
            switch (key)
            {
                case Key.Down: MoveAction(+1); return true;
                case Key.Up: MoveAction(-1); return true;
                case Key.Enter: RunSelectedAction(); return true;
                case Key.Escape: BackOutOfActions(); return true;
                case Key.K when ctrl: CloseActions(); return true;
                default: return false;
            }
        }

        if (alt && !ctrl && DigitOf(key) is { } digit)
        {
            UseNth(digit);
            return true;
        }

        switch (key)
        {
            case Key.Down: Move(+1); return true;
            case Key.Up: Move(-1); return true;
            case Key.PageDown: Move(+PageSize); return true;
            case Key.PageUp: Move(-PageSize); return true;
            case Key.Enter when ctrl: Begin(Intent.Send, null); return true;
            case Key.Enter when !alt: Begin(Intent.Paste, null); return true;
            case Key.O when ctrl: OpenSelectedInMainWindow(); return true;
            case Key.K when ctrl: OpenActions(); return true;
            case Key.Escape:
                if (Query.Length > 0) Query = "";
                else _host.Close();
                return true;
            default: return false;
        }
    }

    private static int? DigitOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };

    /// <summary>Moves the selection by <paramref name="delta"/> prompts, skipping group headers, stopping at the ends.</summary>
    public void Move(int delta)
    {
        var rows = Items.Where(i => i.IsRow).ToList();
        if (rows.Count == 0) return;
        var at = _selected is null ? -1 : rows.IndexOf(_selected);
        Selected = rows[Math.Clamp(at + delta, 0, rows.Count - 1)];
    }

    /// <summary>Alt+N: uses the Nth prompt of the list.</summary>
    public void UseNth(int n)
    {
        var rows = Items.Where(i => i.IsRow).ToList();
        if (n < 1 || n > rows.Count) return;
        Selected = rows[n - 1];
        Begin(Intent.Paste, null);
    }

    // ---- Using a prompt ------------------------------------------------------------------------

    /// <summary>Enter: paste the selected prompt (after the form, if it has blanks).</summary>
    public void UseSelected() => Begin(Intent.Paste, null);

    /// <summary>Ctrl+Enter: send to the prompt's default target (ChatGPT without one).</summary>
    public void SendSelected() => Begin(Intent.Send, null);

    public void OpenSelectedInMainWindow()
    {
        if (_selected is not { Prompt: { } prompt } item) return;
        _host.OpenInMainWindow(prompt, item.IsTeam);
    }

    private void Begin(Intent intent, string? targetId)
    {
        if (_selected is not { Prompt: { } prompt }) return;
        CloseActions();

        if (!PromptTemplate.HasFields(prompt.Text))
        {
            Finish(prompt, intent, targetId, PromptTemplate.Fill(prompt.Text, new Dictionary<string, string>(), _host.Tokens));
            return;
        }

        var label = intent switch { Intent.Send => "Send", Intent.Copy => "Copy", _ => "Paste" };
        var form = new FillInViewModel(prompt.Text, _host.Settings, _host.Tokens, label, prompt.Title);
        form.Completed += (text, caret) =>
        {
            Form = null;
            Finish(prompt, intent, targetId, new FillResult(text, caret));
        };
        form.Cancelled += () =>
        {
            Form = null;
            FocusSearchRequested?.Invoke();
        };
        Form = form;
    }

    private void Finish(Prompt prompt, Intent intent, string? targetId, FillResult filled)
    {
        switch (intent)
        {
            case Intent.Paste: _host.Paste(prompt, filled.Text, filled.CaretBackSteps); break;
            case Intent.Copy: _host.CopyOnly(prompt, filled.Text); break;
            default: _host.Send(prompt, filled.Text, targetId ?? SendTargets.Resolve(prompt.SendTo).Id); break;
        }
    }

    public void TogglePinSelected()
    {
        if (_selected is not { Prompt: { } prompt }) return;
        var pins = _host.Settings.PinnedPromptIds;
        if (!pins.Remove(prompt.Id)) pins.Add(prompt.Id);
        _host.PinsChanged();
        Rebuild(keepSelectionOf: prompt.Id);
    }

    // ---- Ctrl+K menu ---------------------------------------------------------------------------

    public void OpenActions()
    {
        if (_selected?.Prompt is null) return;
        _inSendMenu = false;
        ShowMainActions();
        IsActionsOpen = true;
    }

    private void ShowMainActions()
    {
        var item = _selected!;
        Actions.Clear();
        Actions.Add(new SearchAction("Paste", () => Begin(Intent.Paste, null)));
        Actions.Add(new SearchAction("Copy only", () => Begin(Intent.Copy, null)));
        Actions.Add(new SearchAction("Send to", ShowSendActions, OpensSubmenu: true));
        Actions.Add(new SearchAction("Open in main window", OpenSelectedInMainWindow));
        Actions.Add(new SearchAction(item.IsPinned ? "Unpin" : "Pin", TogglePinSelected));
        SelectedAction = Actions[0];
    }

    private void ShowSendActions()
    {
        _inSendMenu = true;
        Actions.Clear();
        foreach (var target in SendTargets.All)
            Actions.Add(new SearchAction(target.Name, () => Begin(Intent.Send, target.Id)));
        SelectedAction = Actions[0];
        IsActionsOpen = true;
    }

    private void MoveAction(int delta)
    {
        if (Actions.Count == 0) return;
        var at = SelectedAction is null ? 0 : Actions.IndexOf(SelectedAction);
        SelectedAction = Actions[Math.Clamp(at + delta, 0, Actions.Count - 1)];
    }

    /// <summary>Runs the highlighted entry (Enter, or a click).</summary>
    public void RunSelectedAction()
    {
        if (SelectedAction is not { } action) return;
        var opensSubmenu = action.OpensSubmenu;
        if (!opensSubmenu) CloseActions();
        action.Run();
    }

    /// <summary>Esc in the menu: out of Send to, else closed.</summary>
    public void BackOutOfActions()
    {
        if (_inSendMenu)
        {
            _inSendMenu = false;
            ShowMainActions();
        }
        else CloseActions();
    }

    public void CloseActions()
    {
        _inSendMenu = false;
        if (IsActionsOpen) IsActionsOpen = false;
    }

    // ---- Building the list ---------------------------------------------------------------------

    private sealed record Entry(Prompt Prompt, bool IsTeam, string Category, string Emoji);

    private void Rebuild(Guid? keepSelectionOf = null)
    {
        var entries = EntriesById();
        var pinned = _host.Settings.PinnedPromptIds.ToHashSet();
        Items.Clear();

        if (string.IsNullOrWhiteSpace(_query))
            AddGroups(entries, pinned);
        else
        {
            var hits = FuzzySearch.Search(_host.Library, _host.TeamLibrary, _host.Settings, _query, _now())
                .Where(r => !r.IsHidden).Take(MaxResults);
            foreach (var hit in hits)
                if (entries.TryGetValue(hit.Prompt.Id, out var e))
                    Items.Add(Row(e, pinned, hit));
        }

        var n = 0;
        foreach (var item in Items.Where(i => i.IsRow))
            item.NumberHint = ++n <= 9 ? $"Alt+{n}" : "";

        var keep = keepSelectionOf is { } id ? Items.FirstOrDefault(i => i.Prompt?.Id == id) : null;
        _selected = null; // force the preview to refresh even when the same row object is chosen again
        Selected = keep ?? Items.FirstOrDefault(i => i.IsRow);
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void AddGroups(Dictionary<Guid, Entry> entries, HashSet<Guid> pinned)
    {
        var shown = new HashSet<Guid>();
        var groups = new List<(string Name, List<Entry> Entries)>();

        var pins = _host.Settings.PinnedPromptIds.Where(entries.ContainsKey).Distinct().Select(id => entries[id]).ToList();
        if (pins.Count > 0) groups.Add(("Pinned", pins));
        shown.UnionWith(pins.Select(e => e.Prompt.Id));

        var recent = UsageTracker.Recent(_host.Settings, int.MaxValue)
            .Where(id => entries.ContainsKey(id) && !shown.Contains(id)).Take(RecentCount).Select(id => entries[id]).ToList();
        if (recent.Count > 0) groups.Add(("Recent", recent));
        shown.UnionWith(recent.Select(e => e.Prompt.Id));

        var rest = entries.Values.Where(e => !shown.Contains(e.Prompt.Id))
            .OrderBy(e => e.Prompt.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (rest.Count > 0) groups.Add(("All prompts", rest));

        foreach (var (name, list) in groups)
        {
            if (groups.Count > 1) Items.Add(new SearchItem { IsHeader = true, Header = name });
            foreach (var e in list) Items.Add(Row(e, pinned, null));
        }
    }

    private Dictionary<Guid, Entry> EntriesById()
    {
        var map = new Dictionary<Guid, Entry>();
        Add(_host.Library, isTeam: false, hidden: null);
        if (_host.TeamLibrary is { } team) Add(team, isTeam: true, _host.Settings.HiddenTeamPromptIds.ToHashSet());
        return map;

        void Add(Library lib, bool isTeam, HashSet<Guid>? hidden)
        {
            var categories = lib.Categories.ToDictionary(c => c.Id);
            foreach (var p in lib.Prompts)
            {
                if (hidden?.Contains(p.Id) == true) continue;
                categories.TryGetValue(p.CategoryId, out var c);
                map[p.Id] = new Entry(p, isTeam, c?.Name ?? "", c?.Emoji ?? "");
            }
        }
    }

    private const int MaxTagChips = 4;

    private static SearchItem Row(Entry e, HashSet<Guid> pinned, SearchResult? hit)
    {
        var matches = hit?.TitleMatches ?? [];
        // The third line: only when the match came from the tags or the text (the title and description lines show theirs).
        var tags = new List<TagChip>();
        if (hit?.TagMatches is { Count: > 0 } matched)
        {
            var all = e.Prompt.Tags;
            tags.AddRange(matched.Where(i => i < all.Count).Select(i => new TagChip("#" + all[i], true)));
            tags.AddRange(Enumerable.Range(0, all.Count).Where(i => !matched.Contains(i))
                .Select(i => new TagChip("#" + all[i], false)));
            if (tags.Count > MaxTagChips) tags.RemoveRange(MaxTagChips, tags.Count - MaxTagChips);
        }
        var snippet = tags.Count == 0 && hit?.TextSnippet is { } s
            ? Segments(s.Text, [(s.Start, s.Length)])
            : [];

        var description = FirstLine(e.Prompt.Description);
        var subtitle = e.Category.Length == 0 ? description
            : description.Length == 0 ? e.Category
            : $"{e.Category} · {description}";
        return new SearchItem
        {
            Prompt = e.Prompt,
            IsTeam = e.IsTeam,
            IsPinned = pinned.Contains(e.Prompt.Id),
            TitleSegments = Segments(e.Prompt.Title, matches.Select(m => (m.Start, m.Length))),
            Subtitle = subtitle,
            CategoryName = e.Category,
            Emoji = e.Emoji,
            TagChips = tags,
            SnippetSegments = snippet,
        };
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line;
    }

    private static List<TextSegment> Segments(string text, IEnumerable<(int Start, int Length)> marks)
    {
        var result = new List<TextSegment>();
        var pos = 0;
        foreach (var (start, length) in marks.OrderBy(m => m.Start))
        {
            if (start < pos || start + length > text.Length || length <= 0) continue;
            if (start > pos) result.Add(new TextSegment(text[pos..start], false));
            result.Add(new TextSegment(text.Substring(start, length), true));
            pos = start + length;
        }
        if (pos < text.Length) result.Add(new TextSegment(text[pos..], false));
        return result;
    }

    private static IReadOnlyList<TextSegment> BuildPreview(Prompt? prompt)
    {
        if (prompt is null) return [];
        var lines = prompt.Text.Replace("\r\n", "\n").Split('\n');
        var text = string.Join("\n", lines.Take(PreviewLines));
        if (lines.Length > PreviewLines) text += "\n…";
        var spans = PromptTemplate.Spans(text);
        var pieces = new List<TextSegment>();
        var pos = 0;
        foreach (var (start, length, _) in spans)
        {
            if (start > pos) pieces.Add(new TextSegment(text[pos..start], false));
            pieces.Add(new TextSegment(text.Substring(start, length), true));
            pos = start + length;
        }
        if (pos < text.Length) pieces.Add(new TextSegment(text[pos..], false));
        return pieces;
    }
}
