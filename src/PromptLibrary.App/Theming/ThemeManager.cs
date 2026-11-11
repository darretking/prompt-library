using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PromptLibrary.App.Theming;

/// <summary>
/// Owns the light/dark palette. "system" follows the Windows app-mode setting live; "light" and "dark" pin it.
/// Swaps the colour dictionary in <c>Application.Resources</c>; every brush is a DynamicResource, so open windows
/// repaint at once. Use from the UI thread: <c>ThemeManager.Current.Apply("dark")</c>.
/// </summary>
public sealed class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightUri = "/PromptLibrary;component/Themes/Light.xaml";
    private const string DarkUri = "/PromptLibrary;component/Themes/Dark.xaml";
    // How much of the Window and Rail tint stays over Mica (0-255). The tint keeps text contrast on any wallpaper.
    private const byte MicaTintAlpha = 0xC8;

    private bool _subscribed;
    private bool _applied;

    public static ThemeManager Current { get; } = new();

    /// <summary>"system", "light" or "dark".</summary>
    public string Mode { get; private set; } = "system";

    /// <summary>True while the dark palette is showing.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised after the palette changed (and the window chrome needs re-applying).</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Applies a mode: "system" (follow Windows), "light" or "dark". Anything else counts as "system".</summary>
    public void Apply(string? mode)
    {
        Mode = mode is "light" or "dark" ? mode : "system";
        Subscribe();
        Refresh();
    }

    /// <summary>True when Windows is set to dark app mode. Light when the setting can't be read.</summary>
    public static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Subscribe()
    {
        if (_subscribed || Application.Current is not { } app)
            return;
        _subscribed = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != "system" || Application.Current is not { } app)
            return;
        // Raised on whatever thread owns SystemEvents' hidden window: hop to the UI thread.
        app.Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        if (Application.Current is not { } app)
            return;

        var dark = Mode == "dark" || (Mode == "system" && WindowsUsesDarkApps());
        if (_applied && dark == IsDark)
            return;
        _applied = true;
        IsDark = dark;

        var merged = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri(dark ? DarkUri : LightUri, UriKind.Relative) };
        var index = merged.ToList().FindIndex(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (index >= 0)
            merged[index] = palette;
        else
            merged.Insert(0, palette);

        if (WindowBackdrop.MicaSupported)
        {
            app.Resources["Brush.WindowBackdrop"] = Tinted(palette, "Brush.Window");
            app.Resources["Brush.RailBackdrop"] = Tinted(palette, "Brush.Rail");
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SolidColorBrush Tinted(ResourceDictionary palette, string key)
    {
        var color = ((SolidColorBrush)palette[key]).Color;
        var brush = new SolidColorBrush(Color.FromArgb(MicaTintAlpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
