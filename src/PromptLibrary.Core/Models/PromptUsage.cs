namespace PromptLibrary.Core.Models;

/// <summary>How often a prompt has been used and when it last was.</summary>
public class PromptUsage
{
    public int Count { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
