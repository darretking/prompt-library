using System.Text;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests;

/// <summary>Saves are flushed to disk before they replace the file, and library.json keeps its previous save as .bak.</summary>
public sealed class DurableSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public DurableSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    [Fact]
    public void WriteAllText_WritesExactUtf8WithoutBom_AndLeavesNoTmp()
    {
        var path = Path.Combine(_dir, "file.json");

        AtomicFile.WriteAllText(path, "héllo 👋\r\n");

        Assert.Equal(new UTF8Encoding(false).GetBytes("héllo 👋\r\n"), File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAllText_KeepBackup_FirstSave_CreatesNoBak()
    {
        var path = Path.Combine(_dir, "library.json");

        AtomicFile.WriteAllText(path, "first", keepBackup: true);

        Assert.Equal("first", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void WriteAllText_KeepBackup_LaterSave_KeepsThePreviousContentAsBak()
    {
        var path = Path.Combine(_dir, "library.json");
        AtomicFile.WriteAllText(path, "first", keepBackup: true);

        AtomicFile.WriteAllText(path, "second", keepBackup: true);
        AtomicFile.WriteAllText(path, "third", keepBackup: true);

        Assert.Equal("third", File.ReadAllText(path));
        Assert.Equal("second", File.ReadAllText(path + ".bak")); // only one .bak: the save before this one
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir, "library.json.bak*"));
    }

    [Fact]
    public void WriteAllText_WithoutKeepBackup_CreatesNoBak()
    {
        var path = Path.Combine(_dir, "settings.json");
        AtomicFile.WriteAllText(path, "first");

        AtomicFile.WriteAllText(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void SaveLibrary_KeepsThePreviousSaveAsBak_ThatLoadsAsALibrary()
    {
        var path = Path.Combine(_dir, "library.json");
        var categoryId = Guid.NewGuid();
        var library = new Library
        {
            Categories = [new Category { Id = categoryId, Name = "Coding" }],
            Prompts = [new Prompt { Id = Guid.NewGuid(), Title = "Old", Text = "t", CategoryId = categoryId }],
        };
        JsonStore.SaveLibrary(path, library);

        library.Prompts[0].Title = "New";
        JsonStore.SaveLibrary(path, library);

        var previous = JsonStore.LoadLibrary(path + ".bak");
        Assert.Equal(LoadStatus.Ok, previous.Status);
        Assert.Equal("Old", previous.Value!.Prompts[0].Title);
        Assert.Equal("New", JsonStore.LoadLibrary(path).Value!.Prompts[0].Title);
    }

    [Fact]
    public void SaveSettings_CreatesNoBak()
    {
        var path = Path.Combine(_dir, "settings.json");
        JsonStore.SaveSettings(path, new AppSettings());
        JsonStore.SaveSettings(path, new AppSettings { BackupsToKeep = 5 });

        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void PreviousSavePath_IsTheBakNextToTheLibrary()
        => Assert.Equal(Path.Combine(_dir, "library.json.bak"), JsonStore.PreviousSavePath(Path.Combine(_dir, "library.json")));
}
