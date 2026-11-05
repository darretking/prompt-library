using PromptLibrary.Core.Backup;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Tests;

/// <summary>A backup reading library.json must never make a save fail, and a brief lock must not either.</summary>
public sealed class FileSharingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public FileSharingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task Backup_WhileLibraryOpenForReplace_DoesNotBlockSave()
    {
        var library = Path.Combine(_dir, "library.json");
        File.WriteAllText(library, "old");

        // A backup reads the library the way BackupNow does (sharing read, write and delete) and finishes ~60 ms
        // later. Windows still refuses to replace a file while any handle is open, so the save must wait it out
        // (retry) rather than fail.
        var reading = new ManualResetEventSlim();
        var backup = Task.Run(() =>
        {
            using var stream = BackupService.OpenLibraryForCopy(library);
            reading.Set();
            Thread.Sleep(60);
        });
        reading.Wait();

        AtomicFile.WriteAllText(library, "new");
        await backup;

        Assert.Equal("new", File.ReadAllText(library));
    }

    [Fact]
    public void Backup_ReadsLibraryWhileSaveHasItOpenForWriting()
    {
        var library = Path.Combine(_dir, "library.json");
        File.WriteAllText(library, "{}");
        using var writer = new FileStream(library, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        using var reading = BackupService.OpenLibraryForCopy(library); // doesn't throw a sharing violation
        Assert.Equal(2, reading.Length);
    }

    [Fact]
    public void BackupNow_CopiesWhileAnotherReaderSharesTheFile()
    {
        var library = Path.Combine(_dir, "library.json");
        File.WriteAllText(library, "{}");
        using var other = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var result = new BackupService(() => new DateTime(2026, 11, 4, 9, 0, 0)).BackupNow(library, Path.Combine(_dir, "b"), 5);

        Assert.True(result.Success, result.Error);
        Assert.Equal("{}", File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public async Task AtomicFile_RetriesTransientLock()
    {
        var target = Path.Combine(_dir, "library.json");
        File.WriteAllText(target, "old");

        var locked = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var exclusive = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
            locked.Set();
            Thread.Sleep(60);
        });
        locked.Wait();

        AtomicFile.WriteAllText(target, "new");
        await holder;

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void AtomicFile_LockThatOutlastsRetries_ThrowsAndDeletesTmp()
    {
        var target = Path.Combine(_dir, "library.json");
        File.WriteAllText(target, "old");

        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Windows reports a file that can't be replaced as "access denied" or a sharing violation.
            var error = Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(target, "new"));
            Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().Name);
        }

        Assert.Equal("old", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }
}
