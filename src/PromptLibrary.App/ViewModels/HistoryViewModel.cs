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
        }
    }

    public bool CanRestore => _selected is not null;

    /// <summary>What changed from the chosen version to the editor's text: removed lines were in the version only.</summary>
    public IReadOnlyList<DiffRow> Diff => _selected is null
        ? []
        : LineDiff.Compute(_selected.Version.Text, _currentText)
            .Select(l => new DiffRow(l.Kind, l.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => " " }, l.Text))
            .ToList();

    public string Caption => _selected is null
        ? ""
        : $"Changes since this version: red lines were removed, green lines were added.";

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
