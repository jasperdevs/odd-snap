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
        points = SimplifyPoints(points, 2.0f);
        if (points.Count < 2) return;
        var floatPts = SmoothStrokePoints(points, minDistance: 0.8f);
        if (floatPts.Count < 2) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = BuildSmoothStrokePath(floatPts);

        if (strokeShadow)
        {
            DrawSoftPathStrokeShadow(g, path, size);
        }

        using var pen = new Pen(color, Math.Max(2f, size))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Draw a highlight marker (large, semi-transparent, uniform width).
    /// </summary>
    public static void DrawHighlightRect(Graphics g, Rectangle rect, Color color)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(rect, 5);
        using var brush = new SolidBrush(Color.FromArgb(92, color.R, color.G, color.B));
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
        var left = new List<PointF>(pts.Count + 8);
        var right = new List<PointF>(pts.Count + 8);

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

        AddRoundCap(left, right, pts[^1].point, pts.Count > 1 ? pts[^2].point : pts[^1].point, size / 2f, atEnd: true);
        AddRoundCap(left, right, pts[0].point, pts.Count > 1 ? pts[1].point : pts[0].point, size / 2f, atEnd: false);

        // 3. Combine: left forward + right reversed
        right.Reverse();
        var outline = new List<PointF>(left.Count + right.Count);
        outline.AddRange(left);
        outline.AddRange(right);
        return outline.ToArray();
    }

    private static List<PointF> SmoothStrokePoints(List<Point> input, float minDistance)
    {
        var compact = new List<PointF>(input.Count);
        PointF last = new(input[0].X, input[0].Y);
        compact.Add(last);

        float minDistanceSq = minDistance * minDistance;
        for (int i = 1; i < input.Count; i++)
        {
            var next = new PointF(input[i].X, input[i].Y);
            float dx = next.X - last.X;
            float dy = next.Y - last.Y;
            if (dx * dx + dy * dy < minDistanceSq)
                continue;
            compact.Add(next);
            last = next;
        }

        if (compact.Count < 4)
            return compact;

        var smoothed = new List<PointF>(compact.Count);
        smoothed.Add(compact[0]);
        for (int i = 1; i < compact.Count - 1; i++)
        {
            var prev = compact[i - 1];
            var cur = compact[i];
            var next = compact[i + 1];
            smoothed.Add(new PointF(
                (prev.X + cur.X * 2f + next.X) / 4f,
                (prev.Y + cur.Y * 2f + next.Y) / 4f));
        }
        smoothed.Add(compact[^1]);
        return smoothed;
    }

    private static void AddRoundCap(List<PointF> left, List<PointF> right, PointF center, PointF neighbor, float radius, bool atEnd)
    {
        float dx = center.X - neighbor.X;
        float dy = center.Y - neighbor.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
            return;

        float angle = MathF.Atan2(dy, dx);
        float start = atEnd ? angle - MathF.PI / 2f : angle + MathF.PI / 2f;
        float sweep = atEnd ? MathF.PI : -MathF.PI;
        const int steps = 6;

        for (int i = 1; i < steps; i++)
        {
            float t = start + sweep * i / steps;
            var p = new PointF(center.X + MathF.Cos(t) * radius, center.Y + MathF.Sin(t) * radius);
            if (atEnd)
                left.Add(p);
            else
                right.Add(p);
        }
    }

    private static GraphicsPath BuildSmoothStrokePath(List<PointF> points)
    {
        var path = new GraphicsPath();
        if (points.Count < 2)
            return path;

        if (points.Count < 4)
        {
            path.AddLines(points.ToArray());
            return path;
        }

        path.AddCurve(points.ToArray(), 0.35f);
        return path;
    }

    private static void DrawSoftPathStrokeShadow(Graphics g, GraphicsPath path, float thickness)
    {
        foreach (var step in SoftShadowSteps)
        {
            using var shadowPath = (GraphicsPath)path.Clone();
            using var matrix = new Matrix();
            matrix.Translate(step.dx, step.dy);
            shadowPath.Transform(matrix);
            using var pen = new Pen(Color.FromArgb(step.alpha, 0, 0, 0), thickness + (step.dx > 0 ? 1.2f : 0.5f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawPath(pen, shadowPath);
        }
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
