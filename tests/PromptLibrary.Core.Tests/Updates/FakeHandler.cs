using System.Net;
using System.Text;

namespace PromptLibrary.Core.Tests.Updates;

/// <summary>An HTTP handler that records requests and answers with whatever the test sets up.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static FakeHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

    public static FakeHandler Status(HttpStatusCode status) => Json("{}", status);

    public static FakeHandler Throws(Exception ex) => new((_, _) => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return respond(request, ct);
    }

    /// <summary>A real-shaped <c>releases/latest</c> body.</summary>
    public static string ReleaseJson(string tag, string? assetName = "PromptLibrary-Setup.msi", string? digest = "default",
        long size = 69402624)
    {
        var digestJson = digest switch
        {
            null => "null",
            "default" => "\"sha256:8F2AB53888A9A46E21F3E12B9E518A169ABEF44C96882FE322C300F0734E1A69\"",
            _ => $"\"{digest}\"",
        };
        var assets = assetName is null
            ? "[]"
            : $$"""
              [{
                "name": "{{assetName}}",
                "size": {{size}},
                "digest": {{digestJson}},
                "content_type": "application/x-msi",
                "browser_download_url": "https://github.com/darretking/prompt-library/releases/download/{{tag}}/{{assetName}}"
              }]
              """;
        return $$"""
            {
              "url": "https://api.github.com/repos/darretking/prompt-library/releases/1",
              "html_url": "https://github.com/darretking/prompt-library/releases/tag/{{tag}}",
              "tag_name": "{{tag}}",
              "name": "Prompt Library",
              "draft": false,
              "prerelease": false,
              "assets": {{assets}},
              "body": "notes"
            }
            """;
    }
}
