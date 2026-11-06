using CommunityToolkit.Mvvm.ComponentModel;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// One row in the category rail: "All prompts" (<see cref="Id"/> = null) or a category, with its
/// prompt count. Also holds the inline rename state for that row.
/// </summary>
public sealed class CategoryItem : ObservableObject
{
    private bool _isRenaming;
    private string _editName = "";
    private string? _error;

    public CategoryItem(Guid? id, string name, int count, bool canEdit = true)
    {
        Id = id;
        Name = name;
        Count = count;
        CanEdit = canEdit;
    }

    public Guid? Id { get; }
    public string Name { get; }
    public int Count { get; }

    /// <summary>False on a read-only side (team prompts): no Rename or Delete.</summary>
    public bool CanEdit { get; }

    /// <summary>True for the "All prompts" row, which can't be renamed or deleted.</summary>
    public bool IsAll => Id is null;

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

    /// <summary>Validation message shown under the rename box, or null.</summary>
    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }
}
