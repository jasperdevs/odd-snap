using System.Drawing;

namespace OddSnap.Helpers;

public static class WindowsHandleRenderer
{
    public const int Size = 9;
    public const int HitSize = 22;

    public static RectangleF CenteredAt(PointF point) =>
        new(point.X - Size / 2f, point.Y - Size / 2f, Size, Size);

    public static Rectangle HitRect(Point point) =>
        new(point.X - HitSize / 2, point.Y - HitSize / 2, HitSize, HitSize);

    public static void Paint(Graphics g, RectangleF rect)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shadowPath = WindowsDockRenderer.RoundedRect(new RectangleF(rect.X + 1.2f, rect.Y + 1.2f, rect.Width, rect.Height), 3f);
        using var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
        g.FillPath(shadow, shadowPath);

        using var path = WindowsDockRenderer.RoundedRect(rect, 3f);
        using var fill = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.FillPath(fill, path);
    }
}
