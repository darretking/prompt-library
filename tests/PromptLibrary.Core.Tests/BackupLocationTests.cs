using PromptLibrary.Core.Backup;

namespace PromptLibrary.Core.Tests;

public sealed class BackupLocationTests
{
    private static readonly CloudFolder OneDrive = new(CloudProvider.OneDrive, "OneDrive", @"C:\Users\x\OneDrive");
    private static readonly CloudFolder Google = new(CloudProvider.GoogleDrive, "Google Drive", @"G:\My Drive");

    [Fact]
    public void IsUnavailable_FolderExists_False()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"G:\My Drive\Prompt Library Backups" };
        Assert.False(BackupLocation.IsUnavailable(@"G:\My Drive\Prompt Library Backups", existing.Contains));
    }

    [Fact]
    public void IsUnavailable_OnlyParentExists_False_BecauseTheFolderIsCreatedOnFirstBackup()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"G:\My Drive" };
        Assert.False(BackupLocation.IsUnavailable(@"G:\My Drive\Prompt Library Backups", existing.Contains));
    }

    [Fact]
    public void IsUnavailable_NeitherFolderNorParentExists_True()
    {
        Assert.True(BackupLocation.IsUnavailable(@"Z:\nope\Prompt Library Backups", _ => false));
    }

    [Fact]
    public void IsUnavailable_DriveRootThatIsMissing_True()
    {
        Assert.True(BackupLocation.IsUnavailable(@"Z:\", _ => false));
    }

    [Fact]
    public void IsUnavailable_ProbeThrows_True()
    {
        Assert.True(BackupLocation.IsUnavailable(@"\\server\share\x", _ => throw new IOException("network")));
    }

    [Fact]
    public void ProviderFor_FolderInsideCloudRoot_ReturnsDisplayName()
    {
        Assert.Equal("Google Drive",
            BackupLocation.ProviderFor(@"g:\my drive\Prompt Library Backups", [OneDrive, Google]));
    }

    [Fact]
    public void ProviderFor_SimilarPrefixButDifferentFolder_Null()
    {
        // "OneDrive - Contoso" starts with the text "OneDrive" but isn't inside the personal OneDrive folder.
        Assert.Null(BackupLocation.ProviderFor(@"C:\Users\x\OneDrive - Contoso\Backups", [OneDrive]));
    }

    [Fact]
    public void ProviderFor_LongestRootWins()
    {
        var nested = new CloudFolder(CloudProvider.Dropbox, "Dropbox", @"C:\Users\x\OneDrive\Dropbox");
        Assert.Equal("Dropbox", BackupLocation.ProviderFor(@"C:\Users\x\OneDrive\Dropbox\B", [OneDrive, nested]));
    }

    [Fact]
    public void ProviderFor_NoMatch_Null()
    {
        Assert.Null(BackupLocation.ProviderFor(@"E:\USB backups", [OneDrive, Google]));
    }

    [Fact]
    public void SameFolder_IgnoresCaseAndTrailingSeparator()
    {
        Assert.True(BackupLocation.SameFolder(@"C:\Users\x\OneDrive\", @"c:\users\x\onedrive"));
        Assert.False(BackupLocation.SameFolder(@"C:\A", @"C:\B"));
        Assert.False(BackupLocation.SameFolder(null, @"C:\B"));
    }

    [Fact]
    public void PausedMessage_WithProvider_NamesProviderNotPath()
    {
        Assert.Equal("Backups paused: Google Drive isn't available. Is it signed in?",
            BackupLocation.PausedMessage(@"G:\My Drive\Prompt Library Backups", "Google Drive"));
    }

    [Fact]
    public void PausedMessage_WithoutProvider_ShortPathOnly()
    {
        Assert.Equal(@"Backups paused: Z:\nope isn't available.", BackupLocation.PausedMessage(@"Z:\nope", null));
    }

    [Fact]
    public void PausedMessage_WithoutProvider_LongPathIsShortenedToFitTheBar()
    {
        var folder = @"D:\Some very long folder name\with more parts\Prompt Library Backups";
        var message = BackupLocation.PausedMessage(folder, null);
        Assert.Equal($"Backups paused: {BackupLocation.ShortenMiddle(folder, 40)} isn't available.", message);
        Assert.Contains("…", message);
    }

    [Fact]
    public void UnavailableText_WithAndWithoutProvider()
    {
        Assert.Equal(@"Z:\nope isn't available. Is Google Drive signed in?", BackupLocation.UnavailableText(@"Z:\nope", "Google Drive"));
        Assert.Equal(@"Z:\nope isn't available.", BackupLocation.UnavailableText(@"Z:\nope", null));
    }

    [Fact]
    public void ShortenMiddle_ShortPath_Unchanged()
    {
        Assert.Equal(@"C:\Users\x\OneDrive", BackupLocation.ShortenMiddle(@"C:\Users\x\OneDrive", 40));
    }

    [Fact]
    public void ShortenMiddle_LongPath_KeepsStartAndEnd()
    {
        var path = @"C:\Users\someone.with.a.long.name\OneDrive - Contoso Holdings Ltd\Prompt Library Backups";
        var shortened = BackupLocation.ShortenMiddle(path, 48);

        Assert.Equal(48, shortened.Length);
        Assert.Contains("…", shortened);
        Assert.StartsWith(@"C:\Users\", shortened);
        Assert.EndsWith(@"\Prompt Library Backups", shortened);
    }

    [Fact]
    public void BackupDateText_UsesDayMonthYearAndTime()
    {
        Assert.Equal("4 Nov 2026, 09:05", BackupLocation.BackupDateText(new DateTime(2026, 11, 4, 9, 5, 7)));
    }

    [Fact]
    public void PromptCountText_ReadableAndUnreadable()
    {
        Assert.Equal("12 prompts", BackupLocation.PromptCountText(12));
        Assert.Equal("1 prompt", BackupLocation.PromptCountText(1));
        Assert.Equal("Damaged — can't restore", BackupLocation.PromptCountText(-1));
    }

    private static readonly DateTime Now = new(2026, 11, 4, 14, 0, 0);

    [Fact]
    public void RelativeTimeText_Today()
    {
        Assert.Equal("Today, 09:05", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 4, 9, 5, 0), Now));
    }

    [Fact]
    public void RelativeTimeText_Yesterday()
    {
        Assert.Equal("Yesterday, 18:30", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 3, 18, 30, 0), Now));
    }

    [Fact]
    public void RelativeTimeText_MidSentence_LowerCase()
    {
        Assert.Equal("today, 09:05", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 4, 9, 5, 0), Now, midSentence: true));
        Assert.Equal("yesterday, 18:30", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 3, 18, 30, 0), Now, midSentence: true));
        Assert.Equal("1 Nov 2026, 09:05", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 1, 9, 5, 0), Now, midSentence: true));
    }

    [Fact]
    public void RelativeTimeText_Older_FullDate()
    {
        Assert.Equal("1 Nov 2026, 09:05", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 1, 9, 5, 0), Now));
    }

    [Fact]
    public void RelativeTimeText_FutureClock_FullDate()
    {
        Assert.Equal("5 Nov 2026, 09:05", BackupLocation.RelativeTimeText(new DateTime(2026, 11, 5, 9, 5, 0), Now));
    }

    [Fact]
    public void BackupRowText_JoinsTimeAndCount()
    {
        var info = new BackupInfo(@"C:\backups\b.json", new DateTime(2026, 11, 3, 18, 30, 0), 12);
        Assert.Equal("Yesterday, 18:30 · 12 prompts", BackupLocation.BackupRowText(info, Now));
        Assert.Equal("Yesterday, 18:30 · Damaged — can't restore", BackupLocation.BackupRowText(info with { PromptCount = -1 }, Now));
    }

    private const string AppDataSafety = @"C:\Users\x\AppData\Roaming\PromptLibrary\Safety backups";

    [Fact]
    public void SafetyFolder_ConfiguredAndAvailable_UsesConfigured()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"G:\My Drive" };
        Assert.Equal(@"G:\My Drive\Prompt Library Backups",
            BackupLocation.SafetyFolder(@"G:\My Drive\Prompt Library Backups", AppDataSafety, existing.Contains));
    }

    [Fact]
    public void SafetyFolder_NotConfigured_UsesAppData()
    {
        Assert.Equal(AppDataSafety, BackupLocation.SafetyFolder(null, AppDataSafety, _ => true));
    }

    [Fact]
    public void SafetyFolder_ConfiguredButUnavailable_UsesAppData()
    {
        Assert.Equal(AppDataSafety, BackupLocation.SafetyFolder(@"Z:\nope\Backups", AppDataSafety, _ => false));
    }
}
