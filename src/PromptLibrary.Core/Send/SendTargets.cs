namespace PromptLibrary.Core.Send;

/// <summary>An AI site a prompt can be sent to.</summary>
public sealed record SendTarget(string Id, string Name, string BaseUrl, string QueryUrl)
{
    /// <summary>The address that opens the site with the prompt filled in; the site's start page when the text is too long for a link.</summary>
    public string BuildUrl(string? text)
    {
        if (string.IsNullOrEmpty(text)) return BaseUrl;
        var url = QueryUrl + Uri.EscapeDataString(text);
        return url.Length > SendTargets.MaxUrlLength ? BaseUrl : url;
    }

    /// <summary>True when <see cref="BuildUrl"/> had to fall back to the start page.</summary>
    public bool IsTooLong(string? text)
        => !string.IsNullOrEmpty(text) && (QueryUrl + Uri.EscapeDataString(text)).Length > SendTargets.MaxUrlLength;
}

/// <summary>The four supported send targets.</summary>
public static class SendTargets
{
    /// <summary>Longest link opened; beyond this the site's start page opens instead (the text is on the clipboard anyway).</summary>
    public const int MaxUrlLength = 6000;

    /// <summary>The id used when a prompt has no default (Ctrl+Enter in the search window).</summary>
    public const string DefaultId = "chatgpt";

    public static IReadOnlyList<SendTarget> All { get; } =
    [
        new("chatgpt", "ChatGPT", "https://chatgpt.com/", "https://chatgpt.com/?q="),
        new("claude", "Claude", "https://claude.ai/new", "https://claude.ai/new?q="),
        new("copilot", "Copilot", "https://copilot.microsoft.com/", "https://copilot.microsoft.com/?q="),
        new("perplexity", "Perplexity", "https://www.perplexity.ai/", "https://www.perplexity.ai/search?q="),
    ];

    /// <summary>The target with this id (ignoring case), or null.</summary>
    public static SendTarget? Find(string? id)
        => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The prompt's default target, or ChatGPT when it has none (or an unknown one).</summary>
    public static SendTarget Resolve(string? id) => Find(id) ?? Find(DefaultId)!;
}
