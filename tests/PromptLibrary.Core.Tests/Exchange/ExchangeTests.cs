using PromptLibrary.Core.Exchange;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests.Exchange;

public sealed class ExchangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    public ExchangeTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static Library Sample()
    {
        var code = new Category { Id = Guid.NewGuid(), Name = "Coding", Emoji = "\U0001F4BB" };
        var mail = new Category { Id = Guid.NewGuid(), Name = "Email" };
        return new Library
        {
            Categories = [code, mail],
            Prompts =
            [
                new Prompt { Id = Guid.NewGuid(), Title = "Explain this code", Description = "Plain \"English\"", CategoryId = code.Id,
                    Text = "Explain:\r\n\r\n  {code}\r\n\tdone", Tags = ["review", "learning"], SendTo = "claude" },
                new Prompt { Id = Guid.NewGuid(), Title = "Follow-up", CategoryId = mail.Id, Text = "Hi {name}," },
            ],
        };
    }

    private static Library Empty() => new();

    // ---- Export ----------------------------------------------------------------------------------

    [Fact]
    public void Json_HasFormatMarker_AndTheLibrarySchema()
    {
        var json = PromptExport.ToJson(Sample());
        Assert.Contains("\"format\": \"prompt-library-export/1\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"categories\"", json);
        Assert.Contains("\"prompts\"", json);
    }

    [Fact]
    public void Json_RoundTripsThroughImport_IntoAnEmptyLibrary()
    {
        var path = Path.Combine(_dir, "all.json");
        var source = Sample();
        Assert.Equal(2, PromptExport.WriteJson(path, source));

        var target = Empty();
        var result = PromptImport.FromJsonFile(target, path, () => T);

        Assert.Equal(new ImportResult(2, 0, 0), result);
        Assert.Equal(2, target.Categories.Count);
        Assert.Equal("\U0001F4BB", target.Categories.Single(c => c.Name == "Coding").Emoji);
        var explain = target.Prompts.Single(p => p.Title == "Explain this code");
        Assert.Equal(source.Prompts[0].Text, explain.Text);
        Assert.Equal("Plain \"English\"", explain.Description);
        Assert.Equal(["review", "learning"], explain.Tags);
        Assert.Equal("claude", explain.SendTo);
        Assert.NotEqual(source.Prompts[0].Id, explain.Id);
    }

    [Fact]
    public void Json_WholeFileIsStillALibraryFile()
    {
        var path = Path.Combine(_dir, "all.json");
        PromptExport.WriteJson(path, Sample());
        var loaded = JsonStore.LoadLibrary(path);
        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(2, loaded.Value!.Prompts.Count);
    }

    [Fact]
    public void Markdown_WritesCategorySubfolders_WithFrontMatterAndExactBody()
    {
        var folder = Path.Combine(_dir, "md");
        var lib = Sample();
        Assert.Equal(2, PromptExport.WriteMarkdownFolder(folder, lib));

        var file = Path.Combine(folder, "Coding", "Explain this code.md");
        Assert.True(File.Exists(file));
        var content = File.ReadAllText(file);
        Assert.StartsWith("---\r\ntitle: \"Explain this code\"\r\n", content);
        Assert.Contains("category: \"Coding\"", content);
        Assert.Contains("tags: [\"review\", \"learning\"]", content);
        Assert.EndsWith("---\r\n" + lib.Prompts[0].Text, content);
        Assert.True(File.Exists(Path.Combine(folder, "Email", "Follow-up.md")));
    }

    [Fact]
    public void Markdown_UnsafeNamesBecomeSafe_AndCollisionsGetANumber()
    {
        var cat = new Category { Id = Guid.NewGuid(), Name = "A/B: stuff" };
        var lib = new Library
        {
            Categories = [cat],
            Prompts =
            [
                new Prompt { Id = Guid.NewGuid(), Title = "What? Why*", CategoryId = cat.Id, Text = "x" },
                new Prompt { Id = Guid.NewGuid(), Title = "what? why*", CategoryId = cat.Id, Text = "y" },
                new Prompt { Id = Guid.NewGuid(), Title = "CON", CategoryId = cat.Id, Text = "z" },
            ],
        };
        var folder = Path.Combine(_dir, "md");
        PromptExport.WriteMarkdownFolder(folder, lib);

        var files = Directory.GetFiles(Path.Combine(folder, "A_B_ stuff")).Select(Path.GetFileName).ToList();
        string[] expected = ["_CON.md", "What_ Why_.md", "what_ why_ (2).md"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SafeName_Cases()
    {
        Assert.Equal("Untitled", PromptExport.SafeName("  "));
        Assert.Equal("Untitled", PromptExport.SafeName("..."));
        Assert.Equal("a_b", PromptExport.SafeName("a|b"));
        Assert.Equal("_nul", PromptExport.SafeName("nul"));
        Assert.Equal("_COM3", PromptExport.SafeName("COM3"));
        Assert.Equal(100, PromptExport.SafeName(new string('x', 300)).Length);
    }

    [Fact]
    public void Markdown_RoundTripsThroughFolderImport()
    {
        var folder = Path.Combine(_dir, "md");
        var source = Sample();
        PromptExport.WriteMarkdownFolder(folder, source);

        var target = Empty();
        var result = PromptImport.FromFolder(target, folder, () => T);

        Assert.Equal(new ImportResult(2, 0, 0), result);
        var explain = target.Prompts.Single(p => p.Title == "Explain this code");
        Assert.Equal(source.Prompts[0].Text, explain.Text);
        Assert.Equal("Plain \"English\"", explain.Description);
        Assert.Equal(["review", "learning"], explain.Tags);
        Assert.Equal("Coding", target.Categories.Single(c => c.Id == explain.CategoryId).Name);
    }

    // ---- Import ----------------------------------------------------------------------------------

    [Fact]
    public void Folder_FileNameIsTheTitle_ParentFolderIsTheCategory_NoFrontMatterNeeded()
    {
        var dir = Path.Combine(_dir, "in");
        Directory.CreateDirectory(Path.Combine(dir, "Writing"));
        File.WriteAllText(Path.Combine(dir, "Writing", "Tighten this.txt"), "Make it shorter:\n{text}");
        File.WriteAllText(Path.Combine(dir, "Writing", "Notes.md"), "# Heading\nbody");
        File.WriteAllText(Path.Combine(dir, "Writing", "ignored.png"), "x");

        var target = Empty();
        var result = PromptImport.FromFolder(target, dir, () => T);

        Assert.Equal(new ImportResult(2, 0, 0), result);
        var writing = Assert.Single(target.Categories);
        Assert.Equal("Writing", writing.Name);
        Assert.Equal("Make it shorter:\n{text}", target.Prompts.Single(p => p.Title == "Tighten this").Text);
        Assert.Equal("# Heading\nbody", target.Prompts.Single(p => p.Title == "Notes").Text);
    }

    [Fact]
    public void Folder_FrontMatterWinsOverNamesAndFolders_AllTagStyles()
    {
        var dir = Path.Combine(_dir, "in");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.md"),
            "---\ntitle: Real title\ndescription: 'It''s short'\ncategory: Ideas\ntags:\n  - One\n  - \"two\"\n---\nBody line\n");
        File.WriteAllText(Path.Combine(dir, "b.md"), "---\ntitle: Other\ncategory: Ideas\ntags: #x, y\n---\nB");

        var target = Empty();
        PromptImport.FromFolder(target, dir, () => T);

        var a = target.Prompts.Single(p => p.Title == "Real title");
        Assert.Equal("It's short", a.Description);
        Assert.Equal(["one", "two"], a.Tags);
        Assert.Equal("Body line\n", a.Text);
        Assert.Equal("Ideas", target.Categories.Single(c => c.Id == a.CategoryId).Name);
        Assert.Equal(["x", "y"], target.Prompts.Single(p => p.Title == "Other").Tags);
        Assert.Single(target.Categories);
    }

    [Fact]
    public void Folder_UnclosedFrontMatter_IsJustText()
    {
        var dir = Path.Combine(_dir, "in", "Misc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "x.md"), "---\ntitle: nope\nno end");

        var target = Empty();
        PromptImport.FromFolder(target, Path.Combine(_dir, "in"), () => T);

        var p = Assert.Single(target.Prompts);
        Assert.Equal("x", p.Title);
        Assert.Equal("---\ntitle: nope\nno end", p.Text);
    }

    [Fact]
    public void Import_SkipsDuplicateTitlesInTheSameCategory_AndCountsThem()
    {
        var path = Path.Combine(_dir, "all.json");
        PromptExport.WriteJson(path, Sample());

        var target = Empty();
        PromptImport.FromJsonFile(target, path, () => T);
        var again = PromptImport.FromJsonFile(target, path, () => T);

        Assert.Equal(new ImportResult(0, 2, 0), again);
        Assert.Equal(2, target.Prompts.Count);
        Assert.Equal(2, target.Categories.Count);
    }

    [Fact]
    public void Import_SameTitleInAnotherCategory_IsNotADuplicate()
    {
        var existing = Empty();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Other" };
        existing.Categories.Add(cat);
        existing.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Follow-up", CategoryId = cat.Id, Text = "t" });
        var path = Path.Combine(_dir, "all.json");
        PromptExport.WriteJson(path, Sample());

        var result = PromptImport.FromJsonFile(existing, path, () => T);

        Assert.Equal(2, result.Imported);
        Assert.Equal(3, existing.Categories.Count);
    }

    [Fact]
    public void Import_MatchesAnExistingCategoryByName_IgnoringCase()
    {
        var existing = Empty();
        var cat = new Category { Id = Guid.NewGuid(), Name = "CODING" };
        existing.Categories.Add(cat);
        var path = Path.Combine(_dir, "all.json");
        PromptExport.WriteJson(path, Sample());

        PromptImport.FromJsonFile(existing, path, () => T);

        Assert.Equal(2, existing.Categories.Count);
        Assert.Single(existing.Prompts, p => p.CategoryId == cat.Id);
    }

    [Fact]
    public void Import_AnEmptyFile_IsCountedAsUnusable()
    {
        var dir = Path.Combine(_dir, "in", "A");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "empty.txt"), "  \n");
        File.WriteAllText(Path.Combine(dir, "ok.txt"), "text");

        var result = PromptImport.FromFolder(Empty(), Path.Combine(_dir, "in"), () => T);

        Assert.Equal(new ImportResult(1, 0, 1), result);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"hello\": 1}")]
    [InlineData("{\"format\": \"prompt-library-export/9\", \"categories\": [], \"prompts\": []}")]
    public void Import_AFileThatIsNotAnExport_Throws_AndChangesNothing(string content)
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, content);
        var target = Sample();

        Assert.Throws<InvalidDataException>(() => PromptImport.FromJsonFile(target, path, () => T));
        Assert.Equal(2, target.Prompts.Count);
    }

    [Fact]
    public void Import_AnOrdinaryLibraryJson_IsAccepted()
    {
        var path = Path.Combine(_dir, "library.json");
        JsonStore.SaveLibrary(path, Sample());

        var target = Empty();
        Assert.Equal(2, PromptImport.FromJsonFile(target, path, () => T).Imported);
    }

    [Theory]
    [InlineData(12, 3, 0, "Imported 12 prompts. Skipped 3 duplicates.")]
    [InlineData(1, 0, 0, "Imported 1 prompt.")]
    [InlineData(0, 1, 1, "Imported 0 prompts. Skipped 1 duplicate. Skipped 1 file that couldn't be used.")]
    public void Summary_UsesPlainWords(int imported, int dupes, int unusable, string expected)
        => Assert.Equal(expected, new ImportResult(imported, dupes, unusable).Summary);

    // ---- Dialog start folder -----------------------------------------------------------------------

    [Fact]
    public void StartFolder_SkipsCloudFolders()
    {
        var cloud = Path.Combine(_dir, "OneDrive");
        var inside = Path.Combine(cloud, "Documents");
        var local = Path.Combine(_dir, "Local");
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(local);

        Assert.Equal(local, ExportLocations.SafeStartFolder([inside, local], [cloud]));
        Assert.Null(ExportLocations.SafeStartFolder([inside, Path.Combine(_dir, "missing")], [cloud]));
    }

    [Theory]
    [InlineData(@"C:\Users\x\OneDrive\Docs")]
    [InlineData(@"C:\Users\x\Dropbox")]
    [InlineData(@"G:\My Drive\a")]
    public void IsCloud_RecognisesCloudNamesWithoutAProbe(string path)
        => Assert.True(ExportLocations.IsCloud(path, []));
}
