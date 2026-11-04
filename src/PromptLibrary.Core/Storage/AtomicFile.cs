using System.Text;

namespace PromptLibrary.Core.Storage;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to <c>path + ".tmp"</c> then moves it over <paramref name="path"/>, so a crash never leaves a half-written file.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }
}
