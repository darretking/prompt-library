using System.Globalization;
using System.Text.RegularExpressions;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;

namespace PromptLibrary.Core.Team;

/// <summary>Whether a guarded save went through.</summary>
public enum TeamSaveStatus { Saved, ChangedByOther, Failed }

/// <summary>The outcome of a save or restore. <c>NewHash</c> is set when saved; <c>Message</c> when failed.</summary>
public sealed record TeamSaveResult(TeamSaveStatus Status, string? NewHash, string? Message);

/// <summary>One older version of the team file, or a conflict copy made by file sync.</summary>
public sealed record TeamHistoryItem(string Path, DateTimeOffset SavedAt, int PromptCount, bool IsConflictCopy);

/// <summary>The outcome of starting a team library. <c>ExistingName</c> is the name of a team file already in the folder.</summary>
public sealed record TeamCreateResult(bool Created, string FilePath, string? ExistingName, string? Message);

/// <summary>
/// Writes the shared team file safely: refuses to overwrite changes made by someone else since it was read, keeps
/// the last 30 older versions in a history folder next to it, and can start a new team library.
/// </summary>
public sealed class TeamWriter(Func<DateTimeOffset> now)
{
    /// <summary>The folder (next to the team file) that holds older versions.</summary>
    public const string HistoryFolderName = "Prompt Library history";

    private const string HistoryPrefix = "team-prompts ";
    private const int HistoryLimit = 30;
    private const string StampFormat = "yyyy-MM-dd_HHmmss";

