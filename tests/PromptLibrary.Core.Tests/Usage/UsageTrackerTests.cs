using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Tests.Usage;

public class UsageTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_CountsAndStampsLastUse()
    {
        var s = new AppSettings();
        var id = Guid.NewGuid();
        UsageTracker.Record(s, id, Now.AddDays(-1));
        UsageTracker.Record(s, id, Now);

        Assert.Equal(2, s.Usage[id].Count);
        Assert.Equal(Now, s.Usage[id].LastUsedAt);
    }

    [Fact]
    public void Frecency_NeverUsed_IsZero()
    {
        Assert.Equal(0, UsageTracker.Frecency(new AppSettings(), Guid.NewGuid(), Now));
        Assert.Equal(0, UsageTracker.Frecency((PromptUsage?)null, Now));
    }

    [Fact]
    public void Frecency_UsedJustNow_EqualsCount()
        => Assert.Equal(3, UsageTracker.Frecency(new PromptUsage { Count = 3, LastUsedAt = Now }, Now), 6);

    [Fact]
    public void Frecency_HalvesEvery14Days()
    {
        var u = new PromptUsage { Count = 4, LastUsedAt = Now.AddDays(-14) };
        Assert.Equal(2, UsageTracker.Frecency(u, Now), 6);
        u.LastUsedAt = Now.AddDays(-28);
        Assert.Equal(1, UsageTracker.Frecency(u, Now), 6);
    }

    [Fact]
    public void Frecency_FutureTimestamp_IsNotBoosted()
        => Assert.Equal(2, UsageTracker.Frecency(new PromptUsage { Count = 2, LastUsedAt = Now.AddDays(3) }, Now), 6);

    [Fact]
    public void Frecency_RecentBeatsOldAtEqualCount()
        => Assert.True(UsageTracker.Frecency(new PromptUsage { Count = 2, LastUsedAt = Now }, Now)
                       > UsageTracker.Frecency(new PromptUsage { Count = 2, LastUsedAt = Now.AddDays(-30) }, Now));

    [Fact]
    public void Recent_MostRecentFirst_LimitedToN()
    {
        var s = new AppSettings();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < 5; i++) UsageTracker.Record(s, ids[i], Now.AddHours(i));

        Assert.Equal([ids[4], ids[3], ids[2]], UsageTracker.Recent(s, 3));
        Assert.Equal(5, UsageTracker.Recent(s, 10).Count);
        Assert.Empty(UsageTracker.Recent(s, 0));
        Assert.Empty(UsageTracker.Recent(new AppSettings(), 8));
    }

    [Fact]
    public void Prune_RemovesEntriesForMissingPrompts()
    {
        var s = new AppSettings();
        var keep = Guid.NewGuid();
        var gone = Guid.NewGuid();
        UsageTracker.Record(s, keep, Now);
        UsageTracker.Record(s, gone, Now);

        Assert.Equal(1, UsageTracker.Prune(s, [keep]));
        Assert.Equal([keep], s.Usage.Keys);
        Assert.Equal(0, UsageTracker.Prune(s, [keep]));
    }
}
