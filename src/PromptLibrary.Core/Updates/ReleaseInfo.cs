namespace PromptLibrary.Core.Updates;

/// <summary>
/// A GitHub release as the update checker sees it. <paramref name="Version"/> is the parsed tag
/// (<see cref="AppVersion.ToString"/>, e.g. <c>1.0.2</c>); the asset fields describe the installer and are null when
/// the release has no MSI. <paramref name="AssetSha256"/> is lowercase hex (from GitHub's <c>digest</c>), or null.
/// </summary>
public sealed record ReleaseInfo(
    string Version,
    string Tag,
    string PageUrl,
    string? AssetName,
    string? AssetUrl,
    long? AssetSize,
    string? AssetSha256);
