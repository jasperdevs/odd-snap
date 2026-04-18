using System.Drawing;
using System.Drawing.Drawing2D;

namespace OddSnap.Capture;

public static partial class SketchRenderer
{
    /// <summary>
    /// Draw a freehand stroke as a variable-width filled outline (like perfect-freehand).
    /// </summary>
    public static void DrawFreehandStroke(Graphics g, List<Point> points, Color color, float size, bool strokeShadow = false)
    {
        if (points.Count < 2) return;
        // Simplify jagged input
        points = SimplifyPoints(points, 2.0f);
        if (points.Count < 2) return;
        var floatPts = points.Select(p => new PointF(p.X, p.Y)).ToList();

        // Shift-constrained draw uses only two points. That should render as a real line,
        // not vanish because the freehand outline collapses to an empty path.
        if (points.Count == 2)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSoftLineShadow(g, floatPts[0], floatPts[1], size);
            using var pen = new Pen(color, Math.Max(2f, size))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLine(pen, floatPts[0], floatPts[1]);
            g.SmoothingMode = SmoothingMode.Default;
            return;
        }

        var outline = GetStrokeOutline(floatPts, size, 0.5f, 0.5f, 0.5f);
        if (outline.Length < 3)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSoftLineShadow(g, floatPts[0], floatPts[^1], size);
            using var pen = new Pen(color, Math.Max(2f, size))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLine(pen, floatPts[0], floatPts[^1]);
            g.SmoothingMode = SmoothingMode.Default;
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = OutlineToPath(outline);

        if (strokeShadow)
        {
            // Shadow passes — reuse a single cloned path and matrix
            using var shadowPath = (GraphicsPath)path.Clone();
            var m = new System.Drawing.Drawing2D.Matrix();
            m.Translate(2, 2);
            shadowPath.Transform(m);
            g.FillPath(BrushShadow1, shadowPath);
            m.Reset(); m.Translate(1, 1); // from (2,2) to (3,3)
            shadowPath.Transform(m);
            g.FillPath(BrushShadow2, shadowPath);

            // Stroke passes (8 directions) — reuse one clone, translate incrementally
            foreach (var (ox, oy) in StrokeOffsets)
            {
                using var sp = (GraphicsPath)path.Clone();
                m.Reset(); m.Translate(ox, oy);
                sp.Transform(m);
                g.FillPath(BrushStroke, sp);
            }
        }

        // Main pass
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Draw a highlight marker (large, semi-transparent, uniform width).
    /// </summary>
    public static void DrawHighlightRect(Graphics g, Rectangle rect, Color color)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(90, color.R, color.G, color.B));
        using var path = RoundedRect(rect, 3);
        g.FillPath(brush, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    public static void DrawRectShape(Graphics g, Rectangle rect, Color color, bool strokeShadow = false)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(rect, 3);

        if (strokeShadow)
        {
            using var s1Path = RoundedRect(new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), 3);
            using var s2Path = RoundedRect(new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), 3);
            using var shadowPen1 = new Pen(AnnotShadow1, 3f) { LineJoin = LineJoin.Round };
            using var shadowPen2 = new Pen(AnnotShadow2, 3f) { LineJoin = LineJoin.Round };
            g.DrawPath(shadowPen1, s1Path);
            g.DrawPath(shadowPen2, s2Path);
            using var strokePen = new Pen(AnnotStroke, 3f) { LineJoin = LineJoin.Round };
            foreach (var (ox, oy) in StrokeOffsets)
            {
                using var sp = RoundedRect(new Rectangle(rect.X + ox, rect.Y + oy, rect.Width, rect.Height), 3);
                g.DrawPath(strokePen, sp);
            }
        }

        using var pen = new Pen(color, 3f) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    public static void DrawCircleShape(Graphics g, Rectangle rect, Color color, bool strokeShadow = false)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (strokeShadow)
        {
            using var shadowPen1 = new Pen(AnnotShadow1, 3f);
            using var shadowPen2 = new Pen(AnnotShadow2, 3f);
            g.DrawEllipse(shadowPen1, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height));
            g.DrawEllipse(shadowPen2, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));
            using var strokePen = new Pen(AnnotStroke, 3f);
            foreach (var (ox, oy) in StrokeOffsets)
                g.DrawEllipse(strokePen, new Rectangle(rect.X + ox, rect.Y + oy, rect.Width, rect.Height));
        }

        using var pen = new Pen(color, 3f) { LineJoin = LineJoin.Round };
        g.DrawEllipse(pen, rect);
        g.SmoothingMode = SmoothingMode.Default;
    }

    // ─── Variable-width stroke outline (perfect-freehand style) ────

    public static PointF[] GetStrokeOutline(List<PointF> input, float size,
        float thinning, float smoothing, float streamline)
    {
        if (input.Count < 2) return Array.Empty<PointF>();

        // 1. Streamline input
        var pts = new List<(PointF point, float pressure)>();
        PointF prev = input[0];
        float t = 1f - streamline;

        for (int i = 0; i < input.Count; i++)
        {
            var curr = input[i];
            prev = new PointF(prev.X + (curr.X - prev.X) * t, prev.Y + (curr.Y - prev.Y) * t);

            float dist = i > 0 ? Distance(pts[^1].point, prev) : 0;
            // Simulate pressure from velocity (fast = thin)
            float pressure = Math.Clamp(1f - dist / (size * 1.5f), 0.2f, 1f);
            pressure = MathF.Sin(pressure * MathF.PI / 2f); // easeOutSine
            pts.Add((prev, pressure));
        }

        // 2. Generate left/right outline points
        var left = new List<PointF>();
        var right = new List<PointF>();

        for (int i = 1; i < pts.Count; i++)
        {
            float width = size * (1f - thinning * (1f - pts[i].pressure));
            float radius = Math.Max(0.5f, width / 2f);

            float dx = pts[i].point.X - pts[i - 1].point.X;
            float dy = pts[i].point.Y - pts[i - 1].point.Y;
            float len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));

            float px = -dy / len * radius;
            float py = dx / len * radius;

            left.Add(new PointF(pts[i].point.X + px, pts[i].point.Y + py));
            right.Add(new PointF(pts[i].point.X - px, pts[i].point.Y - py));
        }

        // 3. Combine: left forward + right reversed
        right.Reverse();
        var outline = new List<PointF>();
        outline.AddRange(left);
        outline.AddRange(right);
        return outline.ToArray();
    }

    /// <summary>Convert outline points to a smooth GraphicsPath using quadratic bezier approximation.</summary>
    public static GraphicsPath OutlineToPath(PointF[] pts)
    {
        var path = new GraphicsPath(FillMode.Winding);
        if (pts.Length < 3) return path;

        path.StartFigure();
        path.AddLine(pts[0], Midpoint(pts[0], pts[1]));
        for (int i = 1; i < pts.Length - 1; i++)
        {
            var mid = Midpoint(pts[i], pts[i + 1]);
            // Approximate quadratic bezier with cubic
            path.AddBezier(Midpoint(pts[i - 1], pts[i]), pts[i], pts[i], mid);
        }
        path.CloseFigure();
        return path;
    }
}
