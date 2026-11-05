using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

public class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly AppVersion Running = V("1.0.1");

    [Fact]
    public void IsDue_EmptyState_IsTrue() => Assert.True(UpdateSchedule.IsDue(new UpdateState(), Now));

    [Fact]
    public void IsDue_Checked23HoursAgo_IsFalse()
        => Assert.False(UpdateSchedule.IsDue(new UpdateState { LastCheckedAt = Now.AddHours(-23) }, Now));

    [Fact]
    public void IsDue_Checked25HoursAgo_IsTrue()
        => Assert.True(UpdateSchedule.IsDue(new UpdateState { LastCheckedAt = Now.AddHours(-25) }, Now));

    [Fact]
    public void IsDue_FailedAttempt59MinutesAgo_IsFalse()
        => Assert.False(UpdateSchedule.IsDue(
            new UpdateState { LastCheckedAt = Now.AddHours(-25), LastAttemptAt = Now.AddMinutes(-59) }, Now));

    [Fact]
    public void IsDue_FailedAttempt61MinutesAgo_IsTrue()
        => Assert.True(UpdateSchedule.IsDue(
            new UpdateState { LastCheckedAt = Now.AddHours(-25), LastAttemptAt = Now.AddMinutes(-61) }, Now));

    [Fact]
    public void IsDue_NeverSucceededButAttemptedRecently_IsFalse()
        => Assert.False(UpdateSchedule.IsDue(new UpdateState { LastAttemptAt = Now.AddMinutes(-5) }, Now));

    [Fact]
    public void IsDue_TimestampsInTheFuture_IsTrue()
        => Assert.True(UpdateSchedule.IsDue(
            new UpdateState { LastCheckedAt = Now.AddHours(2), LastAttemptAt = Now.AddHours(2) }, Now));

    [Fact]
    public void HasNewer_OnlyWhenLatestIsNewerThanRunning()
    {
        Assert.False(UpdateSchedule.HasNewer(new UpdateState(), Running));
        Assert.True(UpdateSchedule.HasNewer(StateWith("1.0.2"), Running));
        Assert.False(UpdateSchedule.HasNewer(StateWith("1.0.1"), Running));
        Assert.False(UpdateSchedule.HasNewer(StateWith("1.0.0"), Running));
        Assert.False(UpdateSchedule.HasNewer(StateWith("latest"), Running));
    }

    [Fact]
    public void ShouldPrompt_DeclinedOrPromptedVersion_IsFalse_NewerVersion_IsTrue()
    {
        Assert.True(UpdateSchedule.ShouldPrompt(StateWith("1.0.2"), Running));

        var declined = StateWith("1.0.2");
        declined.DeclinedVersion = "1.0.2";
        Assert.False(UpdateSchedule.ShouldPrompt(declined, Running));

        var prompted = StateWith("1.0.2");
        prompted.PromptedVersion = "1.0.2";
        Assert.False(UpdateSchedule.ShouldPrompt(prompted, Running));

        var newerThanDeclined = StateWith("1.0.3");
        newerThanDeclined.DeclinedVersion = "1.0.2";
        newerThanDeclined.PromptedVersion = "1.0.2";
        Assert.True(UpdateSchedule.ShouldPrompt(newerThanDeclined, Running));

        Assert.False(UpdateSchedule.ShouldPrompt(StateWith("1.0.1"), Running));
    }

    [Fact]
    public void Reconcile_RunningVersionCaughtUp_ClearsReleaseAndFlags()
    {
        var state = StateWith("1.0.2");
        state.DeclinedVersion = "1.0.2";
        state.PromptedVersion = "1.0.2";

        Assert.True(UpdateSchedule.Reconcile(state, V("1.0.2")));

        Assert.Null(state.LatestRelease);
        Assert.Null(state.DeclinedVersion);
        Assert.Null(state.PromptedVersion);
    }

    [Fact]
    public void Reconcile_StillBehind_ChangesNothing()
    {
        var state = StateWith("1.0.2");
        state.DeclinedVersion = "1.0.2";

        Assert.False(UpdateSchedule.Reconcile(state, Running));

        Assert.NotNull(state.LatestRelease);
        Assert.Equal("1.0.2", state.DeclinedVersion);
    }

    [Fact]
    public void Reconcile_EmptyState_ChangesNothing()
        => Assert.False(UpdateSchedule.Reconcile(new UpdateState(), Running));

    internal static ReleaseInfo Release(string version) => new(version, "v" + version,
        "https://github.com/darretking/prompt-library/releases/tag/v" + version, "PromptLibrary-Setup.msi",
        $"https://github.com/darretking/prompt-library/releases/download/v{version}/PromptLibrary-Setup.msi", 10,
        new string('a', 64));

    private static UpdateState StateWith(string version) => new() { LatestRelease = Release(version) };

    private static AppVersion V(string text) => AppVersion.TryParse(text, out var v) ? v : throw new ArgumentException(text);
}