    private static readonly Regex HistoryName =
        new(@"^team-prompts (?<stamp>\d{4}-\d{2}-\d{2}_\d{6})( \(\d+\))?\.json$", RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the file can be opened for writing. Opens it and closes it again without changing it.
    /// False on any IO or permission error, including a missing file.
    /// </summary>
    public static bool CanWrite(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Saves the team over <paramref name="filePath"/> only if the file still has the hash it had when it was read
    /// (<paramref name="expectedHash"/>; null or empty means there should be no file yet). The current file is first
    /// copied into the history folder. <see cref="TeamLibrary.UpdatedAt"/> is set to now. Never throws for IO errors.
    /// </summary>
    public TeamSaveResult Save(string filePath, TeamLibrary team, string? expectedHash)
    {
        try
        {
            var exists = File.Exists(filePath);
            string? current = null;
            if (exists)
            {
                current = File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(expectedHash) || TeamFile.Hash(current) != expectedHash)
                    return new TeamSaveResult(TeamSaveStatus.ChangedByOther, null, "The team library was changed by someone else.");
            }
            else if (!string.IsNullOrEmpty(expectedHash))
            {
                return new TeamSaveResult(TeamSaveStatus.ChangedByOther, null, "The team library file is gone.");
            }

            var saveTime = now();
            if (current is not null) CopyToHistory(filePath, current, saveTime);

            team.UpdatedAt = saveTime;
            var text = TeamFile.Serialize(team);
            AtomicFile.WriteAllText(filePath, text);
            return new TeamSaveResult(TeamSaveStatus.Saved, TeamFile.Hash(text), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(ex);
        }
    }

    /// <summary>
    /// Conflict copies first (see <see cref="FindConflictCopies"/>), then the history copies, newest first. Files
    /// that can't be read as a team file are left out. Never throws for IO errors.
    /// </summary>
    public IReadOnlyList<TeamHistoryItem> ListHistory(string filePath)
    {
        var result = new List<TeamHistoryItem>();
        try
        {
            foreach (var path in FindConflictCopies(filePath))
                if (TryCount(path) is { } count)
                    result.Add(new TeamHistoryItem(path, File.GetLastWriteTimeUtc(path), count, true));

            var historyDir = Path.Combine(DirectoryOf(filePath), HistoryFolderName);
            if (Directory.Exists(historyDir))
            {
                var items = new List<TeamHistoryItem>();
                foreach (var path in Directory.EnumerateFiles(historyDir))
                {
                    var m = HistoryName.Match(Path.GetFileName(path));
                    if (!m.Success || TryCount(path) is not { } count) continue;
                    var savedAt = DateTime.TryParseExact(m.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var local)
                        ? new DateTimeOffset(local)
                        : File.GetLastWriteTimeUtc(path);
                    items.Add(new TeamHistoryItem(path, savedAt, count, false));
                }
                result.AddRange(items.OrderByDescending(i => i.SavedAt).ThenByDescending(i => Path.GetFileNameWithoutExtension(i.Path), StringComparer.Ordinal));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Return what was found.
        }
        return result;
    }

    /// <summary>Writes an older version back through <see cref="Save"/>, so the current version also gets a history copy.</summary>
    public TeamSaveResult Restore(string filePath, TeamHistoryItem item, string? expectedHash)
    {
        TeamLibrary old;
        try
        {
            old = TeamFile.Parse(File.ReadAllText(item.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failed(ex);
        }
        return Save(filePath, old, expectedHash);
    }

    /// <summary>
    /// Starts a team library as <see cref="TeamFile.FileName"/> in <paramref name="folder"/>, holding copies (with new
    /// ids) of the chosen categories of <paramref name="seedFrom"/> and optionally the business examples. Refuses when
    /// the folder already has a team file, returning its name. Same-named categories from both sources are merged.
    /// </summary>
    public TeamCreateResult Create(string folder, string name, Library? seedFrom,
        IReadOnlyCollection<Guid> seedCategoryIds, bool addBusinessExamples)
    {
        var filePath = Path.Combine(folder, TeamFile.FileName);
        try
        {
            if (File.Exists(filePath))
            {
                string? existing = null;
                try { existing = TeamFile.Parse(File.ReadAllText(filePath)).Name; }
                catch (InvalidDataException) { }
                return new TeamCreateResult(false, filePath, existing,
                    "There is already a team library in this folder.");
            }

            var createdAt = now();
            var content = new Library();
            if (seedFrom is not null)
            {
                var chosen = seedFrom.Categories.Where(c => seedCategoryIds.Contains(c.Id)).ToList();
                AddCopies(content, chosen, seedFrom.Prompts, createdAt);
            }
            if (addBusinessExamples)
            {
                var examples = TeamSeedLibrary.Create(createdAt);
                AddCopies(content, examples.Categories, examples.Prompts, createdAt);
            }

            var team = new TeamLibrary { Name = name, SchemaVersion = 1, Content = content };
            var saved = Save(filePath, team, "");
            return saved.Status == TeamSaveStatus.Saved
                ? new TeamCreateResult(true, filePath, null, null)
                : new TeamCreateResult(false, filePath, null, saved.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TeamCreateResult(false, filePath, null, FailedMessage(ex));
        }
    }

    /// <summary>
    /// Sibling files named <c>team-prompts*.json</c> other than the team file itself: OneDrive's
    /// <c>team-prompts-PCNAME.json</c> and Dropbox's <c>team-prompts (… conflicted copy …).json</c>. Temporary
    /// (<c>.tmp</c>) and backup (<c>.bak</c>) files don't match. Sorted by name.
    /// </summary>
    public static IReadOnlyList<string> FindConflictCopies(string filePath)
    {
        try
        {
            var dir = DirectoryOf(filePath);
            if (!Directory.Exists(dir)) return [];
            var self = Path.GetFileName(filePath);
            return Directory.EnumerateFiles(dir)
                .Where(p =>
                {
                    var n = Path.GetFileName(p);
                    return n.StartsWith("team-prompts", StringComparison.OrdinalIgnoreCase)
                           && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(n, self, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Copies categories (and their prompts) into the target with fresh ids; a category whose name the target already has is merged into it.
    private static void AddCopies(Library target, IEnumerable<Category> categories, IReadOnlyCollection<Prompt> prompts, DateTimeOffset at)
    {
        foreach (var category in categories)
        {
            var into = target.Categories.FirstOrDefault(c => string.Equals(c.Name, category.Name, StringComparison.CurrentCultureIgnoreCase));
            if (into is null)
            {
                into = new Category { Id = Guid.NewGuid(), Name = category.Name };
                target.Categories.Add(into);
            }
            foreach (var p in prompts.Where(p => p.CategoryId == category.Id))
                target.Prompts.Add(new Prompt
                {
                    Id = Guid.NewGuid(),
                    Title = p.Title,
                    Description = p.Description,
                    CategoryId = into.Id,
                    Text = p.Text,
                    CreatedAt = at,
                    UpdatedAt = at,
                });
        }
    }

    // Copies the current file text into the history folder, then removes all but the newest 30.
    private static void CopyToHistory(string filePath, string currentText, DateTimeOffset at)
    {
        var historyDir = Path.Combine(DirectoryOf(filePath), HistoryFolderName);
        Directory.CreateDirectory(historyDir);

        var stamp = at.ToLocalTime().ToString(StampFormat, CultureInfo.InvariantCulture);
        var target = Path.Combine(historyDir, $"{HistoryPrefix}{stamp}.json");
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(historyDir, $"{HistoryPrefix}{stamp} ({n}).json");
        AtomicFile.WriteAllText(target, currentText);

        var old = Directory.EnumerateFiles(historyDir)
            .Where(p => HistoryName.IsMatch(Path.GetFileName(p)))
            .OrderByDescending(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
            .Skip(HistoryLimit);
        foreach (var path in old)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort. */ }
        }
    }

    private static int? TryCount(string path)
    {
        try { return TeamFile.Parse(File.ReadAllText(path)).Content.Prompts.Count; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    private static string DirectoryOf(string filePath) => Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";

    private static TeamSaveResult Failed(Exception ex) => new(TeamSaveStatus.Failed, null, FailedMessage(ex));

    private static string FailedMessage(Exception ex) => "Couldn't save the team library: " + ex.Message;
}
