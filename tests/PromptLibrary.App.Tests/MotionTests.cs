using System.Windows.Media.Animation;
using PromptLibrary.App.Views;

namespace PromptLibrary.App.Tests;

/// <summary>
/// The search window's opening motion (U4), sampled through a seeked animation clock so no real time passes: a 167 ms fade
/// with an 8 px slide up on the Fluent enter curve, cubic-bezier(0, 0, 0, 1), which front-loads the movement.
/// </summary>
public class MotionTests
{
    internal static double At(AnimationTimeline animation, double defaultValue, int ms)
    {
        var clock = animation.CreateClock();
        clock.Controller!.Begin();
        clock.Controller.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(ms), TimeSeekOrigin.BeginTime);
        return (double)animation.GetCurrentValue(defaultValue, defaultValue, clock);
    }

    [Fact]
    public void SearchWindowOpens_With167MsFadeAndAn8PxSlide_OnTheFluentEnterCurve() => Sta.Run(() =>
    {
        var (opacity, slide) = SearchWindow.ShowAnimations();
        Assert.Equal(TimeSpan.FromMilliseconds(167), opacity.Duration.TimeSpan);
        Assert.Equal(TimeSpan.FromMilliseconds(167), slide.Duration.TimeSpan);

        Assert.Equal(0, At(opacity, 1, 0), 3);
        Assert.Equal(8, At(slide, 0, 0), 3);
        Assert.Equal(1, At(opacity, 1, 167), 3);
        Assert.Equal(0, At(slide, 0, 167), 3);

        // An enter (ease-out) curve: well past half way at half time, and it only ever moves forward.
        Assert.True(At(opacity, 1, 83) > 0.75, $"opacity at 83 ms = {At(opacity, 1, 83):0.00}");
        Assert.True(At(slide, 0, 83) < 2, $"slide at 83 ms = {At(slide, 0, 83):0.00}");
        var previous = -1.0;
        for (var ms = 0; ms <= 167; ms += 10)
        {
            var value = At(opacity, 1, ms);
            Assert.True(value >= previous, $"opacity went back at {ms} ms");
            previous = value;
        }
    });

    [Fact]
    public void Motion_Durations_AreTheFluentRamp_AndZeroWhenSuppressed()
    {
        var was = Motion.Suppress;
        try
        {
            Motion.Suppress = true;
            Assert.Equal(TimeSpan.Zero, Motion.Fast.TimeSpan);
            Assert.Equal(TimeSpan.Zero, Motion.Normal.TimeSpan);
            Motion.Suppress = false;
            if (System.Windows.SystemParameters.ClientAreaAnimation)
            {
                Assert.Equal(TimeSpan.FromMilliseconds(83), Motion.Fast.TimeSpan);
                Assert.Equal(TimeSpan.FromMilliseconds(167), Motion.Normal.TimeSpan);
            }
            var curve = Motion.EnterCurve;
            Assert.Equal((0.0, 0.0, 0.0, 1.0), (curve.ControlPoint1.X, curve.ControlPoint1.Y, curve.ControlPoint2.X, curve.ControlPoint2.Y));
        }
        finally { Motion.Suppress = was; }
    }
}
