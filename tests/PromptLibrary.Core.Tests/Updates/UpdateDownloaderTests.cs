using System.Net;
using System.Security.Cryptography;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.Core.Tests.Updates;

public sealed class UpdateDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
    private static readonly string PayloadSha = Convert.ToHexStringLower(SHA256.HashData(Payload));
    private string ExpectedPath => Path.Combine(_dir, "PromptLibrary-Setup-1.0.2.msi");

    private static ReleaseInfo Release(long? size = null, string? sha = "payload", string? url = "https://example.invalid/setup.msi")
        => new("1.0.2", "v1.0.2", "https://github.com/darretking/prompt-library/releases/tag/v1.0.2",
            url is null ? null : "PromptLibrary-Setup.msi", url, size ?? Payload.Length, sha == "payload" ? PayloadSha : sha);

    private static FakeHandler Serving(byte[] bytes) => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    }));

    [Fact]
    public async Task Download_Good_ReturnsPathAndReportsProgressTo1()
    {
        var reports = new List<double>();
        var progress = new SyncProgress(reports.Add);

        var path = await new UpdateDownloader(new HttpClient(Serving(Payload)))
            .DownloadAsync(Release(), _dir, progress, CancellationToken.None);

        Assert.Equal(ExpectedPath, path);
        Assert.Equal(Payload, File.ReadAllBytes(path));
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1], 3);
        Assert.All(reports, r => Assert.InRange(r, 0, 1));
    }

    [Fact]
    public async Task Download_SizeMismatch_FailsAndDeletesFile()
    {
        var downloader = new UpdateDownloader(new HttpClient(Serving(Payload[..1000])));

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => downloader.DownloadAsync(Release(), _dir, null, CancellationToken.None));

        Assert.False(File.Exists(ExpectedPath));
    }

    [Fact]
    public async Task Download_DigestMismatch_FailsAndDeletesFile()
    {
        var tampered = (byte[])Payload.Clone();
        tampered[12345] ^= 0xFF;
        var downloader = new UpdateDownloader(new HttpClient(Serving(tampered)));

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => downloader.DownloadAsync(Release(), _dir, null, CancellationToken.None));

        Assert.False(File.Exists(ExpectedPath));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Download_NoDigestOrNoAsset_FailsWithoutRequest(bool noDigest, bool noAsset)
    {
        var handler = Serving(Payload);
        var release = Release(sha: noDigest ? null : "payload", url: noAsset ? null : "https://example.invalid/setup.msi");

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => new UpdateDownloader(new HttpClient(handler)).DownloadAsync(release, _dir, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Download_HttpError_Fails()
    {
        var downloader = new UpdateDownloader(new HttpClient(FakeHandler.Status(HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => downloader.DownloadAsync(Release(), _dir, null, CancellationToken.None));
    }

    [Fact]
    public async Task Download_NetworkError_Fails()
    {
        var downloader = new UpdateDownloader(new HttpClient(FakeHandler.Throws(new HttpRequestException("offline"))));

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => downloader.DownloadAsync(Release(), _dir, null, CancellationToken.None));
    }

    [Fact]
    public async Task Download_Cancel_ThrowsOperationCanceledAndDeletes()
    {
        using var cts = new CancellationTokenSource();
        var stream = new StallingStream(Payload[..100_000], onStall: cts.Cancel);
        var downloader = new UpdateDownloader(new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(Release(), _dir, null, cts.Token));

        Assert.False(File.Exists(ExpectedPath));
    }

    [Fact]
    public async Task Download_Stall_Fails()
    {
        var stream = new StallingStream(Payload[..100_000], onStall: null);
        var downloader = new UpdateDownloader(new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }))))
        {
            StallTimeout = TimeSpan.FromMilliseconds(100),
        };

        await Assert.ThrowsAsync<UpdateDownloadException>(
            () => downloader.DownloadAsync(Release(), _dir, null, CancellationToken.None));

        Assert.False(File.Exists(ExpectedPath));
    }

    [Fact]
    public async Task Download_ReplacesExistingFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ExpectedPath, "old partial download");

        var path = await new UpdateDownloader(new HttpClient(Serving(Payload)))
            .DownloadAsync(Release(), _dir, null, CancellationToken.None);

        Assert.Equal(Payload, File.ReadAllBytes(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Progress that reports synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Serves <paramref name="data"/>, then blocks until cancelled (calling <paramref name="onStall"/> first).</summary>
    private sealed class StallingStream(byte[] data, Action? onStall) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position < data.Length)
            {
                var n = Math.Min(buffer.Length, data.Length - _position);
                data.AsMemory(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }
            onStall?.Invoke();
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
