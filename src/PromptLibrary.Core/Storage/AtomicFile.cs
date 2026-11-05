using System.Text;

namespace PromptLibrary.Core.Storage;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const int MoveRetries = 3;
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Writes to <c>path + ".tmp"</c> then moves it over <paramref name="path"/>, so a crash never leaves a half-written file.
    /// A leftover <c>.tmp</c> from an earlier crash is overwritten. If <paramref name="path"/> is briefly open elsewhere
    /// (e.g. a backup is reading it), the move is retried up to 3 times, 50 ms apart. If the write or move still fails,
    /// the <c>.tmp</c> is deleted and the exception is rethrown; <paramref name="path"/> is left as it was.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents, Utf8NoBom);
            MoveWithRetry(tmp, path);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    // Replacing a file that another handle has open fails with a sharing violation (IOException), or with
    // "access denied" when that handle allows delete; both clear as soon as the other reader closes it.
    private static void MoveWithRetry(string source, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MoveRetries)
            {
                Thread.Sleep(MoveRetryDelay);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort; the original error matters more. */ }
    }
}
