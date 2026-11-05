using System.Text;

namespace PromptLibrary.Core.Storage;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const int ReplaceRetries = 3;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Writes to <c>path + ".tmp"</c> (straight through to the disk, flushed) then moves it over <paramref name="path"/>,
    /// so neither a crash nor a power cut leaves a half-written or empty file. A leftover <c>.tmp</c> from an earlier
    /// crash is overwritten. With <paramref name="keepBackup"/> the file being replaced is kept as
    /// <c>path + ".bak"</c> (one only: the save before this one); a first save makes none. If <paramref name="path"/>
    /// is briefly open elsewhere (e.g. a backup is reading it), the replace is retried up to 3 times, 50 ms apart.
    /// If the write or replace still fails, the <c>.tmp</c> is deleted and the exception is rethrown;
    /// <paramref name="path"/> is left as it was.
    /// </summary>
    public static void WriteAllText(string path, string contents, bool keepBackup = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = TempPath(path);
        try
        {
            WriteThrough(tmp, Utf8NoBom.GetBytes(contents));
            ReplaceWithRetry(tmp, path, keepBackup ? path + ".bak" : null);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>The file a save is written to first, before it replaces <paramref name="path"/>.</summary>
    public static string TempPath(string path) => path + ".tmp";

    // NTFS journals the rename but not the file's contents: without this, a power cut just after a save can leave
    // the renamed file empty or zero-filled.
    private static void WriteThrough(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    // Replacing a file that another handle has open fails with a sharing violation (IOException), or with
    // "access denied" when that handle allows delete; both clear as soon as the other reader closes it.
    private static void ReplaceWithRetry(string source, string target, string? backup)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (backup is not null && File.Exists(target))
                {
                    try
                    {
                        File.Replace(source, target, backup, ignoreMetadataErrors: true);
                        return;
                    }
                    catch (Exception ex) when (ex is PlatformNotSupportedException
                                               || (ex is IOException or UnauthorizedAccessException && attempt == ReplaceRetries))
                    {
                        // Replace isn't possible here (e.g. the file system doesn't support it): a plain move still
                        // saves, just without the .bak.
                    }
                }
                File.Move(source, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < ReplaceRetries)
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort; the original error matters more. */ }
    }
}
