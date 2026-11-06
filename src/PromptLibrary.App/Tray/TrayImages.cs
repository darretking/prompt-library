using System.Drawing.Drawing2D;
using Drawing = System.Drawing;

namespace PromptLibrary.App.Tray;

/// <summary>Small menu icons drawn in code (16 px at 100 % scaling) in the app's Ink colour.</summary>
internal static class TrayImages
{
    private static readonly Drawing.Color Ink = Drawing.Color.FromArgb(0x1B, 0x22, 0x30);

    /// <summary>Two people: marks team categories and team prompts.</summary>
    public static Drawing.Image Team(double scale) => Draw(scale, g =>
    {
        using var brush = new Drawing.SolidBrush(Ink);
        g.FillEllipse(brush, 9.5f, 3f, 4.5f, 4.5f);
        g.FillPie(brush, 7.5f, 9f, 8.5f, 9f, 180, 180);
        g.FillEllipse(brush, 2.5f, 2.5f, 5f, 5f);
        g.FillPie(brush, 0.5f, 8.5f, 9.5f, 10f, 180, 180);
    });

    /// <summary>A push-pin: marks pinned shortcuts.</summary>
    public static Drawing.Image Pin(double scale) => Draw(scale, g =>
    {
        using var brush = new Drawing.SolidBrush(Ink);
        using var pen = new Drawing.Pen(Ink, 1.6f);
        g.FillEllipse(brush, 4.5f, 1f, 7f, 7f);
        g.FillRectangle(brush, 3f, 7f, 10f, 2f);
        g.DrawLine(pen, 8f, 9f, 8f, 15f);
    });

    private static Drawing.Image Draw(double scale, Action<Drawing.Graphics> paint)
    {
        var size = (int)Math.Round(16 * scale);
        var bitmap = new Drawing.Bitmap(size, size);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform((float)(size / 16.0), (float)(size / 16.0));
        paint(g);
        return bitmap;
    }
}
