using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Media.Imaging;

namespace OddSnap.Helpers;

/// <summary>
/// Shared rendering helpers for semantic tool icons.
/// Renders semantic tool icons through the shared Fluent icon facade.
/// </summary>
public static class ToolIcons
{
    /// <summary>Map from tool IDs to shared icon IDs where they differ.</summary>
    private static readonly Dictionary<string, string> ToolToStreamlineId = new()
    {
        ["_record"] = "record",
    };

    public static BitmapSource RenderToolIconWpf(string toolId, char glyph, Color color, int size)
    {
        var iconId = ToolToStreamlineId.TryGetValue(toolId, out var mapped) ? mapped : toolId;

        return StreamlineIcons.RenderWpf(iconId, color, size)
               ?? StreamlineIcons.RenderWpf("warning", color, size)!;
    }

    public static BitmapSource RenderStickerWpf(Color color, int size)
    {
        var src = StreamlineIcons.RenderWpf("sticker", color, size);
        if (src != null) return src;
        return RenderShapeWpf(size, g => DrawSticker(g, size, color));
    }

    public static BitmapSource RenderRecordWpf(Color color, int size)
    {
        var src = StreamlineIcons.RenderWpf("record", color, size);
        if (src != null) return src;
        return RenderShapeWpf(size, g => DrawRecord(g, size, color));
    }

    public static BitmapSource RenderFolderWpf(Color color, int size)
    {
        var src = StreamlineIcons.RenderWpf("folder", color, size);
        if (src != null) return src;
        return RenderShapeWpf(size, g => DrawFolder(g, size, color));
    }

    public static BitmapSource RenderAiRedirectWpf(Color color, int size, bool active = false)
    {
        var src = StreamlineIcons.RenderWpf("ai_redirect", color, size, active);
        if (src != null) return src;

        return RenderShapeWpf(size, g =>
        {
            if (active)
                DrawAiRedirectSolid(g, size, color);
            else
                DrawAiRedirect(g, size, color);
        });
    }

    public static BitmapSource RenderGoogleLensWpf(Color color, int size, bool active = false)
    {
        return RenderShapeWpf(size, g => DrawGoogleLens(g, size, color, active));
    }

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

    private static void DrawAiRedirect(Graphics g, int size, Color color)
    {
        var stroke = Math.Max(1.3f, size * 0.08f);
        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        float s = size / 10f;
        g.DrawArc(pen, 0.7f * s, 0.7f * s, 7.0f * s, 7.0f * s, 205, 235);
        g.DrawLine(pen, 7.25f * s, 7.25f * s, 9.2f * s, 9.2f * s);
        g.DrawArc(pen, 5.4f * s, 0.65f * s, 2.5f * s, 2.5f * s, 0, 360);
        g.DrawLine(pen, 7.0f * s, 0.6f * s, 7.0f * s, 2.95f * s);
        g.DrawLine(pen, 5.82f * s, 1.78f * s, 8.18f * s, 1.78f * s);
    }

    private static void DrawAiRedirectSolid(Graphics g, int size, Color color)
    {
        float s = size / 10f;
        using var brush = new SolidBrush(color);
        using var clearBrush = new SolidBrush(Color.Transparent);

        g.FillEllipse(brush, 0.5f * s, 0.5f * s, 7.1f * s, 7.1f * s);

        using (var magnifierPath = new GraphicsPath())
        {
            magnifierPath.AddEllipse(1.6f * s, 1.6f * s, 5.2f * s, 5.2f * s);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillPath(clearBrush, magnifierPath);
            g.CompositingMode = CompositingMode.SourceOver;
        }

        using var sparkPen = new Pen(Color.Transparent, Math.Max(1.5f, size * 0.12f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawLine(sparkPen, 7.0f * s, 0.8f * s, 7.0f * s, 2.7f * s);
        g.DrawLine(sparkPen, 6.05f * s, 1.75f * s, 7.95f * s, 1.75f * s);
        g.CompositingMode = CompositingMode.SourceOver;

        using var handlePen = new Pen(color, Math.Max(1.6f, size * 0.11f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(handlePen, 6.9f * s, 6.9f * s, 9.0f * s, 9.0f * s);
    }

    private static void DrawGoogleLens(Graphics g, int size, Color color, bool active)
    {
        float s = size / 20f;
        float stroke = Math.Max(1.6f, size * 0.085f);
        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        if (active)
        {
            using var fill = new SolidBrush(Color.FromArgb(Math.Min(54, Math.Max(28, color.A / 5)), color.R, color.G, color.B));
            using var body = new GraphicsPath();
            body.AddRoundedRectangle(new RectangleF(4.2f * s, 4.2f * s, 11.6f * s, 11.6f * s), 3.2f * s);
            g.FillPath(fill, body);
        }

        var left = 3.8f * s;
        var top = 3.8f * s;
        var right = 16.2f * s;
        var bottom = 16.2f * s;
        var corner = 4.2f * s;

        g.DrawLine(pen, left + corner, top, left + 1.5f * corner, top);
        g.DrawLine(pen, left, top + corner, left, top + 1.5f * corner);
        g.DrawArc(pen, left, top, corner * 2f, corner * 2f, 180, 78);

        g.DrawLine(pen, right - 1.5f * corner, top, right - corner, top);
        g.DrawLine(pen, right, top + corner, right, top + 1.5f * corner);
        g.DrawArc(pen, right - corner * 2f, top, corner * 2f, corner * 2f, 282, 78);

        g.DrawLine(pen, right, bottom - 1.5f * corner, right, bottom - corner);
        g.DrawLine(pen, right - 1.5f * corner, bottom, right - corner, bottom);
        g.DrawArc(pen, right - corner * 2f, bottom - corner * 2f, corner * 2f, corner * 2f, 0, 78);

        g.DrawLine(pen, left + corner, bottom, left + 1.5f * corner, bottom);
        g.DrawLine(pen, left, bottom - 1.5f * corner, left, bottom - corner);
        g.DrawArc(pen, left, bottom - corner * 2f, corner * 2f, corner * 2f, 102, 78);

        g.DrawEllipse(pen, 7.0f * s, 7.0f * s, 6.0f * s, 6.0f * s);
        g.FillEllipse(brush, 13.0f * s, 12.8f * s, 2.6f * s, 2.6f * s);
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
