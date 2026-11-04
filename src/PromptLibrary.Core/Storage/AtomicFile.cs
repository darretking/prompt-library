using System.Text;

namespace PromptLibrary.Core.Storage;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes to <c>path + ".tmp"</c> then moves it over <paramref name="path"/>, so a crash never leaves a half-written file.
    /// A leftover <c>.tmp</c> from an earlier crash is overwritten. If the write or move fails, the <c>.tmp</c> is deleted
    /// and the exception is rethrown; <paramref name="path"/> is left as it was.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort; the original error matters more. */ }
    }
}
