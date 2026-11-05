using System.Globalization;

namespace PromptLibrary.Core.Storage;

/// <summary>Appends unexpected errors to <c>crash.log</c> so there is something to look at after a crash.</summary>
public static class CrashLog
{
    private static readonly Lock Gate = new();

    /// <summary>Appends the time and <paramref name="exception"/> in full. Best effort: never throws.</summary>
    public static void Append(string path, Exception exception, DateTime now)
    {
        try
        {
            var entry = $"=== {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ==={Environment.NewLine}"
                        + exception + Environment.NewLine + Environment.NewLine;
            lock (Gate) // the UI thread and a background thread may crash at the same time
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(path, entry);
            }
        }
        catch (Exception)
        {
            // Crashing already; a log that can't be written must not make it worse.
        }
    }
}
