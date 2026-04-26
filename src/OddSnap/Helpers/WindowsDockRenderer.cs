using System.Drawing;
using System.Drawing.Drawing2D;

namespace OddSnap.Helpers;

public static class WindowsDockRenderer
{
    public const int SurfaceHeight = UiChrome.ToolbarHeight;
    public const int IconButtonSize = UiChrome.ToolbarButtonSize;
    public const int ButtonSpacing = UiChrome.ToolbarButtonSpacing;
    public const int SurfacePadding = UiChrome.ToolbarInnerPadding;
    public const int SurfaceRadius = 8;

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintSurface(Graphics g, RectangleF rect, float radius = SurfaceRadius)
    {
        PaintShadow(g, rect, radius);

        using var path = RoundedRect(rect, radius);
        using var bg = new SolidBrush(UiChrome.SurfacePill);
        g.FillPath(bg, path);
    }

    public static void PaintShadow(Graphics g, RectangleF rect, float radius)
    {
        var ambient = rect;
        ambient.Inflate(6f, 6f);
        ambient.Offset(0, 1.5f);
        using (var path = RoundedRect(ambient, radius + 8f))
        using (var brush = new SolidBrush(Color.FromArgb(UiChrome.IsDark ? 10 : 8, 0, 0, 0)))
            g.FillPath(brush, path);

        var key = rect;
        key.Inflate(2f, 2f);
        key.Offset(0, 3f);
        using (var path = RoundedRect(key, radius + 3f))
        using (var brush = new SolidBrush(Color.FromArgb(UiChrome.IsDark ? 14 : 10, 0, 0, 0)))
            g.FillPath(brush, path);
    }

    public static void PaintButton(Graphics g, RectangleF rect, bool active, bool hovered, float radius = 5f)
    {
        if (!active && !hovered)
            return;

        int alpha = active
            ? (UiChrome.IsDark ? 28 : 20)
            : (UiChrome.IsDark ? 18 : 14);
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        g.FillPath(brush, path);
    }

    public static void PaintDivider(Graphics g, Point a, Point b)
    {
        using var pen = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
        g.DrawLine(pen, a, b);
    }

    public static void PaintIcon(Graphics g, string iconId, Rectangle bounds, Color color, bool active = false)
    {
        FluentIcons.DrawIcon(g, iconId, bounds, color, active ? 5.5f : 6.5f, active);
    }
}
