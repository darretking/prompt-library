using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Onboarding;

public enum OnboardingStep
{
    Welcome = 1,
    Shortcut = 2,
    Backups = 3,
    Ready = 4,
}

/// <summary>
/// The onboarding wizard's steps (Welcome → Shortcut → Backups → Ready) and what Finish and Skip do to the settings.
/// One quiet way out per page at most: <c>Skip setup</c> on step 1, <c>Skip backups for now</c> on step 3.
/// </summary>
public sealed class OnboardingFlow
{
    public const int StepCount = 4;

    public OnboardingStep Step { get; private set; } = OnboardingStep.Welcome;

    public string StepText => $"Step {(int)Step} of {StepCount}";

    /// <summary>Back is hidden (not just disabled) on step 1.</summary>
    public bool CanGoBack => Step != OnboardingStep.Welcome;

    public bool IsLastStep => Step == OnboardingStep.Ready;

    public string PrimaryText => IsLastStep ? "Finish" : "Next";

    public bool ShowsSkipSetup => Step == OnboardingStep.Welcome;

    public bool ShowsSkipBackups => Step == OnboardingStep.Backups;

    /// <summary>Moves to the next step; false on the last one (the caller finishes instead).</summary>
    public bool Next()
    {
        if (IsLastStep) return false;
        Step++;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        Step--;
        return true;
    }

    /// <summary>
    /// Esc or × after step 1 asks <c>Leave setup?</c> once a backup folder has been chosen (so a stray Esc can't
    /// look like it threw the choice away); otherwise leaving is the same as Skip.
    /// </summary>
    public bool AsksBeforeLeaving(bool backupFolderChosen) => Step != OnboardingStep.Welcome && backupFolderChosen;

    /// <summary>
    /// Esc, × or Skip setup: "your choices so far are kept". On step 4 that includes its checkboxes (as Finish);
    /// before it the defaults stay. Either way the wizard doesn't open by itself again. The caller saves.
    /// </summary>
    public void ApplyLeave(AppSettings settings, bool startWithWindows, bool pasteAfterCopy)
    {
        if (IsLastStep)
            ApplyFinish(settings, startWithWindows, pasteAfterCopy);
        else
            ApplySkip(settings);
    }

    /// <summary>Skip (or leaving early): the defaults stay as they are and the wizard doesn't open again.</summary>
    public static void ApplySkip(AppSettings settings) => settings.FirstRunDone = true;

    /// <summary>Finish: step 4's checkboxes apply and the wizard doesn't open again. The caller saves.</summary>
    public static void ApplyFinish(AppSettings settings, bool startWithWindows, bool pasteAfterCopy)
    {
        settings.StartWithWindows = startWithWindows;
        settings.PasteAfterCopy = pasteAfterCopy;
        settings.FirstRunDone = true;
    }
}
