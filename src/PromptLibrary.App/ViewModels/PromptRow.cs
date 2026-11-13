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
    private bool _isFeatured;
    private TeamMarker _marker;

    public PromptRow(Prompt prompt, bool isTeam, bool isHidden, bool isPinned, TeamMarker marker, bool showTeamIcon,
        IReadOnlyList<(int Start, int Length)>? titleMatches = null,
        IReadOnlyList<(int Start, int Length)>? descriptionMatches = null)
    {
        Prompt = prompt;
        IsTeam = isTeam;
        IsHidden = isHidden;
        _isPinned = isPinned;
        _marker = marker;
        ShowTeamIcon = showTeamIcon;
        TitleSegments = TextSegments.Split(prompt.Title, titleMatches);
        DescriptionSegments = TextSegments.Split(FirstLine(prompt.Description), descriptionMatches);
    }

    /// <summary>The title in matched and unmatched pieces (the matched letters of a search show in Brand).</summary>
    public IReadOnlyList<TextSegment> TitleSegments { get; }

    /// <summary>The description's first line in matched and unmatched pieces.</summary>
    public IReadOnlyList<TextSegment> DescriptionSegments { get; }

    /// <summary>Up to three tags, shown as small chips after the description when they fit.</summary>
    public IReadOnlyList<string> TagChips => Prompt.Tags is { Count: > 0 } tags ? tags.Take(3).ToList() : [];

    public bool HasDescription => !string.IsNullOrWhiteSpace(Prompt.Description);

    private static string FirstLine(string? text)
    {
        text ??= "";
        var cut = text.AsSpan().IndexOfAny('\r', '\n');
        return cut < 0 ? text : text[..cut];
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

    /// <summary>True for a team prompt the editors feature (the row shows a small star).</summary>
    public bool IsFeatured
    {
        get => _isFeatured;
        set => SetProperty(ref _isFeatured, value);
    }

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
