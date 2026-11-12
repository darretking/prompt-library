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

        AssertCounts(2, 0, 0, result);
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

        AssertCounts(2, 0, 0, result);
        var explain = target.Prompts.Single(p => p.Title == "Explain this code");
        Assert.Equal(source.Prompts[0].Text, explain.Text);
        Assert.Equal("Plain \"English\"", explain.Description);
        Assert.Equal(["review", "learning"], explain.Tags);
        Assert.Equal("Coding", target.Categories.Single(c => c.Id == explain.CategoryId).Name);
        Assert.Equal("\U0001F4BB", target.Categories.Single(c => c.Name == "Coding").Emoji);
        Assert.Null(target.Categories.Single(c => c.Name == "Email").Emoji);
        Assert.Equal("claude", explain.SendTo);
        Assert.Null(target.Prompts.Single(p => p.Title == "Follow-up").SendTo);
    }

    [Fact]
    public void Folder_UnknownSendTo_IsIgnored()
    {
        var folder = Path.Combine(_dir, "in", "Email");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Hi.md"), "---\nsendTo: \"somewhere-else\"\ntags: [a, b]\n---\nHi {name}");

        var target = Empty();
        AssertCounts(1, 0, 0, PromptImport.FromFolder(target, Path.Combine(_dir, "in"), () => T));
        var hi = target.Prompts.Single();
        Assert.Null(hi.SendTo);
        Assert.Equal(["a", "b"], hi.Tags);
    }

    [Fact]
    public void Markdown_WritesTheCategoryEmoji_OnlyWhenThereIsOne()
    {
        var lib = Sample();
        var code = PromptExport.ToMarkdown(lib.Prompts[0], "Coding", "\U0001F4BB");
        var mail = PromptExport.ToMarkdown(lib.Prompts[1], "Email", null);
        Assert.Contains("category: \"Coding\"\r\nemoji: \"\U0001F4BB\"\r\n", code);
        Assert.DoesNotContain("emoji:", mail);
    }

    [Theory]
    [InlineData("emoji: \"\U0001F9EE\"", "\U0001F9EE")]
    [InlineData("icon: ⚙️", "⚙️")]
    [InlineData("emoji: \"Finance\"", null)]
    [InlineData("emoji: \"\U0001F9EE\U0001F4B0\"", null)]
    public void Folder_EmojiOrIconInFrontMatter_SetsTheCategoryIcon_WhenItIsOneEmoji(string line, string? expected)
    {
        var folder = Path.Combine(_dir, "in", "Finance");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Budget.md"), $"---\n{line}\n---\nMake a budget for {{team}}.");

        var target = Empty();
        AssertCounts(1, 0, 0, PromptImport.FromFolder(target, Path.Combine(_dir, "in"), () => T));
        Assert.Equal(expected, target.Categories.Single(c => c.Name == "Finance").Emoji);
    }

    [Fact]
    public void Import_GivesAnExistingCategoryTheEmoji_OnlyWhenItHasNone()
    {
        var target = Empty();
        var plain = new Category { Id = Guid.NewGuid(), Name = "Coding" };
        var own = new Category { Id = Guid.NewGuid(), Name = "Email", Emoji = "\U0001F4E7" };
        target.Categories.AddRange([plain, own]);
        // The same prompts are already there, so both count as duplicates; the empty category still gets its emoji.
        target.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Explain this code", CategoryId = plain.Id, Text = "x" });

        var source = Sample();
        source.Categories[1].Emoji = "\U0001F4AC";
        var path = Path.Combine(_dir, "lib.json");
        PromptExport.WriteJson(path, source);
        var result = PromptImport.FromJsonFile(target, path, () => T);

        AssertCounts(1, 1, 0, result);
        Assert.Equal("\U0001F4BB", plain.Emoji);
        Assert.Equal("\U0001F4E7", own.Emoji);
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

        AssertCounts(2, 0, 0, result);
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

        AssertCounts(0, 2, 0, again);
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

        AssertCounts(1, 0, 1, result);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[1,2]", "not a Prompt Library export")]
    [InlineData("{\"hello\": 1}", "not a Prompt Library export")]
    [InlineData("{\"format\": \"prompt-library-export/9\", \"categories\": [], \"prompts\": []}", "made by a newer Prompt Library")]
    [InlineData("{\"categories\": [], \"prompts\": [{\"title\": \"x\"}]}", "not a Prompt Library export")]
    [InlineData("   \n", "empty")]
    public void Import_AFileThatIsNotAnExport_IsAFailedOutcome_NeverAnException_AndChangesNothing(string content, string reason)
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, content);
        var target = Sample();

        var result = PromptImport.FromJsonFile(target, path, () => T);

        var file = Assert.Single(result.Files);
        Assert.Equal("bad.json", file.File);
        Assert.Equal(reason, file.FailureReason);
        Assert.Equal(1, result.Unusable);
        Assert.Equal(0, result.Imported);
        Assert.Equal(2, target.Prompts.Count);
    }

    [Fact]
    public void Import_AMissingJsonFile_IsAFailedOutcome()
    {
        var result = PromptImport.FromJsonFile(Empty(), Path.Combine(_dir, "nope.json"), () => T);

        Assert.Equal("couldn't be read", Assert.Single(result.Files).FailureReason);
    }

    [Fact]
    public void Import_Folder_ReportsEachFile_WithItsReason()
    {
        var dir = Path.Combine(_dir, "in", "A");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "empty.txt"), "  \n");
        File.WriteAllText(Path.Combine(dir, "big.md"), new string('x', 1024 * 1024 + 1));
        File.WriteAllText(Path.Combine(dir, "ok.txt"), "text");
        var target = Empty();
        PromptImport.FromFolder(target, Path.Combine(_dir, "in"), () => T);

        var again = PromptImport.FromFolder(target, Path.Combine(_dir, "in"), () => T);

        Assert.Equal(3, again.Files.Count);
        Assert.Equal(1, again.Duplicates);
        Assert.Equal(0, again.Imported);
        Assert.Equal(
            [(Path.Combine("A", "big.md"), "larger than 1 MB"), (Path.Combine("A", "empty.txt"), "empty")],
            again.Failures.Select(f => (f.File, f.Reason)).OrderBy(f => f.File).ToArray());
    }

    [Fact]
    public void Import_Json_PromptsWithNoTitleOrText_AreOneFailureForTheFile()
    {
        var path = Path.Combine(_dir, "partial.json");
        var export = PromptExport.ToJson(Sample()).Replace("\"title\": \"Follow-up\"", "\"title\": \" \"");
        File.WriteAllText(path, export);

        var result = PromptImport.FromJsonFile(Empty(), path, () => T);

        Assert.Equal(1, result.Imported);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("partial.json", failure.File);
        Assert.Equal("1 prompt with no title or text", failure.Reason);
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
        => Assert.Equal(expected, Make(imported, dupes, unusable).Summary);

    private static ImportResult Make(int imported, int dupes, int unusable) => new(
        [new ImportFileOutcome("a", imported, dupes, 0, null), .. Enumerable.Range(0, unusable).Select(i => new ImportFileOutcome("f" + i, 0, 0, 0, "empty"))]);

    private static void AssertCounts(int imported, int dupes, int unusable, ImportResult result)
    {
        Assert.Equal(imported, result.Imported);
        Assert.Equal(dupes, result.Duplicates);
        Assert.Equal(unusable, result.Unusable);
    }

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
