using CommunityToolkit.Mvvm.ComponentModel;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// One row in the category rail: "All prompts" (<see cref="Id"/> = null), "Recent" (<see cref="IsRecent"/>) or a
/// category, with its prompt count. Also holds the inline rename state for that row.
/// </summary>
public sealed class CategoryItem : ObservableObject
{
    /// <summary>The id the Recent row has in the rail (it isn't a category; nothing else uses this id).</summary>
    public static readonly Guid RecentId = new("5f3a1c52-8d1b-4b53-9c1e-7a7c2b0e4d11");

    /// <summary>
    /// Colour emoji the rail shows where a row has none of its own (all in the bundled Fluent Emoji set, and none used by a
    /// starter category): a house on All prompts, a clock on Recent, and a memo on a category without an emoji.
    /// </summary>
    public const string AllEmoji = "\U0001F3E0", RecentEmoji = "\U0001F552", DefaultEmoji = "\U0001F4DD";

    private bool _isRenaming;
    private string _editName = "";
    private string? _editEmoji;
    private string? _error;

    public CategoryItem(Guid? id, string name, int count, bool canEdit = true, string? emoji = null)
    {
        Id = id;
        Name = name;
        Count = count;
        CanEdit = canEdit;
        Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji;
    }

    public Guid? Id { get; }
    public string Name { get; }
    public int Count { get; }

    /// <summary>The category's emoji, or null.</summary>
    public string? Emoji { get; }

    /// <summary>False on a read-only side (team prompts): no Rename or Delete.</summary>
    public bool CanEdit { get; }

    /// <summary>True for the "All prompts" row, which can't be renamed or deleted.</summary>
    public bool IsAll => Id is null;

    /// <summary>True for the "Recent" row (last-used prompts), which isn't a category.</summary>
    public bool IsRecent => Id == RecentId;

    /// <summary>True for a real category (not All prompts, not Recent): it can be renamed, given an emoji and deleted.</summary>
    public bool IsCategory => Id is not null && !IsRecent;

    /// <summary>The emoji in the rail: always colour, so every row lines up the same way (the category's own when it has one).</summary>
    public string RailEmoji => IsAll ? AllEmoji : IsRecent ? RecentEmoji : Emoji ?? DefaultEmoji;

    /// <summary>What the rail says for a screen reader: the plain name.</summary>
    public string DisplayName => Emoji is null ? Name : $"{Emoji} {Name}";

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    /// <summary>The name being typed while renaming.</summary>
    public string EditName
    {
        get => _editName;
        set
        {
            if (SetProperty(ref _editName, value))
                Error = null;
        }
    }

    /// <summary>The emoji picked while renaming (null = none).</summary>
    public string? EditEmoji
    {
        get => _editEmoji;
        set => SetProperty(ref _editEmoji, value);
    }

    /// <summary>Validation message shown under the rename box, or null.</summary>
    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }
}
