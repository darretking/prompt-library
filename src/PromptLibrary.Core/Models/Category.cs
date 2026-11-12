namespace PromptLibrary.Core.Models;

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Optional single emoji shown before the name.</summary>
    public string? Emoji { get; set; }
}
