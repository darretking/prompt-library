namespace PromptLibrary.Core.Updates;

/// <summary>What the update checker remembers between runs (<c>update-check.json</c>).</summary>
public sealed class UpdateState
{
    /// <summary>The last check that got an answer from GitHub (any result but a failure).</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>The last check that was tried, answered or not; spaces out retries while offline.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>The newest release seen; kept so the reminder shows at startup without asking GitHub again.</summary>
    public ReleaseInfo? LatestRelease { get; set; }

    /// <summary>The version the user said "Not now" to: no more balloons or dialogs for it (the reminder stays).</summary>
    public string? DeclinedVersion { get; set; }

    /// <summary>The version a balloon or dialog has already announced, so a restart doesn't announce it again.</summary>
    public string? PromptedVersion { get; set; }
}
