using PromptLibrary.Core.Models;
using PromptLibrary.Core.Onboarding;

namespace PromptLibrary.Core.Tests;

public class OnboardingFlowTests
{
    [Fact]
    public void StartsOnWelcome_WithSkipSetupAndNoBack()
    {
        var flow = new OnboardingFlow();

        Assert.Equal(OnboardingStep.Welcome, flow.Step);
        Assert.Equal("Step 1 of 4", flow.StepText);
        Assert.False(flow.CanGoBack);
        Assert.True(flow.ShowsSkipSetup);
        Assert.False(flow.ShowsSkipBackups(backupsOn: false));
        Assert.Equal("Next", flow.PrimaryText);
    }

    [Fact]
    public void Next_WalksThroughAllFourSteps_ThenStops()
    {
        var flow = new OnboardingFlow();

        Assert.True(flow.Next());
        Assert.Equal(OnboardingStep.Shortcut, flow.Step);
        Assert.True(flow.Next());
        Assert.Equal(OnboardingStep.Backups, flow.Step);
        Assert.True(flow.Next());
        Assert.Equal(OnboardingStep.Ready, flow.Step);
        Assert.Equal("Step 4 of 4", flow.StepText);
        Assert.Equal("Finish", flow.PrimaryText);
        Assert.True(flow.IsLastStep);

        Assert.False(flow.Next());
        Assert.Equal(OnboardingStep.Ready, flow.Step);
    }

    [Fact]
    public void Back_GoesToPreviousStep_AndStopsAtWelcome()
    {
        var flow = new OnboardingFlow();
        flow.Next();
        flow.Next();

        Assert.True(flow.Back());
        Assert.Equal(OnboardingStep.Shortcut, flow.Step);
        Assert.True(flow.Back());
        Assert.Equal(OnboardingStep.Welcome, flow.Step);
        Assert.False(flow.Back());
        Assert.Equal(OnboardingStep.Welcome, flow.Step);
    }

    [Theory]
    [InlineData(OnboardingStep.Welcome, true, false)]
    [InlineData(OnboardingStep.Shortcut, false, false)]
    [InlineData(OnboardingStep.Backups, false, true)]
    [InlineData(OnboardingStep.Ready, false, false)]
    public void OneQuietLinkAtMost_SkipSetupOnStep1_SkipBackupsOnStep3(OnboardingStep step, bool skipSetup, bool skipBackups)
    {
        var flow = AtStep(step);

        Assert.Equal(skipSetup, flow.ShowsSkipSetup);
        Assert.Equal(skipBackups, flow.ShowsSkipBackups(backupsOn: false));
    }

    [Fact]
    public void SkipBackups_IsHiddenOnceBackupsAreOn()
        => Assert.False(AtStep(OnboardingStep.Backups).ShowsSkipBackups(backupsOn: true));

    [Theory]
    [InlineData(OnboardingStep.Welcome, true, false)]
    [InlineData(OnboardingStep.Shortcut, false, false)]
    [InlineData(OnboardingStep.Shortcut, true, true)]
    [InlineData(OnboardingStep.Backups, true, true)]
    [InlineData(OnboardingStep.Ready, true, true)]
    public void AsksBeforeLeaving_OnlyAfterStep1_OnceABackupFolderWasChosen(OnboardingStep step, bool chosen, bool asks)
        => Assert.Equal(asks, AtStep(step).AsksBeforeLeaving(backupFolderChosen: chosen));

    [Fact]
    public void Skip_MarksFirstRunDone_AndKeepsDefaults()
    {
        var settings = new AppSettings();

        OnboardingFlow.ApplySkip(settings);

        Assert.True(settings.FirstRunDone);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.PasteAfterCopy);
        Assert.Equal("Ctrl+Alt+P", settings.Hotkey);
        Assert.Null(settings.BackupFolder);
    }

    [Fact]
    public void Finish_AppliesBothCheckboxes_AndMarksFirstRunDone()
    {
        var settings = new AppSettings();

        OnboardingFlow.ApplyFinish(settings, startWithWindows: false, pasteAfterCopy: false);

        Assert.True(settings.FirstRunDone);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.PasteAfterCopy);
    }

    [Fact]
    public void Finish_WithBothChecked_TurnsBothOn()
    {
        var settings = new AppSettings { StartWithWindows = false, PasteAfterCopy = false };

        OnboardingFlow.ApplyFinish(settings, startWithWindows: true, pasteAfterCopy: true);

        Assert.True(settings.StartWithWindows);
        Assert.True(settings.PasteAfterCopy);
    }

    private static OnboardingFlow AtStep(OnboardingStep step)
    {
        var flow = new OnboardingFlow();
        while (flow.Step != step) flow.Next();
        return flow;
    }
}

public class OnboardingLeaveTests
{
    [Fact]
    public void LeavingFromStep4_KeepsTheCheckboxChoices()
    {
        var flow = new OnboardingFlow();
        while (!flow.IsLastStep) flow.Next();
        var settings = new AppSettings();

        flow.ApplyLeave(settings, startWithWindows: false, pasteAfterCopy: false);

        Assert.True(settings.FirstRunDone);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.PasteAfterCopy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LeavingBeforeStep4_KeepsTheDefaults(int nexts)
    {
        var flow = new OnboardingFlow();
        for (var i = 0; i < nexts - 1; i++) flow.Next();
        var settings = new AppSettings();

        flow.ApplyLeave(settings, startWithWindows: false, pasteAfterCopy: false);

        Assert.True(settings.FirstRunDone);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.PasteAfterCopy);
    }
}
