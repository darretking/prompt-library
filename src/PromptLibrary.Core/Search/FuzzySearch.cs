using System.Text.RegularExpressions;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Search;

/// <summary>
/// A ranked search hit. <see cref="TitleMatches"/> and <see cref="DescriptionMatches"/> are character ranges
/// (start, length; sorted, not overlapping) the views can show in bold.
/// </summary>
public sealed record SearchResult(
    Prompt Prompt,
    bool IsTeam,
    bool IsHidden,
    double Score,
    IReadOnlyList<(int Start, int Length)> TitleMatches,
    IReadOnlyList<(int Start, int Length)> DescriptionMatches,
    IReadOnlyList<int>? TagMatches = null,
    MatchSnippet? TextSnippet = null);

/// <summary>
/// About 60 characters of a prompt's text around the first match (line breaks shown as spaces; "…" where cut).
/// <see cref="Start"/> and <see cref="Length"/> mark the match inside <see cref="Text"/>.
/// </summary>
public sealed record MatchSnippet(string Text, int Start, int Length);

/// <summary>A parsed search box: <c>cat:name</c> (or <c>cat:"two words"</c>), <c>#tag</c> and free words, all lower-case.</summary>
public sealed record SearchQuery(IReadOnlyList<string> Words, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags)
{
    public bool IsEmpty => Words.Count == 0 && Categories.Count == 0 && Tags.Count == 0;
}

/// <summary>
/// Ranked search over the personal and team libraries. Every filter and word must match (AND). A word matches, best first,
/// as an exact word, a word prefix, a substring, letters in order inside one word ("trp" in "trip"), or a word with one
/// typo (words of 4+ letters). Title hits count most, then tags, description and text. Ties are A-Z.
/// </summary>
public static partial class FuzzySearch
{
    private const double TitleWeight = 10, TagWeight = 6, DescriptionWeight = 4, TextWeight = 1;
    private const double Exact = 1.0, Prefix = 0.8, Substring = 0.6, Subsequence = 0.35, Typo = 0.3;
    private const double MaxFrecencyBonus = 2.0;

