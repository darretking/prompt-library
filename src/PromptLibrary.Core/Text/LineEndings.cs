namespace PromptLibrary.Core.Text;

public static class LineEndings
{
    /// <summary>Converts every line ending (\r\n, lone \r, lone \n) to \r\n; all other characters are untouched.</summary>
    public static string ToCrlf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }
}
