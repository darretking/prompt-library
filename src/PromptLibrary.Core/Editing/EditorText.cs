namespace PromptLibrary.Core.Editing;

/// <summary>The main window's questions about the prompt being edited.</summary>
public static class EditorText
{
    /// <summary>
    /// <c>Save changes to "title"?</c> naming the prompt as it is saved (not a title the user has just cleared), or
    /// <c>Save changes to this new prompt?</c> for a prompt that hasn't been saved yet.
    /// </summary>
    public static string SaveChangesQuestion(string? savedTitle)
        => string.IsNullOrWhiteSpace(savedTitle)
            ? "Save changes to this new prompt?"
            : $"Save changes to \"{savedTitle.Trim()}\"?";
}
