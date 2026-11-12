using System.Net;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

/// <summary>Final-review fixes: URLs, saved state and file names from GitHub are never trusted blindly.</summary>
public sealed class ReleaseSafetyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "update-check.json");

    private UpdateCheckService Service(FakeHandler handler)
    {
        AppVersion.TryParse("1.0.0", out var v);
        return new UpdateCheckService(new HttpClient(handler),
            new UpdateOptions("darretking", "prompt-library", v, StatePath), () => Now);
    }

    [Theory]
    [InlineData("https://github.com/darretking/prompt-library/releases/tag/v1.0.1", true)]
    [InlineData("https://github.com/darretking/prompt-library/releases/latest", true)]
    [InlineData("https://GitHub.com/DarretKing/Prompt-Library/releases/tag/v1.0.1", true)]
    [InlineData("http://github.com/darretking/prompt-library/releases/tag/v1.0.1", false)]
    [InlineData("https://evil.example/darretking/prompt-library/releases/tag/v1.0.1", false)]
    [InlineData("https://github.com/someone-else/prompt-library/releases/tag/v1.0.1", false)]
    [InlineData("https://github.com.evil.example/darretking/prompt-library/releases/x", false)]
    [InlineData("https://github.com/darretking/prompt-library/issues/1", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData(@"\\attacker\share\x.exe", false)]
    [InlineData("ms-msdt:/id PCWDiagnostic", false)]
    [InlineData("search-ms:query=x", false)]
    [InlineData("/darretking/prompt-library/releases/tag/v1.0.1", false)]
    [InlineData("", false)]
    public void IsReleasePage(string url, bool expected)
        => Assert.Equal(expected, ReleaseUrls.IsReleasePage(url, "darretking", "prompt-library"));

    [Theory]
    [InlineData("https://github.com/darretking/prompt-library/releases/download/v1.0.1/PromptLibrary-Setup.msi", true)]
    [InlineData("http://github.com/darretking/prompt-library/releases/download/v1.0.1/PromptLibrary-Setup.msi", false)]
    [InlineData("https://10.0.0.5/darretking/prompt-library/releases/download/v1.0.1/x.msi", false)]
    [InlineData("https://github.com/darretking/prompt-library/releases/tag/v1.0.1", false)]
    [InlineData("file:///C:/x.msi", false)]
    [InlineData("setup.msi", false)]
    public void IsReleaseAsset(string url, bool expected)
        => Assert.Equal(expected, ReleaseUrls.IsReleaseAsset(url, "darretking", "prompt-library"));

    [Fact]
    public void LatestPage_IsTheRepositoryReleasesPage()
        => Assert.Equal("https://github.com/darretking/prompt-library/releases/latest",
            ReleaseUrls.LatestPage("darretking", "prompt-library"));

    [Fact]
    public void ChangelogPage_IsTheChangelogOnMain()
        => Assert.Equal("https://github.com/darretking/prompt-library/blob/main/CHANGELOG.md",
            ReleaseUrls.ChangelogPage("darretking", "prompt-library"));

    [Fact]
    public async Task CheckAsync_UnsafePageUrl_UsesTheRepositoryReleasesPage()
    {
        var json = FakeHandler.ReleaseJson("v1.0.1").Replace(
            "https://github.com/darretking/prompt-library/releases/tag/v1.0.1", "file:///C:/Windows/System32/calc.exe");

        var result = await Service(FakeHandler.Json(json)).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://github.com/darretking/prompt-library/releases/latest", result.Release!.PageUrl);
    }

    [Fact]
    public async Task CheckAsync_UnsafeAssetUrl_DropsTheAsset()
    {
        var json = FakeHandler.ReleaseJson("v1.0.1").Replace(
            "https://github.com/darretking/prompt-library/releases/download/v1.0.1/PromptLibrary-Setup.msi",
            "http://10.0.0.5/evil.msi");

        var result = await Service(FakeHandler.Json(json)).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Release!.AssetUrl);
        Assert.Null(result.Release.AssetSha256);
    }

    [Fact]
    public void Constructor_TamperedState_DropsTheSavedRelease()
    {
        Directory.CreateDirectory(_dir);
        UpdateStore.Save(StatePath, new UpdateState
        {
            LatestRelease = new ReleaseInfo("9.0.0", "v9.0.0", "file:///C:/evil.exe", "x.msi",
                "https://evil.example/x.msi", 10, new string('a', 64)),
        });

        var service = Service(FakeHandler.Status(HttpStatusCode.NotFound));

        Assert.Null(service.State.LatestRelease);
    }

    [Fact]
    public async Task ConfirmLatestAsync_SameVersion_ReturnsTheFreshRelease()
    {
        var service = Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1")));

        var fresh = await service.ConfirmLatestAsync("1.0.1", CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.Equal("8f2ab53888a9a46e21f3e12b9e518a169abef44c96882fe322c300f0734e1a69", fresh.AssetSha256);
    }

    [Theory]
    [InlineData("1.0.2")] // GitHub's latest is no longer the version the dialog offered
    [InlineData("1.0.0")]
    public async Task ConfirmLatestAsync_DifferentVersion_ReturnsNull(string offered)
    {
        var service = Service(FakeHandler.Json(FakeHandler.ReleaseJson("v1.0.1")));

        Assert.Null(await service.ConfirmLatestAsync(offered, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmLatestAsync_Offline_ReturnsNull()
    {
        var service = Service(FakeHandler.Throws(new HttpRequestException("offline")));

        Assert.Null(await service.ConfirmLatestAsync("1.0.1", CancellationToken.None));
    }

    [Theory]
    [InlineData(@"v9.0.0-a\..\..\Startup\x")]
    [InlineData("v1.0.2-rc/1")]
    [InlineData("v1.0.2-rc 1")]
    [InlineData("v1.0.2-rc:1")]
    public void TryParse_PreReleaseWithUnsafeCharacters_IsRejected(string tag)
        => Assert.False(AppVersion.TryParse(tag, out _));

    [Fact]
    public async Task Download_VersionWithPathCharacters_NeverLeavesTheFolder()
    {
        var release = new ReleaseInfo(@"1.0.2-a\..\..\x", "v1", "https://github.com/x", "x.msi",
            "https://github.com/darretking/prompt-library/releases/download/v1/x.msi", 10, new string('a', 64));
        var handler = FakeHandler.Json("0123456789");

        await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            new UpdateDownloader(new HttpClient(handler)).DownloadAsync(release, _dir, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("setup.msi")]
    [InlineData("file:///C:/x.msi")]
    [InlineData("http://github.com/x.msi")]
    public async Task Download_UnusableUrl_FailsWithoutCrashing(string url)
    {
        var release = new ReleaseInfo("1.0.2", "v1.0.2", "https://github.com/x", "x.msi", url, 10, new string('a', 64));

        await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            new UpdateDownloader(new HttpClient(FakeHandler.Json("0123456789")))
                .DownloadAsync(release, _dir, null, CancellationToken.None));
    }

    [Fact]
    public async Task Download_UnexpectedHandlerException_BecomesDownloadException()
    {
        var release = new ReleaseInfo("1.0.2", "v1.0.2", "https://github.com/x", "x.msi",
            "https://github.com/darretking/prompt-library/releases/download/v1.0.2/x.msi", 10, new string('a', 64));

        await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            new UpdateDownloader(new HttpClient(FakeHandler.Throws(new NotSupportedException("odd"))))
                .DownloadAsync(release, _dir, null, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
