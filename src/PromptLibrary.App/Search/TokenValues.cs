using System.Globalization;
using PromptLibrary.Core.Templates;

namespace PromptLibrary.App.Search;

/// <summary>
/// Values of the dynamic tokens (<c>{clipboard}</c>, <c>{date}</c>, <c>{time}</c>) taken at one moment: when the search
/// window opened, or when Copy or Send was pressed in the main window. The one factory both windows use.
/// </summary>
internal sealed class TokenValues(string? clipboard) : ITokenValues
{
    public string? Clipboard { get; } = clipboard;
    public DateTime Now => DateTime.Now;
    public CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>The values now. The clipboard is only read when <paramref name="readClipboard"/> (nobody needs it otherwise).</summary>
    public static TokenValues Capture(bool readClipboard) => new(readClipboard ? ClipboardSnapshot.ReadText() : null);

    /// <summary>The values now for <paramref name="promptText"/>: the clipboard is read only when the text uses <c>{clipboard}</c>.</summary>
    public static TokenValues For(string promptText)
        => Capture(promptText.Contains("{clipboard", StringComparison.OrdinalIgnoreCase));
}
