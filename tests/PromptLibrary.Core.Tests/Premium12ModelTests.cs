using PromptLibrary.Core.Editing;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests;

public sealed class Premium12ModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    public Premium12ModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void TagRules_TrimsStripsHashLowercasesDedupes()
        => Assert.Equal(["travel", "tip"], TagRules.Normalize([" #Travel ", "TRAVEL", "", "  ", "##Tip"]));

    [Fact]
    public void TagRules_MaxTenTags_AndMax24Chars()
    {
        var many = Enumerable.Range(0, 15).Select(i => "t" + i);
        Assert.Equal(10, TagRules.Normalize(many).Count);
        Assert.Equal(24, Assert.Single(TagRules.Normalize([new string('a', 40)])).Length);
        Assert.Empty(TagRules.Normalize(null));
    }

    [Fact]
    public void Library_RoundTripsTagsSendToEmoji()
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "Travel", Emoji = "✈️" };
        var lib = new Library
        {
            Categories = [c],
            Prompts = [new Prompt { Id = Guid.NewGuid(), Title = "t", Text = "x", CategoryId = c.Id, Tags = ["a", "b"], SendTo = "claude" }],
        };
        var path = Path.Combine(_dir, "library.json");
        JsonStore.SaveLibrary(path, lib);

        var loaded = JsonStore.LoadLibrary(path).Value!;
        Assert.Equal("✈️", loaded.Categories[0].Emoji);
        Assert.Equal(["a", "b"], loaded.Prompts[0].Tags);
        Assert.Equal("claude", loaded.Prompts[0].SendTo);
    }

    [Fact]
    public void OldLibraryWithoutNewFields_LoadsWithDefaults()
    {
        var json = """
            {"schemaVersion":1,"categories":[{"id":"11111111-1111-1111-1111-111111111111","name":"A"}],
            "prompts":[{"id":"22222222-2222-2222-2222-222222222222","title":"t","text":"x","categoryId":"11111111-1111-1111-1111-111111111111"}]}
            """;
        var lib = JsonStore.ParseLibrary(json);
        Assert.Empty(lib.Prompts[0].Tags);
        Assert.Null(lib.Prompts[0].SendTo);
        Assert.Null(lib.Categories[0].Emoji);
    }

    [Fact]
    public void NullTags_LoadAsEmpty()
    {
        var json = """
            {"schemaVersion":1,"categories":[{"id":"11111111-1111-1111-1111-111111111111","name":"A"}],
            "prompts":[{"id":"22222222-2222-2222-2222-222222222222","title":"t","text":"x","tags":null,"categoryId":"11111111-1111-1111-1111-111111111111"}]}
            """;
        Assert.Empty(JsonStore.ParseLibrary(json).Prompts[0].Tags);
    }

    [Fact]
    public void Settings_RoundTripNewFields_AndOldSettingsGetDefaults()
    {
        var id = Guid.NewGuid();
        var s = new AppSettings { ShortcutOpens = "menu", Theme = "dark" };
        s.Usage[id] = new PromptUsage { Count = 3, LastUsedAt = T };
        s.PlaceholderValues["city"] = "Rome";
        var path = Path.Combine(_dir, "settings.json");
        JsonStore.SaveSettings(path, s);

        var loaded = JsonStore.LoadSettings(path).Value!;
        Assert.Equal("menu", loaded.ShortcutOpens);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal(3, loaded.Usage[id].Count);
        Assert.Equal(T, loaded.Usage[id].LastUsedAt);
        Assert.Equal("Rome", loaded.PlaceholderValues["city"]);

        File.WriteAllText(path, """{"hotkey":"Ctrl+Alt+P"}""");
        var old = JsonStore.LoadSettings(path).Value!;
        Assert.Equal("search", old.ShortcutOpens);
        Assert.Equal("system", old.Theme);
        Assert.Empty(old.Usage);
        Assert.Empty(old.PlaceholderValues);
    }

    private const string TeamJson = """
        {"kind":"prompt-library-team","schemaVersion":1,"name":"T","updatedAt":"2026-11-05T10:00:00+00:00","futureRootThing":{"x":1},
         "categories":[{"id":"11111111-1111-1111-1111-111111111111","name":"Email","emoji":"✉️","futureCat":true}],
         "prompts":[{"id":"22222222-2222-2222-2222-222222222222","title":"Intro","description":"d","categoryId":"11111111-1111-1111-1111-111111111111",
           "text":"Hello","tags":["sales","email"],"sendTo":"copilot","brandNewField":[1,2],
           "createdAt":"2026-11-05T10:00:00+00:00","updatedAt":"2026-11-05T10:00:00+00:00"}]}
        """;

    [Fact]
    public void TeamFile_ReadsNewFields_IgnoresUnknownMembers()
    {
        var team = TeamFile.Parse(TeamJson);
        var p = Assert.Single(team.Content.Prompts);
        Assert.Equal(["sales", "email"], p.Tags);
        Assert.Equal("copilot", p.SendTo);
        Assert.Equal("✉️", team.Content.Categories[0].Emoji);
    }

    [Fact]
    public void TeamFile_RoundTripKeepsNewAndUnknownFields()
    {
        var team = TeamFile.Parse(TeamJson);
        team.Content.Prompts[0].Tags.Add("extra");
        var text = TeamFile.Serialize(team);
        var again = TeamFile.Parse(text);
        Assert.Equal(["sales", "email", "extra"], again.Content.Prompts[0].Tags);
        Assert.Equal("copilot", again.Content.Prompts[0].SendTo);
        Assert.Equal("✉️", again.Content.Categories[0].Emoji);
        Assert.Contains("brandNewField", text);
        Assert.Contains("futureRootThing", text);
    }

    [Fact]
    public async Task TeamReader_ReadsFileWithUnknownMembers()
    {
        var path = Path.Combine(_dir, TeamFile.FileName);
        File.WriteAllText(path, TeamJson);
        var result = await new TeamReader(new HttpClient(), "1.2.0").ReadAsync(new TeamSource(TeamSourceKind.File, path), default);
        Assert.Equal(TeamReadStatus.Ok, result.Status);
        Assert.Equal(["sales", "email"], result.Team!.Content.Prompts[0].Tags);
    }

    [Fact]
    public void TeamWriter_CopyIntoNewTeam_KeepsTagsSendToEmoji()
    {
        var c = new Category { Id = Guid.NewGuid(), Name = "Mine", Emoji = "\U0001F4A1" };
        var src = new Library
        {
            Categories = [c],
            Prompts = [new Prompt { Id = Guid.NewGuid(), Title = "t", Text = "x", CategoryId = c.Id, Tags = ["a"], SendTo = "claude" }],
        };
        var writer = new TeamWriter(() => T);
        var created = writer.Create(_dir, "Team", src, [c.Id], addBusinessExamples: false);
        Assert.True(created.Created);
        var back = TeamFile.Parse(File.ReadAllText(created.FilePath));
        Assert.Equal("\U0001F4A1", back.Content.Categories[0].Emoji);
        Assert.Equal(["a"], back.Content.Prompts[0].Tags);
        Assert.Equal("claude", back.Content.Prompts[0].SendTo);
    }

    [Fact]
    public void Snapshot_RestoresTagsAndSendTo()
    {
        var lib = new Library();
        var ed = new LibraryEditor(lib, () => T);
        var cat = ed.AddCategory("A");
        var p = ed.AddPrompt(cat.Id, "t", "d", "x", ["one"], "claude");
        var snap = PromptSnapshot.Capture(lib, p.Id);

        ed.UpdatePrompt(p.Id, "t", "d", cat.Id, "x", ["two", "three"], null);
        Assert.Equal(["two", "three"], p.Tags);
        Assert.Null(p.SendTo);

        snap.Restore(lib);
        Assert.Equal(["one"], p.Tags);
        Assert.Equal("claude", p.SendTo);
    }

    [Fact]
    public void UpdatePrompt_OldOverload_KeepsTagsAndSendTo()
    {
        var lib = new Library();
        var ed = new LibraryEditor(lib, () => T);
        var cat = ed.AddCategory("A");
        var p = ed.AddPrompt(cat.Id, "t", "d", "x", [" #Tag "], "claude");
        Assert.Equal(["tag"], p.Tags);
        ed.UpdatePrompt(p.Id, "t2", "d", cat.Id, "x");
        Assert.Equal(["tag"], p.Tags);
        Assert.Equal("claude", p.SendTo);
    }

    [Fact]
    public void SetCategoryEmoji_SetsAndClears()
    {
        var lib = new Library();
        var ed = new LibraryEditor(lib, () => T);
        var cat = ed.AddCategory("A");
        ed.SetCategoryEmoji(cat.Id, "\U0001F4A1");
        Assert.Equal("\U0001F4A1", cat.Emoji);
        ed.SetCategoryEmoji(cat.Id, " ");
        Assert.Null(cat.Emoji);
    }

    [Fact]
    public void DeleteCategory_DeletePrompts_RemovesThemAndReturnsIds()
    {
        var lib = new Library();
        var ed = new LibraryEditor(lib, () => T);
        var a = ed.AddCategory("A");
        var b = ed.AddCategory("B");
        var p1 = ed.AddPrompt(a.Id, "1", "", "x");
        var p2 = ed.AddPrompt(a.Id, "2", "", "x");
        var keep = ed.AddPrompt(b.Id, "3", "", "x");

        var removed = ed.DeleteCategory(a.Id, null, deletePrompts: true);

        Assert.Equal([p1.Id, p2.Id], removed);
        Assert.Equal([keep.Id], lib.Prompts.Select(p => p.Id));
        Assert.Equal([b.Id], lib.Categories.Select(c => c.Id));
    }

    [Fact]
    public void DeleteCategory_Default_StillMovesAndStillRequiresTarget()
    {
        var lib = new Library();
        var ed = new LibraryEditor(lib, () => T);
        var a = ed.AddCategory("A");
        var b = ed.AddCategory("B");
        var p = ed.AddPrompt(a.Id, "1", "", "x");

        Assert.Throws<InvalidOperationException>(() => ed.DeleteCategory(a.Id, null));
        Assert.Equal(2, lib.Categories.Count);
        Assert.Empty(ed.DeleteCategory(a.Id, b.Id));
        Assert.Equal(b.Id, p.CategoryId);
    }

    [Fact]
    public void PromptReferences_RemovesPinsAndUsage()
    {
        var gone = Guid.NewGuid();
        var stay = Guid.NewGuid();
        var s = new AppSettings { PinnedPromptIds = [gone, stay] };
        s.Usage[gone] = new PromptUsage { Count = 1 };
        s.Usage[stay] = new PromptUsage { Count = 1 };

        PromptReferences.Remove(s, [gone]);

        Assert.Equal([stay], s.PinnedPromptIds);
        Assert.Equal([stay], s.Usage.Keys);
    }
}
