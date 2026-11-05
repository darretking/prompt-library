using PromptLibrary.Core.Onboarding;

namespace PromptLibrary.Core.Tests;

public class OnboardingTextTests
{
    [Fact]
    public void Keycaps_SplitTheShortcut()
        => Assert.Equal(["Ctrl", "Alt", "P"], OnboardingText.Keycaps("Ctrl+Alt+P"));

    [Fact]
    public void Keycaps_NoShortcut_Empty()
        => Assert.Empty(OnboardingText.Keycaps(""));

    [Fact]
    public void InUse_NamesTheShortcutAndSuggestsAnother()
        => Assert.Equal("Ctrl+Alt+P is used by another app", OnboardingText.InUseHeading("Ctrl+Alt+P"));

    [Fact]
    public void Suggestion_IsNeverTheTakenShortcut()
    {
        Assert.Equal("Press a different shortcut, like Ctrl+Alt+L.", OnboardingText.PickAnotherText("Ctrl+Alt+P"));
        Assert.Equal("Press a different shortcut, like Ctrl+Alt+K.", OnboardingText.PickAnotherText("Ctrl+Alt+L"));
    }

    [Fact]
    public void CaptureFailed_SaysWhatHappenedAndWhatToDo()
        => Assert.Equal("Ctrl+Alt+K is already used by another app. Try another, like Ctrl+Alt+L.",
            OnboardingText.CaptureInUseText("Ctrl+Alt+K"));

    [Theory]
    [InlineData(true, "Shortcut: Ctrl+Alt+P")]
    [InlineData(false, "Shortcut: none. Click the tray icon to open your prompts.")]
    public void ShortcutSummary(bool registered, string expected)
        => Assert.Equal(expected, OnboardingText.ShortcutSummary("Ctrl+Alt+P", registered));

    [Fact]
    public void BackupSummary_Off()
        => Assert.Equal("Backups: off", OnboardingText.BackupSummary(null, null));

    [Fact]
    public void BackupSummary_NamesTheProvider()
        => Assert.Equal("Backups: OneDrive", OnboardingText.BackupSummary(@"C:\Users\a\OneDrive\Prompt Library Backups", "OneDrive"));

    [Fact]
    public void BackupSummary_ChosenFolder_ShowsThePath()
        => Assert.Equal(@"Backups: D:\Backups", OnboardingText.BackupSummary(@"D:\Backups", null));

    [Fact]
    public void ReadyText_WithShortcutAndPaste()
        => Assert.Equal("Press Ctrl+Alt+P in any app and pick a prompt — it's pasted where you were typing. Prompt Library waits in the tray.",
            OnboardingText.ReadyText("Ctrl+Alt+P", registered: true, pasteAfterCopy: true));

    [Fact]
    public void ReadyText_WithoutPaste_SaysItIsCopied()
        => Assert.Equal("Press Ctrl+Alt+P in any app and pick a prompt — it's copied, ready to paste with Ctrl+V. Prompt Library waits in the tray.",
            OnboardingText.ReadyText("Ctrl+Alt+P", registered: true, pasteAfterCopy: false));

    [Fact]
    public void ReadyText_WithoutShortcut_PointsToTheTray()
        => Assert.Equal("Prompt Library waits in the tray, near the clock.",
            OnboardingText.ReadyText("Ctrl+Alt+P", registered: false, pasteAfterCopy: true));
}
