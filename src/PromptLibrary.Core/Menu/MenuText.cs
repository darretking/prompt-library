namespace PromptLibrary.Core.Menu;

public static class MenuText
{
    public const int MaxLength = 60;

    /// <summary>WinForms menus treat '&amp;' as an accelerator prefix; doubling shows a literal one.</summary>
    public static string Escape(string s) => s.Replace("&", "&&");

    /// <summary>Keeps at most <see cref="MaxLength"/> UTF-16 units, never splitting a surrogate pair.</summary>
    public static string Truncate(string s)
    {
        if (s.Length <= MaxLength) return s;
        var cut = MaxLength - 1;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    public static string ForMenu(string s) => Escape(Truncate(s));
}
