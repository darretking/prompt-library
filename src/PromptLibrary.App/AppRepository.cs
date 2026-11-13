using System.Reflection;
using PromptLibrary.Core.Updates;

namespace PromptLibrary.App;

/// <summary>The app's GitHub repository, from the assembly metadata the project file sets (UpdateRepositoryOwner/Name).</summary>
public static class AppRepository
{
    public static string Owner { get; } = Metadata("UpdateRepositoryOwner") ?? "darretking";
    public static string Name { get; } = Metadata("UpdateRepositoryName") ?? "prompt-library";

    /// <summary>The changelog page: what's new in each version.</summary>
    public static string ChangelogUrl => ReleaseUrls.ChangelogPage(Owner, Name);

    private static string? Metadata(string key) => typeof(AppRepository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value : null;
}
