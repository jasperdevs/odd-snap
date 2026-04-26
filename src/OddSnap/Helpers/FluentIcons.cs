using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaColor = System.Windows.Media.Color;
using MediaRect = System.Windows.Rect;

namespace OddSnap.Helpers;

/// <summary>
/// Shared icon facade backed by Windows Fluent/MDL2 icon fonts.
/// Uses Segoe Fluent Icons on Windows 11 and Segoe MDL2 Assets as the Windows 10 fallback.
/// </summary>
public static class FluentIcons
{
    private const string IconFontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private static readonly ConcurrentDictionary<string, BitmapSource?> WpfCache = new();

    private static readonly IReadOnlyDictionary<string, string> Glyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["rect"] = "\uE257",
        ["center"] = "\uE257",
        ["free"] = "\uE1CE",
        ["ocr"] = "\uE53C",
        ["sticker"] = "\uE8A5",
        ["upscale"] = "\uE740",
        ["picker"] = "\uE13E",
        ["scan"] = "\uE1DE",
        ["select"] = "\uE1E3",
        ["arrow"] = "\uE051",
        ["curvedArrow"] = "\uE146",
        ["text"] = "\uE197",
        ["highlight"] = "\uE0F7",
        ["blur"] = "\uE5A0",
        ["step"] = "\uE1D0",
        ["draw"] = "\uE1F8",
        ["line"] = "\uE11F",
        ["ruler"] = "\uE14E",
        ["magnifier"] = "\uE721",
        ["rectShape"] = "\uE16A",
        ["circleShape"] = "\uE07A",
        ["emoji"] = "\uE167",
        ["eraser"] = "\uE28E",
        ["fullscreen"] = "\uE740",
        ["activeWindow"] = "\uE7C4",
        ["scrollCapture"] = "\uE7C3",
        ["record"] = "\uE7C8",
        ["stop"] = "\uE15B",
        ["stopSquare"] = "\uE15B",
        ["folder"] = "\uE8B7",
        ["filter"] = "\uE71C",
        ["gpu"] = "\uE950",
        ["cpu"] = "\uE950",
        ["download"] = "\uE896",
        ["pin"] = "\uE718",
        ["save"] = "\uE74E",
        ["trash"] = "\uE74D",
        ["copy"] = "\uE8C8",
        ["ai_redirect"] = "\uE945",
        ["search"] = "\uE721",
        ["warning"] = "\uE7BA",
        ["minimize"] = "\uE921",
        ["close"] = "\uE8BB",
        ["more"] = "\uE712",
        ["gear"] = "\uE713",
    };

    public static void Preload()
    {
        using var font = CreateDrawingFont(16f);
        _ = font.Name;
    }

    public static Bitmap? GetIcon(string id, bool active = false)
        => RenderBitmap(id, DrawingColor.White, 32, active);

    public static Bitmap? RenderBitmap(string id, DrawingColor color, int size, bool active = false)
    {
        if (!TryGetGlyph(id, out var glyph))
            return null;

        var bitmap = new Bitmap(size, size, DrawingPixelFormat.Format32bppPArgb);
        using var g = DrawingGraphics.FromImage(bitmap);
        g.Clear(DrawingColor.Transparent);
        DrawGlyph(g, glyph, new RectangleF(0, 0, size, size), color, size * 0.78f);
        return bitmap;
    }

    public static bool HasIcon(string id) => Glyphs.ContainsKey(id);

    public static void DrawIcon(DrawingGraphics g, string id, RectangleF bounds, DrawingColor color, float iconInset = 7f, bool active = false)
    {
        if (!TryGetGlyph(id, out var glyph))
            return;

        var dest = new RectangleF(
            bounds.X + iconInset,
            bounds.Y + iconInset,
            bounds.Width - iconInset * 2f,
            bounds.Height - iconInset * 2f);
        DrawGlyph(g, glyph, dest, color, dest.Height * 0.9f);
    }

    public static BitmapSource? RenderWpf(string id, DrawingColor color, int size, bool active = false)
    {
        var key = $"{id}|{color.ToArgb()}|{size}";
        return WpfCache.GetOrAdd(key, _ => RenderWpfUncached(id, color, size));
    }

    private static BitmapSource? RenderWpfUncached(string id, DrawingColor color, int size)
    {
        if (!TryGetGlyph(id, out var glyph))
            return null;

        var brush = new SolidColorBrush(MediaColor.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new MediaRect(0, 0, size, size));
            var text = new FormattedText(
                glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(new System.Windows.Media.FontFamily(IconFontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size * 0.78,
                brush,
                1.0);
            dc.DrawText(text, new System.Windows.Point((size - text.Width) / 2.0, (size - text.Height) / 2.0));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool TryGetGlyph(string id, out string glyph) => Glyphs.TryGetValue(id, out glyph!);

    private static void DrawGlyph(DrawingGraphics g, string glyph, RectangleF bounds, DrawingColor color, float fontSize)
    {
        var oldTextRenderingHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = CreateDrawingFont(fontSize);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip
        };
        g.DrawString(glyph, font, brush, bounds, format);
        g.TextRenderingHint = oldTextRenderingHint;
    }

    private static DrawingFont CreateDrawingFont(float size)
    {
        try
        {
            return new DrawingFont("Segoe Fluent Icons", size, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch
        {
            return new DrawingFont("Segoe MDL2 Assets", size, DrawingFontStyle.Regular, GraphicsUnit.Pixel);
        }
    }
}
