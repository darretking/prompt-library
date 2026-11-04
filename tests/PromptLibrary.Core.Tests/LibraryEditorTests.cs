using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests;

public class LibraryEditorTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static (Library Lib, LibraryEditor Editor) Create(DateTimeOffset? now = null)
    {
        var lib = new Library();
        return (lib, new LibraryEditor(lib, () => now ?? Fixed));
    }

    [Fact]
    public void AddCategory_DuplicateNameDifferentCase_Throws()
    {
        var (_, editor) = Create();
        editor.AddCategory("Writing");
        Assert.Throws<ArgumentException>(() => editor.AddCategory("  writING "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddCategory_Blank_Throws(string name)
    {
        var (_, editor) = Create();
        Assert.Throws<ArgumentException>(() => editor.AddCategory(name));
    }

    [Fact]
    public void AddCategory_StoresTrimmedName()
    {
        var (lib, editor) = Create();
        var cat = editor.AddCategory("  Coding ");
        Assert.Equal("Coding", cat.Name);
        Assert.Contains(cat, lib.Categories);
    }

    [Fact]
    public void RenameCategory_ToOwnNameDifferentCase_Allowed()
    {
        var (lib, editor) = Create();
        var cat = editor.AddCategory("writing");
        editor.RenameCategory(cat.Id, "Writing");
        Assert.Equal("Writing", lib.Categories.Single().Name);
    }

    [Fact]
    public void RenameCategory_ToOtherCategorysName_Throws()
    {
        var (_, editor) = Create();
        editor.AddCategory("A");
        var b = editor.AddCategory("B");
        Assert.Throws<ArgumentException>(() => editor.RenameCategory(b.Id, "a"));
    }

    [Fact]
    public void RenameCategory_UnknownId_Throws()
    {
        var (_, editor) = Create();
        Assert.Throws<ArgumentException>(() => editor.RenameCategory(Guid.NewGuid(), "X"));
    }

    [Fact]
    public void DeleteCategory_Empty_Removes()
    {
        var (lib, editor) = Create();
        var cat = editor.AddCategory("A");
        editor.DeleteCategory(cat.Id, null);
        Assert.Empty(lib.Categories);
    }

    [Fact]
    public void DeleteCategory_WithPrompts_NoTarget_Throws_AndNothingChanges()
    {
        var (lib, editor) = Create();
        var cat = editor.AddCategory("A");
        editor.AddPrompt(cat.Id, "t", "", "text");

        Assert.Throws<InvalidOperationException>(() => editor.DeleteCategory(cat.Id, null));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteCategory(cat.Id, cat.Id));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteCategory(cat.Id, Guid.NewGuid()));

        Assert.Single(lib.Categories);
        Assert.Equal(cat.Id, lib.Prompts.Single().CategoryId);
    }

    [Fact]
    public void DeleteCategory_WithPrompts_MovesThenRemoves()
    {
        var (lib, editor) = Create();
        var a = editor.AddCategory("A");
        var b = editor.AddCategory("B");
        editor.AddPrompt(a.Id, "one", "", "1");
        editor.AddPrompt(a.Id, "two", "", "2");

        editor.DeleteCategory(a.Id, b.Id);

        Assert.Equal(b.Id, Assert.Single(lib.Categories).Id);
        Assert.Equal(2, lib.Prompts.Count);
        Assert.All(lib.Prompts, p => Assert.Equal(b.Id, p.CategoryId));
    }

    [Fact]
    public void PromptCount_CountsOnlyThatCategory()
    {
        var (_, editor) = Create();
        var a = editor.AddCategory("A");
        var b = editor.AddCategory("B");
        editor.AddPrompt(a.Id, "one", "", "1");
        editor.AddPrompt(a.Id, "two", "", "2");
        editor.AddPrompt(b.Id, "three", "", "3");
        Assert.Equal(2, editor.PromptCount(a.Id));
        Assert.Equal(1, editor.PromptCount(b.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddPrompt_BlankTitle_Throws(string title)
    {
        var (_, editor) = Create();
        var cat = editor.AddCategory("A");
        Assert.Throws<ArgumentException>(() => editor.AddPrompt(cat.Id, title, "", "text"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n ")]
    public void AddPrompt_BlankText_Throws(string text)
    {
        var (_, editor) = Create();
        var cat = editor.AddCategory("A");
        Assert.Throws<ArgumentException>(() => editor.AddPrompt(cat.Id, "t", "", text));
    }

    [Fact]
    public void AddPrompt_UnknownCategory_Throws()
    {
        var (_, editor) = Create();
        Assert.Throws<ArgumentException>(() => editor.AddPrompt(Guid.NewGuid(), "t", "", "text"));
    }

    [Fact]
    public void AddPrompt_EmptyDescription_Allowed_AndSetsFields()
    {
        var (lib, editor) = Create();
        var cat = editor.AddCategory("A");
        var p = editor.AddPrompt(cat.Id, "  Title ", "", "  keep spaces  ");

        Assert.Equal("Title", p.Title);
        Assert.Equal("", p.Description);
        Assert.Equal("  keep spaces  ", p.Text);
        Assert.Equal(Fixed, p.CreatedAt);
        Assert.Equal(Fixed, p.UpdatedAt);
        Assert.Contains(p, lib.Prompts);
    }

    [Fact]
    public void UpdatePrompt_SetsUpdatedAt()
    {
        var now = Fixed;
        var lib = new Library();
        var editor = new LibraryEditor(lib, () => now);
        var a = editor.AddCategory("A");
        var b = editor.AddCategory("B");
        var p = editor.AddPrompt(a.Id, "t", "", "text");

        now = Fixed.AddHours(3);
        editor.UpdatePrompt(p.Id, " New ", "desc", b.Id, "new text");

        Assert.Equal("New", p.Title);
        Assert.Equal("desc", p.Description);
        Assert.Equal(b.Id, p.CategoryId);
        Assert.Equal("new text", p.Text);
        Assert.Equal(Fixed, p.CreatedAt);
        Assert.Equal(Fixed.AddHours(3), p.UpdatedAt);
    }

    [Fact]
    public void UpdatePrompt_UnknownCategory_Throws_AndNothingChanges()
    {
        var (_, editor) = Create();
        var a = editor.AddCategory("A");
        var p = editor.AddPrompt(a.Id, "t", "", "text");

        Assert.Throws<ArgumentException>(() => editor.UpdatePrompt(p.Id, "x", "", Guid.NewGuid(), "y"));
        Assert.Equal("t", p.Title);
    }

    [Fact]
    public void UpdatePrompt_UnknownId_Throws()
    {
        var (_, editor) = Create();
        var a = editor.AddCategory("A");
        Assert.Throws<ArgumentException>(() => editor.UpdatePrompt(Guid.NewGuid(), "x", "", a.Id, "y"));
    }

    [Fact]
    public void DeletePrompt_Removes_UnknownThrows()
    {
        var (lib, editor) = Create();
        var a = editor.AddCategory("A");
        var p = editor.AddPrompt(a.Id, "t", "", "text");
        editor.DeletePrompt(p.Id);
        Assert.Empty(lib.Prompts);
        Assert.Throws<ArgumentException>(() => editor.DeletePrompt(p.Id));
    }
}
