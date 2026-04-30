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

    private static void DrawSoftCurveShadow(Graphics g, PointF[] points, float thickness, bool asCurve)
    {
        foreach (var step in SoftShadowSteps)
        {
            var shadowPts = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                shadowPts[i] = new PointF(points[i].X + step.dx, points[i].Y + step.dy);

            using var pen = new Pen(Color.FromArgb(step.alpha, 0, 0, 0), thickness + (step.dx > 0 ? 1.2f : 0.5f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (asCurve && shadowPts.Length >= 4)
                g.DrawCurve(pen, shadowPts, 0.45f);
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
