namespace PromptLibrary.Core.Models;

public class Library
{
    public int SchemaVersion { get; set; } = 1;
    public List<Category> Categories { get; set; } = new();
    public List<Prompt> Prompts { get; set; } = new();
}
