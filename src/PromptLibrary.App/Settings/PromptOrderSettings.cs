using PromptLibrary.Core.Models;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.App.Settings;

/// <summary>
/// The Settings "Prompt order" choice and "Clear usage history" action, apart from the window so it can be tested.
/// </summary>
public sealed class PromptOrderSettings(
    AppSettings settings, Action save, Action refreshTray, Action raiseUserState, Func<bool> confirmClear)
{
    public bool SortsByUse => UsageOrder.SortsByUse(settings);

    /// <summary>Most used first (true) or A to Z (false); saved and shown everywhere at once.</summary>
    public void SetSortByUse(bool byUse)
    {
        settings.PromptSort = byUse ? UsageOrder.ByUse : UsageOrder.ByName;
        save();
        refreshTray();
        raiseUserState();
    }

    public bool CanClear => settings.Usage.Count > 0;

    /// <summary>Asks first; true when the usage history was cleared.</summary>
    public bool ClearUsage()
    {
        if (!confirmClear()) return false;
        settings.Usage.Clear();
        save();
        refreshTray();
        raiseUserState();
        return true;
    }
}
