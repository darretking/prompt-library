using CommunityToolkit.Mvvm.ComponentModel;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>Which library the main window shows.</summary>
public enum LibrarySide
{
    /// <summary>Your prompts (the personal library).</summary>
    Yours,

    /// <summary>Team prompts (read only unless the team library is being edited).</summary>
    Team,
}

/// <summary>
/// One row in the prompt list: a prompt from your library or the team's, with the few extras a row may show (a pin,
/// a "New" / "Updated" tag, the team icon in search results, greyed when hidden from the menu).
/// </summary>
public sealed class PromptRow : ObservableObject
{
    private bool _isPinned;
    private TeamMarker _marker;

    public PromptRow(Prompt prompt, bool isTeam, bool isHidden, bool isPinned, TeamMarker marker, bool showTeamIcon)
    {
        Prompt = prompt;
        IsTeam = isTeam;
        IsHidden = isHidden;
        _isPinned = isPinned;
        _marker = marker;
        ShowTeamIcon = showTeamIcon;
    }

    public Prompt Prompt { get; }
    public Guid Id => Prompt.Id;
    public string Title => Prompt.Title;
    public string Description => Prompt.Description;

    /// <summary>True for a team prompt.</summary>
    public bool IsTeam { get; }

    /// <summary>True for a team prompt hidden from the menu (shown greyed, with Unhide).</summary>
    public bool IsHidden { get; }

    /// <summary>True in search results for a team prompt: the row shows the team icon.</summary>
    public bool ShowTeamIcon { get; }

    /// <summary>A prompt hidden from the menu can't be pinned (the row menu's Pin is off).</summary>
    public bool CanPin => !IsHidden;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!SetProperty(ref _isPinned, value)) return;
            OnPropertyChanged(nameof(PinLabel));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    public TeamMarker Marker
    {
        get => _marker;
        set
        {
            if (!SetProperty(ref _marker, value)) return;
            OnPropertyChanged(nameof(MarkerText));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    /// <summary><c>New</c> or <c>Updated</c> until the prompt is opened once; otherwise null.</summary>
    public string? MarkerText => _marker switch
    {
        TeamMarker.New => "New",
        TeamMarker.Updated => "Updated",
        _ => null,
    };

    /// <summary>The row menu's <c>Pin</c> / <c>Unpin</c>.</summary>
    public string PinLabel => _isPinned ? "Unpin" : "Pin";

    /// <summary>The row menu's <c>Hide from menu</c> / <c>Unhide</c>.</summary>
    public string HideLabel => IsHidden ? "Unhide" : "Hide from menu";

    /// <summary>What a screen reader says for the row, e.g. <c>Follow-up email, team prompt, pinned, new</c>.</summary>
    public string AutomationName
    {
        get
        {
            var parts = new List<string> { Title };
            if (IsTeam) parts.Add("team prompt");
            if (_isPinned) parts.Add("pinned");
            if (MarkerText is { } marker) parts.Add(marker.ToLowerInvariant());
            if (IsHidden) parts.Add("hidden from the menu");
            return string.Join(", ", parts);
        }
    }
}
