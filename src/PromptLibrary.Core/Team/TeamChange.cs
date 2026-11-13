using PromptLibrary.Core.History;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Team;

/// <summary>One of title, description or category that differs: its <paramref name="Name"/>, old and new value.</summary>
public sealed record FieldChange(string Name, string Old, string New);

/// <summary>What changed in a team prompt: the changed fields, and the text as a line diff.</summary>
public sealed record TeamChangeModel(IReadOnlyList<FieldChange> Fields, IReadOnlyList<DiffLine> TextDiff)
{
    /// <summary>True when the text differs (the diff has an added or removed line).</summary>
    public bool TextChanged => TextDiff.Any(l => l.Kind != DiffKind.Same);

    /// <summary>True when nothing differs.</summary>
    public bool IsEmpty => Fields.Count == 0 && !TextChanged;
}

/// <summary>Builds the "what changed" model for a team prompt.</summary>
public static class TeamChange
{
    /// <summary>The snapshot of a prompt and its category name, as a member sees it.</summary>
    public static TeamSeenSnapshot Snapshot(Prompt prompt, string categoryName)
        => new(prompt.Title ?? "", prompt.Description ?? "", prompt.Text ?? "", categoryName ?? "");

    /// <summary>The snapshot of a prompt of <paramref name="library"/> (its category name is looked up there).</summary>
    public static TeamSeenSnapshot Snapshot(Prompt prompt, Library library)
        => Snapshot(prompt, CategoryName(library, prompt));

    /// <summary>The category name of a prompt in a library; empty when the category is missing.</summary>
    public static string CategoryName(Library library, Prompt prompt)
        => library.Categories.FirstOrDefault(c => c.Id == prompt.CategoryId)?.Name ?? "";

    /// <summary>What differs between what was seen and the current prompt.</summary>
    public static TeamChangeModel Between(TeamSeenSnapshot old, Prompt current, string currentCategoryName)
        => Between(old, Snapshot(current, currentCategoryName));

    /// <summary>What differs between two prompts (for example your copy and the team version).</summary>
    public static TeamChangeModel Between(Prompt a, string aCategoryName, Prompt b, string bCategoryName)
        => Between(Snapshot(a, aCategoryName), Snapshot(b, bCategoryName));

    private static TeamChangeModel Between(TeamSeenSnapshot a, TeamSeenSnapshot b)
    {
        var fields = new List<FieldChange>();
        if (a.Title != b.Title) fields.Add(new("Title", a.Title, b.Title));
        if (a.Description != b.Description) fields.Add(new("Description", a.Description, b.Description));
        if (a.CategoryName != b.CategoryName) fields.Add(new("Category", a.CategoryName, b.CategoryName));
        return new TeamChangeModel(fields, LineDiff.Compute(a.Text, b.Text));
    }
}
