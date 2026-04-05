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
    private static readonly (int dx, int dy, int alpha)[] SoftShadowSteps =
    {
        (5, 5, 14),
        (3, 3, 24),
        (1, 1, 42),
        (0, 0, 58),
    };
    private static readonly Color ShadowColor = Color.FromArgb(60, 0, 0, 0);

    private static void DrawSoftLineShadow(Graphics g, PointF from, PointF to, float thickness)
    {
        foreach (var step in SoftShadowSteps)
        {
            using var pen = new Pen(Color.FromArgb(step.alpha, 0, 0, 0), thickness + (step.dx > 0 ? 1.2f : 0.5f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(pen, from.X + step.dx, from.Y + step.dy, to.X + step.dx, to.Y + step.dy);
        }
    }

    private static void DrawSoftCurveShadow(Graphics g, Point[] points, float thickness, bool asCurve)
    {
        foreach (var step in SoftShadowSteps)
        {
            var shadowPts = points.Select(p => new Point(p.X + step.dx, p.Y + step.dy)).ToArray();
            using var pen = new Pen(Color.FromArgb(step.alpha, 0, 0, 0), thickness + (step.dx > 0 ? 1.2f : 0.5f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (asCurve && shadowPts.Length >= 4)
                g.DrawCurve(pen, shadowPts, 0.5f);
            else
                g.DrawLines(pen, shadowPts);
        }
    }

    public static void DrawSoftPathShadow(Graphics g, GraphicsPath path, float extraSpread = 0f)
    {
        foreach (var step in SoftShadowSteps)
        {
            using var brush = new SolidBrush(Color.FromArgb(step.alpha, 0, 0, 0));
            var m = new System.Drawing.Drawing2D.Matrix();
            m.Translate(step.dx, step.dy);
            if (step.dx > 0)
                m.Scale(1f + extraSpread * 0.02f, 1f + extraSpread * 0.02f);
            using var shadowPath = (GraphicsPath)path.Clone();
            shadowPath.Transform(m);
            g.FillPath(brush, shadowPath);
        }
    }

    public static void DrawSoftEllipseShadow(Graphics g, float x, float y, float w, float h)
    {
        foreach (var step in SoftShadowSteps)
        {
            using var brush = new SolidBrush(Color.FromArgb(step.alpha, 0, 0, 0));
            g.FillEllipse(brush, x + step.dx, y + step.dy, w, h);
        }
    }

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

    // Match text annotation shadow/stroke values exactly
    private static readonly Color AnnotShadow1 = Color.FromArgb(50, 0, 0, 0);
    private static readonly Color AnnotShadow2 = Color.FromArgb(25, 0, 0, 0);
    private static readonly Color AnnotStroke = Color.FromArgb(60, 0, 0, 0);

    private static void DrawPenWithStrokeShadow(Graphics g, Pen mainPen, PointF from, PointF to)
    {
        // Shadow: two offset passes (matching text shadow)
        using var s1 = new Pen(AnnotShadow1, mainPen.Width) { StartCap = mainPen.StartCap, EndCap = mainPen.EndCap };
        using var s2 = new Pen(AnnotShadow2, mainPen.Width) { StartCap = mainPen.StartCap, EndCap = mainPen.EndCap };
        g.DrawLine(s1, from.X + 2, from.Y + 2, to.X + 2, to.Y + 2);
        g.DrawLine(s2, from.X + 3, from.Y + 3, to.X + 3, to.Y + 3);

        // Stroke: 8-direction offset (matching text stroke)
        using var strokePen = new Pen(AnnotStroke, mainPen.Width) { StartCap = mainPen.StartCap, EndCap = mainPen.EndCap };
        for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
                if (ox != 0 || oy != 0)
                    g.DrawLine(strokePen, from.X + ox, from.Y + oy, to.X + ox, to.Y + oy);
    }

    /// <summary>Draw a straight line (no arrowhead).</summary>
    public static void DrawLine(Graphics g, PointF from, PointF to, Color color, int seed, bool strokeShadow = false)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 2) return;

        const float thickness = 3f;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(color, thickness)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        if (strokeShadow)
            DrawPenWithStrokeShadow(g, pen, from, to);

        g.DrawLine(pen, from, to);

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Draw a clean arrow with proportional arrowhead (Excalidraw style).</summary>
    public static void DrawArrow(Graphics g, PointF from, PointF to, Color color, int seed, float roughness = 0.5f, bool strokeShadow = false)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 3) return;

        const float thickness = 3.5f;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (strokeShadow)
        {
            // Shadow passes
            using var s1 = new Pen(AnnotShadow1, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(s1, from.X + 2, from.Y + 2, to.X + 2, to.Y + 2);
            DrawArrowhead(g, new PointF(to.X + 2, to.Y + 2), dx / len, dy / len, len, AnnotShadow1, thickness + 0.5f);
            using var s2 = new Pen(AnnotShadow2, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(s2, from.X + 3, from.Y + 3, to.X + 3, to.Y + 3);

            // Stroke passes (8 directions)
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    if (ox != 0 || oy != 0)
                    {
                        using var sp = new Pen(AnnotStroke, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                        g.DrawLine(sp, from.X + ox, from.Y + oy, to.X + ox, to.Y + oy);
                        DrawArrowhead(g, new PointF(to.X + ox, to.Y + oy), dx / len, dy / len, len, AnnotStroke, thickness + 0.5f);
                    }
        }

        using var pen = new Pen(color, thickness)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLine(pen, from, to);

        DrawArrowhead(g, new PointF(to.X, to.Y), dx / len, dy / len, len, color, thickness + 0.5f);

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Draw a curved arrow (smooth line with arrowhead at tip).</summary>
    public static void DrawCurvedArrow(Graphics g, List<Point> points, Color color, int seed, bool strokeShadow = false)
    {
        if (points.Count < 2) return;

        // Simplify jagged input into a smooth polyline
        points = SimplifyPoints(points, 2.5f);
        if (points.Count < 2) return;

        float len = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float ddx = points[i].X - points[i - 1].X, ddy = points[i].Y - points[i - 1].Y;
            len += MathF.Sqrt(ddx * ddx + ddy * ddy);
        }
        if (len < 3) return;

        const float thickness = 3.5f;

        // Calculate arrowhead size first — we need it to find the right direction distance
        float headSize = Math.Clamp(12f + len / 15f, 12f, 28f);
        headSize = Math.Min(headSize, len * 0.4f);

        // Walk backward along the polyline to find a point ~headSize away from tip
        // This gives a stable tangent that matches where the curve is actually going
        var tip = points[^1];
        float walkTarget = Math.Max(headSize, 20f);
        float walked = 0;
        Point dirFrom = points[^2];
        for (int i = points.Count - 1; i > 0; i--)
        {
            float seg = MathF.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) +
                                    (points[i].Y - points[i - 1].Y) * (points[i].Y - points[i - 1].Y));
            walked += seg;
            if (walked >= walkTarget) { dirFrom = points[i - 1]; break; }
        }
        float dx = tip.X - dirFrom.X, dy = tip.Y - dirFrom.Y;
        float l = MathF.Sqrt(dx * dx + dy * dy);
        if (l < 1) return;
        float nx = dx / l, ny = dy / l;

        // Shorten the curve: pull the last point back so the line doesn't poke through the arrowhead
        var shortenedPts = points.ToArray();
        shortenedPts[^1] = new Point(
            (int)(tip.X - nx * headSize * 0.6f),
            (int)(tip.Y - ny * headSize * 0.6f));

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Helper to draw the curve + arrowhead at a given offset with a given color
        void DrawCurveAndHead(Point[] pts, PointF headTip, Color c, float thick)
        {
            using var pen = new Pen(c, thick)
                { StartCap = LineCap.Round, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
            if (pts.Length >= 4)
                g.DrawCurve(pen, pts, 0.5f);
            else
                g.DrawLines(pen, pts);
            DrawArrowhead(g, headTip, nx, ny, len, c, thick + 0.5f);
        }

        Point[] OffsetPts(Point[] src, int ox, int oy) =>
            src.Select(p => new Point(p.X + ox, p.Y + oy)).ToArray();

        if (strokeShadow)
        {
            DrawCurveAndHead(OffsetPts(shortenedPts, 2, 2), new PointF(tip.X + 2, tip.Y + 2), AnnotShadow1, thickness);
            DrawCurveAndHead(OffsetPts(shortenedPts, 3, 3), new PointF(tip.X + 3, tip.Y + 3), AnnotShadow2, thickness);

            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    if (ox != 0 || oy != 0)
                        DrawCurveAndHead(OffsetPts(shortenedPts, ox, oy), new PointF(tip.X + ox, tip.Y + oy), AnnotStroke, thickness);
        }

        DrawCurveAndHead(shortenedPts, new PointF(tip.X, tip.Y), color, thickness);

        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawArrowhead(Graphics g, PointF tip, float nx, float ny,
        float shaftLen, Color color, float thickness)
    {
        float headSize = Math.Clamp(12f + shaftLen / 15f, 12f, 28f);
        headSize = Math.Min(headSize, shaftLen * 0.4f);
        float angle = 25f * MathF.PI / 180f;

        float bx = tip.X - nx * headSize, by = tip.Y - ny * headSize;
        var left = RotatePoint(new PointF(bx, by), tip, -angle);
        var right = RotatePoint(new PointF(bx, by), tip, angle);

        // Draw as two lines (like Excalidraw's "arrow" type)
        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(pen, left, tip);
        g.DrawLine(pen, right, tip);
    }

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
            // Shadow passes (matching text)
            using var shadow1 = (GraphicsPath)path.Clone();
            using var shadow2 = (GraphicsPath)path.Clone();
            var m1 = new System.Drawing.Drawing2D.Matrix(); m1.Translate(2, 2);
            var m2 = new System.Drawing.Drawing2D.Matrix(); m2.Translate(3, 3);
            shadow1.Transform(m1);
            shadow2.Transform(m2);
            using var sb1 = new SolidBrush(AnnotShadow1);
            using var sb2 = new SolidBrush(AnnotShadow2);
            g.FillPath(sb1, shadow1);
            g.FillPath(sb2, shadow2);

            // Stroke passes (8 directions, matching text)
            using var strokeBrush = new SolidBrush(AnnotStroke);
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    if (ox != 0 || oy != 0)
                    {
                        using var sp = (GraphicsPath)path.Clone();
                        var sm = new System.Drawing.Drawing2D.Matrix(); sm.Translate(ox, oy);
                        sp.Transform(sm);
                        g.FillPath(strokeBrush, sp);
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
            using var s1Pen = new Pen(AnnotShadow1, 3f) { LineJoin = LineJoin.Round };
            using var s2Pen = new Pen(AnnotShadow2, 3f) { LineJoin = LineJoin.Round };
            g.DrawPath(s1Pen, s1Path);
            g.DrawPath(s2Pen, s2Path);
            using var strokePen = new Pen(AnnotStroke, 3f) { LineJoin = LineJoin.Round };
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    if (ox != 0 || oy != 0)
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
            using var s1Pen = new Pen(AnnotShadow1, 3f);
            using var s2Pen = new Pen(AnnotShadow2, 3f);
            g.DrawEllipse(s1Pen, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height));
            g.DrawEllipse(s2Pen, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));
            using var strokePen = new Pen(AnnotStroke, 3f);
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    if (ox != 0 || oy != 0)
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

    // ─── Point simplification (Ramer-Douglas-Peucker) ─────────────

    /// <summary>Reduce jagged input points into a smoother polyline.</summary>
    private static List<Point> SimplifyPoints(List<Point> points, float epsilon)
    {
        if (points.Count < 3) return points;
        var result = new List<Point>();
        RdpSimplify(points, 0, points.Count - 1, epsilon, result);
        result.Add(points[^1]);
        return result;
    }

    private static void RdpSimplify(List<Point> pts, int start, int end, float epsilon, List<Point> result)
    {
        float maxDist = 0;
        int index = start;

        float ax = pts[start].X, ay = pts[start].Y;
        float bx = pts[end].X, by = pts[end].Y;
        float dx = bx - ax, dy = by - ay;
        float lenSq = dx * dx + dy * dy;

        for (int i = start + 1; i < end; i++)
        {
            float dist;
            if (lenSq < 0.001f)
            {
                float px = pts[i].X - ax, py = pts[i].Y - ay;
                dist = MathF.Sqrt(px * px + py * py);
            }
            else
            {
                float t = Math.Clamp(((pts[i].X - ax) * dx + (pts[i].Y - ay) * dy) / lenSq, 0, 1);
                float projX = ax + t * dx, projY = ay + t * dy;
                float px = pts[i].X - projX, py = pts[i].Y - projY;
                dist = MathF.Sqrt(px * px + py * py);
            }
            if (dist > maxDist) { maxDist = dist; index = i; }
        }

        if (maxDist > epsilon)
        {
            RdpSimplify(pts, start, index, epsilon, result);
            RdpSimplify(pts, index, end, epsilon, result);
        }
        else
        {
            result.Add(pts[start]);
        }
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
