using System.Text.Json.Nodes;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Team;

/// <summary>A shared team library as read from <see cref="TeamFile.FileName"/>.</summary>
public sealed class TeamLibrary
{
    /// <summary>The team's display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>When the file was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The schema version the file declares.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The categories and prompts.</summary>
    public Library Content { get; set; } = new();

    /// <summary>True when the file was written by a newer app version: readable, but must not be saved over.</summary>
    public bool IsNewerSchema { get; set; }

    /// <summary>The file as parsed, kept so fields this version does not know survive a save.</summary>
    public JsonObject Raw { get; set; } = new();
}
