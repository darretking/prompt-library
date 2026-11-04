using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Tests;

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public JsonStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static Library SampleLibrary(string text)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Coding" };
        var prompt = new Prompt
        {
            Id = Guid.NewGuid(),
            Title = "Run it",
            Description = "desc",
            CategoryId = category.Id,
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return new Library { Categories = { category }, Prompts = { prompt } };
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTrickyText()
    {
        var original = "Line1\n\n```bash\ndotnet run\n```\n{x} \"q\" 'a' 🙂\t end  ";
        var path = PathFor("library.json");

        JsonStore.SaveLibrary(path, SampleLibrary(original));
        var result = JsonStore.LoadLibrary(path);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(LineEndings.ToCrlf(original), result.Value!.Prompts[0].Text);
    }

    [Fact]
    public void SaveThenLoad_KeepsTextThatLooksLikeAnEscapedEmoji()
    {
        var original = @"literal \uD83D\uDE42 and \\uD83D\uDE42 🙂";
        var path = PathFor("library.json");

        JsonStore.SaveLibrary(path, SampleLibrary(original));

        Assert.Equal(original, JsonStore.LoadLibrary(path).Value!.Prompts[0].Text);
    }

    [Fact]
    public void Load_MissingFile_ReturnsMissing()
    {
        var result = JsonStore.LoadLibrary(PathFor("nope.json"));
        Assert.Equal(LoadStatus.Missing, result.Status);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsUnreadable_AndFileUntouched()
    {
        var path = PathFor("library.json");
        File.WriteAllText(path, "{ not json");
        var before = File.ReadAllBytes(path);

        var result = JsonStore.LoadLibrary(path);

        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Load_WrongSchemaVersion_ReturnsUnreadable()
    {
        var path = PathFor("library.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"categories\":[],\"prompts\":[]}");
        Assert.Equal(LoadStatus.Unreadable, JsonStore.LoadLibrary(path).Status);
    }

    [Fact]
    public void Load_PromptWithUnknownCategory_ReturnsUnreadable()
    {
        var library = SampleLibrary("x");
        library.Prompts[0].CategoryId = Guid.NewGuid();
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, library);

        Assert.Equal(LoadStatus.Unreadable, JsonStore.LoadLibrary(path).Status);
    }

    [Fact]
    public void ParseLibrary_NullTitleOrMissingName_Throws()
    {
        var id = Guid.NewGuid();
        var noTitle = $"{{\"schemaVersion\":1,\"categories\":[{{\"id\":\"{id}\",\"name\":\"A\"}}],\"prompts\":[{{\"id\":\"{Guid.NewGuid()}\",\"categoryId\":\"{id}\",\"text\":\"t\"}}]}}";
        var noName = $"{{\"schemaVersion\":1,\"categories\":[{{\"id\":\"{id}\"}}],\"prompts\":[]}}";
        Assert.Throws<InvalidDataException>(() => JsonStore.ParseLibrary(noTitle));
        Assert.Throws<InvalidDataException>(() => JsonStore.ParseLibrary(noName));
    }

    [Fact]
    public void Save_LeavesNoTmpFile()
    {
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, SampleLibrary("x"));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Settings_RoundTrip_AndMissingGivesDefaults()
    {
        var path = PathFor("settings.json");
        Assert.Equal(LoadStatus.Missing, JsonStore.LoadSettings(path).Status);

        JsonStore.SaveSettings(path, new AppSettings { Hotkey = "Ctrl+Alt+K", BackupsToKeep = 7 });
        var result = JsonStore.LoadSettings(path);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal("Ctrl+Alt+K", result.Value!.Hotkey);
        Assert.Equal(7, result.Value.BackupsToKeep);
    }

    [Fact]
    public void SavedJson_IsHumanReadable()
    {
        var path = PathFor("library.json");
        JsonStore.SaveLibrary(path, SampleLibrary("hi 🙂"));
        var text = File.ReadAllText(path);

        Assert.Contains("🙂", text);
        Assert.Contains("\n", text);
        Assert.StartsWith("{", text);
    }
}
