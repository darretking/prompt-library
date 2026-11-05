using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

public class BackupFailureTextTests
{
    [Fact]
    public void Classify_AccessDenied()
        => Assert.Equal(BackupFailureReason.AccessDenied, BackupFailureText.Classify(new UnauthorizedAccessException("x")));

    [Theory]
    [InlineData(0x80070070)] // ERROR_DISK_FULL
    [InlineData(0x80070027)] // ERROR_HANDLE_DISK_FULL
    public void Classify_DiskFull(uint hresult)
        => Assert.Equal(BackupFailureReason.DiskFull, BackupFailureText.Classify(new IOException("x", unchecked((int)hresult))));

    [Fact]
    public void Classify_MissingFolderOrDrive_IsUnavailable()
    {
        Assert.Equal(BackupFailureReason.Unavailable, BackupFailureText.Classify(new DirectoryNotFoundException("x")));
        Assert.Equal(BackupFailureReason.Unavailable, BackupFailureText.Classify(new DriveNotFoundException("x")));
        Assert.Equal(BackupFailureReason.Unavailable,
            BackupFailureText.Classify(new IOException("x", unchecked((int)0x80070015)))); // ERROR_NOT_READY
    }

    [Fact]
    public void Classify_AnythingElse_IsOther()
        => Assert.Equal(BackupFailureReason.Other, BackupFailureText.Classify(new IOException(@"Cannot create 'C:\x' because a file exists.")));

    [Fact]
    public void Message_WithProvider_SaysWhatToCheck()
        => Assert.Equal("Couldn't back up to OneDrive: the disk is full. Check that OneDrive is running, then try again.",
            BackupFailureText.Message(@"C:\Users\a\OneDrive\Prompt Library Backups", "OneDrive", BackupFailureReason.DiskFull));

    [Fact]
    public void Message_CustomFolder_NamesTheShortenedFolder()
    {
        var folder = @"D:\" + new string('x', 80) + @"\Backups";
        Assert.Equal($"Couldn't back up to {BackupLocation.ShortenMiddle(folder, 56)}: Windows didn't allow writing there. Choose another folder.",
            BackupFailureText.Message(folder, null, BackupFailureReason.AccessDenied));
    }

    [Theory]
    [InlineData(BackupFailureReason.Unavailable, "the folder can't be reached")]
    [InlineData(BackupFailureReason.AccessDenied, "Windows didn't allow writing there")]
    [InlineData(BackupFailureReason.DiskFull, "the disk is full")]
    [InlineData(BackupFailureReason.TimedOut, "the folder didn't respond in time")]
    [InlineData(BackupFailureReason.Other, "the backup file couldn't be written")]
    public void Reasons_AreShortAndPathFree(BackupFailureReason reason, string expected)
        => Assert.Equal(expected, BackupFailureText.Reason(reason));

    [Fact]
    public void BackupNow_MissingFolderAndParent_ReportsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "pl-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var library = Path.Combine(root, "library.json");
            File.WriteAllText(library, "{}");
            var result = new BackupService(() => DateTime.Now).BackupNow(library, Path.Combine(root, "gone", "Backups"), 30);

            Assert.False(result.Success);
            Assert.Equal(BackupFailureReason.Unavailable, result.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
