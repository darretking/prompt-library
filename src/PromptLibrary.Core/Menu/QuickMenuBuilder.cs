using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Menu;

public record QuickMenuPrompt(Guid Id, string Title, bool IsTeam = false);

public record QuickMenuCategory(string Name, IReadOnlyList<QuickMenuPrompt> Prompts);

/// <summary>The shortcut menu: pinned shortcuts, then the team block, then the user's own categories.
/// <see cref="TeamCollapsed"/> tells the tray to render the team block as one "Team prompts" submenu.</summary>
public record QuickMenu(
    IReadOnlyList<QuickMenuPrompt> Pinned,
    IReadOnlyList<QuickMenuCategory> Team,
    IReadOnlyList<QuickMenuCategory> Yours,
    bool TeamCollapsed)
{
    /// <summary>Team prompts the editors feature, shown right after Pinned (a pinned one shows only in Pinned).</summary>
    public IReadOnlyList<QuickMenuPrompt> Featured { get; init; } = [];
}

public static class QuickMenuBuilder
{
    /// <summary>More team categories than this collapses the team block into one submenu.</summary>
    public const int TeamCollapseThreshold = 6;

    public static QuickMenu Build(Library personal, Library? team, AppSettings settings)
    {
        var hidden = new HashSet<Guid>(settings.HiddenTeamPromptIds);
        var teamCats = team is null ? [] : Categories(team, true, hidden, settings);

        var pinned = new List<QuickMenuPrompt>();
        foreach (var id in settings.PinnedPromptIds)
        {
            if (personal.Prompts.FirstOrDefault(p => p.Id == id) is { } mine)
                pinned.Add(new QuickMenuPrompt(mine.Id, mine.Title));
            else if (team?.Prompts.FirstOrDefault(p => p.Id == id) is { } t && !hidden.Contains(id))
                pinned.Add(new QuickMenuPrompt(t.Id, t.Title, true));
        }

        var pinnedIds = pinned.Select(p => p.Id).ToHashSet();
        var featured = team is null ? [] : team.Prompts
            .Where(p => p.Featured && !hidden.Contains(p.Id) && !pinnedIds.Contains(p.Id))
            .Select(p => new QuickMenuPrompt(p.Id, p.Title, true))
            .ToList();

        pinned = UsageOrder.Sort(pinned, p => UsageOrder.Uses(settings, p.Id), p => p.Title, settings).ToList();

        return new QuickMenu(pinned, teamCats, Categories(personal, false, null, settings),
            teamCats.Count > TeamCollapseThreshold) { Featured = featured };
    }

    /// <summary>One library as categories, A to Z (no usage is known here).</summary>
    public static IReadOnlyList<QuickMenuCategory> Build(Library lib)
        => Categories(lib, false, null, new AppSettings { PromptSort = UsageOrder.ByName });

    private static IReadOnlyList<QuickMenuCategory> Categories(
        Library lib, bool isTeam, HashSet<Guid>? hidden, AppSettings settings)
    {
        var groups = lib.Categories
            .Select(c => (Category: c, Prompts: UsageOrder.Prompts(
                lib.Prompts.Where(p => p.CategoryId == c.Id && hidden?.Contains(p.Id) != true), settings)))
            .Where(g => g.Prompts.Count > 0);
        // Sorted on the raw category name; the emoji is added only when mapping to the menu entry.
        return UsageOrder.Sort(groups, g => g.Prompts.Sum(p => UsageOrder.Uses(settings, p.Id)), g => g.Category.Name, settings)
            .Select(g => new QuickMenuCategory(
                MenuText.WithEmoji(g.Category.Emoji, g.Category.Name),
                g.Prompts.Select(p => new QuickMenuPrompt(p.Id, p.Title, isTeam)).ToList()))
            .ToList();
    }
}
