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
}
