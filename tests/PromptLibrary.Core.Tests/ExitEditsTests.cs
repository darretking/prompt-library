using PromptLibrary.Core.Editing;

namespace PromptLibrary.Core.Tests;

public class ExitEditsTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 5, 9, 4, 7);

    private static UnsavedEdit Edit(string title = "Review", string text = "Check this.", Guid? categoryId = null,
        string description = "Code review", string? categoryName = "Coding")
        => new(title, description, categoryId ?? CategoryId, categoryName, text);

    [Fact]
    public void Choose_ValidEdit_Saves()
        => Assert.Equal(ExitEditAction.Save, ExitEdits.Choose(Edit()));

    [Theory]
    [InlineData("", "Text")]
    [InlineData("   ", "Text")]
    [InlineData("Title", "")]
    [InlineData("Title", " \r\n ")]
    public void Choose_MissingTitleOrText_Recovers(string title, string text)
        => Assert.Equal(ExitEditAction.Recover, ExitEdits.Choose(Edit(title, text)));

    [Fact]
    public void Choose_NoCategory_Recovers()
        => Assert.Equal(ExitEditAction.Recover, ExitEdits.Choose(Edit() with { CategoryId = null }));

    [Fact]
    public void RecoveryFileName_UsesLocalTimestamp()
        => Assert.Equal("recovered-2026-11-05_090407.txt", ExitEdits.RecoveryFileName(Now));

    [Fact]
    public void RecoveryText_HasEveryFieldAndTheTextExactly()
    {
        var text = ExitEdits.RecoveryText(Edit(text: "Line 1\nLine 2  "), Now);

        Assert.Equal(
            "Prompt Library recovered these unsaved changes on 2026-11-05 09:04:07.\r\n" +
            "\r\n" +
            "Title: Review\r\n" +
            "Description: Code review\r\n" +
            "Category: Coding\r\n" +
            "\r\n" +
            "Text:\r\n" +
            "Line 1\r\nLine 2  \r\n",
            text);
    }

    [Fact]
    public void RecoveryText_EmptyFieldsAndNoCategory_SayNone()
    {
        var text = ExitEdits.RecoveryText(Edit(title: "", text: "", description: "", categoryName: null), Now);

        Assert.Contains("Title: (none)\r\n", text);
        Assert.Contains("Description: (none)\r\n", text);
        Assert.Contains("Category: (none)\r\n", text);
        Assert.EndsWith("Text:\r\n\r\n", text);
    }

    [Fact]
    public void WriteRecoveryFile_WritesIntoTheFolderAndReturnsThePath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pl-recovered-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = ExitEdits.WriteRecoveryFile(folder, Edit(), Now);

            Assert.Equal(Path.Combine(folder, "recovered-2026-11-05_090407.txt"), path);
            Assert.Equal(ExitEdits.RecoveryText(Edit(), Now), File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WriteRecoveryFile_SameSecondTwice_KeepsBoth()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pl-recovered-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = ExitEdits.WriteRecoveryFile(folder, Edit(title: "First"), Now);
            var second = ExitEdits.WriteRecoveryFile(folder, Edit(title: "Second"), Now);

            Assert.NotEqual(first, second);
            Assert.Equal(Path.Combine(folder, "recovered-2026-11-05_090407-2.txt"), second);
            Assert.Contains("Title: First", File.ReadAllText(first));
            Assert.Contains("Title: Second", File.ReadAllText(second));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
