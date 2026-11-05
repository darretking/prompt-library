namespace PromptLibrary.App;

/// <summary>Who asked the app to exit; decides whether questions may be asked first.</summary>
public enum ExitReason
{
    /// <summary>The user chose Exit: unsaved edits are offered for saving (Cancel keeps the app running).</summary>
    User,

    /// <summary>
    /// Another program closed the app (the installer before an upgrade or uninstall): nothing is asked. Complete edits
    /// are saved, others go to a recovery file, and the setup wizard is left keeping its choices.
    /// </summary>
    External,
}
