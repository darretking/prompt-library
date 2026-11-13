using System.Text.Json.Serialization;

namespace PromptLibrary.Core.Models;

public class Prompt
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string Text { get; set; } = "";

    /// <summary>Lower-case labels, see <see cref="TagRules"/>.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Optional default send target id (see <c>SendTargets</c>).</summary>
    public string? SendTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // The three below are left out of the file while empty, so personal library.json files look the same as before.

    /// <summary>Team prompt only: the team marks it as one to start with.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Featured { get; set; }

    /// <summary>Personal copy only: the team prompt it was copied from.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceTeamPromptId { get; set; }

    /// <summary>Personal copy only: the fingerprint of the team prompt at copy time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFingerprint { get; set; }
}
