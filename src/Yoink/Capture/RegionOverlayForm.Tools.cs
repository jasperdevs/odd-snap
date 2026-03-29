using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private void CompleteFreeform()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        var bb = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bb.Width < 3 || bb.Height < 3) return;

        var annotated = RenderAnnotatedBitmap();
        var r = new Bitmap(bb.Width, bb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(r))
        {
            var pts = _freeformPoints.Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();
            using var cp = new GraphicsPath(); cp.AddPolygon(pts); g.SetClip(cp);
            g.DrawImage(annotated, new Rectangle(0, 0, bb.Width, bb.Height), bb, GraphicsUnit.Pixel);
            g.ResetClip();
            using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f)
            {
                DashStyle = DashStyle.Dash,
                DashPattern = new[] { 6f, 4f }
            };
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLines(pen, pts);
            if (pts.Length > 2)
                g.DrawLine(pen, pts[^1], pts[0]);
        }
        annotated.Dispose();
        FreeformSelected?.Invoke(r);
    }

    /// <summary>
    /// Renders the screenshot with all annotations in creation order (Excalidraw style).
    /// </summary>
    public Bitmap RenderAnnotatedBitmap()
    {
        var result = new Bitmap(_bmpW, _bmpH, PixelFormat.Format32bppPArgb);
        using (var copy = Graphics.FromImage(result))
        {
            copy.CompositingMode = CompositingMode.SourceCopy;
            copy.DrawImageUnscaled(_screenshot, 0, 0);
            copy.CompositingMode = CompositingMode.SourceOver;
        }
        using var g = Graphics.FromImage(result);
        RenderAnnotationsTo(g);
        return result;
    }

    /// <summary>
    /// Shared annotation rendering: iterates the typed undo stack and draws each annotation.
    /// Used by both on-screen paint and final bitmap rendering.
    /// </summary>
    private void RenderAnnotationsTo(Graphics g)
    {
        foreach (var entry in _undoStack)
        {
            switch (entry)
            {
                case EraserFill ef:
                    using (var brush = new SolidBrush(ef.Color))
                        g.FillRectangle(brush, ef.Rect);
                    break;
                case BlurRect br:
                    PaintBlurRect(g, br.Rect);
                    break;
                case DrawStroke ds:
                    SketchRenderer.DrawFreehandStroke(g, ds.Points, _toolColor, 6f);
                    break;
                case HighlightAnnotation h:
                    SketchRenderer.DrawHighlightRect(g, h.Rect, h.Color);
                    break;
                case RectShapeAnnotation rs:
                    SketchRenderer.DrawRectShape(g, rs.Rect, rs.Color);
                    break;
                case CircleShapeAnnotation cs:
                    SketchRenderer.DrawCircleShape(g, cs.Rect, cs.Color);
                    break;
                case LineAnnotation ln:
                    SketchRenderer.DrawLine(g, ln.From, ln.To, _toolColor, ln.From.GetHashCode());
                    break;
                case RulerAnnotation ra:
                    PaintRuler(g, ra.From, ra.To);
                    break;
                case ArrowAnnotation a:
                    SketchRenderer.DrawArrow(g, a.From, a.To, _toolColor, a.From.GetHashCode());
                    break;
                case CurvedArrowAnnotation ca:
                    SketchRenderer.DrawCurvedArrow(g, ca.Points, _toolColor, ca.Points.Count * 7919);
                    break;
                case StepNumberAnnotation sn:
                    PaintStepNumber(g, sn.Pos, sn.Number, sn.Color);
                    break;
                case TextAnnotation ta:
                    PaintExcalidrawText(g, ta.Pos, ta.Text, ta.FontSize, ta.Color, ta.Bold, ta.Italic, ta.Stroke, ta.Shadow, ta.FontFamily);
                    break;
                case MagnifierAnnotation ma:
                    PaintPlacedMagnifier(g, ma.Pos, ma.SrcRect);
                    break;
                case EmojiAnnotation ea:
                    PaintEmojiAnnotation(g, ea.Pos, ea.Emoji, ea.Size);
                    break;
            }
        }
    }
}
