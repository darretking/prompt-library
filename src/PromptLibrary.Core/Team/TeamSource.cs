using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Team;

/// <summary>Where a team library is read from.</summary>
public enum TeamSourceKind
{
    /// <summary>A file on this PC or a network or synced folder.</summary>
    File,

    /// <summary>A public https link to the file itself.</summary>
    Url,
}

/// <summary>A team library's source: a file path or an https link.</summary>
public sealed record TeamSource(TeamSourceKind Kind, string Location)
{
    /// <summary>
    /// Turns what the user typed or picked into a source. A folder becomes <c>folder\team-prompts.json</c>, a file path is kept,
    /// and an https link is kept after two rewrites: Dropbox <c>dl=0</c> becomes <c>dl=1</c>, and a GitHub <c>/blob/</c>
    /// link becomes its raw link. Returns null for empty input, http and any other scheme.
    /// When <paramref name="clouds"/> is given, a path that starts with a cloud folder's name (<c>OneDrive - Acme\Shared</c>,
    /// any case) is resolved against that folder's root first.
    /// </summary>
    public static TeamSource? FromInput(string input, IReadOnlyList<CloudFolder>? clouds = null)
    {
        var text = input?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            return new TeamSource(TeamSourceKind.Url, Rewrite(uri).AbsoluteUri);
        }

        text = ResolveCloudRelative(text, clouds);
        if (Directory.Exists(text) || Path.GetExtension(text).Length == 0)
            return new TeamSource(TeamSourceKind.File, Path.Combine(text, TeamFile.FileName));
        return new TeamSource(TeamSourceKind.File, text);
    }

    private static string ResolveCloudRelative(string text, IReadOnlyList<CloudFolder>? clouds)
    {
        if (clouds is null || Path.IsPathRooted(text)) return text;
        foreach (var cloud in clouds)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(cloud.RootPath));
            if (name.Length == 0 || !text.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = text[name.Length..];
            if (rest.Length > 0 && rest[0] != '\\' && rest[0] != '/') continue;
            rest = rest.TrimStart('\\', '/');
            return rest.Length == 0 ? cloud.RootPath : Path.Combine(cloud.RootPath, rest.Replace('/', '\\'));
        }
        return text;
    }

    private static Uri Rewrite(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (host == "dropbox.com" || host.EndsWith(".dropbox.com", StringComparison.Ordinal))
        {
            var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("dl=", StringComparison.OrdinalIgnoreCase)).Append("dl=1");
            return new UriBuilder(uri) { Query = string.Join('&', parts) }.Uri;
        }

        if (host == "github.com")
        {
            var seg = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (seg.Length >= 5 && seg[2] == "blob")
                return new Uri($"https://raw.githubusercontent.com/{seg[0]}/{seg[1]}/{string.Join('/', seg.Skip(3))}");
        }
        return uri;
    }
}
