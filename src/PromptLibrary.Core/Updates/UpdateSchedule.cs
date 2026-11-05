namespace PromptLibrary.Core.Updates;

/// <summary>When to check, and what the saved state means for the version that is running.</summary>
public static class UpdateSchedule
{
    /// <summary>At most one answered automatic check per day, to stay well inside GitHub's rate limit.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>After a failed attempt (e.g. offline), wait this long before trying again automatically.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// True when an automatic check should run: none answered in the last 24 h and none tried in the last hour.
    /// A time in the future (the clock was moved back) counts as due.
    /// </summary>
    public static bool IsDue(UpdateState state, DateTimeOffset now)
        => IsOlderThan(state.LastCheckedAt, CheckInterval, now) && IsOlderThan(state.LastAttemptAt, RetryInterval, now);

    /// <summary>True when the saved latest release is newer than <paramref name="running"/>.</summary>
    public static bool HasNewer(UpdateState state, AppVersion running)
        => LatestVersion(state) is { } latest && latest > running;

    /// <summary>True when the newer release hasn't been declined or announced yet.</summary>
    public static bool ShouldPrompt(UpdateState state, AppVersion running)
        => HasNewer(state, running)
           && !SameVersion(state.DeclinedVersion, state.LatestRelease!.Version)
           && !SameVersion(state.PromptedVersion, state.LatestRelease!.Version);

    /// <summary>
    /// Forgets the saved release and the declined/announced versions once the running app has caught up (or the
    /// saved release is unusable). True when anything changed, so the caller saves.
    /// </summary>
    public static bool Reconcile(UpdateState state, AppVersion running)
    {
        if (HasNewer(state, running)) return false;
        if (state.LatestRelease is null && state.DeclinedVersion is null && state.PromptedVersion is null) return false;

        state.LatestRelease = null;
        state.DeclinedVersion = null;
        state.PromptedVersion = null;
        return true;
    }

    private static AppVersion? LatestVersion(UpdateState state)
        => state.LatestRelease is { } release && AppVersion.TryParse(release.Version, out var v) ? v : null;

    private static bool SameVersion(string? a, string b)
        => a is not null && AppVersion.TryParse(a, out var va) && AppVersion.TryParse(b, out var vb) && va.CompareTo(vb) == 0;

    private static bool IsOlderThan(DateTimeOffset? time, TimeSpan age, DateTimeOffset now)
        => time is not { } t || t > now || now - t >= age;
}
