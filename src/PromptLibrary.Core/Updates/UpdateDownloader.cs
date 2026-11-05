using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PromptLibrary.Core.Updates;

/// <summary>The installer couldn't be downloaded or didn't match the release (the message says which).</summary>
public sealed class UpdateDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads a release's installer and checks it against the size and SHA-256 GitHub published. Only a file that
/// matches both is returned; anything else is deleted and reported as <see cref="UpdateDownloadException"/>.
/// The user's cancel comes back as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class UpdateDownloader(HttpClient http)
{
    private const int BufferSize = 81920;

    /// <summary>The download fails when no data arrives for this long.</summary>
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Downloads to <c>{folder}\PromptLibrary-Setup-{version}.msi</c> (replacing any old copy) and returns that path.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.AssetUrl is null || release.AssetSize is not > 0)
            throw new UpdateDownloadException("This release has no installer to download.");
        if (release.AssetSha256 is null)
            throw new UpdateDownloadException("This release has no checksum, so its installer can't be verified.");

        if (!Uri.TryCreate(release.AssetUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new UpdateDownloadException("The installer's address isn't a secure web address.");

        // The file name comes from the parsed version (letters, digits, dots and dashes only) and must stay in the folder.
        if (!AppVersion.TryParse(release.Version, out var version))
            throw new UpdateDownloadException("The release's version isn't valid.");
        var fullFolder = Path.GetFullPath(folder);
        var path = Path.GetFullPath(Path.Combine(fullFolder, $"PromptLibrary-Setup-{version}.msi"));
        if (!string.Equals(Path.GetDirectoryName(path), fullFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new UpdateDownloadException("The release's version isn't valid.");

        var expectedSize = release.AssetSize.Value;
        var completed = false;
        try
        {
            Directory.CreateDirectory(folder);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
            watchdog.CancelAfter(StallTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PromptLibrary", "updater"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, watchdog.Token);
            if (!response.IsSuccessStatusCode)
                throw new UpdateDownloadException($"The download failed ({(int)response.StatusCode}).");

            await using (var source = await response.Content.ReadAsStreamAsync(watchdog.Token))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
                             FileOptions.Asynchronous))
            {
                var buffer = new byte[BufferSize];
                long total = 0;
                progress?.Report(0);
                while (true)
                {
                    watchdog.CancelAfter(StallTimeout); // reset: the limit is on silence, not on the whole download
                    var read = await source.ReadAsync(buffer, watchdog.Token);
                    if (read == 0) break;
                    total += read;
                    if (total > expectedSize)
                        throw new UpdateDownloadException("The download was bigger than the release says.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), watchdog.Token);
                    progress?.Report((double)total / expectedSize);
                }

                if (total != expectedSize)
                    throw new UpdateDownloadException("The download was incomplete.");
            }

            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), release.AssetSha256, StringComparison.Ordinal))
                throw new UpdateDownloadException("The download didn't match the release's checksum.");

            completed = true;
            return path;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateDownloadException("The download stopped responding.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateDownloadException("The download failed: " + ex.Message, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateDownloadException("The installer couldn't be saved: " + ex.Message, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UpdateDownloadException)
        {
            // Anything else (an odd redirect, a handler error) is a failed download, never a crash.
            throw new UpdateDownloadException("The download failed: " + ex.Message, ex);
        }
        finally
        {
            if (!completed) TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file left behind is overwritten by the next download.
        }
    }
}
