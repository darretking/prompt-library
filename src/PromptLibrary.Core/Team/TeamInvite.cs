using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Team;

/// <summary>Builds the invite message a team member sends to colleagues.</summary>
public static class TeamInvite
{
    public const string DownloadUrl = "https://github.com/darretking/prompt-library/releases/latest";

    public static string Build(TeamSource source, IReadOnlyList<CloudFolder> clouds) =>
        "Join our team prompts in Prompt Library.\n"
        + $"1. Get the app: {DownloadUrl}\n"
        + "2. In Prompt Library open Settings → Team prompts → Join, and paste:\n"
        + Describe(source, clouds);

    private static string Describe(TeamSource source, IReadOnlyList<CloudFolder> clouds)
    {
        if (source.Kind == TeamSourceKind.Url) return source.Location;

        var dir = Path.GetDirectoryName(source.Location);
        if (string.IsNullOrEmpty(dir)) return source.Location;
        foreach (var cloud in clouds.OrderByDescending(c => c.RootPath.Length))
        {
            var root = Path.TrimEndingDirectorySeparator(cloud.RootPath);
            var name = Path.GetFileName(root);
            if (name.Length == 0) continue;
            var rel = Path.GetRelativePath(root, dir);
            if (rel == ".") return name;
            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) continue;
            return Path.Combine(name, rel);
        }
        return source.Location;
    }
}
