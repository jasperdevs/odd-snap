using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Media.Imaging;

namespace Yoink.Helpers;

/// <summary>
/// Shared rendering helpers for semantic tool icons.
/// These are used for icons that should look the same everywhere instead of relying on font glyph availability.
/// </summary>
public static class ToolIcons
{
    public static BitmapSource RenderToolIconWpf(string toolId, char glyph, Color color, int size)
    {
        if (toolId == "sticker")
            return RenderStickerWpf(color, size);

        if (toolId == "_record")
            return RenderRecordWpf(color, size);

        return IconFont.RenderGlyphWpf(glyph, color, size);
    }

    public static BitmapSource RenderStickerWpf(Color color, int size) =>
        RenderShapeWpf(size, g => DrawSticker(g, size, color));

    public static BitmapSource RenderRecordWpf(Color color, int size) =>
        RenderShapeWpf(size, g => DrawRecord(g, size, color));

    public static BitmapSource RenderFolderWpf(Color color, int size) =>
        RenderShapeWpf(size, g => DrawFolder(g, size, color));

    private static BitmapSource RenderShapeWpf(int size, Action<Graphics> draw)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            draw(g);
        }
        return BitmapPerf.ToBitmapSource(bmp);
    }

    private static void DrawSticker(Graphics g, int size, Color color)
    {
        var stroke = Math.Max(1.6f, size * 0.09f);
        var corner = Math.Max(3.2f, size * 0.22f);
        var insetX = size * 0.24f;
        var insetY = size * 0.20f;
        var body = new RectangleF(insetX, insetY, size - insetX * 2f, size - insetY * 2f);

        using var pen = new Pen(color, stroke)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        using var path = new GraphicsPath();
        path.AddArc(body.X, body.Y, corner, corner, 180, 90);
        path.AddLine(body.X + corner / 2f, body.Y, body.Right - corner, body.Y);
        path.AddLine(body.Right - corner, body.Y, body.Right, body.Y + corner / 2f);
        path.AddLine(body.Right, body.Y + corner / 2f, body.Right, body.Bottom - corner / 2f);
        path.AddArc(body.Right - corner, body.Bottom - corner, corner, corner, 0, 90);
        path.AddArc(body.X, body.Bottom - corner, corner, corner, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);

        g.DrawLine(pen, body.Right - corner, body.Y, body.Right - corner, body.Y + corner / 2f);
        g.DrawLine(pen, body.Right - corner, body.Y + corner / 2f, body.Right, body.Y + corner / 2f);
    }

    private static void DrawRecord(Graphics g, int size, Color color)
    {
        var stroke = Math.Max(1.5f, size * 0.08f);
        var ring = new RectangleF(size * 0.19f, size * 0.19f, size * 0.62f, size * 0.62f);
        var dot = new RectangleF(size * 0.41f, size * 0.41f, size * 0.18f, size * 0.18f);

        using var pen = new Pen(color, stroke)
        {
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        g.DrawEllipse(pen, ring);
        g.FillEllipse(brush, dot);
    }

    private static void DrawFolder(Graphics g, int size, Color color)
    {
        var stroke = Math.Max(1.6f, size * 0.09f);
        var x = size * 0.14f;
        var y = size * 0.29f;
        var w = size * 0.72f;
        var h = size * 0.44f;
        var tabW = size * 0.30f;
        var tabH = size * 0.13f;

        using var pen = new Pen(color, stroke)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var brush = new SolidBrush(Color.FromArgb(Math.Min(120, Math.Max(70, color.A / 2)), color.R, color.G, color.B));

        var body = new RectangleF(x, y + tabH, w, h - tabH / 2f);
        var tab = new RectangleF(x + stroke / 2f, y, tabW, tabH + stroke * 0.15f);

        using var bodyPath = new GraphicsPath();
        bodyPath.AddRoundedRectangle(body, size * 0.08f);
        g.FillPath(brush, bodyPath);
        g.DrawPath(pen, bodyPath);

        using var tabPath = new GraphicsPath();
        tabPath.AddRoundedRectangle(tab, size * 0.05f);
        g.FillPath(brush, tabPath);
        g.DrawPath(pen, tabPath);

        g.DrawLine(pen, x + stroke, y + tabH + stroke * 0.35f, x + w - stroke * 0.9f, y + tabH + stroke * 0.35f);

        var arrowTail = new PointF(x + w * 0.60f, y + h * 0.56f);
        var arrowHead = new PointF(x + w * 0.86f, y + h * 0.30f);
        g.DrawLine(pen, arrowTail, arrowHead);
        g.DrawLine(pen, arrowHead, new PointF(arrowHead.X - size * 0.09f, arrowHead.Y));
        g.DrawLine(pen, arrowHead, new PointF(arrowHead.X, arrowHead.Y + size * 0.09f));
    }

    private static void AddRoundedRectangle(this GraphicsPath path, RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        if (diameter <= 0.1f)
        {
            path.AddRectangle(rect);
            return;
        }

        var arc = new RectangleF(rect.Location, new System.Drawing.SizeF(diameter, diameter));
        path.AddArc(arc, 180, 90);

        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
    }
}
