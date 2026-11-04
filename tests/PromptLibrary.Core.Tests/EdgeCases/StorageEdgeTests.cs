using System.Diagnostics;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests.EdgeCases;

public sealed class StorageEdgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public StorageEdgeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var file in Directory.EnumerateFiles(_dir))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static Library LibraryWith(params string[] texts)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Cat" };
        var library = new Library { Categories = { category } };
        for (var i = 0; i < texts.Length; i++)
            library.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "P" + i, CategoryId = category.Id, Text = texts[i] });
        return library;
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, LibraryWith("first"));

        JsonStore.SaveLibrary(path, LibraryWith("second"));

        Assert.Equal("second", JsonStore.LoadLibrary(path).Value!.Prompts.Single().Text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_WithStaleTmpPresent_Succeeds()
    {
        var path = PathFor("library.json");
        File.WriteAllText(path + ".tmp", "half-written junk from a crash");

        JsonStore.SaveLibrary(path, LibraryWith("fresh"));

        Assert.Equal("fresh", JsonStore.LoadLibrary(path).Value!.Prompts.Single().Text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_ReadOnlyTarget_Throws_AndNoTmpLeft()
    {
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, LibraryWith("original"));
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.Throws<UnauthorizedAccessException>(() => JsonStore.SaveLibrary(path, LibraryWith("new")));

        Assert.False(File.Exists(path + ".tmp"));
        File.SetAttributes(path, FileAttributes.Normal);
        Assert.Equal("original", JsonStore.LoadLibrary(path).Value!.Prompts.Single().Text);
    }

    [Fact]
    public void Load_FileLockedByOtherProcess_ReturnsUnreadable()
    {
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, LibraryWith("locked"));
        var before = File.ReadAllBytes(path);

        LoadResult<Library> result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = JsonStore.LoadLibrary(path);

        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void RoundTrip_LargeLibrary()
    {
        var path = PathFor("library.json");
        var texts = Enumerable.Range(0, 5_000).Select(i => $"Prompt number {i}\nwith two lines").ToList();
        texts[0] = new string('x', 1024 * 1024);
        var library = LibraryWith([.. texts]);

        var stopwatch = Stopwatch.StartNew();
        JsonStore.SaveLibrary(path, library);
        var loaded = JsonStore.LoadLibrary(path);
        stopwatch.Stop();

        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(5_000, loaded.Value!.Prompts.Count);
        Assert.Equal(1024 * 1024, loaded.Value.Prompts[0].Text.Length);
        Assert.Equal("Prompt number 4999\r\nwith two lines", loaded.Value.Prompts[4_999].Text);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Round trip took {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void RoundTrip_UnicodeZoo()
    {
        // CJK, Arabic (RTL), e + combining acute, ZWJ family emoji, tab, NBSP, U+2028 line separator.
        var zoo = string.Concat(
            "中文 日本語 한국어 | مرحبا بالعالم | e", (char)0x0301, " | ",
            char.ConvertFromUtf32(0x1F468), (char)0x200D, char.ConvertFromUtf32(0x1F469), (char)0x200D, char.ConvertFromUtf32(0x1F467),
            " | \t | ", (char)0x00A0, " | ", (char)0x2028, " |\nend");
        var path = PathFor("library.json");

        JsonStore.SaveLibrary(path, LibraryWith(zoo));
        var loaded = JsonStore.LoadLibrary(path).Value!.Prompts.Single().Text;

        Assert.Equal(zoo.Replace("\n", "\r\n"), loaded);
    }

    [Fact]
    public void RoundTrip_MixedLineEndings()
    {
        var path = PathFor("library.json");

        JsonStore.SaveLibrary(path, LibraryWith("a\r\r\nb\n\rc"));

        Assert.Equal("a\r\n\r\nb\r\n\r\nc", JsonStore.LoadLibrary(path).Value!.Prompts.Single().Text);
    }

    [Fact]
    public void Load_EmptyFile_Unreadable()
    {
        var path = PathFor("library.json");
        File.WriteAllText(path, "");

        Assert.Equal(LoadStatus.Unreadable, JsonStore.LoadLibrary(path).Status);
        Assert.Equal("", File.ReadAllText(path));
    }

    [Fact]
    public void Load_JsonArrayInsteadOfObject_Unreadable()
    {
        var path = PathFor("library.json");
        File.WriteAllText(path, "[{\"schemaVersion\":1}]");

        Assert.Equal(LoadStatus.Unreadable, JsonStore.LoadLibrary(path).Status);
    }
}
