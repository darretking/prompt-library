using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.History;

namespace PromptLibrary.App.ViewModels;

/// <summary>One earlier version in the history list: when it was saved and its first line.</summary>
public sealed record HistoryItem(PromptVersion Version, string When, string FirstLine);

/// <summary>One line of the diff pane: a marker (+ / - / blank, so colour isn't the only cue) and the line.</summary>
public sealed record DiffRow(DiffKind Kind, string Marker, string Text);

/// <summary>A target of the Send menu; <see cref="IsDefault"/> puts a check on the prompt's default.</summary>
public sealed record SendChoice(string Id, string Name, bool IsDefault);

/// <summary>
/// Version history of one prompt: the earlier versions (newest first) and, for the chosen one, a line diff against the
/// text in the editor now. <see cref="Restore"/> hands the version to the editor as an unsaved change.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly string _currentText;
    private readonly Action<PromptVersion> _apply;
    private HistoryItem? _selected;

    public HistoryViewModel(IReadOnlyList<PromptVersion> versions, string currentText, DateTimeOffset now,
        Action<PromptVersion> apply)
    {
        _currentText = currentText;
        _apply = apply;
        Items = versions.Select(v => new HistoryItem(v, RelativeAge(v.SavedAt, now), FirstLine(v.Text))).ToList();
        _selected = Items.FirstOrDefault();
    }

    public IReadOnlyList<HistoryItem> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => Items.Count > 0;

    public const string EmptyText =
        "No earlier versions yet. Each time you save a change, the previous version is kept here.";

    public HistoryItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(Diff));
            OnPropertyChanged(nameof(CanRestore));
            OnPropertyChanged(nameof(Caption));
            OnPropertyChanged(nameof(DiffTitle));
        }
    }

    public bool CanRestore => _selected is not null;

    private bool _compareWithCurrent;

    /// <summary>
    /// Off: the diff is what saving changed in the chosen version (the chosen version against the one saved just after it,
    /// or the editor's text for the newest). On: the chosen version against the editor's text now.
    /// </summary>
    public bool CompareWithCurrent
    {
        get => _compareWithCurrent;
        set
        {
            if (!SetProperty(ref _compareWithCurrent, value)) return;
            OnPropertyChanged(nameof(Diff));
            OnPropertyChanged(nameof(DiffTitle));
            OnPropertyChanged(nameof(Caption));
        }
    }

    /// <summary>The text the chosen version is compared with.</summary>
    internal string ComparedText
    {
        get
        {
            if (_selected is null) return _currentText;
            var index = IndexOf(_selected);
            return _compareWithCurrent || index <= 0 ? _currentText : Items[index - 1].Version.Text;
        }
    }

    private int IndexOf(HistoryItem item)
    {
        for (var i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i], item)) return i;
        return -1;
    }

    /// <summary>Removed lines were in the chosen version only; added lines are in the text it is compared with.</summary>
    public IReadOnlyList<DiffRow> Diff => _selected is null
        ? []
        : LineDiff.Compute(_selected.Version.Text, ComparedText)
            .Select(l => new DiffRow(l.Kind, l.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => " " }, l.Text))
            .ToList();

    public string DiffTitle => _compareWithCurrent ? "This version compared with the current text" : "Changes made in this version";

    public string Caption => _selected is null
        ? ""
        : _compareWithCurrent
            ? "Red lines are only in this version, green lines are only in the text you have now."
            : "What saving changed in this version: red lines were removed, green lines were added.";

    /// <summary>The version the user asked to restore as a new prompt, or null.</summary>
    public PromptVersion? CopyRequested { get; private set; }

    /// <summary>
    /// "Restore as a new prompt": the chosen version becomes a copy next to the prompt (the caller creates it once the
    /// window has closed, see <see cref="CopyRequested"/>); the open prompt isn't touched.
    /// </summary>
    public bool RestoreAsNew()
    {
        if (_selected is null) return false;
        CopyRequested = _selected.Version;
        return true;
    }

    /// <summary>Loads the chosen version into the editor (title, description, text, tags) as unsaved changes.</summary>
    public bool Restore()
    {
        if (_selected is null) return false;
        _apply(_selected.Version);
        return true;
    }

    /// <summary>"just now", "5 minutes ago", "2 hours ago", "yesterday", "3 days ago", then the date.</summary>
    public static string RelativeAge(DateTimeOffset saved, DateTimeOffset now)
    {
        var age = now - saved;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return Plural((int)age.TotalMinutes, "minute");
        if (age < TimeSpan.FromHours(24)) return Plural((int)age.TotalHours, "hour");
        var days = (now.Date - saved.ToOffset(now.Offset).Date).Days;
        if (days <= 1) return "yesterday";
        if (days < 30) return $"{days} days ago";
        return saved.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

    private static string FirstLine(string text)
    {
        var line = (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line ?? "(empty)";
    }
}
