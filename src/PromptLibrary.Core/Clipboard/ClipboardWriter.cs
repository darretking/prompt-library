using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Clipboard;

/// <summary>
/// Copies text through an injected setter (the tray app passes Clipboard.SetText), retrying when the
/// clipboard is busy. Text is normalised to CRLF before it is set.
/// </summary>
public class ClipboardWriter(Action<string> setText, Action<TimeSpan> sleep, int attempts = 5, int delayMs = 50)
{
    public bool TryCopy(string text)
    {
        var crlf = LineEndings.ToCrlf(text);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                setText(crlf);
                return true;
            }
            catch (Exception)
            {
                if (attempt < attempts)
                    sleep(TimeSpan.FromMilliseconds(delayMs));
            }
        }

        return false;
    }
}
