using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private void CompleteFreeform()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        var bb = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bb.Width < 3 || bb.Height < 3) return;

        var annotated = RenderAnnotatedBitmap();
        var r = new Bitmap(bb.Width, bb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(r))
        {
            var pts = _freeformPoints.Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();
            using var cp = new GraphicsPath();
            cp.AddPolygon(pts);
            g.SetClip(cp);
            g.DrawImage(annotated, new Rectangle(0, 0, bb.Width, bb.Height), bb, GraphicsUnit.Pixel);
        }
        annotated.Dispose();

        if (_mode == Models.CaptureMode.Ocr)
            OcrFreeformSelected?.Invoke(r);
        else
            FreeformSelected?.Invoke(r);
    }

    public Bitmap RenderAnnotatedBitmap()
    {
        var result = new Bitmap(_screenshot);
        using var g = Graphics.FromImage(result);

        // Smart eraser fills
        foreach (var (rect, color) in _eraserFills)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, rect);
        }

        foreach (var br in _blurRects)
            PaintBlurRect(g, br);

        if (_drawStrokes.Count > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.Red, 3f) { LineJoin = LineJoin.Round };
            foreach (var stroke in _drawStrokes)
                if (stroke.Count >= 2)
                    g.DrawLines(pen, stroke.ToArray());
            g.SmoothingMode = SmoothingMode.Default;
        }

        foreach (var arrow in _arrows)
            PaintArrow(g, arrow.from, arrow.to);

        return result;
    }

}
