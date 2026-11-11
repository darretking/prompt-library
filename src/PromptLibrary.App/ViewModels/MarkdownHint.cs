using System.Text.RegularExpressions;

namespace PromptLibrary.App.ViewModels;

/// <summary>
/// A cheap check for "this text uses markdown the preview would draw": a heading, a list, **bold** or a fenced code block.
/// Plain prose (and code that merely contains symbols) is not markdown, so its Edit | Preview switch stays hidden.
/// </summary>
public static partial class MarkdownHint
{
    public static bool Looks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Heading().IsMatch(text) || ListItem().IsMatch(text) || Bold().IsMatch(text) || text.Contains("```");
    }

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+\S", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ \t]*([-*+]|\d{1,3}[.)])[ \t]+\S", RegexOptions.Multiline)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"\*\*[^*\r\n]+\*\*")]
    private static partial Regex Bold();
}
