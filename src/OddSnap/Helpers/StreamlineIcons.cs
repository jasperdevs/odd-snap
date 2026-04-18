using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaRect = System.Windows.Rect;

namespace OddSnap.Helpers;

/// <summary>
/// Shared icon facade backed by Microsoft Fluent UI System Icons SVG path data.
/// Normal state uses Regular; active/hover/selected uses Filled.
/// </summary>
public static class StreamlineIcons
{
    private const int ViewBoxSize = 20;
    private static readonly ConcurrentDictionary<string, BitmapSource?> WpfCache = new();
    private static readonly ConcurrentDictionary<string, Geometry?> GeometryCache = new();

    public static void Preload()
    {
        _ = FluentIconData.Icons.Count;
    }

    public static Bitmap? GetIcon(string id, bool active = false)
        => RenderBitmap(id, DrawingColor.White, 32, active);

    public static Bitmap? RenderBitmap(string id, DrawingColor color, int size, bool active = false)
    {
        var source = RenderWpf(id, color, size, active);
        if (source is null)
            return null;

        var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, DrawingPixelFormat.Format32bppPArgb);
        var rect = new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppPArgb);
        try
        {
            source.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public static bool HasIcon(string id) => FluentIconData.Icons.ContainsKey(id);

    public static void DrawIcon(DrawingGraphics g, string id, RectangleF bounds, DrawingColor color, float iconInset = 7f, bool active = false)
    {
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width - iconInset * 2f));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height - iconInset * 2f));
        int size = Math.Max(width, height);
        using var bitmap = RenderBitmap(id, color, size, active);
        if (bitmap is null)
            return;

        var dest = new RectangleF(
            bounds.X + iconInset,
            bounds.Y + iconInset,
            bounds.Width - iconInset * 2f,
            bounds.Height - iconInset * 2f);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(bitmap, dest);
    }

    public static BitmapSource? RenderWpf(string id, DrawingColor color, int size, bool active = false)
    {
        var key = $"{id}|{active}|{color.ToArgb()}|{size}";
        return WpfCache.GetOrAdd(key, _ => RenderWpfUncached(id, color, size, active));
    }

    private static BitmapSource? RenderWpfUncached(string id, DrawingColor color, int size, bool active)
    {
        if (!FluentIconData.Icons.TryGetValue(id, out var icon))
            return null;

        var pathData = active ? icon.Filled : icon.Regular;
        if (string.IsNullOrWhiteSpace(pathData))
            return null;

        var geometryKey = $"{id}|{active}";
        var geometry = GeometryCache.GetOrAdd(geometryKey, _ => ParseGeometry(pathData));
        if (geometry is null)
            return null;

        var brush = new SolidColorBrush(MediaColor.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();

        var drawing = new GeometryDrawing(brush, null, geometry);
        var group = new DrawingGroup();
        group.Children.Add(drawing);

        double inset = Math.Max(1.0, size * 0.06);
        double scale = (size - inset * 2) / ViewBoxSize;
        group.Transform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform(inset, inset)
            }
        };
        group.Freeze();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(MediaBrushes.Transparent, null, new MediaRect(0, 0, size, size));
            dc.DrawDrawing(group);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Geometry? ParseGeometry(string pathData)
    {
        try
        {
            var geometry = Geometry.Parse(pathData);
            geometry.Freeze();
            return geometry;
        }
        catch
        {
            return null;
        }
    }
}
