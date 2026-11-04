using System.Diagnostics;
using System.Globalization;
using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests.EdgeCases;

public sealed class BackupEdgeTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 9, 5, 7);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly string _library;

    public BackupEdgeTests()
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

    private static void SaveLibrary(string path, int promptCount)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Cat" };
        var library = new Library { Categories = { category } };
        for (var i = 0; i < promptCount; i++)
            library.Prompts.Add(new Prompt { Id = Guid.NewGuid(), Title = "P" + i, CategoryId = category.Id, Text = "text" + i });
        JsonStore.SaveLibrary(path, library);
    }

    private static string BackupName(string stamp) => $"PromptLibrary-backup-{stamp}.json";

    private void WriteBackup(string stamp, string content = "{}")
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, BackupName(stamp)), content);
    }

    private string[] BackupFileNames() =>
        Directory.GetFiles(_folder, "PromptLibrary-backup-*.json").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Restore_NoCurrentLibrary_Succeeds_NoSafetyBackup()
    {
        var backup = Path.Combine(_dir, "chosen.json");
        SaveLibrary(backup, 3);

        var result = ServiceAt(Now).Restore(backup, _library, _folder, 30);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.BackupPath);
        Assert.Equal(File.ReadAllText(backup), File.ReadAllText(_library));
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public void BackupNow_PrunesEndToEnd()
    {
        SaveLibrary(_library, 1);
        WriteBackup("2026-11-01_080000");
        WriteBackup("2026-11-02_080000");
        WriteBackup("2026-11-03_080000");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "keep me");

        var result = ServiceAt(Now).BackupNow(_library, _folder, keep: 2);

        Assert.True(result.Success, result.Error);
        Assert.Equal([BackupName("2026-11-03_080000"), BackupName("2026-11-04_090507")], BackupFileNames());
        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BackupNow_KeepZero_KeepsNewOne(int keep)
    {
        SaveLibrary(_library, 1);
        WriteBackup("2026-11-03_080000");

        var result = ServiceAt(Now).BackupNow(_library, _folder, keep);

        Assert.True(result.Success, result.Error);
        Assert.Equal([BackupName("2026-11-04_090507")], BackupFileNames());
        Assert.True(File.Exists(result.BackupPath));
    }

    [Fact]
    public void ShouldBackup_FutureLastBackup_Due()
    {
        var settings = new AppSettings
        {
            LastBackupAt = new DateTimeOffset(Now).AddDays(3), // the clock was moved back since
            LastBackedUpLibraryHash = "old",
        };

        Assert.True(ServiceAt(Now).ShouldBackup(BackupTrigger.Daily, "new", settings));
    }

    [Fact]
    public void BackupNow_HashIsOfCopiedFile()
    {
        SaveLibrary(_library, 2);

        var result = ServiceAt(Now).BackupNow(_library, _folder, 30);
        File.AppendAllText(_library, " "); // the live library changes after the copy

        Assert.True(result.Success, result.Error);
        Assert.Equal(LibraryHash.OfFile(result.BackupPath!), result.Hash);
        Assert.NotEqual(LibraryHash.OfFile(_library), result.Hash);
    }

    [Fact]
    public void BackupNow_MissingLibrary_Fails()
    {
        var result = ServiceAt(Now).BackupNow(_library, _folder, 30);

        Assert.False(result.Success);
        Assert.Null(result.BackupPath);
        Assert.Contains(_library, result.Error);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Prune_SuffixOrdering_Numeric()
    {
        WriteBackup("2026-11-04_090507");
        WriteBackup("2026-11-04_090507-2");
        WriteBackup("2026-11-04_090507-10");

        ServiceAt(Now).Prune(_folder, keep: 1);

        Assert.Equal([BackupName("2026-11-04_090507-10")], BackupFileNames());
    }

    [Fact]
    public void BackupNow_UnicodeAndSpacesInFolder()
    {
        SaveLibrary(_library, 1);
        var folder = Path.Combine(_dir, "Prompt Library Backups ✓");

        var result = ServiceAt(Now).BackupNow(_library, folder, 30);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(folder, BackupName("2026-11-04_090507")), result.BackupPath);
        Assert.Equal(File.ReadAllBytes(_library), File.ReadAllBytes(result.BackupPath!));
        Assert.Single(ServiceAt(Now).List(folder));
    }

    [Fact]
    public void List_1000Files_NewestFirst()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0);
        for (var i = 0; i < 1000; i++)
            WriteBackup(start.AddHours(i).ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));

        var stopwatch = Stopwatch.StartNew();
        var list = ServiceAt(Now).List(_folder);
        stopwatch.Stop();

        Assert.Equal(1000, list.Count);
        Assert.Equal(start.AddHours(999), list[0].Timestamp);
        Assert.Equal(start, list[^1].Timestamp);
        Assert.True(list.Zip(list.Skip(1)).All(pair => pair.First.Timestamp > pair.Second.Timestamp));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"List took {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }
}
