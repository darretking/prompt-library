using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

public sealed class UpdateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "update-check.json");

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var state = new UpdateState
        {
            LastCheckedAt = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(-6)),
            LastAttemptAt = new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(-6)),
            LatestRelease = UpdateScheduleTests.Release("1.0.2"),
            DeclinedVersion = "1.0.2",
            PromptedVersion = "1.0.2",
        };

        UpdateStore.Save(StatePath, state);
        var loaded = UpdateStore.Load(StatePath);

        Assert.Equal(state.LastCheckedAt, loaded.LastCheckedAt);
        Assert.Equal(state.LastAttemptAt, loaded.LastAttemptAt);
        Assert.Equal(state.LatestRelease, loaded.LatestRelease);
        Assert.Equal("1.0.2", loaded.DeclinedVersion);
        Assert.Equal("1.0.2", loaded.PromptedVersion);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        var loaded = UpdateStore.Load(StatePath);
        Assert.Null(loaded.LastCheckedAt);
        Assert.Null(loaded.LatestRelease);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    public void Load_UnreadableFile_ReturnsEmptyState(string contents)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, contents);

        var loaded = UpdateStore.Load(StatePath);

        Assert.Null(loaded.LastCheckedAt);
        Assert.Null(loaded.LatestRelease);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
