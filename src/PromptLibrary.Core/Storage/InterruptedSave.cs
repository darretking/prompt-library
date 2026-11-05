namespace PromptLibrary.Core.Storage;

/// <summary>Which copy <see cref="InterruptedSave.Recover"/> put back.</summary>
public enum InterruptedSaveSource
{
    /// <summary>Nothing to do: the library exists, or there's no readable copy (e.g. first run).</summary>
    None,

    /// <summary>The save that was being written (<c>library.json.tmp</c>), which was flushed before the replace.</summary>
    NewSave,

    /// <summary>The save before it (<c>library.json.bak</c>).</summary>
    PreviousSave,
}

/// <summary>
/// Saving with a previous-save copy replaces <c>library.json</c> in two renames (library.json → .bak, then .tmp →
/// library.json). If the PC dies between them, library.json is missing while both copies survive. Starting up then
/// must not seed a new library (that write would overwrite the .tmp, and the next save the .bak).
/// </summary>
public static class InterruptedSave
{
    /// <summary>
    /// If <paramref name="libraryPath"/> is missing, puts back the newest readable copy: the new save (moved into
    /// place) or else the previous save (copied, so the .bak stays). Unreadable copies are left untouched.
    /// File errors are not caught: the caller must not seed a new library over a copy it failed to restore.
    /// </summary>
    public static InterruptedSaveSource Recover(string libraryPath)
    {
        if (File.Exists(libraryPath)) return InterruptedSaveSource.None;

        var newSave = AtomicFile.TempPath(libraryPath);
        if (IsReadableLibrary(newSave))
        {
            File.Move(newSave, libraryPath);
            return InterruptedSaveSource.NewSave;
        }

        var previousSave = JsonStore.PreviousSavePath(libraryPath);
        if (IsReadableLibrary(previousSave))
        {
            File.Copy(previousSave, libraryPath);
            return InterruptedSaveSource.PreviousSave;
        }

        return InterruptedSaveSource.None;
    }

    private static bool IsReadableLibrary(string path) => JsonStore.LoadLibrary(path).Status == LoadStatus.Ok;
}
