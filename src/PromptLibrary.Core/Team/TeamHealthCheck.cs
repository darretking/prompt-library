using System.Text.RegularExpressions;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.Core.Team;

/// <summary>The kinds of problem <see cref="TeamHealthCheck"/> looks for.</summary>
public enum HealthIssueKind { DuplicateTitle, DuplicateText, BrokenField, EmptyCategory, MixedChoices, NoDescription, LongTitle }

/// <summary>One problem for the editors. <see cref="PromptId"/> or <see cref="CategoryId"/> says where it is.</summary>
public sealed record HealthIssue(HealthIssueKind Kind, Guid? PromptId, Guid? CategoryId, string Message);

/// <summary>
/// Lists quality problems in a team library. Pure: it reads the library and changes nothing. Issues come in kind order;
/// within a kind, by category name then title.
/// </summary>
public static class TeamHealthCheck
{
    private const int MaxTitleLength = 80;

    public static IReadOnlyList<HealthIssue> Run(Library library)
    {
        var issues = new List<HealthIssue>();
        issues.AddRange(DuplicateTitles(library));
        issues.AddRange(DuplicateTexts(library));
        issues.AddRange(BrokenFields(library));
        issues.AddRange(EmptyCategories(library));
        issues.AddRange(MixedChoices(library));
        issues.AddRange(NoDescriptions(library));
        issues.AddRange(LongTitles(library));
        return issues;
    }

    private static List<HealthIssue> DuplicateTitles(Library library)
    {
        var seen = new HashSet<(Guid, string)>();
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var p in library.Prompts)
        {
            if (seen.Add((p.CategoryId, p.Title.Trim().ToLowerInvariant()))) continue;
            var category = CategoryName(library, p.CategoryId);
            found.Add((category, p.Title, new HealthIssue(HealthIssueKind.DuplicateTitle, p.Id, p.CategoryId,
                $"\"{p.Title}\" appears more than once in {category}.")));
        }
        return InOrder(found);
    }

    private static List<HealthIssue> DuplicateTexts(Library library)
    {
        var firstByText = new Dictionary<string, Prompt>();
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var p in library.Prompts)
        {
            var text = Regex.Replace(p.Text, @"\s+", " ").Trim();
            if (!firstByText.TryGetValue(text, out var other))
            {
                firstByText[text] = p;
                continue;
            }
            found.Add((CategoryName(library, p.CategoryId), p.Title, new HealthIssue(HealthIssueKind.DuplicateText, p.Id, null,
                $"\"{p.Title}\" has the same text as \"{other.Title}\".")));
        }
        return InOrder(found);
    }

    private static List<HealthIssue> BrokenFields(Library library)
    {
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var p in library.Prompts.Where(p => HasBrokenField(p.Text)))
            found.Add((CategoryName(library, p.CategoryId), p.Title, new HealthIssue(HealthIssueKind.BrokenField, p.Id, null,
                $"\"{p.Title}\" has a field that isn't closed.")));
        return InOrder(found);
    }

    private static List<HealthIssue> EmptyCategories(Library library)
    {
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var c in library.Categories.Where(c => !library.Prompts.Any(p => p.CategoryId == c.Id)))
            found.Add((c.Name, "", new HealthIssue(HealthIssueKind.EmptyCategory, null, c.Id, $"{c.Name} has no prompts.")));
        return InOrder(found);
    }

    // Each distinct option set (case-insensitive, trimmed) is kept once, in the order first seen.
    private static List<HealthIssue> MixedChoices(Library library)
    {
        var sets = new List<(Prompt Owner, List<string> Shown, HashSet<string> Keys)>();
        foreach (var p in library.Prompts)
            foreach (var field in PromptTemplate.Parse(p.Text).Where(f => f.Kind == TemplateFieldKind.Choice))
            {
                var shown = field.Options.Select(o => o.Trim()).ToList();
                var keys = shown.Select(o => o.ToLowerInvariant()).ToHashSet();
                if (sets.Any(s => s.Keys.SetEquals(keys))) continue;
                sets.Add((p, shown, keys));
            }

        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
            {
                var a = sets[i];
                var b = sets[j];
                if (a.Keys.Count(k => b.Keys.Contains(k)) < 2) continue;
                var message = $"The choices {{{string.Join(" / ", a.Shown)}}} and {{{string.Join(" / ", b.Shown)}}} are almost the same. Use one list everywhere.";
                found.Add((CategoryName(library, a.Owner.CategoryId), a.Owner.Title,
                    new HealthIssue(HealthIssueKind.MixedChoices, a.Owner.Id, null, message)));
            }
        return InOrder(found);
    }

    private static List<HealthIssue> NoDescriptions(Library library)
    {
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var p in library.Prompts.Where(p => string.IsNullOrWhiteSpace(p.Description)))
            found.Add((CategoryName(library, p.CategoryId), p.Title, new HealthIssue(HealthIssueKind.NoDescription, p.Id, null,
                $"\"{p.Title}\" has no description.")));
        return InOrder(found);
    }

    private static List<HealthIssue> LongTitles(Library library)
    {
        var found = new List<(string Category, string Title, HealthIssue Issue)>();
        foreach (var p in library.Prompts.Where(p => p.Title.Length > MaxTitleLength))
            found.Add((CategoryName(library, p.CategoryId), p.Title, new HealthIssue(HealthIssueKind.LongTitle, p.Id, null,
                $"\"{p.Title}\" has a long title (over {MaxTitleLength} characters).")));
        return InOrder(found);
    }

    // "{}" or a "{" followed by a letter with no "}" before the end of the line. A "{{" is an escaped brace and is skipped.
    private static bool HasBrokenField(string text)
    {
        foreach (var line in text.Split('\n'))
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '{') continue;
                if (i + 1 < line.Length && line[i + 1] == '{') { i++; continue; }
                if (i + 1 < line.Length && line[i + 1] == '}') return true;
                if (i + 1 < line.Length && char.IsLetter(line[i + 1]) && line.IndexOf('}', i) < 0) return true;
            }
        return false;
    }

    private static string CategoryName(Library library, Guid id) => library.Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "";

    private static List<HealthIssue> InOrder(List<(string Category, string Title, HealthIssue Issue)> found) =>
        found.OrderBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Issue)
            .ToList();
}
