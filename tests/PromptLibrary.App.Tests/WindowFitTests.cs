using PromptLibrary.App.Views;

namespace PromptLibrary.App.Tests;

public class WindowFitTests
{
    [Theory]
    [InlineData(720, 1032, 720)] // 1920×1080 at 100 %: fits
    [InlineData(720, 688, 688)]  // 1920×1080 at 150 %: no taller than the work area, so Close stays on screen
    [InlineData(600, 0, 600)]    // no work area known: as asked
    public void Fit_NeverTallerThanTheWorkArea(double wanted, double workArea, double expected)
        => Assert.Equal(expected, WindowFit.Height(wanted, workArea));
}
