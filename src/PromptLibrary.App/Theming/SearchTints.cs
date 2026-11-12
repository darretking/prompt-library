using System.Windows.Media;

namespace PromptLibrary.App.Theming;

/// <summary>
/// The search window's layers. Over the Acrylic backdrop the chrome (search bar, footer), the list and the preview are
/// translucent tints of the theme's Surface and Window colours so the backdrop shows through; without the backdrop they
/// are the solid theme colours. Text stays readable on the worst backdrop (pure black or pure white behind the tint):
/// the muted text is a little stronger than the theme's Muted while the window is translucent.
/// </summary>
public readonly record struct SearchTints(Color Chrome, Color List, Color Preview, Color Muted)
{
    /// <summary>Opacity of the tints over Acrylic, light theme (0.72).</summary>
    public const byte LightAlpha = 184;

    /// <summary>Opacity of the tints over Acrylic, dark theme (0.78).</summary>
    public const byte DarkAlpha = 199;

    // Muted text while translucent: darker (light theme) or lighter (dark theme) than the theme's Muted, so it keeps
    // 4.5:1 against the tint over pure black / pure white.
    private static readonly Color LightTranslucentMuted = Color.FromRgb(0x34, 0x3C, 0x4B);
    private static readonly Color DarkTranslucentMuted = Color.FromRgb(0xCB, 0xD1, 0xDD);

    /// <summary>The layers for a theme (<paramref name="surface"/>, <paramref name="window"/> and <paramref name="muted"/> are its tokens).</summary>
    public static SearchTints For(bool dark, bool translucent, Color surface, Color window, Color muted)
    {
        if (!translucent) return new SearchTints(surface, window, surface, muted);
        var alpha = dark ? DarkAlpha : LightAlpha;
        return new SearchTints(With(surface, alpha), With(window, alpha), With(surface, alpha),
            dark ? DarkTranslucentMuted : LightTranslucentMuted);
    }

    private static Color With(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary><paramref name="top"/> drawn over an opaque <paramref name="below"/>.</summary>
    public static Color Over(Color top, Color below)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * a + b * (1 - a));
        return Color.FromRgb(Mix(top.R, below.R), Mix(top.G, below.G), Mix(top.B, below.B));
    }

    /// <summary>WCAG 2 contrast ratio of two opaque colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo) (hi, lo) = (lo, hi);
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
