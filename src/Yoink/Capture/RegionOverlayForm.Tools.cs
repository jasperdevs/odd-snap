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
        }
        annotated.Dispose();
        FreeformSelected?.Invoke(r);
    }

    /// <summary>
    /// Renders the screenshot with all annotations in creation order (Excalidraw style).
    /// </summary>
    public Bitmap RenderAnnotatedBitmap()
    {
        var result = new Bitmap(_screenshot);
        using var g = Graphics.FromImage(result);

        int iDraw = 0, iBlur = 0, iArrow = 0, iCurved = 0;
        int iEraser = 0, iText = 0, iStep = 0, iHighlight = 0, iEmoji = 0;

        foreach (var entry in _undoStack)
        {
            switch (entry)
            {
                case "eraser" when iEraser < _eraserFills.Count:
                    var (er, ec) = _eraserFills[iEraser++];
                    using (var brush = new SolidBrush(ec))
                        g.FillRectangle(brush, er);
                    break;

                case "blur" when iBlur < _blurRects.Count:
                    PaintBlurRect(g, _blurRects[iBlur++]);
                    break;

                case "draw" when iDraw < _drawStrokes.Count:
                    SketchRenderer.DrawFreehandStroke(g, _drawStrokes[iDraw++], _toolColor, 6f);
                    break;

                case "highlight" when iHighlight < _highlightRects.Count:
                    var (hr, hc) = _highlightRects[iHighlight++];
                    SketchRenderer.DrawHighlightRect(g, hr, hc);
                    break;

                case "arrow" when iArrow < _arrows.Count:
                    var a = _arrows[iArrow++];
                    SketchRenderer.DrawArrow(g, a.from, a.to, _toolColor, a.from.GetHashCode());
                    break;

                case "curvedArrow" when iCurved < _curvedArrows.Count:
                    SketchRenderer.DrawCurvedArrow(g, _curvedArrows[iCurved++], _toolColor, iCurved * 7919);
                    break;

                case "step" when iStep < _stepNumbers.Count:
                    var (sp, sn, sc) = _stepNumbers[iStep++];
                    PaintStepNumber(g, sp, sn, sc);
                    break;

                case "text" when iText < _textAnnotations.Count:
                    var (tp, tt, tf, tc, tb, tff) = _textAnnotations[iText++];
                    PaintExcalidrawText(g, tp, tt, tf, tc, tb, tff);
                    break;

                case "emoji" when iEmoji < _emojiAnnotations.Count:
                    var (ep, ee, es) = _emojiAnnotations[iEmoji++];
                    PaintEmojiAnnotation(g, ep, ee, es);
                    break;
            }
        }

        return result;
    }
}
