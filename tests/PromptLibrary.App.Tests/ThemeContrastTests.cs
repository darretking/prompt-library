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
        Check("Brush.OnBrand", "Brush.BrandFill");
        Check("Brush.OnBrand", "Brush.BrandHover");
        Check("Brush.OnBrand", "Brush.DangerFill");
        Check("Brush.OnBrand", "Brush.DangerHover");
        Check("Brush.WarnText", "Brush.Warn");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
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
