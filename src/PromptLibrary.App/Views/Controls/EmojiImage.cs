using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PromptLibrary.App.Views.Controls;

/// <summary>
/// Draws an emoji string as a bundled Microsoft Fluent Emoji colour image (Assets/Emoji, 32 px and @2x 64 px). An emoji
/// outside the bundled set falls back to a plain Segoe UI Emoji text. Images are decoded once, frozen and cached.
/// </summary>
public sealed class EmojiImage : Decorator
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmojiImage), new PropertyMetadata(null, (d, _) => ((EmojiImage)d).Rebuild()));

    /// <summary>The nominal size, as the font size the text fallback uses; the image is 2 px larger.</summary>
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(EmojiImage), new PropertyMetadata(16.0, (d, _) => ((EmojiImage)d).Rebuild()));

    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();

    public EmojiImage()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>The asset file stem for an emoji: lower-case hex code points joined by "-", without variation selectors (U+FE0F).</summary>
    public static string AssetName(string emoji)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1)
        {
            var cp = char.ConvertToUtf32(emoji, i);
            if (cp == 0xFE0F) continue;
            if (sb.Length > 0) sb.Append('-');
            sb.Append(cp.ToString("x", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The pack URI of the bundled image for an emoji, at the given scale (existence is not checked).</summary>
    public static Uri AssetUri(string emoji, bool hiDpi)
        => new($"pack://application:,,,/Assets/Emoji/{AssetName(emoji)}{(hiDpi ? "@2x" : "")}.png", UriKind.Absolute);

    /// <summary>The cached, frozen image for an emoji at the given scale, or null when no bundled image exists.</summary>
    public static ImageSource? Load(string? emoji, bool hiDpi)
    {
        if (string.IsNullOrEmpty(emoji)) return null;
        return Cache.GetOrAdd(AssetName(emoji) + (hiDpi ? "@2x" : ""), _ =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = AssetUri(emoji, hiDpi);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or InvalidOperationException)
            {
                return null; // not in the bundled set
            }
        });
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => Rebuild();

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        Rebuild();
    }

    private void Rebuild()
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            Child = null;
            return;
        }
        var hiDpi = VisualTreeHelper.GetDpi(this).DpiScaleX > 1.25;
        var source = Load(text, hiDpi);
        if (source is null)
        {
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = FontSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }
        var size = FontSize + 2;
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Child = image;
    }
}
