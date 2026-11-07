using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private DateTimeOffset _clock = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private TeamWriter Writer() => new(() => _clock = _clock.AddSeconds(1));

    private string FilePath => Path.Combine(_dir, TeamFile.FileName);

    private string HistoryDir => Path.Combine(_dir, "Prompt Library history");

    private static TeamLibrary Team(string name, params string[] titles)
    {
        var cat = new Category { Id = Guid.NewGuid(), Name = "Cat" };
        return new TeamLibrary
        {
            Name = name,
            Content = new Library
            {
                Categories = [cat],
                Prompts = titles.Select(t => new Prompt { Id = Guid.NewGuid(), Title = t, CategoryId = cat.Id, Text = "x {y}" }).ToList(),
            },
        };
    }

    // Writes a team file as a first save and returns its hash.
    private string WriteInitial(string name = "Acme", params string[] titles)
    {
        var r = Writer().Save(FilePath, Team(name, titles), "");
        Assert.Equal(TeamSaveStatus.Saved, r.Status);
        return r.NewHash!;
    }

    [Fact]
    public void Save_Unchanged_WritesAndKeepsHistory()
    {
        var hash1 = WriteInitial("Acme", "One");
        Assert.False(Directory.Exists(HistoryDir)); // nothing to copy on a first save

        var team = TeamFile.Parse(File.ReadAllText(FilePath));
        team.Name = "Acme 2";
        var result = Writer().Save(FilePath, team, hash1);

        Assert.Equal(TeamSaveStatus.Saved, result.Status);
        Assert.Equal(TeamFile.Hash(File.ReadAllText(FilePath)), result.NewHash);
        Assert.Equal("Acme 2", TeamFile.Parse(File.ReadAllText(FilePath)).Name);
        Assert.Equal(team.UpdatedAt, TeamFile.Parse(File.ReadAllText(FilePath)).UpdatedAt);
        var history = Directory.GetFiles(HistoryDir);
        var copy = Assert.Single(history);
        Assert.Matches(@"^team-prompts \d{4}-\d{2}-\d{2}_\d{6}\.json$", Path.GetFileName(copy));
        Assert.Equal("Acme", TeamFile.Parse(File.ReadAllText(copy)).Name);
    }

    [Fact]
    public void Save_ChangedSinceLoad_ReturnsChangedByOther_AndWritesNothing()
    {
        var hash1 = WriteInitial();
        var team = TeamFile.Parse(File.ReadAllText(FilePath));
        File.WriteAllText(FilePath, TeamFile.Serialize(Team("Someone else")));
        var before = File.ReadAllText(FilePath);

        team.Name = "Mine";
        var result = Writer().Save(FilePath, team, hash1);

        Assert.Equal(TeamSaveStatus.ChangedByOther, result.Status);
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.False(Directory.Exists(HistoryDir));
    }

    [Fact]
    public void Save_KeepsNewest30History()
    {
        var hash = WriteInitial();
        for (var i = 0; i < 35; i++)
        {
            var team = TeamFile.Parse(File.ReadAllText(FilePath));
            team.Name = "v" + i;
            hash = Writer().Save(FilePath, team, hash).NewHash!;
        }

        var files = Directory.GetFiles(HistoryDir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(30, files.Count);
        // 35 copies were made (Acme, v0..v33); the 5 oldest (Acme, v0..v3) are gone, so v4 is the oldest kept.
        var oldest = TeamFile.Parse(File.ReadAllText(Path.Combine(HistoryDir, files[0]!)));
        Assert.Equal("v4", oldest.Name);
    }

    [Fact]
    public void Restore_WritesOldVersion_AndHistoryOfCurrent()
    {
        var hash = WriteInitial("Old", "A");
        var t = TeamFile.Parse(File.ReadAllText(FilePath));
        t.Name = "New";
        hash = Writer().Save(FilePath, t, hash).NewHash!;

        var writer = Writer();
        var item = writer.ListHistory(FilePath).Single();
        Assert.Equal(1, item.PromptCount);
        Assert.False(item.IsConflictCopy);

        var result = writer.Restore(FilePath, item, hash);

        Assert.Equal(TeamSaveStatus.Saved, result.Status);
        Assert.Equal("Old", TeamFile.Parse(File.ReadAllText(FilePath)).Name);
        var names = writer.ListHistory(FilePath).Select(h => TeamFile.Parse(File.ReadAllText(h.Path)).Name).ToList();
        Assert.Equal(["New", "Old"], names); // newest first: the copy of the current version, then the original
    }

    [Fact]
    public void Restore_ChangedByOther_Refuses()
    {
        var hash = WriteInitial("Old");
        var t = TeamFile.Parse(File.ReadAllText(FilePath));
        t.Name = "New";
        Writer().Save(FilePath, t, hash);
        var writer = Writer();
        var item = writer.ListHistory(FilePath).Single();

        var result = writer.Restore(FilePath, item, "stale");

        Assert.Equal(TeamSaveStatus.ChangedByOther, result.Status);
        Assert.Equal("New", TeamFile.Parse(File.ReadAllText(FilePath)).Name);
    }

    [Fact]
    public void Save_ReadOnlyFile_Fails_WithPlainMessage()
    {
        var hash = WriteInitial();
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        var team = TeamFile.Parse(File.ReadAllText(FilePath));

        var result = Writer().Save(FilePath, team, hash);

        Assert.Equal(TeamSaveStatus.Failed, result.Status);
        Assert.StartsWith("Couldn't save the team library: ", result.Message);
    }

    [Fact]
    public void Create_NewFolder_WritesFileWithFreshIds()
    {
        var seed = new Library { Categories = [new Category { Id = Guid.NewGuid(), Name = "Mine" }, new Category { Id = Guid.NewGuid(), Name = "Other" }] };
        seed.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "P", CategoryId = seed.Categories[0].Id, Text = "t" });
        seed.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Q", CategoryId = seed.Categories[1].Id, Text = "u" });
        var seedIds = seed.Categories.Select(c => c.Id).Concat(seed.Prompts.Select(p => p.Id)).ToList();
        var folder = Path.Combine(_dir, "new", "folder");

        var result = Writer().Create(folder, "Acme", seed, [seed.Categories[0].Id], false);

        Assert.True(result.Created);
        Assert.Equal(Path.Combine(folder, TeamFile.FileName), result.FilePath);
        var team = TeamFile.Parse(File.ReadAllText(result.FilePath));
        Assert.Equal("Acme", team.Name);
        var cat = Assert.Single(team.Content.Categories);
        Assert.Equal("Mine", cat.Name);
        var prompt = Assert.Single(team.Content.Prompts);
        Assert.Equal("P", prompt.Title);
        Assert.Equal(cat.Id, prompt.CategoryId);
        Assert.DoesNotContain(cat.Id, seedIds);
        Assert.DoesNotContain(prompt.Id, seedIds);
        // the seed library is unchanged
        Assert.Equal(2, seed.Categories.Count);
        Assert.Equal(2, seed.Prompts.Count);
        Assert.Equal(seedIds, seed.Categories.Select(c => c.Id).Concat(seed.Prompts.Select(p => p.Id)).ToList());
    }

    [Fact]
    public void Create_ExistingFile_RefusesAndReturnsItsName()
    {
        WriteInitial("Already here");
        var before = File.ReadAllText(FilePath);

        var result = Writer().Create(_dir, "New", null, [], false);

        Assert.False(result.Created);
        Assert.Equal("Already here", result.ExistingName);
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Create_WithBusinessExamples_SeedsTheSet()
    {
        var result = Writer().Create(_dir, "Acme", null, [], true);

        Assert.True(result.Created);
        var team = TeamFile.Parse(File.ReadAllText(result.FilePath));
        Assert.Equal(24, team.Content.Prompts.Count);
        Assert.Equal(6, team.Content.Categories.Count);
        var reference = TeamSeedLibrary.Create(_clock);
        Assert.DoesNotContain(team.Content.Prompts.Select(p => p.Id), id => reference.Prompts.Any(p => p.Id == id));
    }

    [Fact]
    public void Create_SameCategoryNameInBothSources_MergesIntoOne()
    {
        var seed = new Library { Categories = [new Category { Id = Guid.NewGuid(), Name = "Sales" }] };
        seed.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "Mine", CategoryId = seed.Categories[0].Id, Text = "t" });

        var result = Writer().Create(_dir, "Acme", seed, [seed.Categories[0].Id], true);

        var team = TeamFile.Parse(File.ReadAllText(result.FilePath));
        Assert.Single(team.Content.Categories, c => c.Name == "Sales");
        Assert.Equal(6, team.Content.Categories.Count);
        Assert.Equal(25, team.Content.Prompts.Count);
        var sales = team.Content.Categories.Single(c => c.Name == "Sales");
        Assert.Equal(5, team.Content.Prompts.Count(p => p.CategoryId == sales.Id));
    }

    [Fact]
    public void Create_UnwritableFolder_FailsWithoutThrowing()
    {
        var blocker = Path.Combine(_dir, "afile");
        File.WriteAllText(blocker, "x");

        var result = Writer().Create(Path.Combine(blocker, "sub"), "Acme", null, [], false);

        Assert.False(result.Created);
        Assert.StartsWith("Couldn't save the team library: ", result.Message);
    }

    [Fact]
    public void CanWrite_ReadOnlyFile_False()
    {
        WriteInitial();
        var before = File.ReadAllText(FilePath);
        Assert.True(TeamWriter.CanWrite(FilePath));
        Assert.Equal(before, File.ReadAllText(FilePath));

        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        Assert.False(TeamWriter.CanWrite(FilePath));
        Assert.False(TeamWriter.CanWrite(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void FindConflictCopies_OneDriveAndDropboxNames()
    {
        WriteInitial();
        var oneDrive = Path.Combine(_dir, "team-prompts-DESKTOP-AB12.json");
        var dropbox = Path.Combine(_dir, "team-prompts (Sam's conflicted copy 2026-11-05).json");
        foreach (var p in new[] { oneDrive, dropbox, FilePath + ".tmp", FilePath + ".bak", Path.Combine(_dir, "notes.json"), Path.Combine(_dir, "team-prompts-x.txt") })
            File.WriteAllText(p, "{}");

        var found = TeamWriter.FindConflictCopies(FilePath);

        Assert.Equal(new[] { dropbox, oneDrive }.OrderBy(p => p, StringComparer.Ordinal).ToList(), found);
    }

    [Fact]
    public void ListHistory_ConflictCopiesFirst_AndUnreadableSkipped()
    {
        var hash = WriteInitial("Acme", "A");
        var t = TeamFile.Parse(File.ReadAllText(FilePath));
        Writer().Save(FilePath, t, hash);
        var conflict = Path.Combine(_dir, "team-prompts-PC.json");
        File.WriteAllText(conflict, TeamFile.Serialize(Team("Theirs", "A", "B")));
        File.WriteAllText(Path.Combine(_dir, "team-prompts-bad.json"), "not json");

        var list = Writer().ListHistory(FilePath);

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsConflictCopy);
        Assert.Equal(conflict, list[0].Path);
        Assert.Equal(2, list[0].PromptCount);
        Assert.False(list[1].IsConflictCopy);
    }

    [Fact]
    public void MoveConflictCopiesToHistory_MovesSiblings_KeepsThemRestorable()
    {
        var hash = WriteInitial("Acme", "A");
        Writer().Save(FilePath, TeamFile.Parse(File.ReadAllText(FilePath)), hash); // one history copy
        var conflict = Path.Combine(_dir, "team-prompts-PC.json");
        File.WriteAllText(conflict, TeamFile.Serialize(Team("Theirs", "A", "B")));
        var stamp = new DateTime(2026, 11, 4, 9, 30, 0, DateTimeKind.Local);
        File.SetLastWriteTime(conflict, stamp);
        var bad = Path.Combine(_dir, "team-prompts-bad.json");
        File.WriteAllText(bad, "not json");
        var teamText = File.ReadAllText(FilePath);

        var moved = Writer().MoveConflictCopiesToHistory(FilePath);

        Assert.Equal(2, moved);
        Assert.Empty(TeamWriter.FindConflictCopies(FilePath));
        Assert.False(File.Exists(conflict));
        Assert.False(File.Exists(bad));
        Assert.Equal(teamText, File.ReadAllText(FilePath)); // the team file itself is untouched
        Assert.Equal(3, Directory.GetFiles(HistoryDir).Length); // nothing deleted

        // The other version is now an ordinary older version, at the time it was last written.
        var list = Writer().ListHistory(FilePath);
        Assert.All(list, i => Assert.False(i.IsConflictCopy));
        var theirs = Assert.Single(list, i => i.PromptCount == 2);
        Assert.Equal(stamp, theirs.SavedAt.LocalDateTime);
    }

    [Fact]
    public void MoveConflictCopiesToHistory_NoCopies_MovesNothing()
    {
        WriteInitial("Acme", "A");
        Assert.Equal(0, Writer().MoveConflictCopiesToHistory(FilePath));
        Assert.False(Directory.Exists(HistoryDir));
    }
}
