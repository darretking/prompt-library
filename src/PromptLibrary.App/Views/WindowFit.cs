using System.Windows;

namespace PromptLibrary.App.Views;

/// <summary>Keeps windows inside the screen's work area (e.g. about 688 px tall at 1920×1080 and 150 % scaling).</summary>
public static class WindowFit
{
    /// <summary><paramref name="wanted"/>, but never taller than <paramref name="workArea"/> (ignored when not positive).</summary>
    public static double Height(double wanted, double workArea) => workArea > 0 ? Math.Min(wanted, workArea) : wanted;

    /// <summary>The primary screen's work area height (without the taskbar), in device-independent pixels.</summary>
    public static double WorkAreaHeight => SystemParameters.WorkArea.Height;
}
