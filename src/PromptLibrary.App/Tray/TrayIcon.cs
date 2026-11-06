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

    private readonly Forms.NotifyIcon? _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon? _icon;
    private System.Drawing.Image? _teamImage;
    private System.Drawing.Image? _pinImage;

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

    /// <summary>The "Update to X…" item at the top of the menu was clicked.</summary>
    public event EventHandler? UpdateRequested;

    // What a click on the current balloon does (e.g. open the update dialog); null for a plain message.
    private Action? _balloonClick;

    // "Update to 1.0.2…" while an update is available; kept across Rebuild.
    private string? _updateItemText;

    public TrayIcon(string tooltip) : this(tooltip, headless: false) { }

    /// <summary>Headless (tests): the menu exists but there is no notification-area icon.</summary>
    internal TrayIcon(string tooltip, bool headless)
    {
        _menuOwner.CreateHandle(new Forms.CreateParams());
        _menu.Closed += OnMenuClosed;
        if (headless) return;

        _icon = LoadAppIcon();
        _notifyIcon = new Forms.NotifyIcon { Icon = _icon, Visible = true };
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            // After this callback has returned: the click may open a modal dialog and even exit the app, which
            // disposes this icon, and that must not happen inside its own event.
            if (click is not null)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(click);
        };
        _notifyIcon.BalloonTipClosed += (_, _) => _balloonClick = null;
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
        set
        {
            if (_notifyIcon is not null)
                _notifyIcon.Text = value.Length > TooltipMaxLength ? value[..TooltipMaxLength] : value;
        }
    }

    /// <summary>Rebuilds the menu: Pinned, then the team block (when a team library is connected), then your
    /// own categories, then Settings, Open and Exit.</summary>
    public void Rebuild(QuickMenu menu)
    {
        // Dispose the old items so their handles do not pile up on every save, then the icons they used.
        var old = _menu.Items.Cast<Forms.ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old) item.Dispose();
        _teamImage?.Dispose();
        _pinImage?.Dispose();
        var scale = Math.Max(1.0, _menu.DeviceDpi / 96.0);
        _teamImage = TrayImages.Team(scale);
        _pinImage = TrayImages.Pin(scale);

        // Headings show only when a team block exists, so a solo menu looks as it always did.
        var hasTeam = menu.Team.Count > 0;
        var sections = new List<List<Forms.ToolStripItem>>
        {
            menu.Pinned.Select(p => (Forms.ToolStripItem)PromptItem(p, pinned: true)).ToList(),
        };

        if (hasTeam)
        {
            var team = new List<Forms.ToolStripItem>();
            if (menu.TeamCollapsed)
            {
                var all = new Forms.ToolStripMenuItem("Team prompts") { Image = _teamImage };
                foreach (var category in menu.Team) all.DropDownItems.Add(CategoryItem(category));
                team.Add(all);
            }
            else
            {
                team.Add(Heading("Team prompts"));
                team.AddRange(menu.Team.Select(CategoryItem));
            }
            sections.Add(team);
        }

        var yours = new List<Forms.ToolStripItem>();
        if (hasTeam) yours.Add(Heading("Your prompts"));
        yours.AddRange(menu.Yours.Select(CategoryItem));
        if (yours.Count == 0 && menu.Pinned.Count == 0)
            yours.Add(new Forms.ToolStripMenuItem("No prompts yet") { Enabled = false });
        sections.Add(yours);

        var settings = new Forms.ToolStripMenuItem("Settings…");
        settings.Click += (_, _) =>
        {
            _returnFocusTo = IntPtr.Zero; // the Settings window takes focus instead
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };

        var open = new Forms.ToolStripMenuItem("Open Prompt Library…");
        open.Click += (_, _) =>
        {
            _returnFocusTo = IntPtr.Zero; // the main window takes focus instead
            OpenRequested?.Invoke(this, EventArgs.Empty);
        };

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            ReturnFocus();
            // After this click handler has returned: exiting disposes this menu, which must not happen inside it.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                () => ExitRequested?.Invoke(this, EventArgs.Empty));
        };
        sections.Add([settings, open, exit]);

        // One separator between non-empty sections: never doubled, leading or trailing.
        var first = true;
        foreach (var section in sections.Where(x => x.Count > 0))
        {
            if (!first) _menu.Items.Add(new Forms.ToolStripSeparator());
            first = false;
            foreach (var item in section) _menu.Items.Add(item);
        }

        AddUpdateItem();
    }

    /// <summary>The menu strip, for tests.</summary>
    internal Forms.ContextMenuStrip MenuStrip => _menu;

    private static Forms.ToolStripMenuItem Heading(string text) => new(text) { Enabled = false };

    private Forms.ToolStripMenuItem CategoryItem(QuickMenuCategory category)
    {
        var categoryItem = new Forms.ToolStripMenuItem(MenuText.ForMenu(category.Name));
        foreach (var prompt in category.Prompts)
            categoryItem.DropDownItems.Add(PromptItem(prompt, pinned: false));
        if (category.Prompts.Any(p => p.IsTeam)) categoryItem.Image = _teamImage;
        return categoryItem;
    }

    private Forms.ToolStripMenuItem PromptItem(QuickMenuPrompt prompt, bool pinned)
    {
        var id = prompt.Id;
        var item = new Forms.ToolStripMenuItem(MenuText.ForMenu(prompt.Title));
        if (pinned) item.Image = _pinImage;
        else if (prompt.IsTeam) item.Image = _teamImage;
        item.Click += (_, _) =>
        {
            PromptClicked?.Invoke(this, new PromptPick(id, _pasteTarget));
            ReturnFocus();
        };
        return item;
    }

    /// <summary>Shows "Update to X…" (and a separator) at the top of the menu; null removes it.</summary>
    public void SetUpdateItem(string? text)
    {
        if (text == _updateItemText) return;
        RemoveUpdateItem();
        _updateItemText = text;
        AddUpdateItem();
    }

    private const string UpdateItemTag = "update";

    private void AddUpdateItem()
    {
        if (_updateItemText is null) return;
        var item = new Forms.ToolStripMenuItem(_updateItemText) { Tag = UpdateItemTag };
        item.Click += (_, _) =>
        {
            _returnFocusTo = IntPtr.Zero; // the update dialog takes focus instead
            // After this click handler has returned: the dialog is modal and must not run inside the menu's click.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                () => UpdateRequested?.Invoke(this, EventArgs.Empty));
        };
        _menu.Items.Insert(0, item);
        _menu.Items.Insert(1, new Forms.ToolStripSeparator { Tag = UpdateItemTag });
    }

    private void RemoveUpdateItem()
    {
        foreach (var item in _menu.Items.Cast<Forms.ToolStripItem>().Where(i => Equals(i.Tag, UpdateItemTag)).ToList())
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }
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

    /// <summary>A notification; <paramref name="onClick"/> runs if the user clicks it (until the next one replaces it).</summary>
    public void ShowMessage(string title, string text, bool isError = false, Action? onClick = null)
    {
        _balloonClick = onClick;
        _notifyIcon?.ShowBalloonTip(3000, title, text, isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        // Hide before disposing so no ghost icon stays in the notification area.
        if (_notifyIcon is not null) _notifyIcon.Visible = false;
        _notifyIcon?.Dispose();
        _menu.Dispose();
        _teamImage?.Dispose();
        _pinImage?.Dispose();
        _icon?.Dispose();
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
