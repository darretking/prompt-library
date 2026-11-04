using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests.EdgeCases;

public class EditingEdgeTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static (Library Lib, LibraryEditor Editor) Create()
    {
        var lib = new Library();
        return (lib, new LibraryEditor(lib, () => Fixed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void RenameCategory_Blank_Throws(string name)
    {
        var (_, editor) = Create();
        var category = editor.AddCategory("Writing");

        Assert.Throws<ArgumentException>(() => editor.RenameCategory(category.Id, name));
        Assert.Equal("Writing", category.Name);
    }

    [Theory]
    [InlineData("", "text")]
    [InlineData("Title", "")]
    [InlineData("Title", "  \r\n ")]
    public void AddPrompt_Invalid_LeavesLibraryUnchanged(string title, string text)
    {
        var (lib, editor) = Create();
        var category = editor.AddCategory("Writing");
        editor.AddPrompt(category.Id, "Existing", "", "body");

        Assert.Throws<ArgumentException>(() => editor.AddPrompt(category.Id, title, "", text));
        Assert.Throws<ArgumentException>(() => editor.AddPrompt(Guid.NewGuid(), "Title", "", "text"));

        Assert.Equal("Existing", Assert.Single(lib.Prompts).Title);
        Assert.Single(lib.Categories);
    }

    [Fact]
    public void AddCategory_NameDifferingOnlyByInnerWhitespace_Allowed()
    {
        var (lib, editor) = Create();
        editor.AddCategory("Code Review");

        var second = editor.AddCategory("Code  Review");

        Assert.Equal("Code  Review", second.Name);
        Assert.Equal(2, lib.Categories.Count);
    }

    [Fact]
    public void AddCategory_300CharName_Allowed()
    {
        var (_, editor) = Create();
        var name = new string('n', 300);

        Assert.Equal(name, editor.AddCategory(name).Name);
    }

    [Fact]
    public void DeleteCategory_LastCategoryWithPrompts_NoTarget_Throws()
    {
        var (lib, editor) = Create();
        var only = editor.AddCategory("Only");
        editor.AddPrompt(only.Id, "Keep me", "", "text");

        Assert.Throws<InvalidOperationException>(() => editor.DeleteCategory(only.Id, movePromptsTo: null));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteCategory(only.Id, movePromptsTo: only.Id));

        Assert.Single(lib.Categories);
        Assert.Equal(only.Id, Assert.Single(lib.Prompts).CategoryId);
    }
}
