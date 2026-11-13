using PromptLibrary.Core.History;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>One changed field in the Changes window: <c>Title</c>, then what it was and what it is now.</summary>
public sealed record FieldChangeRow(string Name, string Old, string New);

/// <summary>What the Changes window shows: a title, a quiet caption, the changed fields, then the text as a line diff.</summary>
public sealed class ChangesViewModel
{
    public ChangesViewModel(string title, string caption, TeamChangeModel change)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Untitled prompt" : title;
        Caption = caption;
        Fields = [.. change.Fields.Select(f => new FieldChangeRow(f.Name, f.Old, f.New))];
        Diff = [.. change.TextDiff.Select(l => new DiffRow(l.Kind,
            l.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => " " }, l.Text))];
        HasFields = Fields.Count > 0;
        HasText = change.TextChanged;
    }

    public string Title { get; }
    public string Caption { get; }
    public IReadOnlyList<FieldChangeRow> Fields { get; }
    public IReadOnlyList<DiffRow> Diff { get; }
    public bool HasFields { get; }
    public bool HasText { get; }
}
