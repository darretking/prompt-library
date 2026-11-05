using System.Net;
using System.Text;
using PromptLibrary.Core.Team;
using PromptLibrary.Core.Tests.Updates;

namespace PromptLibrary.Core.Tests.Team;

public class TeamReaderTests : IDisposable
{
    private const string Good = """
        {"kind":"prompt-library-team","schemaVersion":1,"name":"Sales","updatedAt":"2026-11-05T10:00:00+00:00",
         "categories":[],"prompts":[]}
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static TeamReader Reader(FakeHandler? h = null) => new(new HttpClient(h ?? FakeHandler.Json(Good))) { RetryDelay = TimeSpan.Zero };

    private static TeamSource Url(string u = "https://example.com/t.json") => new(TeamSourceKind.Url, u);

    private static FakeHandler Body(string body, string mediaType = "application/json")
        => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        }));

    [Fact]
    public async Task Read_File_Ok()
    {
        var path = Path.Combine(_dir, "team-prompts.json");
        File.WriteAllText(path, Good);
        var r = await Reader().ReadAsync(new TeamSource(TeamSourceKind.File, path), default);
        Assert.Equal(TeamReadStatus.Ok, r.Status);
        Assert.Equal("Sales", r.Team!.Name);
        Assert.Equal(Good, r.Json);
        Assert.Equal(TeamFile.Hash(Good), r.Hash);
    }

    [Fact]
    public async Task Read_MissingFolder_Unreachable()
    {
        var r = await Reader().ReadAsync(new TeamSource(TeamSourceKind.File, Path.Combine(_dir, "nope", "team-prompts.json")), default);
        Assert.Equal(TeamReadStatus.Unreachable, r.Status);
        Assert.Equal("Can't reach that folder.", r.Message);
    }

    [Fact]
    public async Task Read_TruncatedJson_KeepsCacheAndReportsUnreadable()
    {
        var path = Path.Combine(_dir, "team-prompts.json");
        var cache = Path.Combine(_dir, "cache.json");
        var source = new TeamSource(TeamSourceKind.File, path);
        File.WriteAllText(path, Good);
        var ok = await Reader().ReadAsync(source, default);
        Assert.True(TeamRefresh.Apply(ok, cache, source, DateTimeOffset.UtcNow));
        var before = File.ReadAllText(cache);

        File.WriteAllText(path, Good[..40]);
        var bad = await Reader().ReadAsync(source, default);
        Assert.Equal(TeamReadStatus.NotATeamFile, bad.Status);
        Assert.Equal("That isn't a Prompt Library team file.", bad.Message);
        Assert.False(TeamRefresh.Apply(bad, cache, source, DateTimeOffset.UtcNow));
        Assert.Equal(before, File.ReadAllText(cache));
    }

    [Fact]
    public async Task Read_DuplicateKeys_NotATeamFile()
    {
        var path = Path.Combine(_dir, "t.json");
        File.WriteAllText(path, """{"kind":"prompt-library-team","kind":"x","schemaVersion":1,"name":"a","categories":[],"prompts":[]}""");
        var r = await Reader().ReadAsync(new TeamSource(TeamSourceKind.File, path), default);
        Assert.Equal(TeamReadStatus.NotATeamFile, r.Status);
    }

    [Fact]
    public async Task Read_NewerButReadable_IsOkWithFlag()
    {
        var path = Path.Combine(_dir, "t.json");
        File.WriteAllText(path, Good.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"));
        var r = await Reader().ReadAsync(new TeamSource(TeamSourceKind.File, path), default);
        Assert.Equal(TeamReadStatus.Ok, r.Status);
        Assert.True(r.Team!.IsNewerSchema);
    }

    [Fact]
    public async Task Read_NewerUnreadable_TooNew()
    {
        var path = Path.Combine(_dir, "t.json");
        File.WriteAllText(path, """{"kind":"prompt-library-team","schemaVersion":3,"name":"a","categories":"new shape","prompts":[]}""");
        var r = await Reader().ReadAsync(new TeamSource(TeamSourceKind.File, path), default);
        Assert.Equal(TeamReadStatus.TooNew, r.Status);
    }

    [Fact]
    public async Task Read_Url_Ok_SendsUserAgent()
    {
        var h = FakeHandler.Json(Good);
        var r = await Reader(h).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.Ok, r.Status);
        Assert.Contains("PromptLibrary", h.Requests.Single().Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Read_Url_Over2MB_NotATeamFile()
    {
        var r = await Reader(Body(new string(' ', 2 * 1024 * 1024 + 10) + Good)).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.NotATeamFile, r.Status);
    }

    [Fact]
    public async Task Read_Url_Timeout_Unreachable()
    {
        var h = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        var reader = new TeamReader(new HttpClient(h)) { Timeout = TimeSpan.FromMilliseconds(50) };
        var r = await reader.ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.Unreachable, r.Status);
    }

    [Fact]
    public async Task Read_Url_404_Unreachable()
    {
        var r = await Reader(FakeHandler.Status(HttpStatusCode.NotFound)).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.Unreachable, r.Status);
    }

    [Fact]
    public async Task Read_Url_NetworkError_Unreachable()
    {
        var r = await Reader(FakeHandler.Throws(new HttpRequestException("down"))).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.Unreachable, r.Status);
    }

    [Theory]
    [InlineData("<html><body>Sign in</body></html>", "text/html")]
    [InlineData("  \n<!DOCTYPE html><html></html>", "application/octet-stream")]
    public async Task Read_Url_WebPage_SaysSo(string body, string mediaType)
    {
        var r = await Reader(Body(body, mediaType)).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.NotATeamFile, r.Status);
        Assert.Equal("That link opens a web page, not the file. Use the shared folder instead, or a direct download link.", r.Message);
    }

    [Fact]
    public async Task Read_Url_RedirectedToHttp_Unreachable()
    {
        var h = new FakeHandler((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Good),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/t.json"),
        }));
        var r = await Reader(h).ReadAsync(Url(), default);
        Assert.Equal(TeamReadStatus.Unreachable, r.Status);
    }
}
