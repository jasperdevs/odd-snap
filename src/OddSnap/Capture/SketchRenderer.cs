using System.Drawing;
using System.Drawing.Drawing2D;

namespace OddSnap.Capture;

/// <summary>
/// Excalidraw-inspired sketchy rendering utilities.
/// Uses seeded RNG for deterministic wobble, bezier curves for organic feel,
/// and variable-width outlines for natural pen strokes.
/// </summary>
public static partial class SketchRenderer
{
    private const float AnnotationStrokeWidth = 6f;

    // Match text annotation shadow/stroke values exactly
    private static readonly Color AnnotShadow1 = Color.FromArgb(50, 0, 0, 0);
    private static readonly Color AnnotShadow2 = Color.FromArgb(25, 0, 0, 0);
    private static readonly Color AnnotStroke = Color.FromArgb(60, 0, 0, 0);

    // Cached GDI objects for stroke/shadow rendering — avoid per-frame allocations
    private static readonly SolidBrush BrushShadow1 = new(AnnotShadow1);
    private static readonly SolidBrush BrushShadow2 = new(AnnotShadow2);
    private static readonly SolidBrush BrushStroke = new(AnnotStroke);

    // Pre-computed 8-direction offsets for stroke outline
    private static readonly (int dx, int dy)[] StrokeOffsets =
    {
        (-1, -1), (-1, 0), (-1, 1),
        (0, -1),           (0, 1),
        (1, -1),  (1, 0),  (1, 1)
    };

    // Reusable point buffer for offset calculations (avoids LINQ .Select().ToArray() per frame)
    [ThreadStatic] private static Point[]? _offsetBuffer;

    /// <summary>Offset points into a reusable buffer — avoids allocating a new array per shadow/stroke pass.</summary>
    private static Point[] OffsetPointsInPlace(Point[] src, int ox, int oy)
    {
        if (_offsetBuffer == null || _offsetBuffer.Length < src.Length)
            _offsetBuffer = new Point[src.Length];
        for (int i = 0; i < src.Length; i++)
            _offsetBuffer[i] = new Point(src[i].X + ox, src[i].Y + oy);
        return _offsetBuffer;
    }

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

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (strokeShadow)
            DrawSoftLineShadow(g, from, to, 3f);
        using var pen = new Pen(color, 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLine(pen, from, to);
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Draw a clean arrow with proportional arrowhead (Excalidraw style).</summary>
    public static void DrawArrow(Graphics g, PointF from, PointF to, Color color, int seed, float roughness = 0.5f, bool strokeShadow = false)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 3) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        float nx = dx / len, ny = dy / len;
        float headSize = GetArrowheadSize(len);
        var shaftEnd = new PointF(to.X - nx * headSize * 0.38f, to.Y - ny * headSize * 0.38f);

        if (strokeShadow)
        {
            DrawSoftLineShadow(g, from, shaftEnd, AnnotationStrokeWidth);
            DrawArrowhead(g, new PointF(to.X + 2, to.Y + 2), nx, ny, len, Color.FromArgb(42, 0, 0, 0), AnnotationStrokeWidth, seed + 3000);
        }

