using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OddSnap.Models;

namespace OddSnap.Capture;

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
        }
        annotated.Dispose();
        FreeformSelected?.Invoke(r);
    }

    /// <summary>
    /// Renders the screenshot with all annotations in creation order (Excalidraw style).
    /// </summary>
    public Bitmap RenderAnnotatedBitmap()
    {
        return new Bitmap(GetCommittedAnnotationsBitmap());
    }

    /// <summary>
    /// Shared annotation rendering: iterates the typed undo stack and draws each annotation.
    /// Used by both on-screen paint and final bitmap rendering.
    /// </summary>
    private void RenderAnnotationsTo(Graphics g)
    {
        using var paintedBlurRegion = new Region();
        paintedBlurRegion.MakeEmpty();
        var highlightRegions = BuildHighlightRegions();
        var paintedHighlightColors = new HashSet<int>();

        try
        {
            for (int i = 0; i < _undoStack.Count; i++)
            {
                switch (_undoStack[i])
                {
                    case BlurRect blur:
                        PaintBlurRectOnce(g, blur, paintedBlurRegion);
                        break;
                    case HighlightAnnotation h:
                        int colorKey = h.Color.ToArgb();
                        if (paintedHighlightColors.Add(colorKey) && highlightRegions.TryGetValue(colorKey, out var region))
                            PaintHighlightRegion(g, region, h.Color);
                        break;
                    default:
                        RenderAnnotationTo(g, _undoStack[i]);
                        break;
                }
            }
        }
        finally
        {
            foreach (var region in highlightRegions.Values)
                region.Dispose();
        }
    }

    private void RenderAnnotationTo(Graphics g, Annotation entry)
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
                SketchRenderer.DrawFreehandStroke(g, ds.Points, ds.Color, 6f, AnnotationStrokeShadow);
                break;
            case HighlightAnnotation h:
                using (var path = SketchRenderer.RoundedRect(h.Rect, 5))
                using (var region = new Region(path))
                    PaintHighlightRegion(g, region, h.Color);
                break;
            case RectShapeAnnotation rs:
                SketchRenderer.DrawRectShape(g, rs.Rect, rs.Color, AnnotationStrokeShadow);
                break;
            case CircleShapeAnnotation cs:
                SketchRenderer.DrawCircleShape(g, cs.Rect, cs.Color, AnnotationStrokeShadow);
                break;
            case LineAnnotation ln:
                SketchRenderer.DrawLine(g, ln.From, ln.To, ln.Color, ln.From.GetHashCode(), AnnotationStrokeShadow);
                break;
            case RulerAnnotation ra:
                PaintRuler(g, ra.From, ra.To);
                break;
            case ArrowAnnotation a:
                SketchRenderer.DrawArrow(g, a.From, a.To, a.Color, a.From.GetHashCode(), strokeShadow: AnnotationStrokeShadow);
                break;
            case CurvedArrowAnnotation ca:
                SketchRenderer.DrawCurvedArrow(g, ca.Points, ca.Color, ca.Points.Count * 7919, AnnotationStrokeShadow);
                break;
            case StepNumberAnnotation sn:
                PaintStepNumber(g, sn.Pos, sn.Number, sn.Color);
                break;
            case TextAnnotation ta:
                PaintExcalidrawText(g, ta.Pos, ta.Text, ta.FontSize, ta.Color, ta.Bold, ta.Italic, ta.Stroke, ta.Shadow, ta.Background, ta.FontFamily);
                break;
            case MagnifierAnnotation ma:
                PaintPlacedMagnifier(g, ma.Pos, ma.SrcRect);
                break;
            case EmojiAnnotation ea:
                PaintEmojiAnnotation(g, ea.Pos, ea.Emoji, ea.Size);
                break;
        }
    }

    private Dictionary<int, Region> BuildHighlightRegions()
    {
        var regions = new Dictionary<int, Region>();
        foreach (var annotation in _undoStack)
        {
            if (annotation is not HighlightAnnotation highlight || highlight.Rect.Width <= 0 || highlight.Rect.Height <= 0)
                continue;

            int colorKey = highlight.Color.ToArgb();
            if (!regions.TryGetValue(colorKey, out var region))
            {
                region = new Region();
                region.MakeEmpty();
                regions[colorKey] = region;
            }

            using var path = SketchRenderer.RoundedRect(highlight.Rect, 5);
            region.Union(path);
        }
        return regions;
    }

    private static void PaintHighlightRegion(Graphics g, Region region, Color color)
    {
        var state = g.Save();
        try
        {
            g.SetClip(region, CombineMode.Replace);
            using var brush = new SolidBrush(Color.FromArgb(92, color.R, color.G, color.B));
            g.FillRegion(brush, region);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void PaintBlurRectOnce(Graphics g, BlurRect blur, Region paintedBlurRegion)
    {
        if (blur.Rect.Width <= 0 || blur.Rect.Height <= 0)
            return;

        using var drawRegion = new Region(blur.Rect);
        drawRegion.Exclude(paintedBlurRegion);

        var state = g.Save();
        try
        {
            g.SetClip(drawRegion, CombineMode.Replace);
            PaintBlurRect(g, blur.Rect);
        }
        finally
        {
            g.Restore(state);
        }

        paintedBlurRegion.Union(blur.Rect);
    }
}
