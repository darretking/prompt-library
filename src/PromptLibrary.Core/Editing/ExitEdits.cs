using System.Globalization;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Editing;

/// <summary>What is in the prompt editor but not saved yet.</summary>
public sealed record UnsavedEdit(string Title, string Description, Guid? CategoryId, string? CategoryName, string Text);

public enum ExitEditAction
{
    /// <summary>The edit is complete: save it as the Save button would.</summary>
    Save,

    /// <summary>The edit can't be saved (no title, text or category): keep it in a recovery file instead.</summary>
    Recover,
}

/// <summary>
/// Unsaved edits when the app is closed with nobody to ask (the installer closing it, Windows logging off):
/// a complete edit is saved; anything else goes to a text file in <c>Recovered\</c> so it isn't lost.
/// </summary>
public static class ExitEdits
{
    /// <summary>Save when the edit passes the editor's own checks (title, text and category), otherwise recover it.</summary>
    public static ExitEditAction Choose(UnsavedEdit edit)
        => string.IsNullOrWhiteSpace(edit.Title) || string.IsNullOrWhiteSpace(edit.Text) || edit.CategoryId is null
            ? ExitEditAction.Recover
            : ExitEditAction.Save;

    /// <summary><c>recovered-yyyy-MM-dd_HHmmss.txt</c> (local time).</summary>
    public static string RecoveryFileName(DateTime now)
        => $"recovered-{now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The recovery file's contents: each field on its own line, then the prompt text as typed (CRLF).</summary>
    public static string RecoveryText(UnsavedEdit edit, DateTime now)
    {
        static string OrNone(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();

        var header =
            $"Prompt Library recovered these unsaved changes on {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}.\n" +
            "\n" +
            $"Title: {OrNone(edit.Title)}\n" +
            $"Description: {OrNone(edit.Description)}\n" +
            $"Category: {OrNone(edit.CategoryName)}\n" +
            "\n" +
            "Text:\n";
        return LineEndings.ToCrlf(header + edit.Text + "\n");
    }

    /// <summary>
    /// Writes the recovery file into <paramref name="folder"/> (created if needed) and returns its path. A file from
    /// the same second is never overwritten: the name then ends in <c>-2</c>, <c>-3</c>…
    /// </summary>
    public static string WriteRecoveryFile(string folder, UnsavedEdit edit, DateTime now)
    {
        var name = Path.GetFileNameWithoutExtension(RecoveryFileName(now));
        var path = Path.Combine(folder, name + ".txt");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{name}-{n}.txt");

        AtomicFile.WriteAllText(path, RecoveryText(edit, now));
        return path;
    }
}
