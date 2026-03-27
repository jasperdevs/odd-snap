using System.Drawing;
using System.Drawing.Drawing2D;

namespace Yoink.Capture;

/// <summary>
/// Excalidraw-inspired sketchy rendering utilities.
/// Uses seeded RNG for deterministic wobble, bezier curves for organic feel,
/// and variable-width outlines for natural pen strokes.
/// </summary>
public static class SketchRenderer
{
    /// <summary>Draw a wobbly line between two points (like rough.js).</summary>
    public static void DrawSketchyLine(Graphics g, Pen pen, PointF p1, PointF p2, int seed, float roughness = 1f)
    {
        var rng = new Random(seed);
        float len = Distance(p1, p2);
        if (len < 2) { g.DrawLine(pen, p1, p2); return; }

        float offset = roughness * Math.Min(len * 0.15f, 8f);
        float bow = roughness * Math.Min(len * 0.1f, 6f);

        // Direction perpendicular to line
        float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        float nx = -dy / len, ny = dx / len;

        // First pass
        var (s1, c1a, c1b, e1) = WobbleBezier(rng, p1, p2, offset, bow, nx, ny);
        g.DrawBezier(pen, s1, c1a, c1b, e1);

        // Second pass (multi-stroke for hand-drawn feel)
        if (roughness > 0.3f)
        {
            var (s2, c2a, c2b, e2) = WobbleBezier(rng, p1, p2, offset * 0.6f, bow * 0.5f, nx, ny);
            using var p2Pen = new Pen(Color.FromArgb((int)(pen.Color.A * 0.5f), pen.Color), pen.Width * 0.8f);
            p2Pen.LineJoin = LineJoin.Round;
            g.DrawBezier(p2Pen, s2, c2a, c2b, e2);
        }
    }

    /// <summary>Draw a sketchy rectangle.</summary>
    public static void DrawSketchyRect(Graphics g, Pen pen, RectangleF rect, int seed, float roughness = 1f)
    {
        var corners = new[] {
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Right, rect.Top),
            new PointF(rect.Right, rect.Bottom),
            new PointF(rect.Left, rect.Bottom)
        };
        for (int i = 0; i < 4; i++)
            DrawSketchyLine(g, pen, corners[i], corners[(i + 1) % 4], seed + i * 1000, roughness);
    }

    /// <summary>Draw an arrow with Excalidraw-style arrowhead that scales with length.</summary>
    public static void DrawArrow(Graphics g, PointF from, PointF to, Color color, int seed, float roughness = 0.5f)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 3) return;

        // Shaft thickness scales: 1.5 -> 3.5
        float thickness = Math.Clamp(1.5f + len / 120f, 1.5f, 3.5f);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, thickness) { LineJoin = LineJoin.Round };

        // Draw sketchy shaft
        DrawSketchyLine(g, pen, from, to, seed, roughness);

        // Arrowhead: size scales with length, capped
        float headSize = Math.Clamp(8f + len / 20f, 8f, 25f);
        headSize = Math.Min(headSize, len * 0.4f); // never bigger than 40% of shaft

        float angle = 22f * MathF.PI / 180f; // 22 degrees like Excalidraw
        float nx = dx / len, ny = dy / len;

        // Base point
        float bx = to.X - nx * headSize, by = to.Y - ny * headSize;

        // Rotate around tip
        var left = RotatePoint(new PointF(bx, by), to, -angle);
        var right = RotatePoint(new PointF(bx, by), to, angle);

        // Draw arrowhead lines (slightly sketchy)
        DrawSketchyLine(g, pen, left, to, seed + 5000, roughness * 0.5f);
        DrawSketchyLine(g, pen, right, to, seed + 6000, roughness * 0.5f);

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Draw a curved arrow (freehand path with arrowhead).</summary>
    public static void DrawCurvedArrow(Graphics g, List<Point> points, Color color, int seed)
    {
        if (points.Count < 2) return;
        float len = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float ddx = points[i].X - points[i - 1].X, ddy = points[i].Y - points[i - 1].Y;
            len += MathF.Sqrt(ddx * ddx + ddy * ddy);
        }
        float thickness = Math.Clamp(1.5f + len / 100f, 1.5f, 3.5f);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw the freehand path as a filled variable-width outline
        var outline = GetStrokeOutline(points.Select(p => new PointF(p.X, p.Y)).ToList(),
            thickness * 2f, 0.3f, 0.5f, 0.4f);
        if (outline.Length > 2)
        {
            using var brush = new SolidBrush(color);
            using var path = OutlineToPath(outline);
            g.FillPath(brush, path);
        }

        // Arrowhead at end
        if (points.Count >= 2)
        {
            var last = points[^1];
            var prev = points[Math.Max(0, points.Count - 8)];
            float dx = last.X - prev.X, dy = last.Y - prev.Y;
            float l = MathF.Sqrt(dx * dx + dy * dy);
            if (l > 2)
            {
                float headSize = Math.Clamp(8f + len / 25f, 8f, 18f);
                float angle = 22f * MathF.PI / 180f;
                float nx = dx / l, ny = dy / l;
                float bx = last.X - nx * headSize, by = last.Y - ny * headSize;
                var left = RotatePoint(new PointF(bx, by), new PointF(last.X, last.Y), -angle);
                var right = RotatePoint(new PointF(bx, by), new PointF(last.X, last.Y), angle);

                var headPts = new[] { new PointF(last.X, last.Y), left, right };
                using var hBrush = new SolidBrush(color);
                g.FillPolygon(hBrush, headPts);
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Draw a freehand stroke as a variable-width filled outline (like perfect-freehand).
    /// </summary>
    public static void DrawFreehandStroke(Graphics g, List<Point> points, Color color, float size)
    {
        if (points.Count < 2) return;
        var floatPts = points.Select(p => new PointF(p.X, p.Y)).ToList();
        var outline = GetStrokeOutline(floatPts, size, 0.5f, 0.5f, 0.5f);
        if (outline.Length < 3) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        using var path = OutlineToPath(outline);
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
        var path = new GraphicsPath();
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

    // ─── Helpers ───────────────────────────────────────────────────

    private static (PointF start, PointF ctrl1, PointF ctrl2, PointF end) WobbleBezier(
        Random rng, PointF p1, PointF p2, float offset, float bow, float nx, float ny)
    {
        float midX = (p1.X + p2.X) / 2f;
        float midY = (p1.Y + p2.Y) / 2f;

        var start = new PointF(
            p1.X + Rand(rng, offset * 0.5f),
            p1.Y + Rand(rng, offset * 0.5f));
        var end = new PointF(
            p2.X + Rand(rng, offset * 0.5f),
            p2.Y + Rand(rng, offset * 0.5f));
        var ctrl1 = new PointF(
            midX + nx * bow * Rand(rng, 1.5f) + Rand(rng, offset),
            midY + ny * bow * Rand(rng, 1.5f) + Rand(rng, offset));
        var ctrl2 = new PointF(
            midX + nx * bow * Rand(rng, 1.5f) + Rand(rng, offset),
            midY + ny * bow * Rand(rng, 1.5f) + Rand(rng, offset));

        return (start, ctrl1, ctrl2, end);
    }

    private static float Rand(Random rng, float scale) =>
        ((float)rng.NextDouble() - 0.5f) * 2f * scale;

    public static float Distance(PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static PointF Midpoint(PointF a, PointF b) =>
        new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private static PointF RotatePoint(PointF point, PointF center, float angle)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float dx = point.X - center.X, dy = point.Y - center.Y;
        return new PointF(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
