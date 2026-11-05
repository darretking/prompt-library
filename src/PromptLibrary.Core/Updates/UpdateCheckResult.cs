namespace PromptLibrary.Core.Updates;

/// <summary>Where the app's releases live and which version is running.</summary>
public sealed record UpdateOptions(
    string Owner,
    string Repo,
    AppVersion Running,
    string StatePath,
    string AssetName = "PromptLibrary-Setup.msi");

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,

    /// <summary>An automatic check inside the 24-hour window (or the 1-hour retry wait); nothing was asked.</summary>
    NotDue,

    Failed,
}

public enum UpdateFailure { None, Network, Timeout, RateLimited, NotFound, BadResponse }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, UpdateFailure Failure = UpdateFailure.None);
