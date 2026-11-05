using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PromptLibrary.Core.Updates;

/// <summary>
/// Checks <c>GET /repos/{owner}/{repo}/releases/latest</c> (which skips drafts and pre-releases), at most once a day
/// automatically, and keeps <see cref="UpdateState"/> in <see cref="UpdateOptions.StatePath"/>. Saving the state is
/// best effort: a check still answers when the file can't be written.
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
    private readonly HttpClient _http;
    private readonly UpdateOptions _options;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();
    private Task<UpdateCheckResult>? _inFlight;

    public UpdateCheckService(HttpClient http, UpdateOptions options, Func<DateTimeOffset> now)
    {
        _http = http;
        _options = options;
        _now = now;
        State = UpdateStore.Load(options.StatePath);
        var changed = false;
        if (State.LatestRelease is { } saved && !IsTrustworthy(saved))
        {
            // The file is only a cache: a release whose URLs aren't this app's GitHub releases is dropped.
            State.LatestRelease = null;
            changed = true;
        }
        if (UpdateSchedule.Reconcile(State, options.Running) || changed) Save();
    }

    private bool IsTrustworthy(ReleaseInfo release)
        => ReleaseUrls.IsReleasePage(release.PageUrl, _options.Owner, _options.Repo)
           && (release.AssetUrl is null || ReleaseUrls.IsReleaseAsset(release.AssetUrl, _options.Owner, _options.Repo))
           && AppVersion.TryParse(release.Version, out _);

    /// <summary>
    /// Asks GitHub again and returns its latest release only if it is still <paramref name="version"/>. Update now
    /// downloads from this fresh answer, never from the saved state, so the file and its checksum always come from
    /// the same live response. Null when offline, changed or anything else went wrong. Doesn't change the state.
    /// </summary>
    public async Task<ReleaseInfo?> ConfirmLatestAsync(string version, CancellationToken ct)
    {
        var result = await FetchAsync(ct);
        return result is { Status: UpdateCheckStatus.UpdateAvailable, Release: { } release }
               && AppVersion.TryParse(version, out var offered) && AppVersion.TryParse(release.Version, out var latest)
               && offered.CompareTo(latest) == 0
            ? release
            : null;
    }

    public UpdateState State { get; }

    public AppVersion Running => _options.Running;

    /// <summary>How long one check may take before it counts as <see cref="UpdateFailure.Timeout"/>.</summary>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running) return running;
            if (!manual && !UpdateSchedule.IsDue(State, _now()))
                return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.NotDue, null));
            return _inFlight = RunCheckAsync(ct);
        }
    }

    public void MarkPrompted(string version)
    {
        lock (_gate) State.PromptedVersion = version;
        Save();
    }

    public void MarkDeclined(string version)
    {
        lock (_gate) State.DeclinedVersion = version;
        Save();
    }

    private async Task<UpdateCheckResult> RunCheckAsync(CancellationToken ct)
    {
        var result = await FetchAsync(ct);

        lock (_gate)
        {
            var now = _now();
            State.LastAttemptAt = now;
            if (result.Status != UpdateCheckStatus.Failed)
            {
                State.LastCheckedAt = now;
                State.LatestRelease = result.Status == UpdateCheckStatus.UpdateAvailable ? result.Release : null;
                UpdateSchedule.Reconcile(State, _options.Running);
            }
        }
        Save();
        return result;
    }

    private async Task<UpdateCheckResult> FetchAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PromptLibrary", _options.Running.ToString()));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"(+https://github.com/{_options.Owner}/{_options.Repo})"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed(UpdateFailure.RateLimited);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Failed(UpdateFailure.NotFound);
            if (!response.IsSuccessStatusCode)
                return Failed(UpdateFailure.BadResponse);

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (ParseRelease(json, _options) is not { } release
                || !AppVersion.TryParse(release.Version, out var latest))
                return Failed(UpdateFailure.BadResponse);

            return latest > _options.Running
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, release);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(UpdateFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return Failed(UpdateFailure.Network);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return Failed(UpdateFailure.BadResponse);
        }
    }

    /// <summary>
    /// The release in a <c>releases/latest</c> body, or null when its tag or page is missing or the tag isn't a version.
    /// A page URL that isn't this repository's releases is replaced by <see cref="ReleaseUrls.LatestPage"/>; an
    /// installer URL that isn't one of its release downloads is dropped (Update now then opens the page).
    /// </summary>
    internal static ReleaseInfo? ParseRelease(string json, UpdateOptions options)
    {
        var release = ParseRawRelease(json, options.AssetName);
        if (release is null) return null;
        if (!ReleaseUrls.IsReleasePage(release.PageUrl, options.Owner, options.Repo))
            release = release with { PageUrl = ReleaseUrls.LatestPage(options.Owner, options.Repo) };
        if (release.AssetUrl is not null && !ReleaseUrls.IsReleaseAsset(release.AssetUrl, options.Owner, options.Repo))
            release = release with { AssetName = null, AssetUrl = null, AssetSize = null, AssetSha256 = null };
        return release;
    }

    private static ReleaseInfo? ParseRawRelease(string json, string preferredAsset)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || String(root, "tag_name") is not { } tag
            || String(root, "html_url") is not { } page
            || !AppVersion.TryParse(tag, out var version))
            return null;

        JsonElement? asset = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var msis = assets.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.Object && String(a, "browser_download_url") is not null
                            && String(a, "name") is { } n && n.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                .ToList();
            asset = msis.FirstOrDefault(a => string.Equals(String(a, "name"), preferredAsset, StringComparison.OrdinalIgnoreCase)) is
                { ValueKind: JsonValueKind.Object } preferred
                ? preferred
                : msis.Count > 0 ? msis[0] : null;
        }

        string? sha256 = null;
        long? size = null;
        if (asset is { } a2)
        {
            if (String(a2, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                && digest.Length == 7 + 64 && digest[7..].All(char.IsAsciiHexDigit))
                sha256 = digest[7..].ToLowerInvariant();
            if (a2.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n))
                size = n;
        }

        return new ReleaseInfo(version.ToString(), tag, page,
            asset is { } a3 ? String(a3, "name") : null,
            asset is { } a4 ? String(a4, "browser_download_url") : null,
            size, sha256);
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static UpdateCheckResult Failed(UpdateFailure failure) => new(UpdateCheckStatus.Failed, null, failure);

    private void Save()
    {
        try
        {
            lock (_gate) UpdateStore.Save(_options.StatePath, State);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the next start just checks again.
        }
    }
}
