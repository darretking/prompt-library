using System.Security.Cryptography;

namespace PromptLibrary.Core.Backup;

public static class LibraryHash
{
    /// <summary>Lowercase hex SHA-256 of the file's bytes.</summary>
    public static string OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
