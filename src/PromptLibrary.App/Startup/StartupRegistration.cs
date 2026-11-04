using Microsoft.Win32;

namespace PromptLibrary.App.Startup;

/// <summary>Adds or removes the "start with Windows" entry under the current user's Run key.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PromptLibrary";

    /// <summary>Rewritten on every launch so the path follows the exe if it moves.</summary>
    public static void Apply(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
