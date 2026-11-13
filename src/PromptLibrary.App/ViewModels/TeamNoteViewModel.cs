using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>What the Note for everyone dialog ended with.</summary>
public enum TeamNoteChoice { None, Save, Remove }

/// <summary>
/// The "Note for everyone" dialog: a multi-line box (at most <see cref="TeamFile.MaxNoteLength"/> characters, with a
/// count), Save note, Remove note (only while a note exists) and Cancel.
/// </summary>
public sealed class TeamNoteViewModel(string? current) : ObservableObject
{
    private string _text = current ?? "";

    /// <summary>The text shown in the box; Save note needs something other than blanks.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public int MaxLength => TeamFile.MaxNoteLength;

    public string CountText => $"{_text.Length} / {MaxLength}";

    public bool CanSave => !string.IsNullOrWhiteSpace(_text) && _text.Length <= MaxLength;

    /// <summary>Remove note shows only when the team library has a note.</summary>
    public bool CanRemove { get; } = !string.IsNullOrWhiteSpace(current);

    public TeamNoteChoice Choice { get; private set; }

    /// <summary>The note to store (trimmed) when <see cref="Choice"/> is <see cref="TeamNoteChoice.Save"/>.</summary>
    public string NoteText => _text.Trim();

    public bool Save()
    {
        if (!CanSave) return false;
        Choice = TeamNoteChoice.Save;
        return true;
    }

    public bool Remove()
    {
        if (!CanRemove) return false;
        Choice = TeamNoteChoice.Remove;
        return true;
    }
}
