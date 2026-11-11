using PromptLibrary.App.Search;

namespace PromptLibrary.App.Tests;

public class PlacementTests
{
    [Fact]
    public void Launcher_At100Percent_Is640x420_CentredAQuarterDown()
    {
        var spot = Placement.Launcher(new PixelRect(0, 0, 1920, 1040), 1.0);
        Assert.Equal(new PixelRect(640, 260, 640, 420), spot);
    }

    [Fact]
    public void Launcher_At150Percent_ScalesInPixels()
    {
        // A 1920x1080 panel at 150 %: work area 1920x1040 px = 1280x693 DIPs.
        var spot = Placement.Launcher(new PixelRect(0, 0, 1920, 1040), 1.5);
        Assert.Equal(960, spot.Width);   // 640 DIPs
        Assert.Equal(630, spot.Height);  // 420 DIPs
        Assert.Equal(480, spot.X);       // centred
        Assert.Equal(260, spot.Y);       // a quarter of the work area's height
    }

    [Fact]
    public void Launcher_OnASecondMonitorWithOtherOrigin_UsesThatMonitorsPixels()
    {
        // Left monitor 150 %, right monitor 100 % starting at x = 2880: the mouse is on the right one.
        var right = new PixelRect(2880, 0, 1920, 1040);
        var spot = Placement.Launcher(right, 1.0);
        Assert.Equal(2880 + 640, spot.X);
        Assert.Equal(640, spot.Width);
        Assert.Equal(420, spot.Height);
    }

    [Fact]
    public void Launcher_OnAMonitorLeftOfThePrimary_KeepsNegativeCoordinates()
    {
        var left = new PixelRect(-1920, 0, 1920, 1040);
        var spot = Placement.Launcher(left, 1.0);
        Assert.Equal(-1920 + 640, spot.X);
    }

    [Fact]
    public void Launcher_ShortScreen_ShrinksAndNeverPassesTheBottom()
    {
        var work = new PixelRect(0, 0, 1366, 400);
        var spot = Placement.Launcher(work, 1.0);
        Assert.Equal(368, spot.Height); // 400 - 32 DIPs
        Assert.True(spot.Bottom <= work.Bottom);
        Assert.True(spot.Y >= work.Y);
    }

    [Fact]
    public void Launcher_NarrowWorkArea_NeverWiderThanIt()
        => Assert.Equal(500, Placement.Launcher(new PixelRect(0, 0, 500, 800), 1.0).Width);

    [Fact]
    public void Toast_IsBottomCentre_48DipsAboveTheWorkAreasEdge()
    {
        var spot = Placement.Toast(new PixelRect(0, 0, 1920, 1040), 1.0, 300, 44);
        Assert.Equal(new PixelRect(810, 1040 - 48 - 44, 300, 44), spot);
    }

    [Fact]
    public void Toast_At200Percent_LiftsBy96Pixels()
    {
        var spot = Placement.Toast(new PixelRect(3840, 0, 3840, 2080), 2.0, 300, 44);
        Assert.Equal(600, spot.Width);
        Assert.Equal(3840 + (3840 - 600) / 2, spot.X);
        Assert.Equal(2080 - 96 - 88, spot.Y);
    }
}