    [GeneratedRegex("""cat:(?:"([^"]*)"|(\S*))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CategoryToken();

    /// <summary>Splits the text into filters and words.</summary>
    public static SearchQuery ParseQuery(string? query)
    {
        var categories = new List<string>();
        var rest = CategoryToken().Replace(query ?? "", m =>
        {
            var value = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            if (value.Length > 0) categories.Add(value.ToLowerInvariant());
            return " ";
        });

        var words = new List<string>();
        var tags = new List<string>();
        foreach (var token in rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token[0] == '#')
            {
                var tag = token.TrimStart('#').ToLowerInvariant();
                if (tag.Length > 0) tags.Add(tag);
            }
            else words.Add(token.ToLowerInvariant());
        }
        return new SearchQuery(words, categories, tags);
    }

    /// <summary>
    /// Searches both libraries; hidden team prompts are returned flagged so they can be shown greyed. An empty query
    /// returns nothing. Best first (score plus a small bonus for prompts used often and lately), then A-Z.
    /// </summary>
    public static IReadOnlyList<SearchResult> Search(Library personal, Library? team, AppSettings settings, string? query, DateTimeOffset now)
    {
        var q = ParseQuery(query);
        if (q.IsEmpty) return [];

        var results = new List<SearchResult>();
        Collect(personal, isTeam: false);
        if (team is not null) Collect(team, isTeam: true);

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Prompt.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        void Collect(Library lib, bool isTeam)
        {
            var categoryNames = lib.Categories.ToDictionary(c => c.Id, c => c.Name);
            foreach (var p in lib.Prompts)
            {
                var name = categoryNames.GetValueOrDefault(p.CategoryId) ?? "";
                if (!q.Categories.All(c => name.StartsWith(c, StringComparison.CurrentCultureIgnoreCase))) continue;
                if (!q.Tags.All(t => p.Tags.Any(pt => pt.StartsWith(t, StringComparison.OrdinalIgnoreCase)))) continue;

                var match = Match(p, q.Words, q.Tags);
                if (match is null) continue;

                var bonus = Math.Min(MaxFrecencyBonus, UsageTracker.Frecency(settings, p.Id, now) * 0.5);
                results.Add(new SearchResult(p, isTeam, isTeam && settings.HiddenTeamPromptIds.Contains(p.Id),
                    match.Value.Score + bonus, match.Value.Title, match.Value.Description,
                    match.Value.Tags, match.Value.Snippet));
            }
        }
    }


    private enum Field { None, Title, Tags, Description, Text }

    private static (double Score, IReadOnlyList<(int, int)> Title, IReadOnlyList<(int, int)> Description,
        IReadOnlyList<int>? Tags, MatchSnippet? Snippet)? Match(Prompt p, IReadOnlyList<string> words, IReadOnlyList<string> tagFilters)
    {
        var tags = string.Join(' ', p.Tags);
        var titleRanges = new List<(int, int)>();
        var descRanges = new List<(int, int)>();
        var tagHits = new SortedSet<int>();
        MatchSnippet? snippet = null;
        var score = 0.0;

        // A #tag filter matched these tags: they are what the row shows.
        for (var i = 0; i < p.Tags.Count; i++)
            if (tagFilters.Any(t => p.Tags[i].StartsWith(t, StringComparison.OrdinalIgnoreCase))) tagHits.Add(i);

        foreach (var word in words)
        {
            var best = 0.0;
            var source = Field.None;
            Consider(Field.Title, p.Title, TitleWeight, true, titleRanges);
            Consider(Field.Tags, tags, TagWeight, true, null);
            Consider(Field.Description, p.Description, DescriptionWeight, true, descRanges);
            Consider(Field.Text, p.Text, TextWeight, false, null);
            if (best == 0) return null;
            score += best;

            // Only a word whose best hit is in the tags or the text adds something the row doesn't already show.
            if (source == Field.Tags)
                for (var i = 0; i < p.Tags.Count; i++)
                    if (MatchWord(p.Tags[i], word, true) is not null) tagHits.Add(i);
            if (source == Field.Text && snippet is null && MatchWord(p.Text, word, false) is { } textMatch)
                snippet = MakeSnippet(p.Text, textMatch.Ranges[0].Start, textMatch.Ranges[0].Length);

            void Consider(Field field, string text, double weight, bool allowSubsequence, List<(int, int)>? ranges)
            {
                var m = MatchWord(text, word, allowSubsequence);
                if (m is null) return;
                ranges?.AddRange(m.Value.Ranges);
                if (m.Value.Quality * weight > best)
                {
                    best = m.Value.Quality * weight;
                    source = field;
                }
            }
        }
        return (score, Merge(titleRanges), Merge(descRanges), tagHits.Count > 0 ? tagHits.ToList() : null, snippet);
    }

    private const int SnippetBefore = 22, SnippetTotal = 60;

    /// <summary>About <see cref="SnippetTotal"/> characters of <paramref name="text"/> around the match, on one line.</summary>
    private static MatchSnippet MakeSnippet(string text, int start, int length)
    {
        var from = Math.Max(0, start - SnippetBefore);
        var to = Math.Min(text.Length, Math.Max(start + length, from + SnippetTotal));
        from = Math.Max(0, Math.Min(from, to - SnippetTotal));

        var sb = new System.Text.StringBuilder();
        if (from > 0) sb.Append('…');
        int? matchFrom = null, matchTo = null;
        for (var i = from; i < to; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length == 0 || sb[^1] == ' ' || (from > 0 && sb.Length == 1)) continue; // one space, none at the start
                c = ' ';
            }
            if (i >= start && i < start + length)
            {
                matchFrom ??= sb.Length;
                matchTo = sb.Length + 1;
            }
            sb.Append(c);
        }
        if (to < text.Length) sb.Append('…');
        var s = matchFrom ?? 0;
        return new MatchSnippet(sb.ToString(), s, (matchTo ?? s) - s);
    }

    private readonly record struct WordMatch(double Quality, List<(int Start, int Length)> Ranges);

    private static WordMatch? MatchWord(string? field, string word, bool allowSubsequence)
    {
        if (string.IsNullOrEmpty(field)) return null;
        WordMatch? best = null;
        void Offer(double quality, int start, int length)
        {
            if (best is null || quality > best.Value.Quality) best = new(quality, [(start, length)]);
        }

        var index = field.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) Offer(Substring, index, word.Length);

        foreach (var (start, token) in Tokens(field))
        {
            if (token == word) { Offer(Exact, start, token.Length); break; }
            if (token.StartsWith(word, StringComparison.Ordinal)) Offer(Prefix, start, word.Length);
        }
        if (best is { Quality: >= Prefix }) return best;

        foreach (var (start, token) in Tokens(field))
        {
            if (allowSubsequence && token.Length > word.Length && token[0] == word[0]
                && SubsequenceRanges(token, word, start) is { } ranges
                && (best is null || Subsequence > best.Value.Quality))
                best = new(Subsequence, ranges);

            if (word.Length >= 4 && (best is null || Typo > best.Value.Quality)
                && (Distance(word, token) <= 1
                    || (token.Length > word.Length && Distance(word, token[..word.Length]) <= 1)))
                best = new(Typo, [(start, token.Length)]);
        }
        return best;
    }

    // Runs of letters and digits, lower-cased (same length as the original, so positions line up).
    private static IEnumerable<(int Start, string Token)> Tokens(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            if (!char.IsLetterOrDigit(s[i])) { i++; continue; }
            var start = i;
            while (i < s.Length && char.IsLetterOrDigit(s[i])) i++;
            yield return (start, string.Create(i - start, (s, start), (span, st) =>
            {
                for (var k = 0; k < span.Length; k++) span[k] = char.ToLowerInvariant(st.s[st.start + k]);
            }));
        }
    }

    private static List<(int, int)>? SubsequenceRanges(string token, string word, int offset)
    {
        var ranges = new List<(int, int)>();
        var w = 0;
        for (var t = 0; t < token.Length && w < word.Length; t++)
            if (token[t] == word[w]) { ranges.Add((offset + t, 1)); w++; }
        return w == word.Length ? ranges : null;
    }

    private static List<(int Start, int Length)> Merge(List<(int Start, int Length)> ranges)
    {
        var merged = new List<(int Start, int Length)>();
        foreach (var (start, length) in ranges.OrderBy(r => r.Start).ThenByDescending(r => r.Length))
        {
            if (merged.Count > 0 && start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Length, start + length - last.Start));
            }
            else merged.Add((start, length));
        }
        return merged;
    }

    /// <summary>Edit distance counting an insert, delete, substitution or swap of two neighbours as one step.</summary>
    public static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1) return 2; // callers only care about "within 1"
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
