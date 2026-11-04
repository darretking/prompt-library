using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Tests;

public class PromptSnapshotTests
{
    private static readonly DateTimeOffset Before = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset After = Before.AddHours(1);

    private static (Library Lib, LibraryEditor Editor, Category A, Category B) Create()
    {
        var lib = new Library();
        var time = Before;
        var editor = new LibraryEditor(lib, () => time);
        var a = editor.AddCategory("A");
        var b = editor.AddCategory("B");
        return (lib, editor, a, b);
    }

    [Fact]
    public void Restore_ExistingPrompt_PutsEveryFieldBack()
    {
        var (lib, editor, a, b) = Create();
        var prompt = editor.AddPrompt(a.Id, "Old title", "Old desc", "Old text");
        var snapshot = PromptSnapshot.Capture(lib, prompt.Id);

        new LibraryEditor(lib, () => After).UpdatePrompt(prompt.Id, "New title", "New desc", b.Id, "New text");
        snapshot.Restore(lib);

        Assert.Equal("Old title", prompt.Title);
        Assert.Equal("Old desc", prompt.Description);
        Assert.Equal(a.Id, prompt.CategoryId);
        Assert.Equal("Old text", prompt.Text);
        Assert.Equal(Before, prompt.UpdatedAt);
    }

    [Fact]
    public void Restore_NewPrompt_RemovesIt()
    {
        var (lib, editor, a, _) = Create();
        var prompt = editor.AddPrompt(a.Id, "Draft", "", "Text");
        var snapshot = PromptSnapshot.ForNewPrompt(prompt.Id);

        snapshot.Restore(lib);

        Assert.DoesNotContain(lib.Prompts, p => p.Id == prompt.Id);
    }

    [Fact]
    public void Restore_OldCategoryDeletedMeanwhile_KeepsCurrentCategory()
    {
        var (lib, editor, a, b) = Create();
        var prompt = editor.AddPrompt(a.Id, "T", "", "Text");
        var snapshot = PromptSnapshot.Capture(lib, prompt.Id);

        editor.UpdatePrompt(prompt.Id, "T2", "", b.Id, "Text");
        editor.DeleteCategory(a.Id, movePromptsTo: null); // empty now, so allowed
        snapshot.Restore(lib);

        Assert.Equal("T", prompt.Title);
        Assert.Equal(b.Id, prompt.CategoryId); // never points at a missing category
    }

    [Fact]
    public void Restore_PromptDeletedMeanwhile_DoesNothing()
    {
        var (lib, editor, a, _) = Create();
        var prompt = editor.AddPrompt(a.Id, "T", "", "Text");
        var snapshot = PromptSnapshot.Capture(lib, prompt.Id);
        editor.DeletePrompt(prompt.Id);

        snapshot.Restore(lib);

        Assert.Empty(lib.Prompts);
    }

    [Fact]
    public void Capture_UnknownPrompt_Throws()
    {
        var (lib, _, _, _) = Create();
        Assert.Throws<ArgumentException>(() => PromptSnapshot.Capture(lib, Guid.NewGuid()));
    }
}
