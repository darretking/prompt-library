namespace PromptLibrary.Core.Backup;

/// <summary>Why a backup failed, in terms a user can act on.</summary>
public enum BackupFailureReason
{
    Other,
    Unavailable,
    AccessDenied,
    DiskFull,
    TimedOut,
}

/// <summary>
/// Turns a backup failure into one short sentence for the backup location picker, e.g.
/// <c>Couldn't back up to OneDrive: the disk is full. Check that OneDrive is running, then try again.</c>
/// No raw exception text and no long paths.
/// </summary>
public static class BackupFailureText
{
    private const int FolderDisplayLength = 56;

    // Win32 error codes carried in an IOException's HResult (0x8007xxxx).
    private const int ErrorNotReady = 21;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorDiskFull = 112;

    public static BackupFailureReason Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => BackupFailureReason.AccessDenied,
        DirectoryNotFoundException or DriveNotFoundException => BackupFailureReason.Unavailable,
        IOException io => (io.HResult & 0xFFFF) switch
        {
            ErrorDiskFull or ErrorHandleDiskFull => BackupFailureReason.DiskFull,
            ErrorNotReady or ErrorBadNetPath or ErrorBadNetName => BackupFailureReason.Unavailable,
            _ => BackupFailureReason.Other,
        },
        _ => BackupFailureReason.Other,
    };

    public static string Reason(BackupFailureReason reason) => reason switch
    {
        BackupFailureReason.Unavailable => "the folder can't be reached",
        BackupFailureReason.AccessDenied => "Windows didn't allow writing there",
        BackupFailureReason.DiskFull => "the disk is full",
        BackupFailureReason.TimedOut => "the folder didn't respond in time",
        _ => "the backup file couldn't be written",
    };

    /// <summary>With a cloud provider the sentence also says what to check; a chosen folder is named (shortened).</summary>
    public static string Message(string folder, string? providerName, BackupFailureReason reason)
        => providerName is null
            ? $"Couldn't back up to {BackupLocation.ShortenMiddle(folder, FolderDisplayLength)}: {Reason(reason)}."
            : $"Couldn't back up to {providerName}: {Reason(reason)}. Check that {providerName} is running, then try again.";
}
