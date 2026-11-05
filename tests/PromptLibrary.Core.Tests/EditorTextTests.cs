using PromptLibrary.Core.Editing;

namespace PromptLibrary.Core.Tests;

public class EditorTextTests
{
    [Fact]
    public void SaveChangesQuestion_NamesTheSavedTitle()
        => Assert.Equal("Save changes to \"Fix this error\"?", EditorText.SaveChangesQuestion("Fix this error"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveChangesQuestion_NoSavedTitle_SaysThisNewPrompt(string? savedTitle)
        => Assert.Equal("Save changes to this new prompt?", EditorText.SaveChangesQuestion(savedTitle));
}
