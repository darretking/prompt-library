using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace PromptLibrary.App.Views;

/// <summary>
/// Keeps menus and drop-downs opening from their left edge. With Windows' "right-handed" pen setting (Tablet PC settings →
/// Other → Handedness), WPF right-aligns every <c>Placement="Bottom"</c> popup to its owner, so the File and Help menus,
/// combo boxes and the emoji picker opened to the left of their button and past the window edge. WPF reads that setting
/// into a private field of <see cref="SystemParameters"/>; this sets it to left-aligned and again whenever Windows
/// refreshes it. Does nothing if the field isn't there (a future WPF).
/// </summary>
public static class DropAlignment
{
    private static readonly FieldInfo? Field =
        typeof(SystemParameters).GetField("_menuDropAlignment", BindingFlags.NonPublic | BindingFlags.Static);

    private static bool _hooked;

    public static void KeepLeftAligned()
    {
        Apply();
        if (_hooked || Field is null) return;
        _hooked = true;
        SystemParameters.StaticPropertyChanged += OnChanged;
    }

    private static void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.MenuDropAlignment)) Apply();
    }

    private static void Apply()
    {
        try
        {
            if (Field is not null && SystemParameters.MenuDropAlignment) Field.SetValue(null, false);
        }
        catch (Exception ex) when (ex is FieldAccessException or ArgumentException or TargetException)
        {
            // Cosmetic only: popups then follow the Windows setting.
        }
    }
}