        using var pen = new Pen(color, AnnotationStrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLine(pen, from, shaftEnd);
        DrawArrowhead(g, to, nx, ny, len, color, AnnotationStrokeWidth, seed + 6000);

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Draw a curved arrow (smooth line with arrowhead at tip).</summary>
    public static void DrawCurvedArrow(Graphics g, List<Point> points, Color color, int seed, bool strokeShadow = false)
    {
        if (points.Count < 2) return;

        // Simplify jagged input into a smooth polyline
        points = SimplifyPoints(points, 2.5f);
        if (points.Count < 2) return;
        var curvePts = SmoothCurvePoints(points, minDistance: 1.2f);
        if (curvePts.Length < 2) return;

        float len = 0;
        for (int i = 1; i < curvePts.Length; i++)
        {
            float ddx = curvePts[i].X - curvePts[i - 1].X, ddy = curvePts[i].Y - curvePts[i - 1].Y;
            len += MathF.Sqrt(ddx * ddx + ddy * ddy);
        }
        if (len < 3) return;

        const float thickness = AnnotationStrokeWidth;

        // Calculate arrowhead size first — we need it to find the right direction distance
        float headSize = Math.Clamp(12f + len / 15f, 12f, 28f);
        headSize = Math.Min(headSize, len * 0.4f);

        // Walk backward along the polyline to find a point ~headSize away from tip
        // This gives a stable tangent that matches where the curve is actually going
        var tip = curvePts[^1];
        float walkTarget = Math.Max(headSize, 20f);
        float walked = 0;
        PointF dirFrom = curvePts[^2];
        for (int i = curvePts.Length - 1; i > 0; i--)
        {
            float seg = MathF.Sqrt((curvePts[i].X - curvePts[i - 1].X) * (curvePts[i].X - curvePts[i - 1].X) +
                                    (curvePts[i].Y - curvePts[i - 1].Y) * (curvePts[i].Y - curvePts[i - 1].Y));
            walked += seg;
            if (walked >= walkTarget) { dirFrom = curvePts[i - 1]; break; }
        }
        float dx = tip.X - dirFrom.X, dy = tip.Y - dirFrom.Y;
        float l = MathF.Sqrt(dx * dx + dy * dy);
        if (l < 1) return;
        float nx = dx / l, ny = dy / l;

        // Shorten the curve: pull the last point back so the line doesn't poke through the arrowhead
        var shortenedPts = (PointF[])curvePts.Clone();
        shortenedPts[^1] = new PointF(
            tip.X - nx * headSize * 0.55f,
            tip.Y - ny * headSize * 0.55f);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Helper to draw curve with a given pen
        void DrawCurve(PointF[] pts, Pen pen)
        {
            if (pts.Length >= 4)
                g.DrawCurve(pen, pts, 0.45f);
            else
                g.DrawLines(pen, pts);
        }

        if (strokeShadow)
        {
            DrawSoftCurveShadow(g, shortenedPts, thickness, shortenedPts.Length >= 4);
            DrawArrowhead(g, new PointF(tip.X + 2, tip.Y + 2), nx, ny, len, Color.FromArgb(42, 0, 0, 0), thickness + 0.5f, seed + 4000);
        }

        using var mainPen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
        DrawCurve(shortenedPts, mainPen);
        DrawArrowhead(g, tip, nx, ny, len, color, thickness + 0.5f, seed + 7000);

        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawArrowhead(Graphics g, PointF tip, float nx, float ny,
        float shaftLen, Color color, float thickness, int seed = 0)
    {
        float headSize = GetArrowheadSize(shaftLen);
        float angle = 25f * MathF.PI / 180f;

        float bx = tip.X - nx * headSize, by = tip.Y - ny * headSize;
        var left = RotatePoint(new PointF(bx, by), tip, -angle);
        var right = RotatePoint(new PointF(bx, by), tip, angle);

        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLine(pen, left, tip);
        g.DrawLine(pen, right, tip);

        if (color.A > 80)
        {
            using var echoPen = new Pen(Color.FromArgb((int)(color.A * 0.35f), color.R, color.G, color.B), Math.Max(1.4f, thickness * 0.55f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLine(echoPen, left, tip);
            g.DrawLine(echoPen, right, tip);
        }
    }

    private static float GetArrowheadSize(float shaftLen)
        => Math.Min(Math.Clamp(12f + shaftLen / 15f, 12f, 28f), shaftLen * 0.4f);

    private static void DrawRoughStrokeLine(Graphics g, PointF from, PointF to, Color color, int seed, float width, float roughness)
    {
        using var mainPen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        DrawSketchyLine(g, mainPen, from, to, seed, roughness);

        using var echoPen = new Pen(Color.FromArgb(120, color.R, color.G, color.B), Math.Max(1.5f, width * 0.72f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        DrawSketchyLine(g, echoPen, from, to, seed + 911, roughness * 0.55f);
    }

    private static int RoughOffset(int seed, int max)
    {
        if (max <= 0)
            return 0;
        var rng = new Random(seed);
        return rng.Next(-max, max + 1);
    }

    private static PointF[] SmoothCurvePoints(List<Point> points, float minDistance)
    {
        var compact = new List<PointF>(points.Count);
        PointF last = new(points[0].X, points[0].Y);
        compact.Add(last);

        float minDistanceSq = minDistance * minDistance;
        for (int i = 1; i < points.Count; i++)
        {
            var next = new PointF(points[i].X, points[i].Y);
            float dx = next.X - last.X;
            float dy = next.Y - last.Y;
            if (dx * dx + dy * dy < minDistanceSq)
                continue;

            compact.Add(next);
            last = next;
        }

        if (compact.Count < 4)
            return compact.ToArray();

        var smoothed = new PointF[compact.Count];
        smoothed[0] = compact[0];
        for (int i = 1; i < compact.Count - 1; i++)
        {
            var prev = compact[i - 1];
            var cur = compact[i];
            var next = compact[i + 1];
            smoothed[i] = new PointF(
                (prev.X + cur.X * 2f + next.X) / 4f,
                (prev.Y + cur.Y * 2f + next.Y) / 4f);
        }
        smoothed[^1] = compact[^1];
        return smoothed;
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
}
