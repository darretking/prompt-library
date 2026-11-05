using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamCacheTests : IDisposable
{
    private const string Good = """
        {"kind":"prompt-library-team","schemaVersion":1,"name":"Sales","updatedAt":"2026-11-05T10:00:00+00:00",
         "categories":[],"prompts":[]}
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Cache_RoundTrip()
    {
        var path = Path.Combine(_dir, "sub", "cache.json");
        var source = new TeamSource(TeamSourceKind.Url, "https://example.com/t.json");
        var at = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        TeamCache.Save(path, source, Good, at);

        var loaded = TeamCache.Load(path)!.Value;
        Assert.Equal(source, loaded.Source);
        Assert.Equal(Good, loaded.Json);
        Assert.Equal("Sales", loaded.Team.Name);
        Assert.Equal(at, loaded.FetchedAt);

        TeamCache.Delete(path);
        Assert.Null(TeamCache.Load(path));
        TeamCache.Delete(path);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"kind":"File","location":"x","fetchedAt":"2026-11-05T10:00:00Z","json":"{}"}""")]
    public void Cache_Corrupt_ReturnsNull(string content)
    {
        var path = Path.Combine(_dir, "cache.json");
        File.WriteAllText(path, content);
        Assert.Null(TeamCache.Load(path));
        Assert.Null(TeamCache.Load(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void Apply_ReportsChangeOnlyWhenHashDiffers()
    {
        var path = Path.Combine(_dir, "cache.json");
        var s = new TeamSource(TeamSourceKind.File, "x");
        TeamReadResult Ok(string json) => new(TeamReadStatus.Ok, TeamFile.Parse(json), json, TeamFile.Hash(json), null);
        var now = DateTimeOffset.UtcNow;
        Assert.True(TeamRefresh.Apply(Ok(Good), path, s, now));
        Assert.False(TeamRefresh.Apply(Ok(Good.Replace("\n", "\r\n")), path, s, now));
        Assert.True(TeamRefresh.Apply(Ok(Good.Replace("Sales", "Ops")), path, s, now));
    }
}
