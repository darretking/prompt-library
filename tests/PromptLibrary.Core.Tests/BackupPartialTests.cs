using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

/// <summary>A backup is copied under a <c>.partial</c> name and renamed at the end, so a cut-off copy never looks real.</summary>
public sealed class BackupPartialTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 9, 5, 7);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly string _library;

    public BackupPartialTests()
    {
        _folder = Path.Combine(_dir, "backups");
        Directory.CreateDirectory(_folder);
        _library = Path.Combine(_dir, "library.json");
        File.WriteAllText(_library, """{"schemaVersion":1,"categories":[],"prompts":[]}""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static BackupService Service() => new(() => Now);

    private string Partial(string name, TimeSpan age)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "{ cut off");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void BackupNow_Success_LeavesOnlyTheFinalFile()
    {
        var result = Service().BackupNow(_library, _folder, 30);

        Assert.True(result.Success);
        Assert.Equal([result.BackupPath!], Directory.GetFiles(_folder));
        Assert.Equal(File.ReadAllBytes(_library), File.ReadAllBytes(result.BackupPath!));
    }

    [Fact]
    public void BackupNow_SourceLocked_LeavesNoFileBehind()
    {
        using (new FileStream(_library, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = Service().BackupNow(_library, _folder, 30);
            Assert.False(result.Success);
        }

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void PartialFiles_AreNotListedOrCountedOrPruned()
    {
        var partial = Partial("PromptLibrary-backup-2026-11-03_090000.json.partial", TimeSpan.FromMinutes(5));
        var service = Service();
        service.BackupNow(_library, _folder, 30);

        Assert.Single(service.List(_folder));
        service.Prune(_folder, 1);
        Assert.True(File.Exists(partial));
    }

    [Fact]
    public void BackupNow_DeletesLeftoverPartialsOlderThanAnHour_KeepsNewerOnes()
    {
        var old = Partial("PromptLibrary-backup-2026-11-01_090000.json.partial", TimeSpan.FromHours(2));
        var recent = Partial("PromptLibrary-backup-2026-11-04_085000.json.partial", TimeSpan.FromMinutes(10));
        var unrelated = Partial("notes.partial", TimeSpan.FromDays(3));

        Assert.True(Service().BackupNow(_library, _folder, 30).Success);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent)); // may still be copying (another run, a slow exit backup)
        Assert.True(File.Exists(unrelated)); // only our own backup names are touched
    }
}
