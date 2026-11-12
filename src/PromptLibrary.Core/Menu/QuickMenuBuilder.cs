using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Menu;

public record QuickMenuPrompt(Guid Id, string Title, bool IsTeam = false);

public record QuickMenuCategory(string Name, IReadOnlyList<QuickMenuPrompt> Prompts);

/// <summary>The shortcut menu: pinned shortcuts, then the team block, then the user's own categories.
/// <see cref="TeamCollapsed"/> tells the tray to render the team block as one "Team prompts" submenu.</summary>
public record QuickMenu(
    IReadOnlyList<QuickMenuPrompt> Pinned,
    IReadOnlyList<QuickMenuCategory> Team,
    IReadOnlyList<QuickMenuCategory> Yours,
    bool TeamCollapsed);

public static class QuickMenuBuilder
{
    /// <summary>More team categories than this collapses the team block into one submenu.</summary>
    public const int TeamCollapseThreshold = 6;

    public static QuickMenu Build(Library personal, Library? team, AppSettings settings)
    {
        var hidden = new HashSet<Guid>(settings.HiddenTeamPromptIds);
        var teamCats = team is null ? [] : Categories(team, true, hidden);

        var pinned = new List<QuickMenuPrompt>();
        foreach (var id in settings.PinnedPromptIds)
        {
            if (personal.Prompts.FirstOrDefault(p => p.Id == id) is { } mine)
                pinned.Add(new QuickMenuPrompt(mine.Id, mine.Title));
            else if (team?.Prompts.FirstOrDefault(p => p.Id == id) is { } t && !hidden.Contains(id))
                pinned.Add(new QuickMenuPrompt(t.Id, t.Title, true));
        }

        return new QuickMenu(pinned, teamCats, Categories(personal, false, null),
            teamCats.Count > TeamCollapseThreshold);
    }

    public static IReadOnlyList<QuickMenuCategory> Build(Library lib)
        => Categories(lib, false, null);

    private static IReadOnlyList<QuickMenuCategory> Categories(Library lib, bool isTeam, HashSet<Guid>? hidden)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return lib.Categories
            .OrderBy(c => c.Name, comparer)
            .Select(c => new QuickMenuCategory(
                MenuText.WithEmoji(c.Emoji, c.Name),
                lib.Prompts
                    .Where(p => p.CategoryId == c.Id && hidden?.Contains(p.Id) != true)
                    .OrderBy(p => p.Title, comparer)
                    .Select(p => new QuickMenuPrompt(p.Id, p.Title, isTeam))
                    .ToList()))
            .Where(c => c.Prompts.Count > 0)
            .ToList();
    }
}
