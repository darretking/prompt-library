using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamSuggestionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string TeamPath => Path.Combine(_dir, TeamFile.FileName);
    private string Folder => TeamSuggestions.SuggestionsFolder(TeamPath);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static Suggestion Make(string title = "Title", int minute = 0, string text = "Body") =>
        new(Guid.NewGuid(), title, "Desc", text, ["Alpha", "#beta"], "Sales", "A note", "Ann",
            new DateTimeOffset(2026, 11, 13, 10, minute, 0, TimeSpan.Zero));

    [Fact]
    public void Write_then_list_round_trips_newest_first()
    {
        var a = Make("Old", 1);
        var b = Make("New", 5);
        Assert.Equal(SuggestionWriteResult.Written, TeamSuggestions.TryWrite(TeamPath, a));
        Assert.Equal(SuggestionWriteResult.Written, TeamSuggestions.TryWrite(TeamPath, b));

        var list = TeamSuggestions.List(TeamPath);
        Assert.Equal(["New", "Old"], list.Select(s => s.Title));
        var got = list[0];
        Assert.Equal(b.Id, got.Id);
        Assert.Equal("Body", got.Text);
        Assert.Equal("Sales", got.CategoryName);
        Assert.Equal("A note", got.Note);
        Assert.Equal("Ann", got.From);
        Assert.Equal(["alpha", "beta"], got.Tags);
        Assert.Equal(b.CreatedAt, got.CreatedAt);
    }

    [Fact]
    public void Write_into_an_invalid_path_is_not_writable()
    {
        var blocker = Path.Combine(_dir, "file.txt");
        File.WriteAllText(blocker, "x");
        Assert.Equal(SuggestionWriteResult.NotWritable,
            TeamSuggestions.TryWrite(Path.Combine(blocker, TeamFile.FileName), Make()));
        Assert.Equal(SuggestionWriteResult.NotWritable, TeamSuggestions.TryWrite("Z:\\no\\such\\|bad", Make()));
    }

    [Fact]
    public void List_ignores_untrusted_files_and_caps_fields()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "bad.json"), "{ not json");
        File.WriteAllText(Path.Combine(Folder, "kind.json"), """{"kind":"other","title":"t","text":"x"}""");
        File.WriteAllText(Path.Combine(Folder, "notitle.json"), """{"kind":"prompt-library-suggestion","text":"x"}""");
        File.WriteAllText(Path.Combine(Folder, "big.json"),
            $$"""{"kind":"prompt-library-suggestion","title":"big","text":"{{new string('x', 65 * 1024)}}"}""");
        File.WriteAllText(Path.Combine(Folder, "note.txt"), "x");
        File.WriteAllText(Path.Combine(Folder, "long.json"),
            $$"""{"kind":"prompt-library-suggestion","title":"{{new string('t', 500)}}","text":"x","note":"{{new string('n', 500)}}"}""");

        var only = Assert.Single(TeamSuggestions.List(TeamPath));
        Assert.Equal(TeamSuggestions.MaxTitle, only.Title.Length);
        Assert.Equal(280, only.Note!.Length);
    }

    [Fact]
    public void List_reads_at_most_200_files_and_skips_done_folder()
    {
        for (var i = 0; i < 201; i++) TeamSuggestions.TryWrite(TeamPath, Make("S" + i, i % 50));
        var done = Make("Done");
        TeamSuggestions.TryWrite(TeamPath, done);
        TeamSuggestions.MarkDone(TeamPath, done.Id);

        var list = TeamSuggestions.List(TeamPath);
        Assert.Equal(200, list.Count);
        Assert.DoesNotContain(list, s => s.Id == done.Id);
    }

    [Fact]
    public void MarkDone_moves_the_file_and_keeps_newest_50()
    {
        var first = Make();
        TeamSuggestions.TryWrite(TeamPath, first);
        Assert.True(TeamSuggestions.MarkDone(TeamPath, first.Id));
        Assert.Empty(TeamSuggestions.List(TeamPath));
        var doneDir = Path.Combine(Folder, "done");
        Assert.True(File.Exists(Path.Combine(doneDir, first.Id + ".json")));
        File.SetLastWriteTimeUtc(Path.Combine(doneDir, first.Id + ".json"), DateTime.UtcNow.AddDays(-1));

        for (var i = 0; i < 55; i++)
        {
            var s = Make("S" + i);
            TeamSuggestions.TryWrite(TeamPath, s);
            TeamSuggestions.MarkDone(TeamPath, s.Id);
        }
        Assert.Equal(50, Directory.GetFiles(doneDir).Length);
        Assert.False(File.Exists(Path.Combine(doneDir, first.Id + ".json")));
        Assert.False(TeamSuggestions.MarkDone(TeamPath, Guid.NewGuid()));
    }

    [Fact]
    public void Clipboard_text_has_all_parts()
    {
        var text = TeamSuggestions.ToClipboardText(Make("My title"));
        Assert.Contains("My title", text);
        Assert.Contains("Sales", text);
        Assert.Contains("Body", text);
        Assert.Contains("A note", text);
        Assert.Contains("Ann", text);
    }

    [Fact]
    public void List_of_a_missing_folder_is_empty()
    {
        Assert.Empty(TeamSuggestions.List(TeamPath));
    }

    private static void Junction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "could not create a junction");
    }

    [Fact]
    public void MarkDone_RefusesWhenDoneIsAJunction()
    {
        var s = Make();
        TeamSuggestions.TryWrite(TeamPath, s);
        var elsewhere = Directory.CreateTempSubdirectory().FullName;
        var done = Path.Combine(Folder, TeamSuggestions.DoneFolderName);
        Junction(done, elsewhere);
        try
        {
            Assert.False(TeamSuggestions.MarkDone(TeamPath, s.Id));
            Assert.True(File.Exists(Path.Combine(Folder, s.Id + ".json")));
            Assert.Empty(Directory.GetFiles(elsewhere));
        }
        finally
        {
            Directory.Delete(done); // removes only the junction
            Directory.Delete(elsewhere, true);
        }
    }

    [Fact]
    public void MarkDone_RefusesWhenSuggestionsFolderIsAJunction()
    {
        var real = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(real, "x.json"), "{}");
        Junction(Folder, real);
        try { Assert.False(TeamSuggestions.MarkDone(TeamPath, Guid.NewGuid())); }
        finally { Directory.Delete(Folder); Directory.Delete(real, true); }
    }

    [Fact]
    public void MarkDone_DoesNotOverwriteAndPrunesOnlySuggestionFiles()
    {
        var done = Path.Combine(Folder, TeamSuggestions.DoneFolderName);
        Directory.CreateDirectory(done);
        var keep = Path.Combine(done, "notes.txt");
        var keepJson = Path.Combine(done, "mine.json");
        File.WriteAllText(keep, "x");
        File.WriteAllText(keepJson, "x");
        File.SetLastWriteTimeUtc(keep, DateTime.UtcNow.AddYears(-5));
        File.SetLastWriteTimeUtc(keepJson, DateTime.UtcNow.AddYears(-5));
        for (var i = 0; i < TeamSuggestions.DoneLimit + 5; i++)
        {
            var f = Path.Combine(done, Guid.NewGuid() + ".json");
            File.WriteAllText(f, "{}");
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-1 - i));
        }
        var s = Make();
        TeamSuggestions.TryWrite(TeamPath, s);
        File.WriteAllText(Path.Combine(done, s.Id + ".json"), "original");

        Assert.True(TeamSuggestions.MarkDone(TeamPath, s.Id));
        Assert.Equal("original", File.ReadAllText(Path.Combine(done, s.Id + ".json")));
        Assert.True(File.Exists(Path.Combine(done, s.Id + "-2.json")));
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(keepJson));
        Assert.Equal(TeamSuggestions.DoneLimit, Directory.GetFiles(done).Count(f => TeamSuggestions.IsSuggestionFileName(Path.GetFileName(f))));
    }

    [Theory]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e.json", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e-3.json", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e-x.json", false)]
    [InlineData("notes.json", false)]
    [InlineData("0f8fad5bd9cb469fa16570867728950e.json", false)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e.txt", false)]
    public void IsSuggestionFileName_matches_only_guid_names(string name, bool expected) =>
        Assert.Equal(expected, TeamSuggestions.IsSuggestionFileName(name));

    [Fact]
    public void List_stops_after_the_scan_limit()
    {
        Directory.CreateDirectory(Folder);
        for (var i = 0; i < TeamSuggestions.MaxScan + 50; i++)
            File.WriteAllText(Path.Combine(Folder, $"junk{i}.json"), "nope");
        Assert.Empty(TeamSuggestions.List(TeamPath));
    }

    [Fact]
    public void List_keeps_at_most_200_valid_suggestions()
    {
        for (var i = 0; i < TeamSuggestions.MaxFiles + 20; i++)
            TeamSuggestions.TryWrite(TeamPath, Make("T" + i, i % 50));
        Assert.Equal(TeamSuggestions.MaxFiles, TeamSuggestions.List(TeamPath).Count);
    }
}
