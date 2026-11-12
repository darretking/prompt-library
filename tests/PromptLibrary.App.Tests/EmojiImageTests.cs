using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PromptLibrary.App.Views.Controls;
using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;
using PromptLibrary.Core.Team;

namespace PromptLibrary.App.Tests;

[Collection("WpfApplication")]
public class EmojiImageTests
{
    private static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PromptLibrary.App", "Assets", "Emoji");

    private static IEnumerable<string> Needed()
        => EmojiChoices.All
            .Concat(SeedLibrary.Create(DateTimeOffset.UtcNow).Categories.Select(c => c.Emoji))
            .Concat(TeamSeedLibrary.Create(DateTimeOffset.UtcNow).Categories.Select(c => c.Emoji))
            .Where(e => !string.IsNullOrEmpty(e))
            .Select(e => e!)
            .Distinct();

    [Fact]
    public void EveryPickerAndSeedEmoji_HasBothBundledImages()
    {
        var all = Needed().ToList();
        Assert.True(all.Count >= 36);
        foreach (var e in all)
        {
            var name = EmojiImage.AssetName(e);
            Assert.True(File.Exists(Path.Combine(AssetDir, name + ".png")), $"missing {name}.png for {e}");
            Assert.True(File.Exists(Path.Combine(AssetDir, name + "@2x.png")), $"missing {name}@2x.png for {e}");
        }
    }

    [Fact]
    public void AssetName_DropsVariationSelectors() => Assert.Equal("2708", EmojiImage.AssetName("✈️"));

    [Fact]
    public void Control_LoadsBundledImage_AndFallsBackToText() => Sta.RunOnApp(() =>
    {
        ChoiceTypingTests.StartApp();
        Assert.NotNull(EmojiImage.Load("\U0001F4BC", false));
        Assert.NotNull(EmojiImage.Load("\U0001F4BC", true));
        Assert.Same(EmojiImage.Load("\U0001F4BC", false), EmojiImage.Load("\U0001F4BC", false));
        Assert.True(EmojiImage.Load("\U0001F4BC", false)!.IsFrozen);
        Assert.Null(EmojiImage.Load("\U0001F9FF", false)); // nazar amulet: not bundled

        var known = new EmojiImage { Text = "\U0001F4BC" };
        Assert.IsType<Image>(known.Child);
        var unknown = new EmojiImage { Text = "\U0001F9FF" };
        var tb = Assert.IsType<TextBlock>(unknown.Child);
        Assert.Equal("Segoe UI Emoji", tb.FontFamily.Source);
        known.Text = null;
        Assert.Null(known.Child);
    });

    [Fact]
    public void App_DoesNotReferenceEmojiWpf()
    {
        var csproj = File.ReadAllText(Path.Combine(AssetDir, "..", "..", "PromptLibrary.App.csproj"));
        Assert.DoesNotContain("Emoji.Wpf", csproj);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Emoji.Wpf");
    }
}
