namespace PromptLibrary.Core.Updates;

/// <summary>Asks GitHub whether a newer release exists and remembers what the user was told.</summary>
public interface IUpdateCheckService
{
    /// <summary>The saved state (latest release seen, declined and announced versions).</summary>
    UpdateState State { get; }

    /// <summary>The version that is running.</summary>
    AppVersion Running { get; }

    /// <summary>
    /// Checks GitHub. An automatic check (<paramref name="manual"/> false) only runs when
    /// <see cref="UpdateSchedule.IsDue"/>; otherwise it returns <see cref="UpdateCheckStatus.NotDue"/>. Network,
    /// HTTP and parse problems come back as <see cref="UpdateCheckStatus.Failed"/>; nothing is thrown for them.
    /// A call while another check is running joins that check.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct = default);

    /// <summary>
    /// Asks GitHub again; its latest release if that is still <paramref name="version"/>, else null. Update now
    /// downloads from this, never from the saved state.
    /// </summary>
    Task<ReleaseInfo?> ConfirmLatestAsync(string version, CancellationToken ct);

    /// <summary>A balloon or dialog announced <paramref name="version"/>; don't announce it again.</summary>
    void MarkPrompted(string version);

    /// <summary>The user said "Not now" to <paramref name="version"/>.</summary>
    void MarkDeclined(string version);
}
