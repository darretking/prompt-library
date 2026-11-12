using System.IO;

namespace PromptLibrary.App.Tests.Gallery;

/// <summary>
/// A fact that runs only when the <c>PL_SCREENSHOTS</c> environment variable holds an absolute output folder; otherwise it
/// is skipped, so a normal <c>dotnet test</c> stays fast and writes nothing. When it runs, the app's data folder is
/// pointed at a throw-away sandbox (before <c>Paths</c> is first read) so the real %APPDATA% library is never touched.
/// </summary>
internal sealed class GalleryFactAttribute : FactAttribute
{
    public const string OutputVariable = "PL_SCREENSHOTS";

    public static string? OutputFolder { get; } =
        Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } dir && Path.IsPathFullyQualified(dir) ? dir : null;

    public GalleryFactAttribute()
    {
        if (OutputFolder is null)
        {
            Skip = $"Set {OutputVariable} to an absolute folder to render the screenshot gallery.";
            return;
        }
        Environment.SetEnvironmentVariable("PROMPTLIBRARY_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "pl-gallery-data-" + Guid.NewGuid().ToString("N")));
    }
}
