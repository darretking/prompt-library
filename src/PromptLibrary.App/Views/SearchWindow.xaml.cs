using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PromptLibrary.App.Search;
using PromptLibrary.App.Theming;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>Visible when the bound value is false.</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The launcher window. Created once and kept hidden; <see cref="ShowNearMouse"/> shows it on the monitor with the
/// mouse. It hides on Esc (via the view model) and whenever it loses focus. All logic lives in <see cref="SearchViewModel"/>.
/// </summary>
public partial class SearchWindow : Window
{
    private static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(167));
    private static readonly Duration ExitTime = new(TimeSpan.FromMilliseconds(83));

    private readonly SearchViewModel _viewModel;
    private bool _warming;
    private int _hideGeneration;

    public SearchWindow(SearchViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyBackground();
        EventHandler onThemeChanged = (_, _) => ApplyBackground();
        ThemeManager.Current.ThemeChanged += onThemeChanged;
        Closed += (_, _) => ThemeManager.Current.ThemeChanged -= onThemeChanged;

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (!_warming) Dismiss();
        };
        viewModel.FocusSearchRequested += FocusSearchLater;
        viewModel.PropertyChanged += OnViewModelChanged;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _viewModel.CloseActions();
                _placed = false;
                Startup.MemoryTrimmer.Schedule(); // idle memory goes back to Windows a few seconds after closing
            }
        };
    }

    /// <summary>Test seam: show far off screen and never take the focus (the speed test must not disturb the desktop).</summary>
    internal bool Offscreen { get; set; }

    /// <summary>Raised after <see cref="ShowNearMouse"/> has shown and activated the window.</summary>
    public event EventHandler? Shown;

    /// <summary>Shows and hides the window once off-screen so the first real opening is as fast as the later ones.</summary>
    public void Warm()
    {
        _warming = true;
        try
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Top = -10000;
            ShowActivated = false;
            Show();
            UpdateLayout();
            Hide();
        }
        finally
        {
            ShowActivated = true;
            _warming = false;
        }
    }

    /// <summary>Shows the window centred on the monitor holding the mouse, about a quarter from its top, and focuses the search box.</summary>
    public void ShowNearMouse()
    {
        _hideGeneration++; // cancels a fade-out still running
        if (Offscreen) ShowActivated = false;
        ApplyBackground();
        Place();
        Show();
        Place(); // again once shown: the first move can change the window's DPI
        if (!Offscreen)
        {
            Activate();
            BringToFront();
        }
        FocusSearch();
        PlayShowAnimation();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hides the window with an 83 ms fade (Fluent exit curve, cubic-bezier(1,0,1,1)); at once when Windows animations
    /// are off. The window is inert while it fades.
    /// </summary>
    public void Dismiss()
    {
        if (!IsVisible) return;
        if (_warming || Offscreen || !SystemParameters.ClientAreaAnimation)
        {
            Hide();
            return;
        }

        var generation = ++_hideGeneration;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = ExitTime };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(Body.Opacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(ExitTime.TimeSpan), new KeySpline(1, 0, 1, 1)));
        animation.Completed += (_, _) =>
        {
            if (generation == _hideGeneration) Hide();
        };
        Body.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// True when the Acrylic backdrop is showing: Windows 11 22H2 or later, "Transparency effects" on, and the theme's
    /// backdrop brushes translucent (the screenshot gallery makes them opaque, as a bitmap can't show Acrylic).
    /// </summary>
    internal static bool BackdropActive()
        => WindowBackdrop.MicaSupported && WindowBackdrop.TransparencyEnabled()
            && Application.Current?.Resources["Brush.WindowBackdrop"] is SolidColorBrush { Color.A: < 255 };

    // Over the backdrop: the window itself paints nothing, and the search bar, list, preview and footer are translucent
    // tints with text colours checked against the worst backdrop (SearchTints). Without it everything is solid.
    private void ApplyBackground()
    {
        var translucent = BackdropActive();
        static Color Token(string key) => Application.Current.Resources[key] is SolidColorBrush b ? b.Color : Colors.Magenta;
        var tints = SearchTints.For(ThemeManager.Current.IsDark, translucent, Token("Brush.Surface"), Token("Brush.Window"), Token("Brush.Muted"));
        Brush Frozen(Color c) { var brush = new SolidColorBrush(c); brush.Freeze(); return brush; }
        Resources["Brush.Search.Chrome"] = Frozen(tints.Chrome);
        Resources["Brush.Search.List"] = Frozen(tints.List);
        Resources["Brush.Search.Preview"] = Frozen(tints.Preview);
        Resources["Brush.Muted"] = Frozen(tints.Muted); // this window's muted text, on the tints
        if (translucent)
            Background = Brushes.Transparent;
        else
            SetResourceReference(BackgroundProperty, "Brush.Window");
    }

    private PixelRect _work = new(0, 0, 1920, 1040);
    private double _scale = 1.0;
    private bool _placed; // true between ShowNearMouse and hiding: only then does the window move itself

    /// <summary>The window height, in DIPs, the open fill-in form needs (its fields, preview and buttons, plus the footer and frame).</summary>
    internal double FormHeightDips()
    {
        FormPanel.Measure(new Size(Placement.LauncherWidth - 2, double.PositiveInfinity));
        return FormPanel.DesiredSize.Height + 32 + 2;
    }

    // The form sizes the window to itself (up to 80 % of the work area), the list goes back to the standard height.
    private void RefitHeight()
    {
        if (!IsVisible || !_placed) return;
        var spot = Placement.Launcher(_work, _scale, _viewModel.IsFormOpen ? FormHeightDips() : null);
        SetPlacement(spot);
    }

    private void SetPlacement(PixelRect spot)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // Twice: the first move may make Windows change the window's DPI (and size); the second sets the exact pixels.
        SetWindowPos(handle, IntPtr.Zero, spot.X, spot.Y, spot.Width, spot.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowPos(handle, IntPtr.Zero, spot.X, spot.Y, spot.Width, spot.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // Placed in device pixels with the DPI of the monitor under the mouse (not the window's current DPI), so a
    // window last shown on another monitor still lands right on a mixed-DPI setup. See Placement.Launcher.
    private void Place()
    {
        var mouse = System.Windows.Forms.Cursor.Position;
        var (work, scale) = Offscreen ? (new PixelRect(-20000, 0, 1920, 1040), 1.0) : Monitors.UnderPoint(mouse.X, mouse.Y);
        (_work, _scale) = (work, scale);
        _placed = true;
        SetPlacement(Placement.Launcher(work, scale));
    }

    private void FocusSearch()
    {
        if (!SearchBox.IsVisible) return;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void FocusSearchLater()
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusSearch);

    private void BringToFront()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) SetForegroundWindow(handle);
    }

    // 167 ms fade and an 8 px slide up, easing cubic-bezier(0,0,0,1); none when Windows animations are off.
    private void PlayShowAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Body.Opacity = 1;
            Slide.Y = 0;
            return;
        }

        var spline = new KeySpline(0, 0, 0, 1);
        Body.BeginAnimation(OpacityProperty, Frames(0, 1, spline));
        Slide.BeginAnimation(TranslateTransform.YProperty, Frames(8, 0, spline));
    }

    private static DoubleAnimationUsingKeyFrames Frames(double from, double to, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = FadeTime };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(FadeTime.TimeSpan), spline));
        return animation;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_viewModel.HandleKey(key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsFormOpen))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefitHeight); // once the form has its fields
        if (e.PropertyName == nameof(SearchViewModel.Selected) && _viewModel.Selected is { } selected)
        {
            var index = _viewModel.Items.IndexOf(selected);
            // The first prompt of a group scrolls its header into view too.
            if (index > 0 && _viewModel.Items[index - 1].IsHeader)
                ResultList.ScrollIntoView(_viewModel.Items[index - 1]);
            ResultList.ScrollIntoView(selected);
        }
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SearchItem { IsRow: true } item }) return;
        _viewModel.Selected = item;
        _viewModel.UseSelected();
        e.Handled = true;
    }

    private void Action_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SearchAction action }) return;
        _viewModel.SelectedAction = action;
        _viewModel.RunSelectedAction();
        e.Handled = true;
    }

    private const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
