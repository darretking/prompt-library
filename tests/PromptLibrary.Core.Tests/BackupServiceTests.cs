using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 9, 5, 7);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly string _library;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _folder = Path.Combine(_dir, "backups");
        _library = Path.Combine(_dir, "library.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static BackupService ServiceAt(DateTime now) => new(() => now);

    private static string LibraryJson(int promptCount, string tag = "")
    {
        var categoryId = Guid.NewGuid();
        var library = new Library
        {
            Categories = [new Category { Id = categoryId, Name = "Cat" + tag }],
            Prompts = Enumerable.Range(0, promptCount)
                .Select(i => new Prompt { Id = Guid.NewGuid(), Title = "P" + i, Text = "text" + i + tag, CategoryId = categoryId })
                .ToList(),
        };
        var path = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonStore.SaveLibrary(path, library);
            return File.ReadAllText(path);
        }
        finally { File.Delete(path); }
    }

    private void WriteLibrary(string json) => File.WriteAllText(_library, json);

    private string[] BackupFiles() => Directory.GetFiles(_folder, "PromptLibrary-backup-*.json");

    [Fact]
    public void BackupNow_CreatesNamedFile()
    {
        WriteLibrary(LibraryJson(2));
        var result = ServiceAt(Now).BackupNow(_library, _folder, 30);

        var expected = Path.Combine(_folder, "PromptLibrary-backup-2026-11-04_090507.json");
        Assert.True(result.Success);
        Assert.Equal(expected, result.BackupPath);
        Assert.Equal(LibraryHash.OfFile(_library), result.Hash);
        Assert.Equal(File.ReadAllBytes(_library), File.ReadAllBytes(expected));
    }

    [Fact]
    public void BackupNow_SameSecondTwice_AppendsSuffix()
    {
        WriteLibrary(LibraryJson(1));
        var service = ServiceAt(Now);
        service.BackupNow(_library, _folder, 30);
        var second = service.BackupNow(_library, _folder, 30);
        var third = service.BackupNow(_library, _folder, 30);

        Assert.EndsWith("PromptLibrary-backup-2026-11-04_090507-1.json", second.BackupPath);
        Assert.EndsWith("PromptLibrary-backup-2026-11-04_090507-2.json", third.BackupPath);
        Assert.Equal(3, BackupFiles().Length);
    }

    [Fact]
    public void BackupNow_FolderUnavailable_ReturnsFailure_DoesNotThrow()
    {
        WriteLibrary(LibraryJson(1));
        var blocker = Path.Combine(_dir, "iAmAFile");
        File.WriteAllText(blocker, "x");

        var result = ServiceAt(Now).BackupNow(_library, Path.Combine(blocker, "sub"), 30);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void BackupNow_FolderParentMissing_ReturnsFailure()
    {
        WriteLibrary(LibraryJson(1));
        var result = ServiceAt(Now).BackupNow(_library, Path.Combine(_dir, "nope", "backups"), 30);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void Prune_KeepsNewest30_IgnoresOtherFiles()
    {
        Directory.CreateDirectory(_folder);
        var start = new DateTime(2026, 10, 1, 8, 0, 0);
        for (var i = 0; i < 35; i++)
            File.WriteAllText(Path.Combine(_folder, $"PromptLibrary-backup-{start.AddDays(i):yyyy-MM-dd_HHmmss}.json"), "{}");
        var notes = Path.Combine(_folder, "notes.txt");
        var old = Path.Combine(_folder, "PromptLibrary-backup-old.json");
        File.WriteAllText(notes, "n");
        File.WriteAllText(old, "o");

        ServiceAt(Now).Prune(_folder, 30);

        // 30 dated backups remain, plus the non-matching "-old" file which also fits the glob.
        Assert.Equal(30 + 1, BackupFiles().Length);
        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(Path.Combine(_folder, $"PromptLibrary-backup-{start:yyyy-MM-dd_HHmmss}.json")));
        Assert.True(File.Exists(Path.Combine(_folder, $"PromptLibrary-backup-{start.AddDays(34):yyyy-MM-dd_HHmmss}.json")));
    }

    [Fact]
    public void Prune_SuffixedFileOrdersAfterPlainOfSameSecond()
    {
        Directory.CreateDirectory(_folder);
        var plain = Path.Combine(_folder, "PromptLibrary-backup-2026-11-04_090507.json");
        var suffixed = Path.Combine(_folder, "PromptLibrary-backup-2026-11-04_090507-1.json");
        File.WriteAllText(plain, "{}");
        File.WriteAllText(suffixed, "{}");

        ServiceAt(Now).Prune(_folder, 1);

        Assert.False(File.Exists(plain));
        Assert.True(File.Exists(suffixed));
    }

    [Fact]
    public void ShouldBackup_Unchanged_FalseExceptManual()
    {
        var s = new AppSettings { LastBackedUpLibraryHash = "abc", LastBackupAt = new DateTimeOffset(Now).AddDays(-5) };
        var service = ServiceAt(Now);

        Assert.False(service.ShouldBackup(BackupTrigger.Daily, "abc", s));
        Assert.False(service.ShouldBackup(BackupTrigger.OnExit, "abc", s));
        Assert.True(service.ShouldBackup(BackupTrigger.Manual, "abc", s));
    }

    [Fact]
    public void ShouldBackup_Daily_ChangedButWithin24h_False()
    {
        var s = new AppSettings { LastBackedUpLibraryHash = "abc", LastBackupAt = new DateTimeOffset(Now).AddHours(-23) };
        Assert.False(ServiceAt(Now).ShouldBackup(BackupTrigger.Daily, "def", s));
    }

    [Fact]
    public void ShouldBackup_Daily_ChangedAfter24h_True()
    {
        var service = ServiceAt(Now);
        var exactly = new AppSettings { LastBackedUpLibraryHash = "abc", LastBackupAt = new DateTimeOffset(Now).AddHours(-24) };
        var never = new AppSettings();

        Assert.True(service.ShouldBackup(BackupTrigger.Daily, "def", exactly));
        Assert.True(service.ShouldBackup(BackupTrigger.Daily, "def", never));
    }

    [Fact]
    public void ShouldBackup_OnExit_Changed_True()
    {
        var s = new AppSettings { LastBackedUpLibraryHash = "abc", LastBackupAt = new DateTimeOffset(Now).AddMinutes(-1) };
        Assert.True(ServiceAt(Now).ShouldBackup(BackupTrigger.OnExit, "def", s));
    }

    [Fact]
    public void List_NewestFirst_WithPromptCounts()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "PromptLibrary-backup-2026-11-01_100000.json"), LibraryJson(1));
        File.WriteAllText(Path.Combine(_folder, "PromptLibrary-backup-2026-11-03_100000.json"), LibraryJson(3));
        File.WriteAllText(Path.Combine(_folder, "PromptLibrary-backup-2026-11-02_100000.json"), "not json");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "x");

        var list = ServiceAt(Now).List(_folder);

        Assert.Equal(3, list.Count);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0), list[0].Timestamp);
        Assert.Equal(3, list[0].PromptCount);
        Assert.Equal(-1, list[1].PromptCount);
        Assert.Equal(1, list[2].PromptCount);
    }

    [Fact]
    public void List_MissingFolder_ReturnsEmpty()
        => Assert.Empty(ServiceAt(Now).List(Path.Combine(_dir, "missing")));

    [Fact]
    public void Restore_ReplacesLibrary_AndBacksUpCurrentFirst()
    {
        var chosenJson = LibraryJson(2, "chosen");
        Directory.CreateDirectory(_folder);
        var chosen = Path.Combine(_folder, "PromptLibrary-backup-2026-10-01_080000.json");
        File.WriteAllText(chosen, chosenJson);
        var currentJson = LibraryJson(4, "current");
        WriteLibrary(currentJson);

        var result = ServiceAt(Now).Restore(chosen, _library, _folder, 30);

        Assert.True(result.Success);
        Assert.Equal(2, BackupFiles().Length);
        Assert.Equal(chosenJson, File.ReadAllText(_library));
        Assert.Equal(currentJson, File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void Restore_CorruptBackup_Fails_NothingTouched()
    {
        Directory.CreateDirectory(_folder);
        var corrupt = Path.Combine(_folder, "PromptLibrary-backup-2026-10-01_080000.json");
        File.WriteAllText(corrupt, "{ not valid");
        WriteLibrary(LibraryJson(2));
        var before = File.ReadAllBytes(_library);

        var result = ServiceAt(Now).Restore(corrupt, _library, _folder, 30);

        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllBytes(_library));
        Assert.Single(BackupFiles());
    }

    [Fact]
    public void Restore_PreBackupFails_AbortsAndLeavesLibraryUntouched()
    {
        var chosen = Path.Combine(_dir, "PromptLibrary-backup-2026-10-01_080000.json");
        File.WriteAllText(chosen, LibraryJson(1));
        WriteLibrary(LibraryJson(2));
        var before = File.ReadAllBytes(_library);
        var blocker = Path.Combine(_dir, "iAmAFile");
        File.WriteAllText(blocker, "x");

        var result = ServiceAt(Now).Restore(chosen, _library, Path.Combine(blocker, "sub"), 30);

        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllBytes(_library));
    }

    [Fact]
    public void LibraryHash_IsLowercaseHexSha256()
    {
        File.WriteAllText(_library, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", LibraryHash.OfFile(_library));
    }
}
