using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using PromptLibrary.App.Search;
using PromptLibrary.Core.Clipboard;

namespace PromptLibrary.App.Views;

/// <summary>
/// Shows a short confirmation near the bottom of the screen. Other code calls this instead of tray balloons
/// for "Pasted", "Copied" and "Opened ..." messages. Use on the UI thread.
/// </summary>
public interface IToast
{
    /// <summary>Shows <paramref name="text"/> on the monitor under the mouse.</summary>
    void Show(string text, ToastKind kind);

    /// <summary>Shows <paramref name="text"/> on the monitor holding <paramref name="nearWindow"/> (the app the user pasted into).</summary>
    void Show(string text, ToastKind kind, IntPtr nearWindow);
}

/// <summary>The one toast service: a single window that is reused, so a new message replaces the old one.</summary>
public sealed class ToastService : IToast
{
    public static ToastService Instance { get; } = new();

    private ToastWindow? _window;

    public void Show(string text, ToastKind kind) => Show(text, kind, IntPtr.Zero);

    public void Show(string text, ToastKind kind, IntPtr nearWindow)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _window ??= new ToastWindow();
        _window.Present(text, kind, nearWindow);
    }
}

/// <summary>
/// A small borderless toast: topmost, never takes the focus (<c>WS_EX_NOACTIVATE</c>), lets clicks through
/// (<c>WS_EX_TRANSPARENT</c>), 167 ms fade in, 1.4 s hold, 167 ms fade out. No animation when Windows animations are off.
/// </summary>
public partial class ToastWindow : Window
{
    private static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(167);
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(1400);

    private int _generation;

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThroughAndNonActivating();
    }

    /// <summary>Sets the text and the check or warning mark (also used by the screenshot gallery).</summary>
    public void SetContent(string text, ToastKind kind)
    {
        Message.Text = text;
        Glyph.Text = kind == ToastKind.Error ? "" : "";
        Glyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            kind == ToastKind.Error ? "Brush.Danger" : "Brush.Brand");
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }

    /// <summary>Shows the toast (or replaces the one on screen) and starts the fade.</summary>
    public void Present(string text, ToastKind kind, IntPtr nearWindow)
    {
        var generation = ++_generation;
        SetContent(text, kind);
        Measure(new Size(MaxWidth, double.PositiveInfinity));
        var size = DesiredSize;

        var (work, scale) = nearWindow != IntPtr.Zero
            ? Monitors.OfWindow(nearWindow)
            : Monitors.UnderPoint(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
        var spot = Placement.Toast(work, scale, size.Width, size.Height);

        if (!IsVisible)
        {
            // Placed before it is shown, then by pixels: the right size on any monitor's DPI.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = spot.X / scale;
            Top = spot.Y / scale;
            Card.Opacity = 0;
            Show();
        }
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), spot.X, spot.Y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE);
        Animate(generation);
    }

    private void Animate(int generation)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Card.BeginAnimation(OpacityProperty, null);
            Card.Opacity = 1;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = HoldTime };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (generation == _generation) Hide();
            };
            timer.Start();
            return;
        }

        var enter = new KeySpline(0, 0, 0, 1); // Fluent "decelerate": quick start, soft landing
        var exit = new KeySpline(1, 0, 1, 1);  // Fluent "accelerate"
        var animation = new DoubleAnimationUsingKeyFrames { Duration = FadeTime + HoldTime + FadeTime };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(Card.Opacity, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeTime), enter));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeTime + HoldTime)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(FadeTime + HoldTime + FadeTime), exit));
        animation.Completed += (_, _) =>
        {
            if (generation == _generation) Hide();
        };
        Card.BeginAnimation(OpacityProperty, animation);
    }

    private void MakeClickThroughAndNonActivating()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(handle, GWL_EXSTYLE,
            new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED));
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
