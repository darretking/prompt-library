using PromptLibrary.App.Tray;
using PromptLibrary.Core.Menu;
using Forms = System.Windows.Forms;

namespace PromptLibrary.App.Tests;

public class TrayMenuTests
{
    private static QuickMenuPrompt P(string t, bool team = false) => new(Guid.NewGuid(), t, team);

    private static QuickMenuCategory C(string n, bool team = false, int count = 1)
        => new(n, Enumerable.Range(0, count).Select(i => P($"{n} p{i}", team)).ToList());

    private static List<string> Read(QuickMenu menu)
    {
        var result = new List<string>();
        Sta.Run(() =>
        {
            using var tray = new TrayIcon("t", headless: true);
            tray.Rebuild(menu);
            foreach (Forms.ToolStripItem item in tray.MenuStrip.Items)
            {
                result.Add(item switch
                {
                    Forms.ToolStripSeparator => "---",
                    Forms.ToolStripMenuItem { DropDownItems.Count: > 0 } m =>
                        $"{m.Text}>{string.Join(",", m.DropDownItems.Cast<Forms.ToolStripItem>().Select(i => i.Text))}",
                    _ => item.Enabled ? item.Text ?? "" : $"[{item.Text}]",
                });
            }
        });
        return result;
    }

    [Fact]
    public void Tray_PinnedTeamPrompt_ShowsTeamIconAndPersonalPinShowsPinIcon() => Sta.Run(() =>
    {
        using var tray = new TrayIcon("t", headless: true);
        tray.Rebuild(new QuickMenu([P("Mine"), P("Theirs", true)], [C("Sales", true)], [C("Work")], false));
        var items = tray.MenuStrip.Items.Cast<Forms.ToolStripItem>().ToList();
        Assert.Same(tray.PinImage, items[0].Image);
        Assert.Same(tray.TeamImage, items[1].Image);
        Assert.NotSame(tray.PinImage, tray.TeamImage);
    });

    [Fact]
    public void Tray_TeamShownButYoursEmpty_ShowsHeadingAndNoPromptsYet()
    {
        Assert.Equal(
            ["[Team prompts]", "Sales>Sales p0", "---", "[Your prompts]", "[No prompts yet]", "---",
                "Settings…", "Open Prompt Library…", "Exit"],
            Read(new QuickMenu([], [C("Sales", true)], [], false)));
    }

    [Fact]
    public void Tray_Rebuild_KeepsUpdateItemFirst() => Sta.Run(() =>
    {
        using var tray = new TrayIcon("t", headless: true);
        tray.SetUpdateItem("Update to 9.9.9…");
        tray.Rebuild(new QuickMenu([], [], [C("Work")], false));
        tray.Rebuild(new QuickMenu([], [], [C("Work")], false));
        var items = tray.MenuStrip.Items;
        Assert.Equal("Update to 9.9.9…", items[0].Text);
        Assert.IsType<Forms.ToolStripSeparator>(items[1]);
        Assert.Equal("Work", items[2].Text);
        Assert.Single(items.Cast<Forms.ToolStripItem>(), i => i.Text == "Update to 9.9.9…");
    });

    [Fact]
    public void Tray_Rebuild_OrderMatchesQuickMenu()
    {
        // (a) solo, nothing pinned: as before.
        Assert.Equal(
            ["Work>Work p0", "---", "Settings…", "Open Prompt Library…", "Exit"],
            Read(new QuickMenu([], [], [C("Work")], false)));

        Assert.Equal(
            ["[No prompts yet]", "---", "Settings…", "Open Prompt Library…", "Exit"],
            Read(new QuickMenu([], [], [], false)));

        // (b) team and pins.
        Assert.Equal(
            ["Pin A", "---", "[Team prompts]", "Sales>Sales p0", "---", "[Your prompts]", "Work>Work p0", "---",
                "Settings…", "Open Prompt Library…", "Exit"],
            Read(new QuickMenu([P("Pin A", true)], [C("Sales", true)], [C("Work")], false)));

        // (c) collapsed team block: one submenu, no separate heading.
        var many = Enumerable.Range(1, 7).Select(i => C($"T{i}", true)).ToList();
        var collapsed = Read(new QuickMenu([], many, [C("Work")], true));
        Assert.Equal(
            ["Team prompts>T1,T2,T3,T4,T5,T6,T7", "---", "[Your prompts]", "Work>Work p0", "---",
                "Settings…", "Open Prompt Library…", "Exit"],
            collapsed);
    }
}
