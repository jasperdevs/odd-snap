using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Yoink.Helpers;

/// <summary>
/// Loads and caches Streamline Micro icons from embedded PNG resources.
/// Four variants per icon: line (inactive) / solid (active) × light / dark theme.
/// Resource names: {id}.png, {id}_dark.png, {id}_solid.png, {id}_solid_dark.png
/// </summary>
public static class StreamlineIcons
{
    private static readonly Dictionary<string, Bitmap?> _cache = new();

    private static readonly string[] KnownIcons =
    {
        "rect", "free", "ocr", "sticker", "picker", "scan",
        "upscale",
        "ai_redirect",
        "select", "arrow", "curvedArrow", "text", "highlight", "blur",
        "step", "draw", "line", "ruler", "rectShape", "circleShape",
        "emoji", "eraser", "gear", "close", "more", "record", "folder",
        "download", "pin", "save", "trash", "copy"
    };

    public static void Preload()
    {
        foreach (var id in KnownIcons)
        {
            LoadCached(id, "");
            LoadCached(id, "_solid");
        }
    }

    private static Bitmap? LoadCached(string id, string suffix)
    {
        var cacheKey = $"{id}{suffix}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resourceName = $"Yoink.Resources.Icons.{id}{suffix}.png";
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _cache[cacheKey] = null;
            return null;
        }

        var bmp = new Bitmap(stream);
        _cache[cacheKey] = bmp;
        return bmp;
    }

    /// <summary>Gets the icon bitmap: line (inactive) or solid (active). All icons are white; tint at draw time.</summary>
    public static Bitmap? GetIcon(string id, bool active = false)
    {
        var suffix = active ? "_solid" : "";
        return LoadCached(id, suffix);
    }

    public static bool HasIcon(string id) => LoadCached(id, "") != null;

    /// <summary>
    /// Draws a Streamline icon into the given bounds.
    /// Uses line variant normally, solid when active. Picks light/dark based on theme.
    /// </summary>
    public static void DrawIcon(Graphics g, string id, RectangleF bounds, Color color, float iconInset = 7f, bool active = false)
    {
        var bmp = GetIcon(id, active);
        if (bmp == null) return;

        var prevSmooth = g.SmoothingMode;
        var prevInterp = g.InterpolationMode;
        var prevPixel = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var dest = new RectangleF(
            bounds.X + iconInset,
            bounds.Y + iconInset,
            bounds.Width - iconInset * 2f,
            bounds.Height - iconInset * 2f);

        // Icons are stored as white-on-transparent PNGs and tinted to the requested color.
        float r = color.R / 255f;
        float gr = color.G / 255f;
        float b2 = color.B / 255f;
        float a = color.A / 255f;

        using var attrs = new ImageAttributes();
        var cm = new ColorMatrix(new[]
        {
            new[] { r, 0f, 0f, 0f, 0f },
            new[] { 0f, gr, 0f, 0f, 0f },
            new[] { 0f, 0f, b2, 0f, 0f },
            new[] { 0f, 0f, 0f, a,  0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        g.DrawImage(bmp,
            new[] {
                new PointF(dest.X, dest.Y),
                new PointF(dest.Right, dest.Y),
                new PointF(dest.X, dest.Bottom)
            },
            new RectangleF(0, 0, bmp.Width, bmp.Height),
            GraphicsUnit.Pixel,
            attrs);

        g.SmoothingMode = prevSmooth;
        g.InterpolationMode = prevInterp;
        g.PixelOffsetMode = prevPixel;
    }

    /// <summary>
    /// Renders a Streamline icon as a WPF BitmapSource for settings, wizards, and other WPF UI.
    /// Tints the white source icon to the specified color.
    /// </summary>
    public static BitmapSource? RenderWpf(string id, Color color, int size, bool active = false)
    {
        var bmp = GetIcon(id, active);
        if (bmp == null) return null;

        using var result = new Bitmap(size, size);
        using (var g = Graphics.FromImage(result))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float inset = size * 0.08f;
            var dest = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);

            float r = color.R / 255f;
            float gr = color.G / 255f;
            float b = color.B / 255f;
            float a = color.A / 255f;

            using var attrs = new ImageAttributes();
            var cm = new ColorMatrix(new[]
            {
                new[] { r, 0f, 0f, 0f, 0f },
                new[] { 0f, gr, 0f, 0f, 0f },
                new[] { 0f, 0f, b,  0f, 0f },
                new[] { 0f, 0f, 0f, a,  0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            });
            attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            g.DrawImage(bmp,
                new[] {
                    new PointF(dest.X, dest.Y),
                    new PointF(dest.Right, dest.Y),
                    new PointF(dest.X, dest.Bottom)
                },
                new RectangleF(0, 0, bmp.Width, bmp.Height),
                GraphicsUnit.Pixel,
                attrs);
        }
        return BitmapPerf.ToBitmapSource(result);
    }
}
