using System.Drawing;
using System.Drawing.Drawing2D;

namespace OddSnap.Capture;

public static partial class SketchRenderer
{
    private static readonly (int dx, int dy, int alpha)[] SoftShadowSteps =
    {
        (5, 5, 14),
        (3, 3, 24),
        (1, 1, 42),
        (0, 0, 58),
    };
    // Pre-cached brushes for the four fixed SoftShadowSteps alphas — avoids re-alloc per call.
    private static readonly SolidBrush[] SoftShadowBrushes =
    {
        new(Color.FromArgb(14, 0, 0, 0)),
        new(Color.FromArgb(24, 0, 0, 0)),
        new(Color.FromArgb(42, 0, 0, 0)),
        new(Color.FromArgb(58, 0, 0, 0)),
    };

    // Shadow pens are black with one of 4 fixed alphas; thickness varies by caller's annotation width.
    // Cache keyed on (alpha, width-quantized) — bounded since thicknesses come from a small set.
    private static readonly Dictionary<long, Pen> _shadowPens = new();

    private static Pen GetShadowPen(int alpha, float width)
    {
        long key = ((long)alpha << 32) | (uint)(int)Math.Round(width * 16f);
        if (_shadowPens.TryGetValue(key, out var pen)) return pen;
        TrimCacheIfNeeded(_shadowPens, MaxDynamicGdiCacheEntries);
        pen = new Pen(Color.FromArgb(alpha, 0, 0, 0), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        _shadowPens[key] = pen;
        return pen;
    }

    private static void DrawSoftLineShadow(Graphics g, PointF from, PointF to, float thickness)
    {
        foreach (var step in SoftShadowSteps)
        {
            float w = thickness + (step.dx > 0 ? 1.2f : 0.5f);
            g.DrawLine(GetShadowPen(step.alpha, w),
                from.X + step.dx, from.Y + step.dy,
                to.X + step.dx, to.Y + step.dy);
        }
    }

    [ThreadStatic] private static PointF[]? _curveShadowBuffer;

    private static void DrawSoftCurveShadow(Graphics g, PointF[] points, float thickness, bool asCurve)
    {
        // Reuse a thread-static buffer (grow-only) — avoids 4× LINQ allocations per call.
        if (_curveShadowBuffer == null || _curveShadowBuffer.Length < points.Length)
            _curveShadowBuffer = new PointF[points.Length];
        var buffer = _curveShadowBuffer;
        int n = points.Length;

        foreach (var step in SoftShadowSteps)
        {
            for (int i = 0; i < n; i++)
                buffer[i] = new PointF(points[i].X + step.dx, points[i].Y + step.dy);

            float w = thickness + (step.dx > 0 ? 1.2f : 0.5f);
            var pen = GetShadowPen(step.alpha, w);
            if (asCurve && n >= 4)
                g.DrawCurve(pen, buffer, 0, n - 1, 0.45f);
            else if (buffer.Length == n)
                g.DrawLines(pen, buffer);
            else
            {
                var slice = new PointF[n];
                Array.Copy(buffer, slice, n);
                g.DrawLines(pen, slice);
            }
        }
    }

    public static void DrawSoftPathShadow(Graphics g, GraphicsPath path, float extraSpread = 0f)
    {
        for (int i = 0; i < SoftShadowSteps.Length; i++)
        {
            var step = SoftShadowSteps[i];
            using var m = new System.Drawing.Drawing2D.Matrix();
            m.Translate(step.dx, step.dy);
            if (step.dx > 0)
                m.Scale(1f + extraSpread * 0.02f, 1f + extraSpread * 0.02f);
            using var shadowPath = (GraphicsPath)path.Clone();
            shadowPath.Transform(m);
            g.FillPath(SoftShadowBrushes[i], shadowPath);
        }
    }

    public static void DrawSoftEllipseShadow(Graphics g, float x, float y, float w, float h)
    {
        for (int i = 0; i < SoftShadowSteps.Length; i++)
        {
            var step = SoftShadowSteps[i];
            g.FillEllipse(SoftShadowBrushes[i], x + step.dx, y + step.dy, w, h);
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
