using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Backup;

public enum BackupTrigger { Daily, OnExit, Manual }

public record BackupResult(bool Success, string? BackupPath, string? Hash, string? Error);

/// <summary>PromptCount is -1 when the backup file can't be read or parsed.</summary>
public record BackupInfo(string Path, DateTime Timestamp, int PromptCount);

public class BackupService(Func<DateTime> nowLocal)
{
    public const string FilePrefix = "PromptLibrary-backup-";

    private const string TimestampFormat = "yyyy-MM-dd_HHmmss";

    private static readonly Regex BackupName =
        new(@"^PromptLibrary-backup-(?<stamp>\d{4}-\d{2}-\d{2}_\d{6})(?:-(?<n>\d+))?\.json$",
            RegexOptions.CultureInvariant);

    public bool ShouldBackup(BackupTrigger trigger, string currentHash, AppSettings s)
    {
        if (trigger == BackupTrigger.Manual) return true;
        if (currentHash == s.LastBackedUpLibraryHash) return false;
        if (trigger == BackupTrigger.OnExit) return true;

        if (s.LastBackupAt is null) return true;

        // A last backup "in the future" means the clock was moved back; treat it as due rather than waiting.
        var sinceLast = new DateTimeOffset(nowLocal()) - s.LastBackupAt.Value;
        return sinceLast < TimeSpan.Zero || sinceLast >= TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Copies the library file byte for byte into the folder, then prunes (always keeping at least the new backup).
    /// The result's Hash is of the copied file. Never throws for IO problems.
    /// </summary>
    public BackupResult BackupNow(string libraryPath, string backupFolder, int keep)
    {
        string target;
        try
        {
            if (!File.Exists(libraryPath))
                return Failure($"The library file '{libraryPath}' does not exist, so there is nothing to back up.");

            var parent = Path.GetDirectoryName(Path.GetFullPath(backupFolder));
            if (!Directory.Exists(backupFolder))
            {
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                    return Failure($"Backup folder '{backupFolder}' is not available.");
                Directory.CreateDirectory(backupFolder);
            }

            target = UniqueTargetPath(backupFolder);
            File.Copy(libraryPath, target, overwrite: false);
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return Failure(ex.Message);
        }

        string hash;
        try
        {
            hash = LibraryHash.OfFile(target);
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return Failure(ex.Message);
        }

        try { Prune(backupFolder, keep); }
        catch (Exception ex) when (IsIoProblem(ex)) { /* The backup itself succeeded; pruning is retried next time. */ }

        return new BackupResult(true, target, hash, null);
    }

    /// <summary>
    /// Deletes the oldest backups beyond <paramref name="keep"/> (values below 1 count as 1, so the newest backup is never deleted).
    /// Only files with the exact backup name pattern are touched.
    /// </summary>
    public void Prune(string backupFolder, int keep)
    {
        if (!Directory.Exists(backupFolder)) return;

        var doomed = Directory.EnumerateFiles(backupFolder)
            .Select(file => (File: file, Key: ParseName(file)))
            .Where(x => x.Key is not null)
            .OrderByDescending(x => x.Key!.Value.Timestamp)
            .ThenByDescending(x => x.Key!.Value.Suffix)
            .Skip(Math.Max(keep, 1))
            .Select(x => x.File);

        foreach (var file in doomed)
            File.Delete(file);
    }

    public IReadOnlyList<BackupInfo> List(string backupFolder)
    {
        if (!Directory.Exists(backupFolder)) return [];

        try
        {
            return Directory.EnumerateFiles(backupFolder)
                .Select(file => (File: file, Key: ParseName(file)))
                .Where(x => x.Key is not null)
                .OrderByDescending(x => x.Key!.Value.Timestamp)
                .ThenByDescending(x => x.Key!.Value.Suffix)
                .Select(x => new BackupInfo(x.File, x.Key!.Value.Timestamp, CountPrompts(x.File)))
                .ToList();
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// Validates the chosen backup, saves a safety copy of the current library into <paramref name="safetyFolder"/>
    /// (pruned to <paramref name="keep"/>), then replaces the library with the backup's original text. Any failure
    /// before the final replace leaves the library untouched. The folder holding <paramref name="backupPath"/> is
    /// only read, never written or pruned (unless it is also the safety folder).
    /// The result's BackupPath is the safety copy of the previous library (null if there was none).
    /// </summary>
    public BackupResult Restore(string backupPath, string libraryPath, string safetyFolder, int keep)
    {
        string text;
        try
        {
            text = File.ReadAllText(backupPath);
            JsonStore.ParseLibrary(text);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException || IsIoProblem(ex))
        {
            return Failure("The selected backup can't be restored: " + ex.Message);
        }

        string? safetyCopy = null;
        if (File.Exists(libraryPath))
        {
            var pre = BackupNow(libraryPath, safetyFolder, keep);
            if (!pre.Success) return Failure("Could not back up the current library first: " + pre.Error);
            safetyCopy = pre.BackupPath;
        }

        try
        {
            AtomicFile.WriteAllText(libraryPath, text);
            return new BackupResult(true, safetyCopy, LibraryHash.OfFile(libraryPath), null);
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return Failure("Could not write the restored library: " + ex.Message);
        }
    }

    private string UniqueTargetPath(string backupFolder)
    {
        var stamp = nowLocal().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var candidate = Path.Combine(backupFolder, $"{FilePrefix}{stamp}.json");
        for (var n = 1; File.Exists(candidate); n++)
            candidate = Path.Combine(backupFolder, $"{FilePrefix}{stamp}-{n}.json");
        return candidate;
    }

    private static (DateTime Timestamp, int Suffix)? ParseName(string path)
    {
        var match = BackupName.Match(Path.GetFileName(path));
        if (!match.Success) return null;
        if (!DateTime.TryParseExact(match.Groups["stamp"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
            return null;
        var suffix = match.Groups["n"].Success && int.TryParse(match.Groups["n"].Value, out var n) ? n : 0;
        return (timestamp, suffix);
    }

    private static int CountPrompts(string path)
    {
        try
        {
            return JsonStore.ParseLibrary(File.ReadAllText(path)).Prompts.Count;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException || IsIoProblem(ex))
        {
            return -1;
        }
    }

    private static bool IsIoProblem(Exception ex)
        => ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private static BackupResult Failure(string error) => new(false, null, null, error);
}
