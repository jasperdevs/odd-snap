using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Raw screenshot background
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_screenshot, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;

        // Annotations render first (they get baked under the darkening overlay)
        PaintAnnotations(g);

        // Frosted glass top bar (fades with dock)
        if (_toolbarHide < 0.98f)
            g.DrawImageUnscaled(_topBar, 0, 0);

        if (_mode == CaptureMode.ColorPicker)
        {
            PaintToolbar(g);
            if (_pickerReady) PaintMagnifier(g);
            return;
        }

        bool isOcr = _mode == CaptureMode.Ocr;

        // Darken outside selection (rect/OCR)
        if (_hasSelection && (_mode == CaptureMode.Rectangle || _mode == CaptureMode.Ocr))
        {
            using var overlay = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            var sel = _selectionRect;
            g.FillRectangle(overlay, 0, 0, ClientSize.Width, sel.Top);
            g.FillRectangle(overlay, 0, sel.Bottom, ClientSize.Width, ClientSize.Height - sel.Bottom);
            g.FillRectangle(overlay, 0, sel.Top, sel.Left, sel.Height);
            g.FillRectangle(overlay, sel.Right, sel.Top, ClientSize.Width - sel.Right, sel.Height);
        }

        // Selection borders (on top of everything)
        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
            case CaptureMode.Ocr when _hasSelection:
                for (int i = 3; i >= 1; i--)
                {
                    var s = _selectionRect;
                    s.Inflate(i * 2, i * 2);
                    using var sp = new Pen(Color.FromArgb(25, 0, 0, 0), 2f);
                    g.DrawRectangle(sp, s);
                }
                using (var pen = new Pen(isOcr ? Color.FromArgb(100, 180, 255) : Color.White, 2f))
                    g.DrawRectangle(pen, _selectionRect);
                DrawLabel(g, _selectionRect, isOcr);
                break;

            case CaptureMode.Freeform when _freeformPoints.Count >= 2:
                using (var pen = new Pen(Color.White, 2f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLines(pen, _freeformPoints.ToArray());
                    if (!_isSelecting && _freeformPoints.Count > 2)
                        g.DrawLine(pen, _freeformPoints[^1], _freeformPoints[0]);
                    g.SmoothingMode = SmoothingMode.Default;
                }
                break;
        }

        PaintToolbar(g);
    }

    // All annotations: draw strokes, arrows, eraser fills, blur, plus active previews
    private void PaintAnnotations(Graphics g)
    {
        // Eraser fills
        foreach (var (rect, color) in _eraserFills)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, rect);
        }
        if (_mode == CaptureMode.Eraser && _isEraserDragging)
        {
            var pr = NormRect(_eraserStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 0 && pr.Height > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(180, _eraserColor));
                g.FillRectangle(brush, pr);
                using var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, pr);
            }
        }

        // Blur rects
        foreach (var br in _blurRects)
            PaintBlurRect(g, br);
        if (_mode == CaptureMode.Blur && _isBlurring)
        {
            var pr = NormRect(_blurStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 2 && pr.Height > 2)
            {
                using var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, pr);
            }
        }

        // Draw strokes
        if (_drawStrokes.Count > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var drawPen = new Pen(Color.Red, 3f) { LineJoin = LineJoin.Round };
            foreach (var stroke in _drawStrokes)
                if (stroke.Count >= 2)
                    g.DrawLines(drawPen, stroke.ToArray());
            g.SmoothingMode = SmoothingMode.Default;
        }

        // Arrows
        foreach (var arrow in _arrows)
            PaintArrow(g, arrow.from, arrow.to);
        if (_mode == CaptureMode.Arrow && _isArrowDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            PaintArrow(g, _arrowStart, cur);
        }
    }

    private void PaintToolbar(Graphics g)
    {
        float t = 1f - MathF.Pow(1f - _toolbarAnim, 3f);
        // Combine entrance animation with hide-during-selection
        float hideOffset = _toolbarHide * -70; // slide 70px up
        float hideAlpha = 1f - _toolbarHide;
        int oy = (int)((1f - t) * -30 + hideOffset);
        if (hideAlpha < 0.02f) return; // fully hidden, skip painting
        t *= hideAlpha;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y + oy,
            _toolbarRect.Width, _toolbarRect.Height);

        using (var p = RRect(r, 14))
        {
            var oldClip = g.Clip;
            using (var dockRegion = new Region(p))
            {
                g.Clip = dockRegion;
                g.DrawImage(_blurred, r, r, GraphicsUnit.Pixel);
            }
            g.Clip = oldClip;

            using var fill = new SolidBrush(Color.FromArgb((int)(t * 130), 0, 0, 0));
            g.FillPath(fill, p);
            using var bp = new Pen(Color.FromArgb((int)(t * 60), 255, 255, 255), 1f);
            g.DrawPath(bp, p);
        }

        string[] icons = { "rect", "free", "ocr", "picker",
            "draw", "arrow", "blur", "eraser", "gear", "close" };
        string[] labels = { "Rectangle (1)", "Freeform (2)",
            "OCR (3)", "Color Picker (4)", "Draw (5)", "Arrow (6)", "Blur (7)", "Eraser (8)",
            "Settings", "Close (Esc)" };
        CaptureMode[] modes = { CaptureMode.Rectangle, CaptureMode.Freeform,
            CaptureMode.Ocr, CaptureMode.ColorPicker,
            CaptureMode.Draw, CaptureMode.Arrow,
            CaptureMode.Blur, CaptureMode.Eraser };

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = new Rectangle(_toolbarButtons[i].X, _toolbarButtons[i].Y + oy,
                ButtonSize, ButtonSize);
            bool active = i < modes.Length && _mode == modes[i];
            bool hover = _hoveredButton == i;
            if (active || hover)
            {
                using var p = RRect(btn, 8);
                int alpha = (int)(t * (active ? 60 : 30));
                using var bfill = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillPath(bfill, p);
                if (active)
                {
                    using var border = new Pen(Color.FromArgb((int)(t * 50), 255, 255, 255), 0.5f);
                    g.DrawPath(border, p);
                }
            }
            int ia = (int)(t * (i >= BtnCount - 2 ? 200 : 255));
            DrawIcon(g, icons[i], btn, Color.FromArgb(ia, 255, 255, 255));
        }

        if (_hoveredButton >= 0 && _hoveredButton < labels.Length && t > 0.5f)
        {
            string label = labels[_hoveredButton];
            using var tipFont = new Font("Segoe UI", 9f);
            var sz = g.MeasureString(label, tipFont);
            var btnRect = _toolbarButtons[_hoveredButton];
            float tx = btnRect.X + btnRect.Width / 2f - sz.Width / 2f;
            float ty = r.Bottom + 6 + oy;
            var tipRect = new RectangleF(tx - 6, ty - 2, sz.Width + 12, sz.Height + 4);
            using (var tipPath = RRect(tipRect, 8))
            {
                using var tipBg = new SolidBrush(Color.FromArgb(200, 15, 15, 15));
                g.FillPath(tipBg, tipPath);
                using var tipBorder = new Pen(Color.FromArgb(50, 255, 255, 255), 0.5f);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
            g.DrawString(label, tipFont, tipBrush, tx, ty);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c)
    {
        using var pen = new Pen(c, 1.6f);
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
        switch (icon)
        {
            case "rect":
                // Dashed rectangle
                g.DrawRectangle(pen, cx - 7, cy - 5, 14, 10);
                break;
            case "free":
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawBezier(pen, cx - 7, cy + 4, cx - 3, cy - 7, cx + 3, cy + 6, cx + 7, cy - 4);
                g.SmoothingMode = SmoothingMode.Default;
                break;

            case "ocr":
                // Brackets with T
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy - 5);
                g.DrawLine(pen, cx, cy - 5, cx, cy + 5);
                break;
            case "picker":
                // Eyedropper
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, cx - 3, cy - 7, 6, 6);
                g.DrawLine(pen, cx, cy - 1, cx, cy + 7);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "draw":
                // Pencil line
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, cx - 6, cy + 5, cx + 4, cy - 5);
                g.DrawLine(pen, cx - 6, cy + 5, cx - 7, cy + 7);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "arrow":
                // Arrow pointing top-right
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx, cy - 4);
                g.DrawLine(pen, cx + 5, cy - 5, cx + 4, cy);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "blur":
                // Grid dots for pixelate
                for (int dy = -4; dy <= 4; dy += 4)
                    for (int dx = -4; dx <= 4; dx += 4)
                        g.FillRectangle(new SolidBrush(c), cx + dx - 1, cy + dy - 1, 2, 2);
                break;
            case "eraser":
                // Eraser shape
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawRectangle(pen, cx - 6, cy - 3, 12, 8);
                g.DrawLine(pen, cx - 2, cy - 3, cx - 2, cy + 5);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "gear":
                // Gear: circle with 4 notch lines
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                g.DrawLine(pen, cx, cy - 7, cx, cy - 4);
                g.DrawLine(pen, cx, cy + 4, cx, cy + 7);
                g.DrawLine(pen, cx - 7, cy, cx - 4, cy);
                g.DrawLine(pen, cx + 4, cy, cx + 7, cy);
                // Diagonal notches
                int d = 2;
                g.DrawLine(pen, cx - 5, cy - 5, cx - 5 + d, cy - 5 + d);
                g.DrawLine(pen, cx + 5, cy - 5, cx + 5 - d, cy - 5 + d);
                g.DrawLine(pen, cx - 5, cy + 5, cx - 5 + d, cy + 5 - d);
                g.DrawLine(pen, cx + 5, cy + 5, cx + 5 - d, cy + 5 - d);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "close":
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                g.SmoothingMode = SmoothingMode.Default;
                break;
        }
    }

    private void DrawLabel(Graphics g, Rectangle rect, bool isOcr)
    {
        string text = isOcr ? $"OCR  {rect.Width} x {rect.Height}" : $"{rect.Width} x {rect.Height}";
        using var font = new Font("Segoe UI", 10f);
        var sz = g.MeasureString(text, font);
        float lx = rect.X, ly = rect.Bottom + 8;
        if (ly + sz.Height > ClientSize.Height) ly = rect.Y - sz.Height - 8;
        var lr = new RectangleF(lx - 6, ly - 3, sz.Width + 12, sz.Height + 6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = RRect(lr, 8))
        {
            using var bg = new SolidBrush(Color.FromArgb(180, 15, 15, 15));
            g.FillPath(bg, p);
            using var border = new Pen(Color.FromArgb(45, 255, 255, 255), 0.5f);
            g.DrawPath(border, p);
        }
        g.SmoothingMode = SmoothingMode.Default;
        using var fg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        g.DrawString(text, font, fg, lx, ly);
    }

    private void PaintBlurRect(Graphics g, Rectangle rect)
    {
        int blockSize = Math.Max(6, Math.Min(rect.Width, rect.Height) / 8);
        if (rect.Width < 3 || rect.Height < 3) return;
        var clamped = Rectangle.Intersect(rect, new Rectangle(0, 0, _bmpW, _bmpH));
        if (clamped.Width < 1 || clamped.Height < 1) return;
        int sw = Math.Max(1, clamped.Width / blockSize);
        int sh = Math.Max(1, clamped.Height / blockSize);
        using var small = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.Bilinear;
            sg.DrawImage(_screenshot, new Rectangle(0, 0, sw, sh), clamped, GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, clamped);
        g.InterpolationMode = InterpolationMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }

    private static void PaintArrow(Graphics g, Point from, Point to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 3) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.Red, 3f);
        g.DrawLine(pen, from, to);
        float nx = dx / len, ny = dy / len;
        float bx = to.X - nx * 14, by = to.Y - ny * 14;
        var pts = new PointF[]
        {
            new(to.X, to.Y),
            new(bx - ny * 7, by + nx * 7),
            new(bx + ny * 7, by - nx * 7)
        };
        using var brush = new SolidBrush(Color.Red);
        g.FillPolygon(brush, pts);
        g.SmoothingMode = SmoothingMode.Default;
    }
}
