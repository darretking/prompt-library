using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PromptLibrary.App.Theming;

/// <summary>What a window is, for the Mica flavour it gets.</summary>
public enum BackdropKind
{
    /// <summary>Main, Settings and other full windows: Mica.</summary>
    Main,

    /// <summary>Small dialogs: the lighter transient-window backdrop.</summary>
    Dialog,

    /// <summary>The search window: Acrylic (the transient-window backdrop).</summary>
    Transient,
}

/// <summary>
/// Windows 11 chrome for a window: dark title bar, rounded corners and a Mica backdrop. Silently does nothing on
/// Windows 10 or when a call fails. <see cref="Register"/> applies it to every window the app opens and re-applies
/// it when the theme changes.
/// </summary>
public static class WindowBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    /// <summary>Windows 11 22H2 (build 22621) is the first with the system backdrop attribute.</summary>
    public static bool MicaSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    private static bool RoundedSupported { get; } = Environment.OSVersion.Version.Build >= 22000;

    /// <summary>False when the user turned off Windows' "Transparency effects": the backdrop is then solid.</summary>
    public static bool TransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(BackdropKind), typeof(WindowBackdrop), new PropertyMetadata(BackdropKind.Main));

    public static BackdropKind GetKind(DependencyObject d) => (BackdropKind)d.GetValue(KindProperty);

    public static void SetKind(DependencyObject d, BackdropKind value) => d.SetValue(KindProperty, value);

    /// <summary>Set by the AppWindow style: applies the chrome as soon as the window handle exists, before first show.</summary>
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(WindowBackdrop), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
            return;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyCurrent(window);
        else
            window.SourceInitialized += (_, _) => ApplyCurrent(window);
    }

    private static bool _registered;

    /// <summary>Hooks every Window created from now on (applies before it first shows) and re-applies on theme change.</summary>
    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;

        // Fallback for a window without the AppWindow style: apply when it loads.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (s is Window w) ApplyCurrent(w); }));
        ThemeManager.Current.ThemeChanged += (_, _) =>
        {
            if (Application.Current is not { } app)
                return;
            foreach (Window window in app.Windows)
                ApplyCurrent(window);
        };
    }

    private static void ApplyCurrent(Window window) =>
        Apply(window, ThemeManager.Current.IsDark, GetKind(window));

    public static void Apply(Window window, bool dark) => Apply(window, dark, GetKind(window));

    /// <summary>Applies dark title bar, rounded corners and (Windows 11 22H2+) Mica. Never throws.</summary>
    public static void Apply(Window window, bool dark, BackdropKind kind)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
            if (RoundedSupported)
                Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            if (!MicaSupported)
                return;

            var backdropResult = Set(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, kind == BackdropKind.Main ? DWMSBT_MAINWINDOW : DWMSBT_TRANSIENTWINDOW);
            LastBackdropResult = backdropResult;
            LogBackdrop(window, kind, backdropResult);

            // Let the backdrop show through the whole window: glass over the client area, and nothing opaque
            // painted by WPF underneath (the Window and Rail brushes are translucent tints while Mica is on).
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is { } target)
                target.BackgroundColor = Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            var extendResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            LogExtend(window, extendResult);
        }
        catch (Exception)
        {
            // Cosmetic only: a failure leaves the window with the normal solid look.
        }
    }

    private static int Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    /// <summary>The HRESULT of the last <c>DWMWA_SYSTEMBACKDROP_TYPE</c> call (0 = S_OK), or null before any call.</summary>
    public static int? LastBackdropResult { get; private set; }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogBackdrop(Window window, BackdropKind kind, int hr)
        => System.Diagnostics.Debug.WriteLine($"[backdrop] {window.GetType().Name} {kind}: DWMWA_SYSTEMBACKDROP_TYPE HRESULT 0x{hr:X8}{(hr == 0 ? " (ok)" : " (FAILED)")}, AllowsTransparency={window.AllowsTransparency}");

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogExtend(Window window, int hr)
        => System.Diagnostics.Debug.WriteLine($"[backdrop] {window.GetType().Name}: DwmExtendFrameIntoClientArea HRESULT 0x{hr:X8}");

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
