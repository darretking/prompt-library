using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PromptLibrary.App.Views;

/// <summary>
/// Motion from the style guide (U4): dialogs fade and scale in over 167 ms, menus fade over 83 ms, the selection indicator
/// moves over 83 ms, all on the Fluent enter curve. Every duration is zero when Windows' "Animation effects" are off.
/// </summary>
public static class Motion
{
    /// <summary>Set by tests and the screenshot gallery, so a picture is never taken half way through a fade.</summary>
    public static bool Suppress { get; set; }

    public static bool Enabled => !Suppress && SystemParameters.ClientAreaAnimation;

    /// <summary>83 ms: menus, the Save button, the selection indicator (used from XAML with x:Static).</summary>
    public static Duration Fast => new(TimeSpan.FromMilliseconds(Enabled ? 83 : 0));

    /// <summary>167 ms: dialogs.</summary>
    public static Duration Normal => new(TimeSpan.FromMilliseconds(Enabled ? 167 : 0));

    /// <summary>Fluent enter curve, cubic-bezier(0, 0, 0, 1).</summary>
    public static KeySpline EnterCurve => new(0, 0, 0, 1);

    /// <summary>On a window: its content fades in and grows from 96 % when the window first loads.</summary>
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnterChanged));

    public static bool GetEnter(DependencyObject d) => (bool)d.GetValue(EnterProperty);

    public static void SetEnter(DependencyObject d, bool value) => d.SetValue(EnterProperty, value);

    private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        window.Loaded -= PlayEnter;
        if (e.NewValue is true) window.Loaded += PlayEnter;
    }

    private static void PlayEnter(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;
        window.Loaded -= PlayEnter;
        if (!Enabled || window.Content is not FrameworkElement content || content.RenderTransform is not MatrixTransform { Matrix.IsIdentity: true }) return;

        var scale = new ScaleTransform(0.96, 0.96);
        content.RenderTransformOrigin = new Point(0.5, 0.5);
        content.RenderTransform = scale;
        var duration = Normal;

        DoubleAnimationUsingKeyFrames Curve(double from, double to) => new()
        {
            Duration = duration,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration.TimeSpan), EnterCurve),
            },
        };

        var opacity = Curve(0, 1);
        opacity.Completed += (_, _) =>
        {
            content.BeginAnimation(UIElement.OpacityProperty, null);
            content.RenderTransform = Transform.Identity;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Curve(0.96, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Curve(0.96, 1));
        content.BeginAnimation(UIElement.OpacityProperty, opacity);
    }
}
