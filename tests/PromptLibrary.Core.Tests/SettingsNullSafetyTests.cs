using PromptLibrary.Core.Clipboard;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Storage;
using PromptLibrary.Core.Usage;

namespace PromptLibrary.Core.Tests;

public sealed class SettingsNullSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PromptLibraryTests-" + Guid.NewGuid().ToString("N"));

    public SettingsNullSafetyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void ExplicitNullCollections_LoadAsEmpty()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "usage": null, "placeholderValues": null, "pinnedPromptIds": null, "hiddenTeamPromptIds": null,
              "seenTeamPrompts": null, "preRestoreBackupFiles": null, "theme": null, "shortcutOpens": null }
            """);

        var s = JsonStore.LoadSettings(path).Value!;

        Assert.Empty(s.Usage);
        Assert.Empty(s.PlaceholderValues);
        Assert.Empty(s.PinnedPromptIds);
        Assert.Empty(s.HiddenTeamPromptIds);
        Assert.Empty(s.SeenTeamPrompts);
        Assert.Empty(s.PreRestoreBackupFiles);
        Assert.Equal("system", s.Theme);
        Assert.Equal("search", s.ShortcutOpens);
        Assert.True(s.RestoreClipboard);
    }

    [Fact]
    public void NullUsageEntry_DoesNotBreakRecording()
    {
        var s = new AppSettings();
        var id = Guid.NewGuid();
        s.Usage[id] = null!;
        UsageTracker.Record(s, id, DateTimeOffset.UtcNow);
        Assert.Equal(1, s.Usage[id].Count);
    }

    [Fact]
    public void RestoreClipboard_RoundTrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        JsonStore.SaveSettings(path, new AppSettings { RestoreClipboard = false });
        Assert.False(JsonStore.LoadSettings(path).Value!.RestoreClipboard);
    }

    [Fact]
    public void ClipboardFormats_TextOnlyOnly()
    {
        Assert.True(ClipboardFormats.IsTextOnly(["UnicodeText", "Text", "OEMText", "Locale"]));
        Assert.False(ClipboardFormats.IsTextOnly(["UnicodeText", "HTML Format"]));
        Assert.False(ClipboardFormats.IsTextOnly(["Bitmap"]));
        Assert.False(ClipboardFormats.IsTextOnly([]));
    }
}
