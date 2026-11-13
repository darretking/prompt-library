using System.Text.Json.Nodes;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamModel2Tests
{
    private const string Cat = "11111111-1111-1111-1111-111111111111";
    private const string P1 = "22222222-2222-2222-2222-222222222222";

    private static string Json(string extraRoot = "", string extraPrompt = "")
        => $$"""
        {"kind":"prompt-library-team","schemaVersion":1,"name":"T","updatedAt":"2026-11-05T10:00:00+00:00"{{extraRoot}},
         "categories":[{"id":"{{Cat}}","name":"Email"}],
         "prompts":[{"id":"{{P1}}","title":"Intro","description":"d","categoryId":"{{Cat}}","text":"Hello",
           "createdAt":"2026-11-05T10:00:00+00:00","updatedAt":"2026-11-05T10:00:00+00:00"{{extraPrompt}}}]}
        """;

    private const string Full = """
        ,
        "note":{"text":"Welcome","at":"2026-11-06T09:00:00+00:00"},
        "fillInValues":{" Name ":"Sam","Company":"Acme"},"futureField":{"a":1}
        """;

    [Fact]
    public void Parse_OldStyleFile_HasEmptyDefaults()
    {
        var team = TeamFile.Parse(Json());
        Assert.Null(team.Note);
        Assert.Empty(team.FillInValues);
        Assert.False(team.Content.Prompts[0].Featured);
    }

    [Fact]
    public void Parse_ReadsNoteFillInsAndFeatured()
    {
        var team = TeamFile.Parse(Json(Full, ""","featured":true"""));
        Assert.Equal("Welcome", team.Note!.Text);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T09:00:00+00:00"), team.Note.At);
        Assert.Equal("Sam", team.FillInValues["name"]);
        Assert.Equal("Acme", team.FillInValues["company"]);
        Assert.True(team.Content.Prompts[0].Featured);
    }

    [Fact]
    public void Parse_NoteOverLimit_IsRejected_AtLimitKept()
    {
        var tooLong = new string('x', 281);
        Assert.Null(TeamFile.Parse(Json($$""","note":{"text":"{{tooLong}}","at":"2026-11-06T09:00:00+00:00"}""")).Note);
        var ok = new string('x', 280);
        Assert.Equal(ok, TeamFile.Parse(Json($$""","note":{"text":"{{ok}}","at":"2026-11-06T09:00:00+00:00"}""")).Note!.Text);
    }

    [Fact]
    public void Parse_FillInLimits_DropExtraAndLongValues()
    {
        var entries = string.Join(",", Enumerable.Range(0, 51).Select(i => $"\"k{i}\":\"v\""));
        var team = TeamFile.Parse(Json($$""","fillInValues":{ {{entries}} }"""));
        Assert.Equal(50, team.FillInValues.Count);
        Assert.False(team.FillInValues.ContainsKey("k50"));

        var longValue = new string('v', 201);
        var team2 = TeamFile.Parse(Json($$""","fillInValues":{"a":"{{longValue}}","b":"ok"}"""));
        Assert.Equal(["b"], team2.FillInValues.Keys);
    }

    [Fact]
    public void ParseMergeSerialize_KeepsNewFieldsUnknownFieldsAndTakesModelChange()
    {
        var team = TeamFile.Parse(Json(Full, ""","featured":true,"futureP":7"""));
        team.Content.Prompts[0].Title = "Changed";
        var again = TeamFile.Parse(TeamFile.Serialize(team));

        Assert.Equal("Changed", again.Content.Prompts[0].Title);
        Assert.True(again.Content.Prompts[0].Featured);
        Assert.Equal("Welcome", again.Note!.Text);
        Assert.Equal("Sam", again.FillInValues["name"]);
        Assert.Equal(1, again.Raw["futureField"]!["a"]!.GetValue<int>());
        Assert.Equal(7, ((JsonObject)again.Raw["prompts"]![0]!)["futureP"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_ModelChangesAndClears()
    {
        var team = TeamFile.Parse(Json(Full, ""","featured":true"""));
        team.Content.Prompts[0].Featured = false;
        team.Note = null;
        team.FillInValues = new Dictionary<string, string>();
        var again = TeamFile.Parse(TeamFile.Serialize(team));
        Assert.False(again.Content.Prompts[0].Featured);
        Assert.Null(again.Note);
        Assert.Empty(again.FillInValues);
        Assert.NotNull(again.Raw["futureField"]);

        again.Note = new TeamNote("Hi", DateTimeOffset.Parse("2026-11-07T00:00:00+00:00"));
        again.FillInValues = new Dictionary<string, string> { ["x"] = "y" };
        var third = TeamFile.Parse(TeamFile.Serialize(again));
        Assert.Equal("Hi", third.Note!.Text);
        Assert.Equal("y", third.FillInValues["x"]);
    }

    // Writes the library the way the app does and returns the file text.
    private static string Roundtrip(Library lib)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "library.json");
            JsonStore.SaveLibrary(path, lib);
            return File.ReadAllText(path);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PersonalLibrary_WithoutNewFields_LoadsAndDoesNotWriteThem()
    {
        var lib = JsonStore.ParseLibrary($$"""
            {"schemaVersion":1,"categories":[{"id":"{{Cat}}","name":"Email"}],
             "prompts":[{"id":"{{P1}}","title":"a","description":"","categoryId":"{{Cat}}","text":"t",
               "createdAt":"2026-11-05T10:00:00+00:00","updatedAt":"2026-11-05T10:00:00+00:00"}]}
            """);
        var p = lib.Prompts[0];
        Assert.False(p.Featured);
        Assert.Null(p.SourceTeamPromptId);
        Assert.Null(p.SourceFingerprint);

        var written = Roundtrip(lib);
        Assert.DoesNotContain("featured", written);
        Assert.DoesNotContain("sourceTeamPromptId", written);
        Assert.DoesNotContain("sourceFingerprint", written);

        var id = Guid.NewGuid();
        p.SourceTeamPromptId = id;
        p.SourceFingerprint = "abc";
        var back = JsonStore.ParseLibrary(Roundtrip(lib)).Prompts[0];
        Assert.Equal(id, back.SourceTeamPromptId);
        Assert.Equal("abc", back.SourceFingerprint);
    }

    [Fact]
    public void Settings_NewFieldsRoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            JsonStore.SaveSettings(path, new AppSettings { PresetTeamSourceApplied = "src", SeenTeamNote = "n" });
            var s = JsonStore.LoadSettings(path).Value!;
            Assert.Equal("src", s.PresetTeamSourceApplied);
            Assert.Equal("n", s.SeenTeamNote);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SeenStore_SaveLoadRemoveClear()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "sub", "seen.json");
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var store = new TeamSeenStore(path);
            store.Set(a, new TeamSeenSnapshot("T", "D", "X", "C"));
            store.SetMany([new(b, new TeamSeenSnapshot("T2", "D2", "X2", "C2"))]);
            store.Save();

            var loaded = new TeamSeenStore(path);
            loaded.Load();
            Assert.Equal(new TeamSeenSnapshot("T", "D", "X", "C"), loaded.Get(a));
            Assert.Equal("T2", loaded.Get(b)!.Title);
            Assert.Null(loaded.Get(Guid.NewGuid()));

            loaded.Remove(a);
            Assert.Null(loaded.Get(a));
            loaded.Clear();
            Assert.Null(loaded.Get(b));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "{ not json")]
    [InlineData(true, "[1,2]")]
    [InlineData(true, "{\"not-a-guid\":{}}")]
    public void SeenStore_MissingOrCorruptFile_IsEmpty(bool exists, string content)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "seen.json");
            if (exists) File.WriteAllText(path, content);
            var store = new TeamSeenStore(path);
            store.Load();
            Assert.Null(store.Get(Guid.NewGuid()));
            store.Set(Guid.NewGuid(), new TeamSeenSnapshot("a", "b", "c", "d"));
            store.Save(); // still usable after a bad load
        }
        finally { Directory.Delete(dir, true); }
    }
}
