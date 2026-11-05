using PromptLibrary.Core.Hotkeys;

namespace PromptLibrary.Core.Tests;

public class ShortcutSuggestionTests
{
    [Fact]
    public void InUseText_NamesTheFirstFreeCandidate()
        => Assert.Equal("That shortcut is in use by another app. Try another, like Ctrl+Alt+L.",
            ShortcutSuggestion.InUseText(_ => true));

    [Fact]
    public void InUseText_SkipsCandidatesThatAreTaken()
    {
        var probed = new List<string>();
        var text = ShortcutSuggestion.InUseText(candidate =>
        {
            probed.Add(candidate);
            return candidate == "Ctrl+Alt+Shift+P";
        });

        Assert.Equal("That shortcut is in use by another app. Try another, like Ctrl+Alt+Shift+P.", text);
        Assert.Equal(["Ctrl+Alt+L", "Ctrl+Alt+K", "Ctrl+Alt+Shift+P"], probed);
    }

    [Fact]
    public void InUseText_NoCandidateFree_AsksForADifferentCombination()
        => Assert.Equal("That shortcut is in use by another app. Try a different combination.",
            ShortcutSuggestion.InUseText(_ => false));

    [Fact]
    public void Candidates_AreValidShortcuts_AndNeverCtrlShiftP()
    {
        foreach (var candidate in ShortcutSuggestion.Candidates)
        {
            Assert.True(HotkeyGesture.TryParse(candidate, out var gesture));
            Assert.Equal(candidate, gesture!.ToString());
            Assert.NotEqual("Ctrl+Shift+P", candidate);
        }
    }
}
