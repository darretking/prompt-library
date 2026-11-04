using System.Text.RegularExpressions;

namespace PromptLibrary.Core.Hotkeys;

/// <summary>Values match the Win32 <c>MOD_*</c> flags used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global shortcut such as "Ctrl+Alt+P": one or more modifiers plus a single key.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    // A letter, a digit, or F1-F24.
    private static readonly Regex KeyPattern =
        new(@"^(?:[A-Z0-9]|F(?:[1-9]|1[0-9]|2[0-4]))$", RegexOptions.CultureInvariant);

    public static bool TryParse(string text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+').Select(p => p.Trim()).ToArray();
        var modifiers = HotkeyModifiers.None;

        // Every part but the last must be a modifier; the last must be the key.
        foreach (var part in parts[..^1])
        {
            if (!TryParseModifier(part, out var modifier) || modifiers.HasFlag(modifier))
                return false;
            modifiers |= modifier;
        }

        var key = parts[^1].ToUpperInvariant();
        if (!KeyPattern.IsMatch(key))
            return false;

        // A bare key would hijack normal typing, and so would Shift alone.
        if (modifiers == HotkeyModifiers.None || modifiers == HotkeyModifiers.Shift)
            return false;

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>Canonical form: Ctrl+Alt+Shift+Win+Key.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    private static bool TryParseModifier(string name, out HotkeyModifiers modifier)
    {
        switch (name.ToLowerInvariant())
        {
            case "ctrl" or "control": modifier = HotkeyModifiers.Ctrl; return true;
            case "alt": modifier = HotkeyModifiers.Alt; return true;
            case "shift": modifier = HotkeyModifiers.Shift; return true;
            case "win" or "windows": modifier = HotkeyModifiers.Win; return true;
            default: modifier = HotkeyModifiers.None; return false;
        }
    }
}
