using Microsoft.Win32;
using PromptLibrary.Core.Models;

namespace PromptLibrary.App.Team;

/// <summary>Where the installer's team source lives. An interface so tests never touch the registry.</summary>
public interface ITeamPresetSource
{
    string? Read();
}

/// <summary><c>HKCU\Software\Prompt Library\TeamSource</c>, written by the installer's TEAMSOURCE property.</summary>
public sealed class RegistryTeamPresetSource : ITeamPresetSource
{
    public string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Prompt Library");
            return key?.GetValue("TeamSource") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Joins the installer's team source once. Once applied, leaving the team is respected: only a different value joins
/// again. A failure is quiet and retried at the next start.
/// </summary>
public static class TeamPreset
{
    /// <returns>True when the preset was joined now.</returns>
    public static async Task<bool> ApplyAsync(ITeamPresetSource source, AppSettings settings, bool hasTeam,
        Func<string, Task<TeamJoinResult>> join, Func<bool> saveSettings)
    {
        if (hasTeam) return false;
        var value = source.Read()?.Trim();
        if (string.IsNullOrEmpty(value) || value == settings.PresetTeamSourceApplied) return false;
        try
        {
            if (!(await join(value)).Success) return false;
        }
        catch (Exception)
        {
            return false;
        }
        settings.PresetTeamSourceApplied = value;
        saveSettings();
        return true;
    }
}
