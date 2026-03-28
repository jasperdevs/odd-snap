using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Yoink.Models;

// Unified dash/border constants for consistency across all selection borders
// Every dashed border in the app uses these same values.

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        g.PixelOffsetMode = PixelOffsetMode.None;
        g.SmoothingMode = SmoothingMode.None;

        // Direct screenshot blit
        var clip = e.ClipRectangle;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_screenshot, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        // Draw committed annotations directly on top
        RenderAnnotationsTo(g);

        bool isOcr = _mode == CaptureMode.Ocr;
        bool isScan = _mode == CaptureMode.Scan;
        bool isSelectionMode = _mode == CaptureMode.Rectangle || _mode == CaptureMode.Ocr || _mode == CaptureMode.Scan;

        // Screen dim: dark outside selection, clear inside, light dim when no selection
        if (_hasSelection && isSelectionMode)
        {
            // Dark dim outside selection, NO dim inside
            using var overlay = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            var sel = _selectionRect;
            g.FillRectangle(overlay, 0, 0, ClientSize.Width, sel.Top);
            g.FillRectangle(overlay, 0, sel.Bottom, ClientSize.Width, ClientSize.Height - sel.Bottom);
            g.FillRectangle(overlay, 0, sel.Top, sel.Left, sel.Height);
            g.FillRectangle(overlay, sel.Right, sel.Top, ClientSize.Width - sel.Right, sel.Height);
        }
        else
        {
            // Light dim when idle (no selection)
            using var dimOverlay = new SolidBrush(Color.FromArgb(35, 0, 0, 0));
            g.FillRectangle(dimOverlay, clip);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Live tool previews (active drawing in progress)
        PaintAnnotations(g);

        if (_mode == CaptureMode.ColorPicker)
            return; // magnifier is its own layered window, overlay stays static

        // Auto-detect: show detected window border when hovering
        if (isSelectionMode && !_isSelecting && _autoDetectActive && _autoDetectRect.Width > 0)
        {
            using var adShadow = new Pen(Color.FromArgb(30, 0, 0, 0), 4f);
            g.DrawRectangle(adShadow, _autoDetectRect.X + 1, _autoDetectRect.Y + 1, _autoDetectRect.Width, _autoDetectRect.Height);
            using var adPen = DashedPen(180);
            g.DrawRectangle(adPen, _autoDetectRect);
            _lastAutoDetectRect = _autoDetectRect;
        }
        else if (isSelectionMode && !_hasSelection && !_isSelecting)
        {
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 2f);
            g.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            _lastAutoDetectRect = Rectangle.Empty;
        }

        // Selection borders (on top of everything)
        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
            case CaptureMode.Ocr when _hasSelection:
            case CaptureMode.Scan when _hasSelection:
                // Subtle outer shadow
                using (var shadowPen = new Pen(Color.FromArgb(40, 0, 0, 0), 4f))
                {
                    var sr = _selectionRect;
                    sr.Inflate(1, 1);
                    g.DrawRectangle(shadowPen, sr);
                }
                // Static dashed border
                using (var marchPen = DashedPen(255))
                {
                    g.DrawRectangle(marchPen, _selectionRect);
                }
                DrawLabel(g, _selectionRect, isOcr, isScan);
                _lastSelectionRect = _selectionRect;
                break;

            case CaptureMode.Freeform when _freeformPoints.Count >= 2:
                var pts = _freeformPoints.ToArray();

                // Find the most recent closed loop anywhere in the path.
                // The old logic only checked "latest point near earlier point", which breaks
                // if the user keeps drawing after closing a loop. We instead scan segment
                // intersections across the whole stroke and use the latest valid one.
                int loopStart = -1;
                int loopEnd = -1;
                if (pts.Length > 6)
                {
                    for (int i = 0; i < pts.Length - 3; i++)
                    {
                        var a1 = pts[i];
                        var a2 = pts[i + 1];
                        for (int j = i + 2; j < pts.Length - 1; j++)
                        {
                            // Skip adjacent segments that share endpoints
                            if (j == i || j == i + 1) continue;
                            var b1 = pts[j];
                            var b2 = pts[j + 1];
                            if (SegmentsIntersect(a1, a2, b1, b2))
                            {
                                loopStart = i + 1;
                                loopEnd = j;
                            }
                        }
                    }

                    // Fallback: if the current tail returns near any earlier point, keep dimming
                    // while the user continues drawing after a closed loop.
                    if (loopStart < 0 && _isSelecting)
                    {
                        var last = pts[^1];
                        for (int ci = 0; ci < pts.Length - 15; ci++)
                        {
                            if (Math.Abs(last.X - pts[ci].X) + Math.Abs(last.Y - pts[ci].Y) < 30)
                            {
                                loopStart = ci;
                                loopEnd = pts.Length - 1;
                            }
                        }
                    }
                }

                // Dim outside closed loop
                bool hasClosed = loopStart >= 0 || !_isSelecting;
                Point[]? closedLoopPts = null;
                if (hasClosed && pts.Length > 2)
                {
                    int from = loopStart >= 0 ? loopStart : 0;
                    int to = loopEnd >= from ? loopEnd + 1 : pts.Length;
                    int count = to - from;
                    if (count >= 3)
                    {
                        var loopPts = new Point[count];
                        Array.Copy(pts, from, loopPts, 0, loopPts.Length);

                        // Remove adjacent duplicates and collapse degenerate runs.
                        loopPts = DeduplicateAdjacent(loopPts);

                        // Guard against pathological loops (e.g. degenerate/near-collinear triangles)
                        if (loopPts.Length >= 3 && PolygonArea(loopPts) >= 4f)
                        {
                            closedLoopPts = loopPts;
                            try
                            {
                                using var path = new GraphicsPath();
                                path.AddPolygon(loopPts);
                                using var region = new Region(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                                region.Exclude(path);
                                using var dimBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
                                g.FillRegion(dimBrush, region);
                            }
                            catch (ArgumentException)
                            {
                                hasClosed = false;
                                closedLoopPts = null;
                            }
                        }
                        else
                        {
                            hasClosed = false;
                        }
                    }
                    else
                    {
                        hasClosed = false;
                    }
                }

                // Dashed border matching rectangle selection style
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shadowPen = new Pen(Color.FromArgb(30, 0, 0, 0), 4f))
                    g.DrawLines(shadowPen, pts);
                using (var pen = DashedPen(220))
                {
                    g.DrawLines(pen, pts);
                    if (hasClosed && closedLoopPts is { Length: >= 3 })
                    {
                        g.DrawLine(pen, closedLoopPts[^1], closedLoopPts[0]);
                    }
                }
                g.SmoothingMode = SmoothingMode.Default;
                break;
        }

        if (!_hasSelection)
            _lastSelectionRect = Rectangle.Empty;

        // Crosshair guidelines
        if (ShowCrosshairGuides && _mode != CaptureMode.ColorPicker)
        {
            var cur = _lastCursorPos == Point.Empty
                ? PointToClient(System.Windows.Forms.Cursor.Position)
                : _lastCursorPos;
            // Soft shadow for visibility on light backgrounds
            using var chShadow = new Pen(Color.FromArgb(20, 0, 0, 0), 3f);
            g.DrawLine(chShadow, cur.X + 1, 0, cur.X + 1, ClientSize.Height);
            g.DrawLine(chShadow, 0, cur.Y + 1, ClientSize.Width, cur.Y + 1);
            using var chPen = DashedPen(80, 1f);
            g.DrawLine(chPen, cur.X, 0, cur.X, ClientSize.Height);
            g.DrawLine(chPen, 0, cur.Y, ClientSize.Width, cur.Y);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Static dashed pen for all selection borders.</summary>
    private static Pen DashedPen(int alpha, float width = 2f) => new Pen(Color.FromArgb(alpha, 255, 255, 255), width)
    {
        DashStyle = DashStyle.Dash,
        DashPattern = new[] { 6f, 4f }
    };

    private static bool SegmentsIntersect(Point p1, Point p2, Point q1, Point q2)
    {
        static long Cross(Point a, Point b, Point c)
            => (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);

        static bool OnSegment(Point a, Point b, Point p)
            => Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X)
            && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);

        long d1 = Cross(p1, p2, q1);
        long d2 = Cross(p1, p2, q2);
        long d3 = Cross(q1, q2, p1);
        long d4 = Cross(q1, q2, p2);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        if (d1 == 0 && OnSegment(p1, p2, q1)) return true;
        if (d2 == 0 && OnSegment(p1, p2, q2)) return true;
        if (d3 == 0 && OnSegment(q1, q2, p1)) return true;
        if (d4 == 0 && OnSegment(q1, q2, p2)) return true;

        return false;
    }

    private static float PolygonArea(Point[] pts)
    {
        if (pts.Length < 3) return 0f;
        long sum = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            sum += (long)a.X * b.Y - (long)b.X * a.Y;
        }
        return Math.Abs(sum) * 0.5f;
    }

    private static Point[] DeduplicateAdjacent(Point[] pts)
    {
        if (pts.Length <= 1) return pts;
        var list = new List<Point>(pts.Length) { pts[0] };
        for (int i = 1; i < pts.Length; i++)
        {
            if (pts[i] != list[^1])
                list.Add(pts[i]);
        }
        if (list.Count > 1 && list[0] == list[^1])
            list.RemoveAt(list.Count - 1);
        return list.ToArray();
    }

    private static void PaintShadow(Graphics g, RectangleF rect, float radius, int alpha = 52, float yOffset = 1f)
    {
        var shadowRect = rect;
        shadowRect.Inflate(2f, 2f);
        shadowRect.Offset(0, yOffset);
        using var path = RRect(shadowRect, radius + 2f);
        using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        g.FillPath(brush, path);
    }

    // Annotations are now rendered into the cached base bitmap.
    // This method is kept for the active-tool previews (live drawing).
    private void PaintAnnotations(Graphics g)
    {

        // Active tool previews
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
        if (_mode == CaptureMode.Blur && _isBlurring)
        {
            var pr = NormRect(_blurStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 2 && pr.Height > 2)
            {
                using var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, pr);
            }
        }
        if (_mode == CaptureMode.Highlight && _isHighlighting)
        {
            var pr = NormRect(_highlightStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 1 && pr.Height > 1)
                SketchRenderer.DrawHighlightRect(g, pr, DefaultHighlightColor);
        }
        if (_mode == CaptureMode.RectShape && _isRectShapeDragging)
        {
            var pr = GetShapeRect(PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 1 && pr.Height > 1)
                SketchRenderer.DrawRectShape(g, pr, _toolColor);
        }
        if (_mode == CaptureMode.CircleShape && _isCircleShapeDragging)
        {
            var pr = GetShapeRect(PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 1 && pr.Height > 1)
                SketchRenderer.DrawCircleShape(g, pr, _toolColor);
        }
        if (_mode == CaptureMode.Line && _isLineDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            SketchRenderer.DrawLine(g, _lineStart, cur, _toolColor, _lineStart.GetHashCode());
        }
        if (_mode == CaptureMode.Ruler && _isRulerDragging)
        {
            var cur = GetRulerEnd(PointToClient(System.Windows.Forms.Cursor.Position));
            PaintRuler(g, _rulerStart, cur);
        }
        if (_mode == CaptureMode.Arrow && _isArrowDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            SketchRenderer.DrawArrow(g, _arrowStart, cur, _toolColor, _arrowStart.GetHashCode());
        }
        if (_mode == CaptureMode.CurvedArrow && _isCurvedArrowDragging && _currentCurvedArrow is { Count: >= 2 })
            SketchRenderer.DrawCurvedArrow(g, _currentCurvedArrow, _toolColor, 42);
        if (_mode == CaptureMode.Draw && _isSelecting && _currentStroke is { Count: >= 2 })
            SketchRenderer.DrawFreehandStroke(g, _currentStroke, _toolColor, 6f);

        // Magnifier preview
        if (_mode == CaptureMode.Magnifier)
            PaintMagnifierTool(g);

        // Active text input with selection box (TextBox control handles actual text rendering)
        if (_isTyping)
        {
            var fontStyle = FontStyle.Regular;
            if (_textBold) fontStyle |= FontStyle.Bold;
            if (_textItalic) fontStyle |= FontStyle.Italic;
            using var font = new Font(_textFontFamily, _textFontSize, fontStyle);
            string measure = _textBuffer.Length > 0 ? _textBuffer : "W";
            var textSize = g.MeasureString(measure, font);

            // Dashed selection border around the TextBox
            var textRect = new RectangleF(_textPos.X - 6, _textPos.Y - 4,
                Math.Max(textSize.Width + 30, 120), textSize.Height + 12);
            using var dashPen = new Pen(Color.FromArgb(140, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dashPen, textRect.X, textRect.Y, textRect.Width, textRect.Height);

            // Corner resize handles
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

            // Inline text formatting toolbar above text
            PaintTextToolbar(g, textRect);
        }

        // Emoji placing preview (follow cursor)
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji && _selectedEmoji != null)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            PaintEmojiAnnotation(g, new Point(cur.X - (int)(_emojiPlaceSize / 2), cur.Y - (int)(_emojiPlaceSize / 2)),
                _selectedEmoji, _emojiPlaceSize, 0.6f);
        }

        // Color/emoji/font picker popups are painted on the separate ToolbarForm
    }

    /// <summary>Text annotation: uses DrawString for correct kerning. Shadow and stroke via offset draws.</summary>
    private static void PaintExcalidrawText(Graphics g, Point pos, string text, float fontSize, Color color,
        bool bold = true, bool italic = false, bool stroke = true, bool shadow = true, string fontFamily = "Segoe UI")
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        Font font;
        try { font = new Font(fontFamily, fontSize, style); }
        catch { font = new Font("Segoe UI", fontSize, style); }

        using (font)
        {
            // Shadow: draw text offset in dark color at multiple offsets for soft effect
            if (shadow)
            {
                using var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));
                g.DrawString(text, font, shadowBrush, pos.X + 2, pos.Y + 2);
                using var shadowBrush2 = new SolidBrush(Color.FromArgb(25, 0, 0, 0));
                g.DrawString(text, font, shadowBrush2, pos.X + 3, pos.Y + 3);
            }

            // Stroke: draw text at small offsets in dark color to simulate outline
            if (stroke)
            {
                using var strokeBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
                for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                        if (ox != 0 || oy != 0)
                            g.DrawString(text, font, strokeBrush, pos.X + ox, pos.Y + oy);
            }

            // Main text
            using var fillBrush = new SolidBrush(color);
            g.DrawString(text, font, fillBrush, pos.X, pos.Y);
        }

        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private static void PaintStepNumber(Graphics g, Point pos, int num, Color color)
    {
        int radius = 16;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Soft shadow
        SketchRenderer.DrawSoftEllipseShadow(g, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
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

    private void PaintFontPicker(Graphics g)
    {
        var fonts = GetFilteredFonts();
        int itemH = 28, pad = 6, visibleCount = 8;
        int searchBarH = 28;
        int pw = 240, ph = searchBarH + pad + visibleCount * itemH + pad * 2;

        // Position near the text input area
        int px, py;
        if (_isTyping)
        {
            px = _textPos.X;
            py = _textPos.Y - ph - 10;
            if (py < 10) py = _textPos.Y + 40;
        }
        else
        {
            px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
            py = _toolbarRect.Bottom + 8;
        }
        _fontPickerRect = new Rectangle(px, py, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _fontPickerRect, 10f, 58, 1f);
        using (var bgPath = RRect(_fontPickerRect, 10))
        {
            using var bg = new SolidBrush(Color.FromArgb(235, 18, 18, 18));
            g.FillPath(bg, bgPath);
            using var border = new Pen(Color.FromArgb(40, 255, 255, 255));
            g.DrawPath(border, bgPath);
        }

        // Search bar
        var searchRect = new Rectangle(px + pad, py + pad, pw - pad * 2, searchBarH);
        using (var searchPath = RRect(searchRect, 6))
        {
            using var searchBg = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillPath(searchBg, searchPath);
            using var focusBorder = new Pen(Color.FromArgb(100, 255, 255, 255), 1f);
            g.DrawPath(focusBorder, searchPath);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        string searchDisplay = _fontSearch.Length > 0 ? _fontSearch : "Search fonts...";
        using var searchBrush = new SolidBrush(_fontSearch.Length > 0
            ? Color.FromArgb(230, 255, 255, 255) : Color.FromArgb(70, 255, 255, 255));
        using var searchFont = new Font("Segoe UI", 10f);
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 8, searchRect.Y + 5);
        if (_fontSearch.Length > 0)
        {
            float cursorX = searchRect.X + 8 + g.MeasureString(_fontSearch, searchFont).Width - 2;
            using var cursorPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
            g.DrawLine(cursorPen, cursorX, searchRect.Y + 7, cursorX, searchRect.Bottom - 7);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;

        // Font list
        int listY = py + pad + searchBarH + pad;
        int maxScroll = Math.Max(0, fonts.Length - visibleCount);
        for (int i = 0; i < visibleCount && (_fontPickerScroll + i) < fonts.Length; i++)
        {
            int idx = _fontPickerScroll + i;
            string name = fonts[idx];
            int iy = listY + i * itemH;
            bool active = name == _textFontFamily;
            bool hovered = idx == _fontPickerHovered;

            if (active || hovered)
            {
                var itemRect = new Rectangle(px + pad, iy, pw - pad * 2, itemH);
                using var itemPath = RRect(itemRect, 5);
                int alpha = active ? 40 : 20;
                using var itemBg = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillPath(itemBg, itemPath);
            }

            // Cache font objects for perf
            if (!_fontCache.TryGetValue(name, out var font))
            {
                try { font = new Font(name, 11f); }
                catch { font = new Font("Segoe UI", 11f); }
                _fontCache[name] = font;
            }
            using var brush = new SolidBrush(Color.FromArgb(active ? 255 : 180, 255, 255, 255));
            g.DrawString(name, font, brush, px + pad + 6, iy + 4);
        }

        // Scroll indicator
        if (fonts.Length > visibleCount)
        {
            int trackH = visibleCount * itemH - 4;
            int trackX = px + pw - pad - 3;
            int trackY = listY + 2;
            using var trackBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            g.FillRectangle(trackBrush, trackX, trackY, 3, trackH);
            int thumbH = Math.Max(10, trackH * visibleCount / fonts.Length);
            int thumbY = maxScroll > 0 ? trackY + (int)((float)_fontPickerScroll / maxScroll * (trackH - thumbH)) : trackY;
            using var thumbBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.FillRectangle(thumbBrush, trackX, thumbY, 3, thumbH);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintEmojiAnnotation(Graphics g, Point pos, string emoji, float size, float opacity = 1f)
    {
        var emojiBmp = _emojiRenderer.GetEmoji(emoji, size);

        if (opacity < 1f)
        {
            using var attr = new System.Drawing.Imaging.ImageAttributes();
            float[][] matrix = {
                new[] { 1f, 0, 0, 0, 0 }, new[] { 0, 1f, 0, 0, 0 },
                new[] { 0, 0, 1f, 0, 0 }, new[] { 0, 0, 0, opacity, 0 },
                new[] { 0, 0, 0, 0, 1f }
            };
            attr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(matrix));
            g.DrawImage(emojiBmp, new Rectangle(pos.X, pos.Y, emojiBmp.Width, emojiBmp.Height),
                0, 0, emojiBmp.Width, emojiBmp.Height, GraphicsUnit.Pixel, attr);
        }
        else
        {
            g.DrawImage(emojiBmp, pos.X, pos.Y);
        }
    }

    private void PaintEmojiPicker(Graphics g)
    {
        // Filter emojis by search
        var filtered = GetFilteredEmojiPalette();

        int cols = 8, emojiSize = 32, pad = 6;
        int visibleRows = 4;
        int totalRows = (filtered.Length + cols - 1) / cols;
        int gridH = visibleRows * (emojiSize + pad);
        int searchBarH = 28;
        int pw = cols * (emojiSize + pad) + pad;
        int ph = searchBarH + pad + gridH + pad;

        // Center below toolbar
        int px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
        int py = _toolbarRect.Bottom + 8;
        _emojiPickerRect = new Rectangle(px, py, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _emojiPickerRect, 12f, 58, 1f);
        using (var bgPath = RRect(_emojiPickerRect, 12))
        {
            using var bg = new SolidBrush(Color.FromArgb(230, 32, 32, 32));
            g.FillPath(bg, bgPath);
            using var border = new Pen(Color.FromArgb(35, 255, 255, 255), 1f);
            g.DrawPath(border, bgPath);
        }

        // Search bar with focus indicator
        var searchRect = new Rectangle(px + pad, py + pad, pw - pad * 2, searchBarH);
        using (var searchPath = RRect(searchRect, 6))
        {
            using var searchBg = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillPath(searchBg, searchPath);
            // Focus border
            using var focusBorder = new Pen(Color.FromArgb(100, 255, 255, 255), 1f);
            g.DrawPath(focusBorder, searchPath);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var searchFont = new Font("Segoe UI", 10f);
        string searchDisplay = _emojiSearch.Length > 0 ? _emojiSearch : "Search emoji...";
        using var searchBrush = new SolidBrush(_emojiSearch.Length > 0
            ? Color.FromArgb(230, 255, 255, 255)
            : Color.FromArgb(70, 255, 255, 255));
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 8, searchRect.Y + 5);
        // Text cursor (always visible when picker is open)
        {
            float cursorX = _emojiSearch.Length > 0
                ? searchRect.X + 8 + g.MeasureString(_emojiSearch, searchFont).Width - 2
                : searchRect.X + 8;
            using var cursorPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
            g.DrawLine(cursorPen, cursorX, searchRect.Y + 7, cursorX, searchRect.Bottom - 7);
        }

        using var searchHintFont = new Font("Segoe UI", 8f);
        using var searchHintBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        g.DrawString("Type to search", searchHintFont, searchHintBrush, searchRect.Right - 78, searchRect.Y + 7);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;

        // Emoji grid (render via screen DC for real color emoji)
        int gridY = py + pad + searchBarH + pad;
        int scrollRow = _emojiScrollOffset;
        int startIdx = scrollRow * cols;

        for (int i = 0; i < visibleRows * cols && (startIdx + i) < filtered.Length; i++)
        {
            int idx = startIdx + i;
            int col = i % cols, row = i / cols;
            int ex = px + pad + col * (emojiSize + pad);
            int ey = gridY + row * (emojiSize + pad);

            bool hovered = _emojiHovered == idx;
            if (hovered)
            {
                using var hoverPath = RRect(new RectangleF(ex - 2, ey - 2, emojiSize + 4, emojiSize + 4), 6);
                using var hoverBg = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                g.FillPath(hoverBg, hoverPath);
            }

            var emojiBmp = _emojiRenderer.GetEmoji(filtered[idx].emoji, 22f);
            g.DrawImage(emojiBmp, ex + 2, ey + 2);
        }

        // Scroll indicator
        if (totalRows > visibleRows)
        {
            int trackH = gridH - 4;
            int trackX = px + pw - pad - 3;
            int trackY = gridY + 2;
            using var trackBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            g.FillRectangle(trackBrush, trackX, trackY, 3, trackH);
            int thumbH = Math.Max(10, trackH * visibleRows / totalRows);
            int thumbY = trackY + (int)((float)scrollRow / (totalRows - visibleRows) * (trackH - thumbH));
            using var thumbBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.FillRectangle(thumbBrush, trackX, thumbY, 3, thumbH);
        }

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
        PaintShadow(g, _colorPickerRect, 8f, 58, 1f);
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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y,
            _toolbarRect.Width, _toolbarRect.Height);

        // Pill background -- solid dark, barely-visible border like Windows Snipping Tool
        PaintShadow(g, r, ToolbarHeight / 2f, 58, 1f);
        using (var p = RRect(r, ToolbarHeight / 2))
        {
            using var baseFill = new SolidBrush(Color.FromArgb(255, 32, 32, 32));
            g.FillPath(baseFill, p);
            using var bp = new Pen(Color.FromArgb(18, 255, 255, 255), 1f);
            g.DrawPath(bp, p);
        }

        // Separator lines at group boundaries
        int sepY1 = r.Y + 12;
        int sepY2 = r.Bottom - 12;
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            int sx = _toolbarButtons[idx].Right + (ButtonSpacing + GroupGap) / 2;
            using var sepPen = new Pen(Color.FromArgb(32, 255, 255, 255), 1f);
            g.DrawLine(sepPen, sx, sepY1, sx, sepY2);
        }

        // Build dynamic arrays from visible tools + fixed buttons
        int toolCount = _visibleTools.Length;
        string[] icons = new string[BtnCount];
        string[] labels = new string[BtnCount];
        CaptureMode?[] modes = new CaptureMode?[BtnCount];
        for (int i = 0; i < toolCount; i++)
        {
            icons[i] = _visibleTools[i].Id;
            labels[i] = _visibleTools[i].Label;
            modes[i] = _visibleTools[i].Mode;
        }
        icons[toolCount] = "color"; labels[toolCount] = "Color"; modes[toolCount] = null;
        icons[toolCount + 1] = "gear"; labels[toolCount + 1] = "Settings"; modes[toolCount + 1] = null;
        icons[toolCount + 2] = "close"; labels[toolCount + 2] = "Close (Esc)"; modes[toolCount + 2] = null;

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = _toolbarButtons[i];
            bool active = modes[i] is { } m && _mode == m;
            bool hover = _hoveredButton == i;

            // Color dot button
            if (icons[i] == "color")
            {
                if (hover)
                    g.FillEllipse(new SolidBrush(Color.FromArgb(18, 255, 255, 255)),
                        btn.X, btn.Y, btn.Width, btn.Height);
                int dotSize = 16;
                int dx = btn.X + (btn.Width - dotSize) / 2;
                int dy = btn.Y + (btn.Height - dotSize) / 2;
                using var cBrush = new SolidBrush(_toolColor);
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                continue;
            }

            // Hover: pill-shaped highlight matching the dock radius
            if (hover)
                g.FillEllipse(new SolidBrush(Color.FromArgb(18, 255, 255, 255)),
                    btn.X, btn.Y, btn.Width, btn.Height);

            // Active = full white icon, default = dimmed, hover = mid
            int ia = active ? 255 : hover ? 220 : i >= BtnCount - 2 ? 130 : 160;
            DrawIcon(g, icons[i], btn, Color.FromArgb(ia, 255, 255, 255));
        }

        // Tooltip
        if (_hoveredButton >= 0 && _hoveredButton < labels.Length)
        {
            string label = labels[_hoveredButton];
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var tipFont = new Font("Segoe UI Semibold", 8.25f, FontStyle.Regular);
            var sz = g.MeasureString(label, tipFont);
            var btnRect = _toolbarButtons[_hoveredButton];
            float tx = btnRect.X + btnRect.Width / 2f - sz.Width / 2f;
            float ty = r.Bottom + 6;
            var tipRect = new RectangleF(tx - 10, ty - 4, sz.Width + 20, sz.Height + 8);
            PaintShadow(g, tipRect, tipRect.Height / 2f, 52, 1f);
            using (var tipPath = RRect(tipRect, tipRect.Height / 2f))
            {
                using var tipBg = new SolidBrush(Color.FromArgb(240, 24, 24, 24));
                g.FillPath(tipBg, tipPath);
                using var tipBorder = new Pen(Color.FromArgb(28, 255, 255, 255), 1f);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            g.DrawString(label, tipFont, tipBrush, tx, ty);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        if (_showToolNumberBadges && _hoveredButton >= 0)
        {
            // Annotation tool hotkey badges using key labels from AnnotationKeyMap
            int badgeIndex = 0;
            for (int i = 0; i < toolCount; i++)
            {
                var mode = modes[i];
                if (mode is null) continue;
                if (!IsAnnotationNumberBadgeMode(mode.Value)) continue;
                if (badgeIndex >= AnnotationKeyMap.Length) break;

                string label = AnnotationKeyMap[badgeIndex].label;
                var btn = _toolbarButtons[i];
                var badgeRect = new RectangleF(btn.Right - 9, btn.Bottom - 9, 12, 12);
                using var badgeBg = new SolidBrush(Color.FromArgb(235, 24, 24, 24));
                using var badgeBorder = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
                using var badgeText = new SolidBrush(Color.FromArgb(185, 255, 255, 255));
                using var badgeFont = new Font("Segoe UI", 6.5f, FontStyle.Bold);
                g.FillEllipse(badgeBg, badgeRect);
                g.DrawEllipse(badgeBorder, badgeRect);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(label, badgeFont, badgeText, badgeRect, _iconFmt);
                g.TextRenderingHint = TextRenderingHint.SystemDefault;
                badgeIndex++;
            }
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Called by the separate ToolbarForm to paint toolbar, tooltips, and popups.
    /// Graphics is already translated so overlay coordinates map correctly.
    /// </summary>
    public void PaintToolbarTo(Graphics g, Rectangle clip, Point unused)
    {
        PaintToolbar(g);
        if (_colorPickerOpen) PaintColorPicker(g);
        if (_emojiPickerOpen) PaintEmojiPicker(g);
        if (_fontPickerOpen) PaintFontPicker(g);
    }

    // PaintTopFade removed - always-on dim provides sufficient contrast for toolbar

    // Fixed button glyphs (not in ToolDef)
    private static readonly Dictionary<string, char> FixedGlyphs = new()
    {
        ["gear"]  = '\uE157', // lucide settings
        ["close"] = '\uE1B1', // lucide x
    };

    private static System.Drawing.Text.PrivateFontCollection? _iconFontCollection;
    private static Font? _iconFontCached;

    private static Font GetIconFont()
    {
        if (_iconFontCached != null) return _iconFontCached;
        _iconFontCollection = new System.Drawing.Text.PrivateFontCollection();
        string dir = AppContext.BaseDirectory;
        string path = System.IO.Path.Combine(dir, "lucide.ttf");
        if (System.IO.File.Exists(path))
            _iconFontCollection.AddFontFile(path);
        var family = _iconFontCollection.Families.Length > 0
            ? _iconFontCollection.Families[0]
            : new FontFamily("Segoe UI");
        _iconFontCached = new Font(family, 14f, FontStyle.Regular, GraphicsUnit.Point);
        return _iconFontCached;
    }

    private static readonly StringFormat _iconFmt = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoClip
    };

    // Cached lookup for icon id -> glyph char (avoids LINQ FirstOrDefault per paint)
    private static Dictionary<string, char>? _iconGlyphCache;
    private static Dictionary<string, char> GetIconGlyphMap()
    {
        if (_iconGlyphCache != null) return _iconGlyphCache;
        _iconGlyphCache = new Dictionary<string, char>(ToolDef.AllTools.Length + FixedGlyphs.Count);
        foreach (var t in ToolDef.AllTools)
            _iconGlyphCache[t.Id] = t.Icon;
        foreach (var kv in FixedGlyphs)
            _iconGlyphCache[kv.Key] = kv.Value;
        return _iconGlyphCache;
    }

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c)
    {
        if (icon == "color") return;
        if (!GetIconGlyphMap().TryGetValue(icon, out char glyph)) return;

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var font = GetIconFont();
        using var brush = new SolidBrush(c);
        var rect = new RectangleF(b.X, b.Y, b.Width, b.Height);
        g.DrawString(glyph.ToString(), font, brush, rect, _iconFmt);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private void DrawLabel(Graphics g, Rectangle rect, bool isOcr, bool isScan = false)
    {
        string text = isOcr ? $"OCR  {rect.Width} x {rect.Height}"
            : isScan ? $"SCAN  {rect.Width} x {rect.Height}"
            : $"{rect.Width} x {rect.Height}";
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", 10f);
        var sz = g.MeasureString(text, font);
        float lx = rect.X, ly = rect.Bottom + 8;
        if (ly + sz.Height > ClientSize.Height) ly = rect.Y - sz.Height - 8;
        var lr = new RectangleF(lx - 8, ly - 3, sz.Width + 16, sz.Height + 6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = RRect(lr, 8))
        {
            using var lblBg = new SolidBrush(Color.FromArgb(230, 32, 32, 32));
            g.FillPath(lblBg, p);
            using var border = new Pen(Color.FromArgb(35, 255, 255, 255), 1f);
            g.DrawPath(border, p);
        }
        g.SmoothingMode = SmoothingMode.Default;
        using var fg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        g.DrawString(text, font, fg, lx, ly);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private void PaintTextToolbar(Graphics g, RectangleF textRect)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float btnH = 26, btnPad = 3, pad = 5, sepW = 6;

        using var uiFont = new Font("Segoe UI", 9f);
        using var uiFontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var uiFontItalic = new Font("Segoe UI", 9.5f, FontStyle.Italic);
        using var uiFontSmall = new Font("Segoe UI", 7.5f);

        string fontLabel = _textFontFamily.Length > 14 ? _textFontFamily[..13] + ".." : _textFontFamily;
        var fontLabelSize = g.MeasureString(fontLabel, uiFont);

        float btnW = 26;
        float fontW = fontLabelSize.Width + 18;
        float totalW = btnW * 4 + btnPad * 3 + sepW + fontW + pad * 2;
        float totalH = btnH + pad * 2;

        float tx = textRect.X;
        float ty = textRect.Y - totalH - 6;
        if (ty < 4) ty = textRect.Bottom + 6;

        _textToolbarRect = new RectangleF(tx, ty, totalW, totalH);

        PaintShadow(g, _textToolbarRect, 8f, 48, 1f);
        using (var bgPath = RRect(_textToolbarRect, 8))
        {
            using var bg = new SolidBrush(Color.FromArgb(245, 28, 28, 28));
            g.FillPath(bg, bgPath);
            using var border = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
            g.DrawPath(border, bgPath);
        }

        float cx = tx + pad;
        float cy = ty + pad;

        int btnIdx = 0;
        void DrawToggleBtn(ref RectangleF rect, float x, string label, Font f, bool active)
        {
            rect = new RectangleF(x, cy, btnW, btnH);
            bool hovered = _hoveredTextBtn == btnIdx;
            using var btnPath = RRect(rect, 5);
            int bgAlpha = active ? 50 : hovered ? 30 : 12;
            using var btnBg = new SolidBrush(Color.FromArgb(bgAlpha, 255, 255, 255));
            g.FillPath(btnBg, btnPath);
            if (hovered)
            {
                using var hoverBorder = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
                g.DrawPath(hoverBorder, btnPath);
            }
            int textAlpha = active ? 255 : hovered ? 200 : 120;
            using var brush = new SolidBrush(Color.FromArgb(textAlpha, 255, 255, 255));
            g.DrawString(label, f, brush, rect, _iconFmt);
            btnIdx++;
        }

        // B, I, S(troke), Sh(adow)
        DrawToggleBtn(ref _textBoldBtnRect, cx, "B", uiFontBold, _textBold);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textItalicBtnRect, cx, "I", uiFontItalic, _textItalic);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textStrokeBtnRect, cx, "S", uiFontSmall, _textStroke);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textShadowBtnRect, cx, "Sh", uiFontSmall, _textShadow);
        cx += btnW + sepW;

        // Font selector
        _textFontBtnRect = new RectangleF(cx, cy, fontW, btnH);
        {
            bool fontHovered = _hoveredTextBtn == 4;
            using var btnPath = RRect(_textFontBtnRect, 5);
            int bgAlpha = _fontPickerOpen ? 40 : fontHovered ? 30 : 12;
            using var btnBg = new SolidBrush(Color.FromArgb(bgAlpha, 255, 255, 255));
            g.FillPath(btnBg, btnPath);
            if (fontHovered)
            {
                using var hoverBorder = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
                g.DrawPath(hoverBorder, btnPath);
            }
        }
        int fontTextAlpha = _hoveredTextBtn == 4 ? 255 : 200;
        using var fontBrush = new SolidBrush(Color.FromArgb(fontTextAlpha, 255, 255, 255));
        g.DrawString(fontLabel, uiFont, fontBrush, _textFontBtnRect, _iconFmt);

        // Tooltip for hovered text button
        if (_hoveredTextBtn >= 0 && _textBtnTooltip.Length > 0)
        {
            var hovRect = _hoveredTextBtn switch
            {
                0 => _textBoldBtnRect, 1 => _textItalicBtnRect,
                2 => _textStrokeBtnRect, 3 => _textShadowBtnRect,
                _ => _textFontBtnRect
            };
            using var tipFont = new Font("Segoe UI", 8f);
            var tipSize = g.MeasureString(_textBtnTooltip, tipFont);
            float tipX = hovRect.X + hovRect.Width / 2f - tipSize.Width / 2f - 6;
            float tipY = _textToolbarRect.Y - tipSize.Height - 10;
            var tipRect = new RectangleF(tipX, tipY, tipSize.Width + 12, tipSize.Height + 6);
            PaintShadow(g, tipRect, tipRect.Height / 2f, 40, 1f);
            using var tipPath = RRect(tipRect, tipRect.Height / 2f);
            using var tipBg = new SolidBrush(Color.FromArgb(240, 24, 24, 24));
            g.FillPath(tipBg, tipPath);
            using var tipBorder = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
            g.DrawPath(tipBorder, tipPath);
            using var tipBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            g.DrawString(_textBtnTooltip, tipFont, tipBrush, tipX + 6, tipY + 3);
        }

        g.TextRenderingHint = TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintRuler(Graphics g, Point from, Point to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;

        using var shadowPen = new Pen(Color.FromArgb(45, 0, 0, 0), 4f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 2f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLine(shadowPen, from.X + 1, from.Y + 1, to.X + 1, to.Y + 1);
        g.DrawLine(pen, from, to);
        using var dotBrush = new SolidBrush(Color.White);
        g.FillEllipse(dotBrush, from.X - 3, from.Y - 3, 6, 6);
        g.FillEllipse(dotBrush, to.X - 3, to.Y - 3, 6, 6);

        string text = $"{(int)dist}px   {Math.Abs(dx):0}px x {Math.Abs(dy):0}px   {angle:0.#} deg";
        using var font = new Font("Segoe UI", 10f, FontStyle.Regular);
        var sz = g.MeasureString(text, font);
        var mid = new PointF((from.X + to.X) / 2f, (from.Y + to.Y) / 2f);
        var label = new RectangleF(mid.X - sz.Width / 2f - 8, mid.Y - sz.Height - 16, sz.Width + 16, sz.Height + 8);
        PaintShadow(g, label, 8f, 48, 1f);
        using var path = RRect(label, 8f);
        using var bg = new SolidBrush(Color.FromArgb(235, 32, 32, 32));
        using var border = new Pen(Color.FromArgb(28, 255, 255, 255), 1f);
        using var fg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        g.FillPath(bg, path);
        g.DrawPath(border, path);
        g.DrawString(text, font, fg, label.X + 8, label.Y + 4);
        g.SmoothingMode = SmoothingMode.Default;
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


}
