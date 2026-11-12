using System.Windows;
using System.Windows.Media;
using PromptLibrary.App.Theming;

namespace PromptLibrary.App.Tests;

/// <summary>The search window's translucent layers keep text at 4.5:1 even on the worst backdrop (pure black or pure white).</summary>
[Collection("WpfApplication")]
public class SearchTintContrastTests
{
    public static TheoryData<string> Themes => new() { "light", "dark" };

    // The theme's own dictionary, not the live app resources (ThemeManager remembers its mode across the app instances tests create).
    private static ResourceDictionary Palette(string theme)
        => new() { Source = new Uri($"/PromptLibrary;component/Themes/{(theme == "dark" ? "Dark" : "Light")}.xaml", UriKind.Relative) };

    [Theory]
    [MemberData(nameof(Themes))]
    public void TranslucentLayers_KeepInkAndMutedReadable_OnBlackAndOnWhite(string theme) => Sta.Run(() =>
    {
        ChoiceTypingTests.StartApp();
        var palette = Palette(theme);
        Color Token(string key) => ((SolidColorBrush)palette[key]).Color;
        var tints = SearchTints.For(theme == "dark", translucent: true, Token("Brush.Surface"), Token("Brush.Window"), Token("Brush.Muted"));
        var failures = new List<string>();

        foreach (var (layerName, layer) in new[] { ("chrome", tints.Chrome), ("list", tints.List), ("preview", tints.Preview) })
        foreach (var (backdropName, backdrop) in new[] { ("black", Colors.Black), ("white", Colors.White) })
        {
            var seen = SearchTints.Over(layer, backdrop);
            foreach (var (textName, text) in new[] { ("Ink", Token("Brush.Ink")), ("Muted", tints.Muted) })
            {
                var ratio = SearchTints.Contrast(text, seen);
                if (ratio < 4.5) failures.Add($"{theme}: {textName} on {layerName} over {backdropName} = {ratio:0.00}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    });

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheTintsAreTranslucent_AndTheSolidFallbackIsTheThemeColour(string theme) => Sta.Run(() =>
    {
        ChoiceTypingTests.StartApp();
        var palette = Palette(theme);
        Color Token(string key) => ((SolidColorBrush)palette[key]).Color;
        var dark = theme == "dark";
        var glass = SearchTints.For(dark, true, Token("Brush.Surface"), Token("Brush.Window"), Token("Brush.Muted"));
        Assert.Equal(dark ? 199 : 184, glass.Chrome.A);
        Assert.True(glass.List.A < 255 && glass.Preview.A < 255);

        var solid = SearchTints.For(dark, false, Token("Brush.Surface"), Token("Brush.Window"), Token("Brush.Muted"));
        Assert.Equal(Token("Brush.Surface"), solid.Chrome);
        Assert.Equal(Token("Brush.Window"), solid.List);
        Assert.Equal(Token("Brush.Muted"), solid.Muted);
        Assert.Equal(255, solid.Chrome.A);
    });
}
