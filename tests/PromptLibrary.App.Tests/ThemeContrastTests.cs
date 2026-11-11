using System.IO;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Media;

namespace PromptLibrary.App.Tests;

/// <summary>WCAG 2 contrast of the text/background pairs in Themes/Light.xaml and Themes/Dark.xaml.</summary>
public class ThemeContrastTests
{
    private const double Minimum = 4.5;

    public static TheoryData<string> Themes => new() { "Light", "Dark" };

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_pairs_meet_4_5_to_1(string theme)
    {
        var palette = Load(theme);
        var failures = new List<string>();

        void Check(string text, string background)
        {
            var ratio = Contrast(palette[text], palette[background]);
            if (ratio < Minimum)
                failures.Add($"{theme}: {text} on {background} = {ratio:0.00}");
        }

        foreach (var background in new[] { "Brush.Window", "Brush.Rail", "Brush.Surface" })
        {
            Check("Brush.Ink", background);
            Check("Brush.Muted", background);
        }
        Check("Brush.Brand", "Brush.Surface");
        Check("Brush.Danger", "Brush.Surface");
        Check("Brush.Danger", "Brush.Window"); // helper text of the delete-category dialog, import failure reasons
        Check("Brush.Brand", "Brush.Window");
        Check("Brush.OnBrand", "Brush.BrandFill");
        Check("Brush.OnBrand", "Brush.BrandHover");
        Check("Brush.OnBrand", "Brush.DangerFill");
        Check("Brush.OnBrand", "Brush.DangerHover");
        Check("Brush.WarnText", "Brush.Warn");
        Check("Brush.Brand", "Brush.BrandSoft");
        Check("Brush.Ink", "Brush.CodeBlock");
        Check("Brush.Muted", "Brush.CodeBlock");
        Check("Brush.DiffAddedText", "Brush.DiffAdded");
        Check("Brush.DiffRemovedText", "Brush.DiffRemoved");

        // Row hover is BrandSoft at 50 % over whatever the row sits on; text and matched letters stay readable on it.
        foreach (var background in new[] { "Brush.Window", "Brush.Rail", "Brush.Surface" })
        {
            var hover = Over(palette["Brush.Hover"], palette[background]);
            foreach (var text in new[] { "Brush.Ink", "Brush.Muted", "Brush.Brand" })
            {
                var ratio = Contrast(palette[text], hover);
                if (ratio < Minimum)
                    failures.Add($"{theme}: {text} on Hover over {background} = {ratio:0.00}");
            }
        }
        // Tag chips: muted 12 px text on BrandSoft.
        Check("Brush.Muted", "Brush.BrandSoft");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Secondary text (descriptions, counts, hints) is read all day: 5.5:1 on every layer it sits on, not just 4.5:1.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void Muted_text_meets_5_5_to_1_on_window_rail_surface_and_the_soft_fills(string theme)
    {
        var palette = Load(theme);
        var failures = new List<string>();
        foreach (var background in new[] { "Brush.Window", "Brush.Rail", "Brush.Surface", "Brush.BrandSoft", "Brush.CodeBlock" })
        {
            var ratio = Contrast(palette["Brush.Muted"], palette[background]);
            if (ratio < 5.5) failures.Add($"{theme}: Brush.Muted on {background} = {ratio:0.00}");
        }
        foreach (var background in new[] { "Brush.Window", "Brush.Rail", "Brush.Surface" })
        {
            var hover = Over(palette["Brush.Hover"], palette[background]);
            var ratio = Contrast(palette["Brush.Muted"], hover);
            if (ratio < 5.5) failures.Add($"{theme}: Brush.Muted on Hover over {background} = {ratio:0.00}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>The editor card's shadow: black, 8 % in light and 30 % in dark.</summary>
    [Theory]
    [InlineData("Light", 0.08)]
    [InlineData("Dark", 0.30)]
    public void Card_shadow_is_black_at_the_documented_strength(string theme, double opacity)
    {
        var shadow = Load(theme)["Brush.Shadow"];
        Assert.Equal((0, 0, 0), (shadow.R, shadow.G, shadow.B));
        Assert.InRange(shadow.A / 255.0, opacity - 0.01, opacity + 0.01);
    }

    [Fact]
    public void Both_themes_define_the_same_keys()
    {
        Assert.Equal(Load("Light").Keys.Order(), Load("Dark").Keys.Order());
    }

    private static Dictionary<string, Color> Load(string theme)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "PromptLibrary.App", "Themes")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        using var stream = File.OpenRead(Path.Combine(dir!, "src", "PromptLibrary.App", "Themes", theme + ".xaml"));
        var dictionary = (ResourceDictionary)XamlReader.Load(stream);
        return dictionary.Keys.Cast<string>().ToDictionary(k => k, k => ((SolidColorBrush)dictionary[k]).Color);
    }

    /// <summary>The colour that shows when <paramref name="top"/> (with its own alpha) lies over the opaque <paramref name="bottom"/>.</summary>
    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo)
            (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
