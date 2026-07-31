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

        g.DrawPath(GetRoundCapPen(color, Math.Max(2f, size)), path);
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
        var highlight = Color.FromArgb(92, color.R, color.G, color.B);
        g.FillPath(GetToolColorBrush(highlight), path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    public static void DrawRectShape(Graphics g, Rectangle rect, Color color, bool strokeShadow = false)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(rect, 3);

        if (strokeShadow)
        {
            // Translate the same path instead of rebuilding it 10 times.
            var state = g.Save();
            try
            {
                g.TranslateTransform(2, 2);
                g.DrawPath(ShapeShadowPen1, path);
                g.TranslateTransform(1, 1); // now at +3,+3
                g.DrawPath(ShapeShadowPen2, path);
            }
            finally { g.Restore(state); }

            foreach (var (ox, oy) in StrokeOffsets)
            {
                var s = g.Save();
                try
                {
                    g.TranslateTransform(ox, oy);
                    g.DrawPath(ShapeStrokePen, path);
                }
                finally { g.Restore(s); }
            }
        }

        g.DrawPath(GetRoundJoinPen(color, 3f), path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    public static void DrawCircleShape(Graphics g, Rectangle rect, Color color, bool strokeShadow = false)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (strokeShadow)
        {
            g.DrawEllipse(ShapeShadowPen1, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height));
            g.DrawEllipse(ShapeShadowPen2, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));
            foreach (var (ox, oy) in StrokeOffsets)
                g.DrawEllipse(ShapeStrokePen, new Rectangle(rect.X + ox, rect.Y + oy, rect.Width, rect.Height));
        }

        g.DrawEllipse(GetRoundJoinPen(color, 3f), rect);
        g.SmoothingMode = SmoothingMode.Default;
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
        // Translate the same path each step instead of cloning + matrix per pass.
        var state = g.Save();
        try
        {
            int prevDx = 0, prevDy = 0;
            foreach (var step in SoftShadowSteps)
            {
                g.TranslateTransform(step.dx - prevDx, step.dy - prevDy);
                prevDx = step.dx; prevDy = step.dy;
                float w = thickness + (step.dx > 0 ? 1.2f : 0.5f);
                g.DrawPath(GetShadowPen(step.alpha, w), path);
            }
        }
        finally { g.Restore(state); }
    }

}
