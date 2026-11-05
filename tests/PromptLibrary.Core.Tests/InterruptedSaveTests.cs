using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests;

/// <summary>
/// A save replaces library.json in two renames (library.json → .bak, .tmp → library.json). If the PC dies between
/// them, library.json is missing; the next start must put the newest readable copy back instead of seeding.
/// </summary>
public sealed class InterruptedSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _library;

    public InterruptedSaveTests()
    {
        Directory.CreateDirectory(_dir);
        _library = Path.Combine(_dir, "library.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private static Library LibraryWithPrompt(string title)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Coding" };
        var library = new Library();
        library.Categories.Add(category);
        library.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = title, Text = "text", CategoryId = category.Id });
        return library;
    }

    // Writes a valid library file at `path` (via a scratch name so no .bak/.tmp is left behind).
    private void WriteLibraryAt(string path, string title)
    {
        var scratch = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        JsonStore.SaveLibrary(scratch, LibraryWithPrompt(title), keepPreviousSave: false);
        File.Move(scratch, path, overwrite: true);
    }

    private string TitleIn(string path) => JsonStore.LoadLibrary(path).Value!.Prompts.Single().Title;

    [Fact]
    public void Recover_LibraryExists_ChangesNothing()
    {
        WriteLibraryAt(_library, "current");
        WriteLibraryAt(AtomicFile.TempPath(_library), "stray tmp");

        var result = InterruptedSave.Recover(_library);

        Assert.Equal(InterruptedSaveSource.None, result);
        Assert.Equal("current", TitleIn(_library));
        Assert.True(File.Exists(AtomicFile.TempPath(_library)));
    }

    [Fact]
    public void Recover_Missing_ReadableNewSave_PutsItBack()
    {
        WriteLibraryAt(AtomicFile.TempPath(_library), "new save");
        WriteLibraryAt(JsonStore.PreviousSavePath(_library), "previous save");

        var result = InterruptedSave.Recover(_library);

        Assert.Equal(InterruptedSaveSource.NewSave, result);
        Assert.Equal("new save", TitleIn(_library));
        Assert.False(File.Exists(AtomicFile.TempPath(_library)));
        Assert.Equal("previous save", TitleIn(JsonStore.PreviousSavePath(_library)));
    }

    [Fact]
    public void Recover_Missing_UnreadableNewSave_UsesPreviousSave_AndKeepsIt()
    {
        File.WriteAllText(AtomicFile.TempPath(_library), "{ half written");
        WriteLibraryAt(JsonStore.PreviousSavePath(_library), "previous save");

        var result = InterruptedSave.Recover(_library);

        Assert.Equal(InterruptedSaveSource.PreviousSave, result);
        Assert.Equal("previous save", TitleIn(_library));
        Assert.True(File.Exists(JsonStore.PreviousSavePath(_library)));
    }

    [Fact]
    public void Recover_Missing_OnlyPreviousSave_UsesIt()
    {
        WriteLibraryAt(JsonStore.PreviousSavePath(_library), "previous save");

        Assert.Equal(InterruptedSaveSource.PreviousSave, InterruptedSave.Recover(_library));
        Assert.Equal("previous save", TitleIn(_library));
    }

    [Fact]
    public void Recover_Missing_NothingReadable_ReturnsNone_AndWritesNothing()
    {
        File.WriteAllText(AtomicFile.TempPath(_library), "not json");

        var result = InterruptedSave.Recover(_library);

        Assert.Equal(InterruptedSaveSource.None, result);
        Assert.False(File.Exists(_library));
        Assert.Equal("not json", File.ReadAllText(AtomicFile.TempPath(_library)));
    }

    [Fact]
    public void Recover_FirstRun_NoFilesAtAll_ReturnsNone()
    {
        Assert.Equal(InterruptedSaveSource.None, InterruptedSave.Recover(_library));
        Assert.False(File.Exists(_library));
    }

    [Fact]
    public void TempPath_IsTheFileAtomicFileWritesFirst()
        => Assert.Equal(_library + ".tmp", AtomicFile.TempPath(_library));
}
