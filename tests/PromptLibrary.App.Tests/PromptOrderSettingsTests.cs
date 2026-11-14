using PromptLibrary.App.Settings;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.App.Tests;

public class PromptOrderSettingsTests
{
    private sealed class Calls
    {
        public int Save, Refresh, Raise, Confirm;
    }

    private static PromptOrderSettings Make(AppSettings s, Calls c, bool confirm = true)
        => new(s, () => c.Save++, () => c.Refresh++, () => c.Raise++, () => { c.Confirm++; return confirm; });

    [Fact]
    public void Default_SortsByUse()
        => Assert.True(Make(new AppSettings(), new Calls()).SortsByUse);

    [Fact]
    public void SetSortByUse_False_SetsName_SavesRefreshesRaises()
    {
        var s = new AppSettings();
        var c = new Calls();
        var order = Make(s, c);

        order.SetSortByUse(false);

        Assert.Equal(UsageOrder.ByName, s.PromptSort);
        Assert.False(order.SortsByUse);
        Assert.Equal((1, 1, 1), (c.Save, c.Refresh, c.Raise));

        order.SetSortByUse(true);
        Assert.Equal(UsageOrder.ByUse, s.PromptSort);
    }

    [Fact]
    public void CanClear_False_WhenNoUsage()
        => Assert.False(Make(new AppSettings(), new Calls()).CanClear);

    [Fact]
    public void ClearUsage_Confirmed_EmptiesUsage_SavesRefreshesRaises()
    {
        var s = new AppSettings();
        s.Usage[Guid.NewGuid()] = new PromptUsage();
        var c = new Calls();
        var order = Make(s, c);
        Assert.True(order.CanClear);

        Assert.True(order.ClearUsage());

        Assert.Empty(s.Usage);
        Assert.False(order.CanClear);
        Assert.Equal((1, 1, 1, 1), (c.Save, c.Refresh, c.Raise, c.Confirm));
    }

    [Fact]
    public void ClearUsage_Cancelled_KeepsUsage_NoSave()
    {
        var s = new AppSettings();
        s.Usage[Guid.NewGuid()] = new PromptUsage();
        var c = new Calls();

        Assert.False(Make(s, c, confirm: false).ClearUsage());

        Assert.Single(s.Usage);
        Assert.Equal((0, 0, 0, 1), (c.Save, c.Refresh, c.Raise, c.Confirm));
    }
}
