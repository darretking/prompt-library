using PromptLibrary.Core.Text;

namespace PromptLibrary.Core.Clipboard;

/// <summary>
/// Copies text through an injected setter (the tray app passes Clipboard.SetText), retrying when the
/// clipboard is busy. Text is normalised to CRLF before it is set.
/// </summary>
public class ClipboardWriter(Action<string> setText, Action<TimeSpan> sleep, int attempts = 5, int delayMs = 50)
{
    private readonly int _attempts = attempts >= 1
        ? attempts
        : throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "At least one attempt is required.");

    private readonly int _delayMs = delayMs >= 0
        ? delayMs
        : throw new ArgumentOutOfRangeException(nameof(delayMs), delayMs, "The delay can't be negative.");

    public bool TryCopy(string text)
    {
        var crlf = LineEndings.ToCrlf(text);

        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            try
            {
                setText(crlf);
                return true;
            }
            catch (Exception)
            {
                if (attempt < _attempts)
                    sleep(TimeSpan.FromMilliseconds(_delayMs));
            }
        }

        return false;
    }
}
