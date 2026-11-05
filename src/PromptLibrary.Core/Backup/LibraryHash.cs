using System.Security.Cryptography;

namespace PromptLibrary.Core.Backup;

public static class LibraryHash
{
    /// <summary>Lowercase hex SHA-256 of the file's bytes. Reads without blocking a save that replaces the file.</summary>
    public static string OfFile(string path)
    {
        using var stream = BackupService.OpenLibraryForCopy(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
