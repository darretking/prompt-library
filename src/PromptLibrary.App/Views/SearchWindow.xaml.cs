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
    private const double WantedWidth = 640;
    private const double WantedHeight = 420;
    private const double TopFraction = 0.25;
    private static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(167));

    private readonly SearchViewModel _viewModel;
    private bool _warming;

    public SearchWindow(SearchViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (!_warming) Hide();
        };
        viewModel.FocusSearchRequested += FocusSearchLater;
        viewModel.PropertyChanged += OnViewModelChanged;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) _viewModel.CloseActions();
        };
    }

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
        ApplyBackground();
        Place();
        Show();
        Activate();
        BringToFront();
        FocusSearch();
        PlayShowAnimation();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    // Solid when Windows' transparency effects are off; otherwise the translucent tint over Acrylic (Brush.WindowBackdrop).
    private void ApplyBackground()
    {
        if (WindowBackdrop.TransparencyEnabled())
            SetResourceReference(BackgroundProperty, "Brush.WindowBackdrop");
        else
            SetResourceReference(BackgroundProperty, "Brush.Window");
    }

    private void Place()
    {
        var mouse = System.Windows.Forms.Cursor.Position;
        var area = System.Windows.Forms.Screen.FromPoint(mouse).WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        Width = WantedWidth;
        Height = Math.Min(WantedHeight, Math.Max(240, area.Height / scale - 32));
        Left = (area.Left + area.Width / 2.0) / scale - Width / 2;
        Top = (area.Top + area.Height * TopFraction) / scale;
        // Never lower than the bottom of the work area.
        var bottom = area.Bottom / scale;
        if (Top + Height > bottom) Top = Math.Max(area.Top / scale, bottom - Height);
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
