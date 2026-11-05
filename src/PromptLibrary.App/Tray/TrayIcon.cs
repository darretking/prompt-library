using System.Runtime.InteropServices;
using PromptLibrary.Core.Menu;
using Forms = System.Windows.Forms;

namespace PromptLibrary.App.Tray;

/// <summary>
/// The notification-area icon and the quick menu (categories → prompts, then Settings, Open and Exit).
/// The same menu is shown for a tray click and for the global hotkey.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int TooltipMaxLength = 127;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon _icon;

    // A hidden top-level window that we bring to the foreground before showing the menu.
    // While our app owns the foreground, clicking anywhere else deactivates it and closes the menu.
    private readonly Forms.NativeWindow _menuOwner = new();

    // The window that had focus when the hotkey was pressed; focus goes back there after a copy
    // so Ctrl+V pastes into it.
    private IntPtr _returnFocusTo;

    // The same window, kept for the whole time the shortcut menu is open (zero for a tray click). It travels with a
    // picked prompt, so pasting never depends on whether the menu's Closed event (which gives focus back and clears
    // _returnFocusTo) runs before or after the item's Click. Both give focus to this same window, so they never disagree.
    private IntPtr _pasteTarget;

    /// <summary>A prompt was picked; <see cref="PromptPick.ReturnTo"/> is set when the menu came from the global shortcut.</summary>
    public event EventHandler<PromptPick>? PromptClicked;
    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? DoubleClicked;

    public TrayIcon(string tooltip)
    {
        _menuOwner.CreateHandle(new Forms.CreateParams());
        _menu.Closed += OnMenuClosed;

        _icon = LoadAppIcon();
        _notifyIcon = new Forms.NotifyIcon { Icon = _icon, Visible = true };
        ToolTipText = tooltip;
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right)
                ShowMenuAtCursor();
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            _menu.Close();
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        };
    }

    public string ToolTipText
    {
        set => _notifyIcon.Text = value.Length > TooltipMaxLength ? value[..TooltipMaxLength] : value;
    }

    public void Rebuild(IReadOnlyList<QuickMenuCategory> categories)
    {
        // Dispose the old items so their handles do not pile up on every save.
        var old = _menu.Items.Cast<Forms.ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old) item.Dispose();

        if (categories.Count == 0)
        {
            _menu.Items.Add(new Forms.ToolStripMenuItem("No prompts yet") { Enabled = false });
        }

        foreach (var category in categories)
        {
            var categoryItem = new Forms.ToolStripMenuItem(MenuText.ForMenu(category.Name));
            foreach (var prompt in category.Prompts)
            {
                var id = prompt.Id;
                var promptItem = new Forms.ToolStripMenuItem(MenuText.ForMenu(prompt.Title));
                promptItem.Click += (_, _) =>
                {
                    PromptClicked?.Invoke(this, new PromptPick(id, _pasteTarget));
                    ReturnFocus();
                };
                categoryItem.DropDownItems.Add(promptItem);
            }
            _menu.Items.Add(categoryItem);
        }

        _menu.Items.Add(new Forms.ToolStripSeparator());

        var settings = new Forms.ToolStripMenuItem("Settings…");
        settings.Click += (_, _) =>
        {
            _returnFocusTo = IntPtr.Zero; // the Settings window takes focus instead
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        _menu.Items.Add(settings);

        var open = new Forms.ToolStripMenuItem("Open Prompt Library…");
        open.Click += (_, _) =>
        {
            _returnFocusTo = IntPtr.Zero; // the main window takes focus instead
            OpenRequested?.Invoke(this, EventArgs.Empty);
        };
        _menu.Items.Add(open);

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            ReturnFocus();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        };
        _menu.Items.Add(exit);
    }

    /// <summary>
    /// Shows the quick menu at the mouse pointer. With <paramref name="returnFocusAfterwards"/> (the hotkey
    /// case) the window that was in front gets focus back once the menu closes, so a paste lands there.
    /// </summary>
    public void ShowMenuAtCursor(bool returnFocusAfterwards = false)
    {
        _returnFocusTo = returnFocusAfterwards ? GetForegroundWindow() : IntPtr.Zero;
        _pasteTarget = _returnFocusTo;
        SetForegroundWindow(_menuOwner.Handle);
        _menu.Show(Forms.Cursor.Position);
        _menu.Focus();
    }

    public void ShowMessage(string title, string text, bool isError = false)
        => _notifyIcon.ShowBalloonTip(3000, title, text, isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.None);

    public void Dispose()
    {
        // Hide before disposing so no ghost icon stays in the notification area.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
        _menuOwner.DestroyHandle();
    }

    private void OnMenuClosed(object? sender, Forms.ToolStripDropDownClosedEventArgs e)
    {
        // Give focus back only when the user finished with the menu itself: an item click (which has
        // normally restored focus already, after doing its work) or Escape. If they clicked another
        // window or elsewhere, that window should keep the focus they just gave it.
        if (e.CloseReason is Forms.ToolStripDropDownCloseReason.ItemClicked
            or Forms.ToolStripDropDownCloseReason.Keyboard)
            ReturnFocus();
        else
            _returnFocusTo = IntPtr.Zero;
    }

    private void ReturnFocus()
    {
        var target = _returnFocusTo;
        _returnFocusTo = IntPtr.Zero;
        if (target != IntPtr.Zero && IsWindow(target))
            SetForegroundWindow(target);
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("The app icon resource is missing.");
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}

/// <summary>A prompt picked from the quick menu. <paramref name="ReturnTo"/> is the window that was in front when the
/// global shortcut opened the menu (zero for a tray click); focus goes back there and the prompt may be pasted into it.</summary>
public sealed record PromptPick(Guid Id, IntPtr ReturnTo);
