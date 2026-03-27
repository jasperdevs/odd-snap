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
        var clip = e.ClipRectangle;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_screenshot, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        // Top fade: blurred backdrop that fades out downward (behind dock area)
        PaintTopFade(g);

        // Annotations render first (they get baked under the darkening overlay)
        PaintAnnotations(g);

        if (_mode == CaptureMode.ColorPicker)
        {
            PaintToolbar(g);
            if (_pickerReady) PaintMagnifier(g);
            return;
        }

        bool isOcr = _mode == CaptureMode.Ocr;
        bool isSelectionMode = _mode == CaptureMode.Rectangle || _mode == CaptureMode.Ocr;

        // Auto-detect: show detected window border when hovering (white animated)
        if (isSelectionMode && !_isSelecting && _autoDetectActive && _autoDetectRect.Width > 0)
        {
            using var adPen = new Pen(Color.FromArgb(180, 255, 255, 255), 2f)
            {
                DashStyle = DashStyle.Dash,
                DashPattern = new[] { 6f, 4f },
                DashOffset = _dashOffset
            };
            g.DrawRectangle(adPen, _autoDetectRect);
        }
        // Show fullscreen border when in selection mode but not yet dragging
        else if (isSelectionMode && !_hasSelection && !_isSelecting)
        {
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 2f);
            g.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }

        // Darken outside selection (rect/OCR)
        if (_hasSelection && isSelectionMode)
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
                // Animated marching ants: white dashes moving right
                using (var marchPen = new Pen(Color.White, 2f)
                {
                    DashStyle = DashStyle.Dash,
                    DashPattern = new[] { 6f, 4f },
                    DashOffset = _dashOffset
                })
                {
                    g.DrawRectangle(marchPen, _selectionRect);
                }
                // Subtle outer shadow
                using (var shadowPen = new Pen(Color.FromArgb(40, 0, 0, 0), 4f))
                {
                    var sr = _selectionRect;
                    sr.Inflate(1, 1);
                    g.DrawRectangle(shadowPen, sr);
                }
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

        // Draw strokes (use tool color)
        if (_drawStrokes.Count > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var drawPen = new Pen(_toolColor, 3f) { LineJoin = LineJoin.Round };
            foreach (var stroke in _drawStrokes)
                if (stroke.Count >= 2)
                    g.DrawLines(drawPen, stroke.ToArray());
            g.SmoothingMode = SmoothingMode.Default;
        }

        // Highlight rectangles (like a real highlighter marker)
        foreach (var (hr, hc) in _highlightRects)
        {
            using var hBrush = new SolidBrush(Color.FromArgb(90, hc.R, hc.G, hc.B));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RRect(hr, 3);
            g.FillPath(hBrush, path);
            g.SmoothingMode = SmoothingMode.Default;
        }
        if (_mode == CaptureMode.Highlight && _isHighlighting)
        {
            var pr = NormRect(_highlightStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 1 && pr.Height > 1)
            {
                var hc = DefaultHighlightColor;
                using var hBrush = new SolidBrush(Color.FromArgb(90, hc.R, hc.G, hc.B));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RRect(pr, 3);
                g.FillPath(hBrush, path);
                g.SmoothingMode = SmoothingMode.Default;
            }
        }

        // Step numbers
        foreach (var (pos, num, color) in _stepNumbers)
            PaintStepNumber(g, pos, num, color);

        // Straight arrows (use tool color, thickness scales with length)
        foreach (var arrow in _arrows)
            PaintArrow(g, arrow.from, arrow.to);
        if (_mode == CaptureMode.Arrow && _isArrowDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            PaintArrow(g, _arrowStart, cur);
        }

        // Curved arrows
        foreach (var ca in _curvedArrows)
            PaintCurvedArrow(g, ca);
        if (_mode == CaptureMode.CurvedArrow && _isCurvedArrowDragging && _currentCurvedArrow is { Count: >= 2 })
            PaintCurvedArrow(g, _currentCurvedArrow);

        // Committed text annotations
        foreach (var (pos, text, fontSize, color) in _textAnnotations)
        {
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            using var brush = new SolidBrush(color);
            using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.DrawString(text, font, shadow, pos.X + 1, pos.Y + 1);
            g.DrawString(text, font, brush, pos.X, pos.Y);
        }

        // Active text input with selection box
        if (_isTyping)
        {
            using var font = new Font("Segoe UI", _textFontSize, FontStyle.Bold);
            string display = _textBuffer.Length > 0 ? _textBuffer : "Type here...";
            var textSize = g.MeasureString(display, font);

            // Dashed selection border
            var textRect = new RectangleF(_textPos.X - 6, _textPos.Y - 4,
                Math.Max(textSize.Width + 12, 100), textSize.Height + 8);
            using var dashPen = new Pen(Color.FromArgb(180, 255, 255, 255), 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dashPen, textRect.X, textRect.Y, textRect.Width, textRect.Height);

            // Corner resize handles (small white squares)
            int hs = 6;
            var handles = new RectangleF[] {
                new(textRect.X - hs/2, textRect.Y - hs/2, hs, hs),
                new(textRect.Right - hs/2, textRect.Y - hs/2, hs, hs),
                new(textRect.X - hs/2, textRect.Bottom - hs/2, hs, hs),
                new(textRect.Right - hs/2, textRect.Bottom - hs/2, hs, hs),
            };
            using var handleBrush = new SolidBrush(Color.White);
            foreach (var h in handles)
                g.FillRectangle(handleBrush, h);

            // Text
            using var brush = new SolidBrush(_textBuffer.Length > 0 ? _toolColor : Color.FromArgb(80, 255, 255, 255));
            using var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
            g.DrawString(display, font, shadow, _textPos.X + 1, _textPos.Y + 1);
            g.DrawString(display, font, brush, _textPos.X, _textPos.Y);

            // Blinking cursor after text
            if (_textBuffer.Length > 0)
            {
                var cursorX = _textPos.X + g.MeasureString(_textBuffer, font).Width - 2;
                using var cursorPen = new Pen(_toolColor, 2f);
                g.DrawLine(cursorPen, cursorX, _textPos.Y + 2, cursorX, _textPos.Y + textSize.Height - 4);
            }

            // Font size indicator
            using var sizeFont = new Font("Segoe UI", 8f);
            using var sizeBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
            g.DrawString($"{(int)_textFontSize}px", sizeFont, sizeBrush, textRect.Right + 4, textRect.Y);
        }

        // Placed magnifiers (always visible)
        foreach (var (mpos, msrc) in _placedMagnifiers)
            PaintPlacedMagnifier(g, mpos, msrc);

        // Magnifier preview while in magnifier mode
        if (_mode == CaptureMode.Magnifier)
            PaintMagnifierTool(g);

        // Color picker popup
        if (_colorPickerOpen)
            PaintColorPicker(g);
    }

    private void PaintCurvedArrow(Graphics g, List<Point> points)
    {
        if (points.Count < 2) return;
        float len = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float dx = points[i].X - points[i-1].X, dy = points[i].Y - points[i-1].Y;
            len += MathF.Sqrt(dx * dx + dy * dy);
        }
        // Thickness grows from 1.5 to 4 based on length
        float thickness = Math.Clamp(1.5f + len / 80f, 1.5f, 4f);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_toolColor, thickness) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, points.ToArray());

        // Arrowhead at end
        if (points.Count >= 2)
        {
            var last = points[^1];
            var prev = points[Math.Max(0, points.Count - 6)]; // look back a few points for direction
            float dx = last.X - prev.X, dy = last.Y - prev.Y;
            float l = MathF.Sqrt(dx * dx + dy * dy);
            if (l > 2)
            {
                float nx = dx / l, ny = dy / l;
                float headLen = Math.Clamp(8 + len / 30f, 8, 16);
                float bx = last.X - nx * headLen, by = last.Y - ny * headLen;
                float spread = headLen * 0.5f;
                var pts = new PointF[] {
                    new(last.X, last.Y),
                    new(bx - ny * spread, by + nx * spread),
                    new(bx + ny * spread, by - nx * spread)
                };
                using var brush = new SolidBrush(_toolColor);
                g.FillPolygon(brush, pts);
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void PaintStepNumber(Graphics g, Point pos, int num, Color color)
    {
        int radius = 16;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Filled circle
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
        // White border
        using var borderPen = new Pen(Color.White, 2f);
        g.DrawEllipse(borderPen, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
        // Number
        using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
        string text = num.ToString();
        var sz = g.MeasureString(text, font);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(text, font, textBrush, pos.X - sz.Width / 2, pos.Y - sz.Height / 2);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintMagnifierTool(Graphics g)
    {
        // Live preview following cursor
        var cur = PointToClient(System.Windows.Forms.Cursor.Position);
        int srcSize = 40;
        int sx = Math.Clamp(cur.X - srcSize / 2, 0, _bmpW - srcSize);
        int sy = Math.Clamp(cur.Y - srcSize / 2, 0, _bmpH - srcSize);
        PaintMagnifierAt(g, cur, new Rectangle(sx, sy, srcSize, srcSize), 0.5f);
    }

    private void PaintPlacedMagnifier(Graphics g, Point pos, Rectangle srcRect)
    {
        PaintMagnifierAt(g, pos, srcRect, 1f);
    }

    private void PaintMagnifierAt(Graphics g, Point pos, Rectangle srcRect, float opacity)
    {
        int zoom = 3;
        int dstSize = srcRect.Width * zoom;

        int px = pos.X + 20;
        int py = pos.Y + 20;
        if (px + dstSize + 6 > ClientSize.Width) px = pos.X - 20 - dstSize;
        if (py + dstSize + 6 > ClientSize.Height) py = pos.Y - 20 - dstSize;

        var dstRect = new Rectangle(px, py, dstSize, dstSize);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bgPath = RRect(new RectangleF(px - 2, py - 2, dstSize + 4, dstSize + 4), 8))
        {
            using var bg = new SolidBrush(Color.FromArgb((int)(200 * opacity), 15, 15, 15));
            g.FillPath(bg, bgPath);
        }
        g.SmoothingMode = SmoothingMode.Default;

        using var clipPath = RRect(dstRect, 6);
        var oldClip = g.Clip;
        g.SetClip(clipPath);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(_screenshot, dstRect, srcRect, GraphicsUnit.Pixel);
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
        g.InterpolationMode = InterpolationMode.Default;
        g.Clip = oldClip;

        int ccx = px + dstSize / 2, ccy = py + dstSize / 2;
        using var crossPen = new Pen(Color.FromArgb((int)(180 * opacity), 255, 255, 255), 1f);
        g.DrawLine(crossPen, ccx - 8, ccy, ccx + 8, ccy);
        g.DrawLine(crossPen, ccx, ccy - 8, ccx, ccy + 8);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var borderPen = new Pen(Color.FromArgb((int)(50 * opacity), 255, 255, 255), 1f);
        g.DrawPath(borderPen, clipPath);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintColorPicker(Graphics g)
    {
        // Small popup grid of color swatches
        int cols = 6, rows = 1, swatchSize = 28, pad = 4;
        int pw = cols * (swatchSize + pad) + pad;
        int ph = rows * (swatchSize + pad) + pad;

        // Position below the color button
        int colorBtnIdx = BtnCount - 3;
        var colorBtn = _toolbarButtons[colorBtnIdx];
        int px = colorBtn.X + colorBtn.Width / 2 - pw / 2;
        int py = colorBtn.Y + colorBtn.Height + 8;

        _colorPickerRect = new Rectangle(px, py, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bgPath = RRect(_colorPickerRect, 8))
        {
            using var bg = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
            g.FillPath(bg, bgPath);
            using var border = new Pen(Color.FromArgb(40, 255, 255, 255));
            g.DrawPath(border, bgPath);
        }

        for (int i = 0; i < ToolColors.Length && i < cols * rows; i++)
        {
            int col = i % cols, row = i / cols;
            int sx = px + pad + col * (swatchSize + pad);
            int sy = py + pad + row * (swatchSize + pad);
            using var brush = new SolidBrush(ToolColors[i]);
            g.FillEllipse(brush, sx, sy, swatchSize, swatchSize);
            if (ToolColors[i] == _toolColor)
            {
                using var selPen = new Pen(Color.White, 2f);
                g.DrawEllipse(selPen, sx, sy, swatchSize, swatchSize);
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintToolbar(Graphics g)
    {
        float t = 1f - MathF.Pow(1f - _toolbarAnim, 3f);
        int oy = (int)((1f - t) * -30);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y + oy,
            _toolbarRect.Width, _toolbarRect.Height);

        using (var p = RRect(r, 14))
        {
            // Dark base fill first (so no bleed from behind)
            using var baseFill = new SolidBrush(Color.FromArgb((int)(t * 200), 15, 15, 15));
            g.FillPath(baseFill, p);

            // Mica-style: draw blurred screenshot clipped to pill, low opacity for glass effect
            var oldClip = g.Clip;
            g.SetClip(p);
            using var blurAttr = new System.Drawing.Imaging.ImageAttributes();
            float[][] matrix = {
                new[] { 1f, 0, 0, 0, 0 },
                new[] { 0, 1f, 0, 0, 0 },
                new[] { 0, 0, 1f, 0, 0 },
                new[] { 0, 0, 0, t * 0.15f, 0 },  // low alpha = subtle glass
                new[] { 0, 0, 0, 0, 1f }
            };
            blurAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(matrix));
            g.DrawImage(_blurred, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, blurAttr);
            g.Clip = oldClip;

            // Border
            using var bp = new Pen(Color.FromArgb((int)(t * 45), 255, 255, 255), 1f);
            g.DrawPath(bp, p);
        }

        // Buttons: rect, free, ocr, picker, draw, arrow, text, blur, eraser, [color], gear, close
        string[] icons = { "rect", "free", "ocr", "picker",
            "draw", "highlight", "arrow", "curvedArrow", "text", "step",
            "blur", "eraser", "magnifier", "color", "gear", "close" };
        string[] labels = { "Rectangle", "Freeform",
            "OCR", "Color Picker", "Draw", "Highlight",
            "Arrow", "Curved Arrow", "Text", "Step Number",
            "Blur", "Eraser", "Magnifier", "Color", "Settings", "Close (Esc)" };
        CaptureMode[] modes = { CaptureMode.Rectangle, CaptureMode.Freeform,
            CaptureMode.Ocr, CaptureMode.ColorPicker,
            CaptureMode.Draw, CaptureMode.Highlight,
            CaptureMode.Arrow, CaptureMode.CurvedArrow,
            CaptureMode.Text, CaptureMode.StepNumber,
            CaptureMode.Blur, CaptureMode.Eraser, CaptureMode.Magnifier };

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = new Rectangle(_toolbarButtons[i].X, _toolbarButtons[i].Y + oy,
                ButtonSize, ButtonSize);
            bool active = i < modes.Length && _mode == modes[i];
            bool hover = _hoveredButton == i;

            // Color dot button: draw filled circle with current tool color
            if (icons[i] == "color")
            {
                int dotSize = 16;
                int dx = btn.X + (btn.Width - dotSize) / 2;
                int dy = btn.Y + (btn.Height - dotSize) / 2;
                using var cBrush = new SolidBrush(Color.FromArgb((int)(t * 255), _toolColor.R, _toolColor.G, _toolColor.B));
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                if (hover)
                {
                    using var cPen = new Pen(Color.FromArgb((int)(t * 120), 255, 255, 255), 1.5f);
                    g.DrawEllipse(cPen, dx, dy, dotSize, dotSize);
                }
                continue;
            }

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
                using var tipBg = new SolidBrush(Color.FromArgb(210, 12, 12, 12));
                g.FillPath(tipBg, tipPath);
                // Subtle blur glass
                var oldClip2 = g.Clip;
                g.SetClip(tipPath);
                using var tipAttr = new System.Drawing.Imaging.ImageAttributes();
                float[][] tipMatrix = {
                    new[] { 1f, 0, 0, 0, 0 }, new[] { 0, 1f, 0, 0, 0 },
                    new[] { 0, 0, 1f, 0, 0 }, new[] { 0, 0, 0, 0.1f, 0 },
                    new[] { 0, 0, 0, 0, 1f }
                };
                tipAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(tipMatrix));
                var tipRI = Rectangle.Round(tipRect);
                g.DrawImage(_blurred, tipRI, tipRI.X, tipRI.Y, tipRI.Width, tipRI.Height, GraphicsUnit.Pixel, tipAttr);
                g.Clip = oldClip2;
                using var tipBorder = new Pen(Color.FromArgb(35, 255, 255, 255), 0.5f);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
            g.DrawString(label, tipFont, tipBrush, tx, ty);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintTopFade(Graphics g)
    {
        // Height of the fade zone: from top of screen down past the toolbar
        int fadeH = _toolbarRect.Bottom + 30;
        if (fadeH <= 0) return;

        // Draw the blurred image in the top strip
        var topRect = new Rectangle(0, 0, ClientSize.Width, fadeH);

        // Use a gradient brush to mask: fully opaque at top, transparent at bottom
        // We can't directly alpha-mask a DrawImage in GDI+, so we draw the blur
        // then overlay a gradient that fades FROM the screenshot (restoring it at bottom)
        
        // Step 1: Draw blurred version in the top strip at low opacity
        using var blurAttr = new System.Drawing.Imaging.ImageAttributes();
        float[][] m = {
            new[] { 1f, 0, 0, 0, 0 },
            new[] { 0, 1f, 0, 0, 0 },
            new[] { 0, 0, 1f, 0, 0 },
            new[] { 0, 0, 0, 0.35f, 0 },
            new[] { 0, 0, 0, 0, 1f }
        };
        blurAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(m));
        g.DrawImage(_blurred, topRect, 0, 0, ClientSize.Width, fadeH, GraphicsUnit.Pixel, blurAttr);

        // Step 2: Dark gradient overlay that fades from dark at top to transparent at bottom
        using var gradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            topRect,
            Color.FromArgb(90, 0, 0, 0),   // dark at top
            Color.FromArgb(0, 0, 0, 0),     // transparent at bottom
            System.Drawing.Drawing2D.LinearGradientMode.Vertical);
        g.FillRectangle(gradBrush, topRect);
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
                // Scan brackets (like a document scanner)
                g.DrawLine(pen, cx - 6, cy - 5, cx - 3, cy - 5); // top-left bracket
                g.DrawLine(pen, cx - 6, cy - 5, cx - 6, cy - 2);
                g.DrawLine(pen, cx + 3, cy - 5, cx + 6, cy - 5); // top-right bracket
                g.DrawLine(pen, cx + 6, cy - 5, cx + 6, cy - 2);
                g.DrawLine(pen, cx - 6, cy + 2, cx - 6, cy + 5); // bottom-left bracket
                g.DrawLine(pen, cx - 6, cy + 5, cx - 3, cy + 5);
                g.DrawLine(pen, cx + 6, cy + 2, cx + 6, cy + 5); // bottom-right bracket
                g.DrawLine(pen, cx + 3, cy + 5, cx + 6, cy + 5);
                // Scan lines inside
                g.DrawLine(pen, cx - 3, cy - 1, cx + 3, cy - 1);
                g.DrawLine(pen, cx - 3, cy + 2, cx + 2, cy + 2);
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
            case "curvedArrow":
                // Curved line with arrowhead
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawBezier(pen, cx - 6, cy + 4, cx - 2, cy - 6, cx + 2, cy + 2, cx + 6, cy - 4);
                g.DrawLine(pen, cx + 6, cy - 4, cx + 2, cy - 5);
                g.DrawLine(pen, cx + 6, cy - 4, cx + 5, cy);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "text":
            {
                // "T" letter
                using var tf = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var tBrush = new SolidBrush(c);
                g.DrawString("T", tf, tBrush, cx - 7, cy - 9);
                break;
            }
            case "step":
            {
                // Numbered circle
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                using var sf = new Font("Segoe UI", 8f, FontStyle.Bold);
                using var sb = new SolidBrush(c);
                g.DrawString("1", sf, sb, cx - 4, cy - 6);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            }
            case "highlight":
                // Marker/highlighter icon
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var thickPen = new Pen(Color.FromArgb(120, c.R, c.G, c.B), 6f))
                {
                    thickPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    thickPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(thickPen, cx - 5, cy + 2, cx + 5, cy + 2);
                }
                g.DrawLine(pen, cx - 5, cy - 4, cx + 5, cy - 4); // line above
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "magnifier":
                // Magnifying glass
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, cx - 5, cy - 6, 10, 10);
                g.DrawLine(pen, cx + 3, cy + 3, cx + 7, cy + 7);
                g.SmoothingMode = SmoothingMode.Default;
                break;
            case "color":
                // Handled in PaintToolbar directly
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
            using var lblBg = new SolidBrush(Color.FromArgb(200, 12, 12, 12));
            g.FillPath(lblBg, p);
            var oldClip3 = g.Clip;
            g.SetClip(p);
            using var lblAttr = new System.Drawing.Imaging.ImageAttributes();
            float[][] lblM = {
                new[] { 1f, 0, 0, 0, 0 }, new[] { 0, 1f, 0, 0, 0 },
                new[] { 0, 0, 1f, 0, 0 }, new[] { 0, 0, 0, 0.12f, 0 },
                new[] { 0, 0, 0, 0, 1f }
            };
            lblAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(lblM));
            var lrI = Rectangle.Round(lr);
            g.DrawImage(_blurred, lrI, lrI.X, lrI.Y, lrI.Width, lrI.Height, GraphicsUnit.Pixel, lblAttr);
            g.Clip = oldClip3;
            using var border = new Pen(Color.FromArgb(35, 255, 255, 255), 0.5f);
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

    private void PaintArrow(Graphics g, Point from, Point to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 3) return;
        // Thickness grows with length: 1.5 -> 4
        float thickness = Math.Clamp(1.5f + len / 100f, 1.5f, 4f);
        float headLen = Math.Clamp(8 + len / 25f, 8, 18);
        float headSpread = headLen * 0.5f;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_toolColor, thickness);
        g.DrawLine(pen, from, to);
        float nx = dx / len, ny = dy / len;
        float bx = to.X - nx * headLen, by = to.Y - ny * headLen;
        var pts = new PointF[]
        {
            new(to.X, to.Y),
            new(bx - ny * headSpread, by + nx * headSpread),
            new(bx + ny * headSpread, by - nx * headSpread)
        };
        using var brush = new SolidBrush(_toolColor);
        g.FillPolygon(brush, pts);
        g.SmoothingMode = SmoothingMode.Default;
    }
}
