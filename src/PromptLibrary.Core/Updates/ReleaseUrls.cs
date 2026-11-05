namespace PromptLibrary.Core.Updates;

/// <summary>
/// The only URLs the update checker will open or download: https pages and assets of this app's own GitHub releases.
/// Anything else from an API response or the saved state (another host, <c>file:</c>, UNC paths, other protocols)
/// is refused, so it can never reach the browser, the shell or the downloader.
/// </summary>
public static class ReleaseUrls
{
    private const string Host = "github.com";

    /// <summary>The repository's latest-release page; the fallback when a release's own page URL isn't usable.</summary>
    public static string LatestPage(string owner, string repo) => $"https://{Host}/{owner}/{repo}/releases/latest";

    /// <summary>True for <c>https://github.com/{owner}/{repo}/releases/…</c>.</summary>
    public static bool IsReleasePage(string? url, string owner, string repo)
        => HasPrefix(url, $"/{owner}/{repo}/releases/");

    /// <summary>True for <c>https://github.com/{owner}/{repo}/releases/download/…</c> (GitHub then redirects to its CDN).</summary>
    public static bool IsReleaseAsset(string? url, string owner, string repo)
        => HasPrefix(url, $"/{owner}/{repo}/releases/download/");

    private static bool HasPrefix(string? url, string pathPrefix)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
           && uri.IsDefaultPort
           && string.IsNullOrEmpty(uri.UserInfo)
           && uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)
           && !uri.AbsolutePath.Contains("/../", StringComparison.Ordinal);
}
