using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PromptLibrary.Core.Hotkeys;

namespace PromptLibrary.App.Views;

/// <summary>
/// A read-only box showing the global shortcut. <see cref="StartListening"/> (or Enter/Space on the box) puts it
/// in listening mode: the border turns Brand and it says <c>Press the new shortcut… Esc to cancel</c>.
/// While listening:
/// <list type="bullet">
/// <item>Tab and Shift+Tab are not captured: they leave the box and stop listening.</item>
/// <item>Esc stops listening and keeps the old shortcut (it doesn't close the window).</item>
/// <item>A combination that can't be a shortcut raises <see cref="Rejected"/> and keeps listening.</item>
/// <item>A valid combination stops listening and raises <see cref="ShortcutPressed"/>; the owner registers it and
/// sets <see cref="Shortcut"/> if that worked.</item>
/// </list>
/// Shared by Settings and the onboarding wizard.
/// </summary>
public sealed class ShortcutBox : TextBox
{
    public const string RejectedHint = "Use Ctrl or Alt with a letter or number, like Ctrl+Alt+P.";
    private const string ListeningText = "Press the new shortcut… Esc to cancel";

    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(string), typeof(ShortcutBox),
        new FrameworkPropertyMetadata("", (d, _) => ((ShortcutBox)d).ShowCurrent()));

    private bool _isListening;

    public ShortcutBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    /// <summary>The shortcut in use, e.g. <c>Ctrl+Alt+P</c>.</summary>
    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public bool IsListening => _isListening;

    /// <summary>Listening started (true) or stopped (false); the owner lets go of / takes back the global hotkey.</summary>
    public event EventHandler<bool>? ListeningChanged;

    /// <summary>A valid shortcut was pressed while listening (canonical text, e.g. <c>Ctrl+Alt+K</c>).</summary>
    public event EventHandler<string>? ShortcutPressed;

    /// <summary>Keys were pressed that can't be a shortcut; the argument is the hint to show.</summary>
    public event EventHandler<string>? Rejected;

    public void StartListening()
    {
        Focus();
        if (_isListening) return;
        _isListening = true;
        ShowCurrent();
        ListeningChanged?.Invoke(this, true);
    }

    public void StopListening()
    {
        if (!_isListening) return;
        _isListening = false;
        ShowCurrent();
        ListeningChanged?.Invoke(this, false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (!_isListening)
        {
            if (key is Key.Enter or Key.Space && modifiers == ModifierKeys.None)
            {
                StartListening();
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
            return;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            StopListening(); // focus moves on as usual
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopListening();
            return;
        }
        if (e.IsRepeat) return;

        if (IsModifierKey(key))
        {
            Text = ModifierText(modifiers) + "…"; // e.g. "Ctrl+Alt+…" while the user is still pressing
            return;
        }

        var keyText = KeyText(key);
        if (keyText is not null && HotkeyGesture.TryParse(ModifierText(modifiers) + keyText, out var gesture))
        {
            StopListening();
            ShortcutPressed?.Invoke(this, gesture!.ToString());
        }
        else
        {
            ShowCurrent();
            Rejected?.Invoke(this, RejectedHint);
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        // Letting go of the modifiers without a key goes back to the listening text.
        if (_isListening && Keyboard.Modifiers == ModifierKeys.None)
            ShowCurrent();
        base.OnPreviewKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        StopListening();
        base.OnLostKeyboardFocus(e);
    }

    private void ShowCurrent()
    {
        Text = _isListening ? ListeningText : Shortcut;
        if (_isListening)
        {
            SetResourceReference(BorderBrushProperty, "Brush.Brand");
            SetResourceReference(ForegroundProperty, "Brush.Muted");
        }
        else
        {
            ClearValue(BorderBrushProperty);
            ClearValue(ForegroundProperty);
        }
    }

    private static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string ModifierText(ModifierKeys modifiers)
    {
        var text = "";
        if (modifiers.HasFlag(ModifierKeys.Control)) text += "Ctrl+";
        if (modifiers.HasFlag(ModifierKeys.Alt)) text += "Alt+";
        if (modifiers.HasFlag(ModifierKeys.Shift)) text += "Shift+";
        if (modifiers.HasFlag(ModifierKeys.Windows)) text += "Win+";
        return text;
    }

    /// <summary>"A"–"Z", "0"–"9" (top row) or "F1"–"F24"; null for any other key.</summary>
    private static string? KeyText(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        return null;
    }
}
