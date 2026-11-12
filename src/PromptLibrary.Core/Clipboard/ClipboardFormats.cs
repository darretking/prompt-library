namespace PromptLibrary.Core.Clipboard;

/// <summary>Decides whether the clipboard can be put back after a paste.</summary>
public static class ClipboardFormats
{
    // The text formats Windows keeps for plain text (it fills in the others itself from UnicodeText).
    private static readonly HashSet<string> TextFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnicodeText", "Text", "OEMText", "Locale", "System.String",
    };

    /// <summary>
    /// True when every format on the clipboard is a plain-text one (so putting the text back loses nothing). False for an
    /// empty clipboard or when anything else (an image, files, rich text, HTML…) is on it.
    /// </summary>
    public static bool IsTextOnly(IEnumerable<string> formats)
    {
        var any = false;
        foreach (var format in formats)
        {
            any = true;
            if (!TextFormats.Contains(format)) return false;
        }
        return any;
    }
}
