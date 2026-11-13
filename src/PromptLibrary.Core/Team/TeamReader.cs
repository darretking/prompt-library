using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PromptLibrary.Core.Team;

/// <summary>How reading a team source went.</summary>
public enum TeamReadStatus
{
    /// <summary>Read and usable (possibly from a newer app version, see <see cref="TeamLibrary.IsNewerSchema"/>).</summary>
    Ok,

    /// <summary>The folder or link could not be reached.</summary>
    Unreachable,

    /// <summary>Reached, but it is not a usable Prompt Library team file.</summary>
    NotATeamFile,

    /// <summary>Made by a newer app version in a way this version can't read at all.</summary>
    TooNew,
}

/// <summary>The outcome of <see cref="TeamReader.ReadAsync"/>. Team, Json and Hash are set only for <see cref="TeamReadStatus.Ok"/>.</summary>
public sealed record TeamReadResult(TeamReadStatus Status, TeamLibrary? Team, string? Json, string? Hash, string? Message);

/// <summary>Reads a team library from a file or an https link. Never throws for a bad source.</summary>
public sealed class TeamReader(HttpClient http, string appVersion)
{
    /// <summary>The largest file read from a link.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>How long a link read may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait before the one retry of a file read.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    internal const string WebPageMessage = "That link opens a web page, not the file. Use the shared folder instead, or a direct download link.";
    private const string NotTeamMessage = "That isn't a Prompt Library team file.";
    private const string NoFolderMessage = "Can't reach that folder.";
    private const string NoLinkMessage = "Can't reach that link.";

    /// <summary>Reads and checks the source.</summary>
    public async Task<TeamReadResult> ReadAsync(TeamSource source, CancellationToken ct)
    {
        try
        {
            return source.Kind == TeamSourceKind.File ? await ReadFileAsync(source.Location, ct) : await ReadUrlAsync(source.Location, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(TeamReadStatus.Unreachable, source.Kind == TeamSourceKind.File ? NoFolderMessage : NoLinkMessage);
        }
    }

    private static TeamReadResult Fail(TeamReadStatus status, string message) => new(status, null, null, null, message);

    private async Task<TeamReadResult> ReadFileAsync(string path, CancellationToken ct)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length > MaxBytes) return Fail(TeamReadStatus.NotATeamFile, NotTeamMessage);
            text = await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return Fail(TeamReadStatus.Unreachable, NoFolderMessage);
        }
        catch (IOException)
        {
            // A sync tool may be writing the file: try once more.
            await Task.Delay(RetryDelay, ct);
            try
            {
                if (new FileInfo(path).Length > MaxBytes) return Fail(TeamReadStatus.NotATeamFile, NotTeamMessage);
                text = await File.ReadAllTextAsync(path, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(TeamReadStatus.Unreachable, NoFolderMessage);
            }
        }
        return Parse(text, fromUrl: false);
    }

    private async Task<TeamReadResult> ReadUrlAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Fail(TeamReadStatus.Unreachable, NoLinkMessage);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PromptLibrary", appVersion));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.Scheme == Uri.UriSchemeHttp)
                return Fail(TeamReadStatus.Unreachable, NoLinkMessage);

            if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                return Fail(TeamReadStatus.NotATeamFile, WebPageMessage);
            if (response.Content.Headers.ContentLength > MaxBytes)
                return Fail(TeamReadStatus.NotATeamFile, NotTeamMessage);

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int n;
            while ((n = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, n);
                if (buffer.Length > MaxBytes) return Fail(TeamReadStatus.NotATeamFile, NotTeamMessage);
            }
            return Parse(new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length), fromUrl: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(TeamReadStatus.Unreachable, NoLinkMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Fail(TeamReadStatus.Unreachable, NoLinkMessage);
        }
    }

    private static TeamReadResult Parse(string text, bool fromUrl)
    {
        text = text.TrimStart('﻿');
        if (fromUrl && text.TrimStart().StartsWith('<'))
            return Fail(TeamReadStatus.NotATeamFile, WebPageMessage);
        try
        {
            var team = TeamFile.Parse(text);
            return new TeamReadResult(TeamReadStatus.Ok, team, text, TeamFile.Hash(text), null);
        }
        catch (Exception)
        {
            return DeclaresNewerSchema(text)
                ? Fail(TeamReadStatus.TooNew, "This team library was made by a newer version of Prompt Library. Update the app to use it.")
                : Fail(TeamReadStatus.NotATeamFile, NotTeamMessage);
        }
    }

    private static bool DeclaresNewerSchema(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject o
                   && o["kind"]?.GetValue<string>() == TeamFile.Kind
                   && o["schemaVersion"]?.GetValue<int>() > 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
