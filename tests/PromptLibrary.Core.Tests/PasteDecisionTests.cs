using PromptLibrary.Core.Clipboard;

namespace PromptLibrary.Core.Tests;

public class PasteDecisionTests
{
    [Fact]
    public void ShouldPaste_HotkeyMenu_SettingOn_OtherAppWindow_True()
        => Assert.True(PasteDecision.ShouldPaste(pasteAfterCopy: true, fromShortcutMenu: true,
            hasTargetWindow: true, targetIsOurs: false));

    [Fact]
    public void ShouldPaste_SettingOff_False()
        => Assert.False(PasteDecision.ShouldPaste(pasteAfterCopy: false, fromShortcutMenu: true,
            hasTargetWindow: true, targetIsOurs: false));

    [Fact]
    public void ShouldPaste_TrayMenu_False()
        => Assert.False(PasteDecision.ShouldPaste(pasteAfterCopy: true, fromShortcutMenu: false,
            hasTargetWindow: true, targetIsOurs: false));

    [Fact]
    public void ShouldPaste_NoTargetWindow_False()
        => Assert.False(PasteDecision.ShouldPaste(pasteAfterCopy: true, fromShortcutMenu: true,
            hasTargetWindow: false, targetIsOurs: false));

    [Fact]
    public void ShouldPaste_OurOwnWindow_False()
        => Assert.False(PasteDecision.ShouldPaste(pasteAfterCopy: true, fromShortcutMenu: true,
            hasTargetWindow: true, targetIsOurs: true));

    [Fact]
    public void Message_Pasted_IsNull()
        => Assert.Null(PasteDecision.CopiedMessage("Explain this code", PasteOutcome.Pasted));

    [Fact]
    public void Message_NotAttempted_IsCopied()
        => Assert.Equal("Copied: Explain this code",
            PasteDecision.CopiedMessage("Explain this code", PasteOutcome.NotAttempted));

    [Fact]
    public void Message_Failed_SaysPressCtrlV()
        => Assert.Equal("Copied: Explain this code. Press Ctrl+V to paste.",
            PasteDecision.CopiedMessage("Explain this code", PasteOutcome.Failed));

    [Fact]
    public void Message_Elevated_ExplainsAdministrator()
        => Assert.Equal(
            "Copied: Explain this code. Press Ctrl+V to paste — Windows doesn't allow pasting into apps running as administrator.",
            PasteDecision.CopiedMessage("Explain this code", PasteOutcome.Elevated));

    [Fact]
    public void Settings_WithoutPasteAfterCopy_DefaultsToOn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pl-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "hotkey": "Ctrl+Alt+P" }""");
            Assert.True(Storage.JsonStore.LoadSettings(path).Value!.PasteAfterCopy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(4, 4, PasteOutcome.Pasted)]
    [InlineData(4, 0, PasteOutcome.Failed)]
    [InlineData(4, 2, PasteOutcome.Failed)]
    public void OutcomeOfSend_FewerEventsThanSent_Failed(int sent, int accepted, PasteOutcome expected)
        => Assert.Equal(expected, PasteDecision.OutcomeOfSend(sent, accepted));
}
