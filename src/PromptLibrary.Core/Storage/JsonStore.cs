using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Storage;

public enum LoadStatus { Ok, Missing, Unreadable }

public record LoadResult<T>(LoadStatus Status, T? Value, string? Error);

public static class JsonStore
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The built-in encoder always escapes characters outside the BMP (emoji) as a pair of
    // backslash-u escapes (a surrogate pair). This turns each pair back into the literal
    // character. The lookbehind and the group of doubled backslashes make sure an escaped
    // backslash followed by text that merely looks like an escape is left alone.
    private static readonly Regex EscapedSurrogatePair =
        new(@"(?<!\\)((?:\\\\)*)\\u(D[89AB][0-9A-F]{2})\\u(D[C-F][0-9A-F]{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Never writes. Invalid or unreadable content gives <see cref="LoadStatus.Unreadable"/>.</summary>
    public static LoadResult<Library> LoadLibrary(string path)
        => Load(path, ParseLibrary);

    /// <summary>
    /// Normalises line endings to CRLF in place (so memory matches disk), then writes atomically. The save being
    /// replaced is kept as <see cref="PreviousSavePath"/> (unless <paramref name="keepPreviousSave"/> is false).
    /// </summary>
    public static void SaveLibrary(string path, Library library, bool keepPreviousSave = true)
    {
        foreach (var prompt in library.Prompts)
        {
            prompt.Text = LineEndings.ToCrlf(prompt.Text);
            prompt.Description = LineEndings.ToCrlf(prompt.Description);
        }
        AtomicFile.WriteAllText(path, Serialize(library), keepBackup: keepPreviousSave);
    }

    /// <summary>Where <see cref="SaveLibrary"/> keeps the save before the latest one: <c>library.json.bak</c>.</summary>
    public static string PreviousSavePath(string libraryPath) => libraryPath + ".bak";

    public static LoadResult<AppSettings> LoadSettings(string path)
        => Load(path, json => JsonSerializer.Deserialize<AppSettings>(json, Options)
            ?? throw new InvalidDataException("Settings file is empty."));

    public static void SaveSettings(string path, AppSettings settings)
        => AtomicFile.WriteAllText(path, Serialize(settings));

    public static Library ParseLibrary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v) || v != 1)
                throw new InvalidDataException("Unsupported or missing schemaVersion (expected 1).");

            return ValidateLibrary(root);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("library.json is not valid: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Checks the categories and prompts of a library-shaped JSON object (required strings, every prompt's category
    /// exists) and returns them. Does not check <c>schemaVersion</c>. Shared by library.json and team files.
    /// </summary>
    internal static Library ValidateLibrary(JsonElement root)
    {
        // Missing properties keep the POCO defaults ("") after deserialising, so check the raw JSON.
        RequireStrings(root, "categories", "name");
        RequireStrings(root, "prompts", "title", "text");

        var library = root.Deserialize<Library>(Options)
            ?? throw new InvalidDataException("library.json is empty.");
        library.Categories ??= [];
        library.Prompts ??= [];

        var categoryIds = library.Categories.Select(c => c.Id).ToHashSet();
        foreach (var prompt in library.Prompts)
        {
            prompt.Description ??= "";
            if (!categoryIds.Contains(prompt.CategoryId))
                throw new InvalidDataException($"Prompt '{prompt.Title}' references a missing category.");
        }

        return library;
    }

    internal static string Serialize<T>(T value)
        => EscapedSurrogatePair.Replace(JsonSerializer.Serialize(value, Options), m =>
            m.Groups[1].Value
            + new string([(char)Convert.ToInt32(m.Groups[2].Value, 16), (char)Convert.ToInt32(m.Groups[3].Value, 16)]));

    private static void RequireStrings(JsonElement root, string arrayName, params string[] names)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
            foreach (var name in names)
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty(name, out var value)
                    || value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"An entry in '{arrayName}' is missing its '{name}'.");
    }

    private static LoadResult<T> Load<T>(string path, Func<string, T> parse)
    {
        if (!File.Exists(path)) return new(LoadStatus.Missing, default, null);
        try
        {
            return new(LoadStatus.Ok, parse(File.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            return new(LoadStatus.Unreadable, default, ex.Message);
        }
    }
}
