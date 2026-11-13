using System.Security.Cryptography;
using System.Text;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Team;

/// <summary>Whether a team prompt is unseen on this PC.</summary>
public enum TeamMarker { None, New, Updated }

/// <summary>Per-PC team state kept in <see cref="AppSettings"/>: pins (any prompt), hides (team prompts) and the
/// New / Updated markers. Pure logic; the caller saves the settings afterwards.</summary>
public static class TeamUserState
{
    /// <summary>The most prompts that can be pinned at once.</summary>
    public const int MaxPins = 10;

    /// <summary>SHA-256 (hex) of title, description, text and category ID, with line endings normalised to
    /// <c>\n</c>. A category rename changes nothing; moving a prompt to another category does.</summary>
    public static string Fingerprint(Prompt p)
    {
        static string N(string s) => (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var joined = N(p.Title) + "\n" + N(p.Description) + "\n" + N(p.Text) + "\n" + p.CategoryId.ToString("D");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    /// <summary>SHA-256 (hex) of title, description and text only, line endings normalised. Kept on a personal copy of a
    /// team prompt (<see cref="Prompt.SourceFingerprint"/>): moving the team prompt to another category does not make the
    /// copy out of date, and comparing it with the copy's own content tells whether the member edited the copy.</summary>
    public static string ContentFingerprint(Prompt p)
    {
        static string N(string s) => (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var joined = N(p.Title) + "\n" + N(p.Description) + "\n" + N(p.Text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    /// <summary>On first join, records every prompt as seen so nothing is marked, and sets the baseline flag.</summary>
    public static void TakeBaseline(AppSettings s, Library team)
    {
        foreach (var p in team.Prompts) s.SeenTeamPrompts[p.Id] = Fingerprint(p);
        s.TeamBaselineTaken = true;
    }

    /// <summary>New when the ID was never seen, Updated when its fingerprint changed, otherwise None.
    /// Always None before the baseline is taken.</summary>
    public static TeamMarker MarkerFor(AppSettings s, Prompt p)
    {
        if (!s.TeamBaselineTaken) return TeamMarker.None;
        if (!s.SeenTeamPrompts.TryGetValue(p.Id, out var seen)) return TeamMarker.New;
        return seen == Fingerprint(p) ? TeamMarker.None : TeamMarker.Updated;
    }

    /// <summary>Records the prompt's current content as seen. The app layer must call this after the editor's own
    /// save of a team prompt (and when a prompt is opened), so an editor's own changes never mark their own copy.</summary>
    public static void MarkSeen(AppSettings s, Prompt p) => s.SeenTeamPrompts[p.Id] = Fingerprint(p);

    /// <summary>"Mark all as seen": records every prompt in the team library.</summary>
    public static void MarkAllSeen(AppSettings s, Library team)
    {
        foreach (var p in team.Prompts) MarkSeen(s, p);
    }

    /// <summary>True while any team prompt carries a marker (drives the switch dot).</summary>
    public static bool HasUnseen(AppSettings s, Library team)
        => team.Prompts.Any(p => MarkerFor(s, p) != TeamMarker.None);

    /// <summary>Appends the pin (newest last) and unhides it. Returns false and changes nothing when
    /// <see cref="MaxPins"/> pins already exist; pinning an already-pinned prompt is a no-op returning true.</summary>
    public static bool Pin(AppSettings s, Guid id)
    {
        if (s.PinnedPromptIds.Contains(id)) return true;
        if (s.PinnedPromptIds.Count >= MaxPins) return false;
        s.PinnedPromptIds.Add(id);
        s.HiddenTeamPromptIds.Remove(id);
        return true;
    }

    /// <summary>
    /// The team library to pass to <see cref="Prune"/>: the loaded copy while a team library is joined, an empty one
    /// when none is (so a solo user's dangling pins are dropped too), and null while one is joined but no copy is
    /// loaded (unreachable: its ids must not be pruned).
    /// </summary>
    public static Library? TeamForPrune(bool joined, Library? loaded) => joined ? loaded : new Library();

    public static void Unpin(AppSettings s, Guid id) => s.PinnedPromptIds.Remove(id);

    /// <summary>Hides a team prompt; hiding also unpins it.</summary>
    public static void Hide(AppSettings s, Guid id)
    {
        s.PinnedPromptIds.Remove(id);
        if (!s.HiddenTeamPromptIds.Contains(id)) s.HiddenTeamPromptIds.Add(id);
    }

    public static void Unhide(AppSettings s, Guid id) => s.HiddenTeamPromptIds.Remove(id);

    /// <summary>Drops pins, hides, seen entries and usage whose prompt is in neither library. Returns whether anything
    /// changed. With no team library loaded (<paramref name="team"/> null) nothing is pruned, so a team that is
    /// briefly unreachable never loses its state (usage of team prompts included).</summary>
    public static bool Prune(AppSettings s, Library personal, Library? team)
    {
        if (team is null) return false;
        var live = new HashSet<Guid>(personal.Prompts.Select(p => p.Id));
        var teamIds = new HashSet<Guid>(team.Prompts.Select(p => p.Id));
        live.UnionWith(teamIds);

        var changed = s.PinnedPromptIds.RemoveAll(id => !live.Contains(id)) > 0;
        changed |= s.HiddenTeamPromptIds.RemoveAll(id => !teamIds.Contains(id)) > 0;
        foreach (var id in s.SeenTeamPrompts.Keys.Where(id => !teamIds.Contains(id)).ToList())
            changed |= s.SeenTeamPrompts.Remove(id);
        changed |= UsageTracker.Prune(s, live) > 0;
        return changed;
    }

    /// <summary>Leaving or switching team source: removes team pins, all hides, seen entries and the baseline flag;
    /// personal pins stay. Does not touch <see cref="AppSettings.TeamSource"/>.</summary>
    public static void ClearTeam(AppSettings s, Library? team)
    {
        if (team is not null)
        {
            var teamIds = new HashSet<Guid>(team.Prompts.Select(p => p.Id));
            s.PinnedPromptIds.RemoveAll(teamIds.Contains);
        }
        s.HiddenTeamPromptIds.Clear();
        s.SeenTeamPrompts.Clear();
        s.TeamBaselineTaken = false;
    }
}
