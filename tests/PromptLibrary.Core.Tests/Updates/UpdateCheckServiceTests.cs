using System.Net;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

public sealed class UpdateCheckServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "update-check.json");

    private UpdateCheckService Service(FakeHandler handler, string running = "1.0.0")
    {
        AppVersion.TryParse(running, out var v);
        return new UpdateCheckService(new HttpClient(handler),
            new UpdateOptions("darretking", "prompt-library", v, StatePath), () => Now);
    }

    [Fact]
    public async Task CheckAsync_Newer_ReturnsUpdateAvailableAndSavesRelease()
    {
        var service = Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1")));

        var result = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        var release = Assert.IsType<ReleaseInfo>(result.Release);
        Assert.Equal("1.0.1", release.Version);
        Assert.Equal("v1.0.1", release.Tag);
        Assert.Equal("https://github.com/darretking/prompt-library/releases/tag/v1.0.1", release.PageUrl);
        Assert.Equal("PromptLibrary-Setup.msi", release.AssetName);
        Assert.Equal("https://github.com/darretking/prompt-library/releases/download/v1.0.1/PromptLibrary-Setup.msi",
            release.AssetUrl);
        Assert.Equal(69402624, release.AssetSize);
        Assert.Equal("8f2ab53888a9a46e21f3e12b9e518a169abef44c96882fe322c300f0734e1a69", release.AssetSha256);

        var saved = UpdateStore.Load(StatePath);
        Assert.Equal(release, saved.LatestRelease);
        Assert.Equal(Now, saved.LastCheckedAt);
        Assert.Equal(Now, saved.LastAttemptAt);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("0.9.0")]
    public async Task CheckAsync_SameOrOlder_ReturnsUpToDate(string tag)
    {
        var service = Service(FakeHandler.Json(FakeHandler.ReleaseJson(tag)));

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(UpdateStore.Load(StatePath).LatestRelease);
        Assert.Equal(Now, UpdateStore.Load(StatePath).LastCheckedAt);
    }

    [Fact]
    public async Task CheckAsync_SendsUserAgentAcceptAndApiVersion()
    {
        var handler = FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.0"));

        await Service(handler).CheckAsync(manual: true);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/darretking/prompt-library/releases/latest", request.RequestUri!.ToString());
        Assert.StartsWith("PromptLibrary/1.0.0", request.Headers.UserAgent.ToString());
        Assert.Contains("github.com/darretking/prompt-library", request.Headers.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", Assert.Single(request.Headers.Accept).MediaType);
        Assert.Equal("2022-11-28", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, UpdateFailure.RateLimited)]
    [InlineData(HttpStatusCode.TooManyRequests, UpdateFailure.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, UpdateFailure.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, UpdateFailure.BadResponse)]
    public async Task CheckAsync_ErrorStatus_ReturnsFailure(HttpStatusCode status, UpdateFailure failure)
    {
        var result = await Service(FakeHandler.Status(status)).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public async Task CheckAsync_Offline_ReturnsFailedAndStampsAttemptOnly()
    {
        var service = Service(FakeHandler.Throws(new HttpRequestException("No such host is known.")));

        var result = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.Network, result.Failure);
        var saved = UpdateStore.Load(StatePath);
        Assert.Equal(Now, saved.LastAttemptAt);
        Assert.Null(saved.LastCheckedAt);
    }

    [Fact]
    public async Task CheckAsync_Timeout_ReturnsTimeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            throw new InvalidOperationException("not reached");
        });
        var service = Service(handler);
        service.CheckTimeout = TimeSpan.FromMilliseconds(50);

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.Timeout, result.Failure);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"html_url\": \"https://github.com/x\"}")]
    public async Task CheckAsync_BadJson_ReturnsBadResponse(string json)
    {
        var result = await Service(FakeHandler.Json(json)).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.BadResponse, result.Failure);
    }

    [Fact]
    public async Task CheckAsync_UnparseableTag_ReturnsBadResponse()
    {
        var result = await Service(FakeHandler.Json(FakeHandler.ReleaseJson("latest"))).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateFailure.BadResponse, result.Failure);
    }

    [Fact]
    public async Task CheckAsync_NoMsiAsset_StillReportsUpdateWithNullAsset()
    {
        var result = await Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1", assetName: null)))
            .CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Release!.AssetUrl);
        Assert.Null(result.Release.AssetName);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToFirstMsiAsset()
    {
        var result = await Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1", assetName: "PromptLibrary-1.0.1.msi")))
            .CheckAsync(manual: true);

        Assert.Equal("PromptLibrary-1.0.1.msi", result.Release!.AssetName);
    }

    [Fact]
    public async Task CheckAsync_MissingDigest_LeavesSha256Null()
    {
        var result = await Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1", digest: null)))
            .CheckAsync(manual: true);

        Assert.Null(result.Release!.AssetSha256);
    }

    [Fact]
    public async Task CheckAsync_AutomaticWithinWindow_ReturnsNotDueWithoutRequest()
    {
        Directory.CreateDirectory(_dir);
        UpdateStore.Save(StatePath, new UpdateState { LastCheckedAt = Now.AddHours(-2), LastAttemptAt = Now.AddHours(-2) });
        var handler = FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1"));

        var result = await Service(handler).CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.NotDue, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CheckAsync_Manual_IgnoresWindow()
    {
        Directory.CreateDirectory(_dir);
        UpdateStore.Save(StatePath, new UpdateState { LastCheckedAt = Now.AddMinutes(-1), LastAttemptAt = Now.AddMinutes(-1) });
        var handler = FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1"));

        var result = await Service(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CheckAsync_Concurrent_SharesOneRequest()
    {
        var gate = new TaskCompletionSource();
        var handler = new FakeHandler(async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FakeHandler.ReleaseJson("v1.0.1")),
            };
        });
        var service = Service(handler);

        var first = service.CheckAsync(manual: false);
        var second = service.CheckAsync(manual: true);
        gate.SetResult();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await first).Status);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await second).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MarkDeclined_And_MarkPrompted_Persist()
    {
        var service = Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1")));
        await service.CheckAsync(manual: true);

        service.MarkPrompted("1.0.1");
        service.MarkDeclined("1.0.1");

        var reopened = Service(FakeHandler.Status(HttpStatusCode.NotFound));
        Assert.Equal("1.0.1", reopened.State.PromptedVersion);
        Assert.Equal("1.0.1", reopened.State.DeclinedVersion);
        Assert.Equal("1.0.1", reopened.State.LatestRelease!.Version);
    }

    [Fact]
    public void Constructor_RunningCaughtUp_ClearsSavedRelease()
    {
        Directory.CreateDirectory(_dir);
        UpdateStore.Save(StatePath, new UpdateState
        {
            LatestRelease = UpdateScheduleTests.Release("1.0.1"), DeclinedVersion = "1.0.1",
        });

        var service = Service(FakeHandler.Status(HttpStatusCode.NotFound), running: "1.0.1");

        Assert.Null(service.State.LatestRelease);
        Assert.Null(UpdateStore.Load(StatePath).DeclinedVersion);
    }

    [Fact]
    public async Task CheckAsync_UnwritableStateFolder_StillReturnsResult()
    {
        // A file where the state folder should be: every save fails, the check still answers.
        File.WriteAllText(_dir, "blocker");
        try
        {
            var result = await Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1"))).CheckAsync(manual: true);
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
