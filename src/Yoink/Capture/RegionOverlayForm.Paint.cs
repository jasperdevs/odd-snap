using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
using Yoink.Helpers;
using Yoink.Models;

// Unified dash/border constants for consistency across all selection borders
// Every dashed border in the app uses these same values.

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private static void ApplyUiGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.None;

        // Blit the cached screenshot + committed annotations layer.
        var clip = e.ClipRectangle;
        var committed = GetCommittedAnnotationsBitmap();
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(committed, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        bool isOcr = _mode == CaptureMode.Ocr;
        bool isScan = _mode == CaptureMode.Scan;
        bool isSelectionMode = _mode is CaptureMode.Rectangle or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Live tool previews (active drawing in progress)
        PaintAnnotations(g);

        // Select tool: draw selection highlight and handles
        if (_mode == CaptureMode.Select && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            var bounds = GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var selRect = Rectangle.Inflate(bounds, 4, 4);
                var selPen = _selectDashPen ??= new Pen(Color.FromArgb(200, 100, 149, 237), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(selPen, selRect);

                // Corner handles
                int hs = 6;
                var hBrush = _selectHandleBrush ??= new SolidBrush(UiChrome.SurfaceTextPrimary);
                var hPen = _selectHandlePen ??= new Pen(Color.FromArgb(200, 100, 149, 237), 1f);
                var corners = new[] {
                    new Rectangle(selRect.X - hs / 2, selRect.Y - hs / 2, hs, hs),
                    new Rectangle(selRect.Right - hs / 2, selRect.Y - hs / 2, hs, hs),
                    new Rectangle(selRect.X - hs / 2, selRect.Bottom - hs / 2, hs, hs),
                    new Rectangle(selRect.Right - hs / 2, selRect.Bottom - hs / 2, hs, hs),
                };
                foreach (var c in corners) { g.FillRectangle(hBrush, c); g.DrawRectangle(hPen, c); }
            }
        }

        if (_mode == CaptureMode.ColorPicker)
            return; // magnifier is its own layered window, overlay stays static

        if (isSelectionMode && !_isSelecting && _autoDetectActive && _autoDetectRect.Width > 0)
        {
            // Clamp the rect so dashes stay within the visible client area
            var drawRect = ClampRectToClient(_autoDetectRect);
            if (drawRect.Width > 0 && drawRect.Height > 0)
            {
                g.DrawRectangle(ShadowPen(30), drawRect.X + 1, drawRect.Y + 1, drawRect.Width, drawRect.Height);
                g.DrawRectangle(DashedPen(180), drawRect);
            }
            _lastAutoDetectRect = _autoDetectRect;
        }
        else if (isSelectionMode && !_hasSelection && !_isSelecting)
        {
            // Full-screen fallback border: use dashed pattern to match auto-detect style
            g.DrawRectangle(DashedPen(120), 2, 2, ClientSize.Width - 5, ClientSize.Height - 5);
            _lastAutoDetectRect = Rectangle.Empty;
        }

        // Selection borders (on top of everything)
        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
            case CaptureMode.Ocr when _hasSelection:
            case CaptureMode.Scan when _hasSelection:
            case CaptureMode.Sticker when _hasSelection:
                // Subtle outer shadow
                {
                    var sr = _selectionRect;
                    sr.Inflate(1, 1);
                    g.DrawRectangle(ShadowPen(40), sr);
                }
                // Static dashed border
                g.DrawRectangle(DashedPen(255), _selectionRect);
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
                                var dimBrush = _freeformDimBrush ??= new SolidBrush(Color.FromArgb(100, 0, 0, 0));
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
                g.DrawLines(ShadowPen(30), pts);
                var freeformPen = DashedPen(220);
                g.DrawLines(freeformPen, pts);
                if (hasClosed && closedLoopPts is { Length: >= 3 })
                {
                    g.DrawLine(freeformPen, closedLoopPts[^1], closedLoopPts[0]);
                }
                g.SmoothingMode = SmoothingMode.Default;
                break;
        }

        if (!_hasSelection)
            _lastSelectionRect = Rectangle.Empty;

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Clamp a rectangle so it stays 2px inside the client area (prevents dashes from being cut off at screen edges).</summary>
    private Rectangle ClampRectToClient(Rectangle rect)
    {
        const int pad = 2;
        int x = Math.Max(pad, rect.X);
        int y = Math.Max(pad, rect.Y);
        int right = Math.Min(ClientSize.Width - pad - 1, rect.Right);
        int bottom = Math.Min(ClientSize.Height - pad - 1, rect.Bottom);
        return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    // Cached GDI objects for hot-path rendering (avoid allocation per frame).
    private static Pen? _cachedDash180, _cachedDash255, _cachedDash120, _cachedDash220;
    private static Pen? _cachedShadow30, _cachedShadow40;
    private static SolidBrush? _freeformDimBrush;
    private static Pen? _selectDashPen, _selectHandlePen;
    private static SolidBrush? _selectHandleBrush;

    /// <summary>Cached dashed pen for selection borders. Reuses instances for common alpha values.</summary>
    private static Pen DashedPen(int alpha, float width = 2f)
    {
        // Fast path: return cached instance for the common alpha values used every frame.
        ref Pen? slot = ref _cachedDash180; // dummy init
        if (width == 2f)
        {
            switch (alpha)
            {
                case 120: slot = ref _cachedDash120; break;
                case 180: slot = ref _cachedDash180; break;
                case 220: slot = ref _cachedDash220; break;
                case 255: slot = ref _cachedDash255; break;
                default: slot = ref _cachedDash180; goto create; // uncommon alpha, always create
            }
            if (slot != null) return slot;
        }
        else goto create;

        create:
        var pen = new Pen(Color.FromArgb(alpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), width)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 6f, 4f }
        };
        if (width == 2f && (alpha is 120 or 180 or 220 or 255))
            slot = pen;
        return pen;
    }

    private static Pen ShadowPen(int alpha)
    {
        ref Pen? slot = ref alpha == 30 ? ref _cachedShadow30 : ref _cachedShadow40;
        return slot ??= new Pen(Color.FromArgb(alpha, 0, 0, 0), 4f);
    }

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
        var oldSmooth = g.SmoothingMode;
        var oldComp = g.CompositingMode;
        var oldCompQual = g.CompositingQuality;
        var oldPix = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var baseRect = rect;
        baseRect.Inflate(2f, 2f);
        var offsets = new (float dx, float dy, int a)[]
        {
            (5f, yOffset + 5f, alpha / 6),
            (3f, yOffset + 3f, alpha / 4),
            (1.5f, yOffset + 1.5f, alpha / 2),
            (0f, yOffset, alpha),
        };
        foreach (var (dx, dy, a) in offsets)
        {
            using var path = RRect(new RectangleF(baseRect.X + dx, baseRect.Y + dy, baseRect.Width, baseRect.Height), radius + 2f);
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(a, 1, 255), 0, 0, 0));
            g.FillPath(brush, path);
        }

        g.SmoothingMode = oldSmooth;
        g.CompositingMode = oldComp;
        g.CompositingQuality = oldCompQual;
        g.PixelOffsetMode = oldPix;
    }

    // This method only renders live previews for the in-progress tool state.
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
                using var pen = new Pen(UiChrome.SurfaceTextPrimary, 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, pr);
            }
        }
        if (_mode == CaptureMode.Blur && _isBlurring)
        {
            var pr = NormRect(_blurStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 2 && pr.Height > 2)
            {
                using var pen = new Pen(UiChrome.SurfaceTextPrimary, 1f) { DashStyle = DashStyle.Dash };
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
                SketchRenderer.DrawRectShape(g, pr, _toolColor, AnnotationStrokeShadow);
        }
        if (_mode == CaptureMode.CircleShape && _isCircleShapeDragging)
        {
            var pr = GetShapeRect(PointToClient(System.Windows.Forms.Cursor.Position));
            if (pr.Width > 1 && pr.Height > 1)
                SketchRenderer.DrawCircleShape(g, pr, _toolColor, AnnotationStrokeShadow);
        }
        if (_mode == CaptureMode.Line && _isLineDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            SketchRenderer.DrawLine(g, _lineStart, cur, _toolColor, _lineStart.GetHashCode(), AnnotationStrokeShadow);
        }
        if (_mode == CaptureMode.Ruler && _isRulerDragging)
        {
            var cur = GetRulerEnd(PointToClient(System.Windows.Forms.Cursor.Position));
            PaintRuler(g, _rulerStart, cur);
        }
        if (_mode == CaptureMode.Arrow && _isArrowDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            SketchRenderer.DrawArrow(g, _arrowStart, cur, _toolColor, _arrowStart.GetHashCode(), strokeShadow: AnnotationStrokeShadow);
        }
        if (_mode == CaptureMode.CurvedArrow && _isCurvedArrowDragging && _currentCurvedArrow is { Count: >= 2 })
            SketchRenderer.DrawCurvedArrow(g, _currentCurvedArrow, _toolColor, 42, AnnotationStrokeShadow);
        if (_mode == CaptureMode.Draw && _isSelecting && _currentStroke is { Count: >= 1 })
        {
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                var start = _currentStroke[0];
                var end = GetConstrainedDrawPoint(PointToClient(System.Windows.Forms.Cursor.Position));
                if (start != end)
                    SketchRenderer.DrawLine(g, start, end, _toolColor, start.GetHashCode(), AnnotationStrokeShadow);
            }
            else if (_currentStroke.Count >= 2)
            {
                SketchRenderer.DrawFreehandStroke(g, _currentStroke, _toolColor, 6f, AnnotationStrokeShadow);
            }
        }

        // Active text input (TextBox is off-screen for input, we paint visually here)
        if (_isTyping)
        {
            var fontStyle = FontStyle.Regular;
            if (_textBold) fontStyle |= FontStyle.Bold;
            if (_textItalic) fontStyle |= FontStyle.Italic;
            var font = GetAnnotationFont(_textFontFamily, _textFontSize, fontStyle);
            string display = _textBuffer.Length > 0 ? _textBuffer : "Type here...";
            var textSize = g.MeasureString(display, font);

            // Dashed selection border — use cached rect so handles match hit areas
            var textRect = GetActiveTextRect();
            using var dashPen = new Pen(UiChrome.SurfaceTextPrimary, 1f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dashPen, textRect.X, textRect.Y, textRect.Width, textRect.Height);

            // Corner resize handles — draw at cached handle positions with shadow outline
            using var handleShadow = new Pen(Color.FromArgb(80, 0, 0, 0), 1f);
            using var handleBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            foreach (var h in _activeTextHandleCache)
            {
                g.FillRectangle(handleBrush, h);
                g.DrawRectangle(handleShadow, h.X, h.Y, h.Width, h.Height);
            }

            // Render text with stroke/shadow
            if (_textBuffer.Length > 0)
            {
                PaintExcalidrawText(g, _textPos, _textBuffer, _textFontSize, _toolColor,
                    _textBold, _textItalic, _textStroke, _textShadow, _textFontFamily);
            }
            else
            {
                using var placeholderBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
                g.DrawString(display, font, placeholderBrush, _textPos.X, _textPos.Y);
            }

            // Blinking cursor — use Graphics.MeasureString to match DrawString positioning
            {
                float cursorX;
                if (_textBuffer.Length > 0)
                {
                    var cursorSize = g.MeasureString(_textBuffer, font, PointF.Empty, StringFormat.GenericDefault);
                    cursorX = _textPos.X + cursorSize.Width - 2;
                }
                else
                {
                    cursorX = _textPos.X;
                }
                // Smooth cursor blink using sine wave
                float blinkAlpha = (float)(Math.Sin(Environment.TickCount64 / 400.0 * Math.PI) * 0.5 + 0.5);
                int alpha = (int)(blinkAlpha * _toolColor.A);
                using var cursorPen = new Pen(Color.FromArgb(alpha, _toolColor), 2f);
                g.DrawLine(cursorPen, cursorX, _textPos.Y + 2, cursorX, _textPos.Y + textSize.Height - 4);
            }

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
        bool bold = true, bool italic = false, bool stroke = true, bool shadow = true, string fontFamily = UiChrome.DefaultFontFamily)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(fontFamily, fontSize, style);
        {
            // Shadow: draw text offset in dark color at multiple offsets for soft effect
            if (shadow)
            {
                g.DrawString(text, font, TextShadowBrush1, pos.X + 2, pos.Y + 2);
                g.DrawString(text, font, TextShadowBrush2, pos.X + 3, pos.Y + 3);
            }

            // Stroke: draw text at small offsets in dark color to simulate outline
            if (stroke)
            {
                for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                        if (ox != 0 || oy != 0)
                            g.DrawString(text, font, TextStrokeBrush, pos.X + ox, pos.Y + oy);
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
        using var borderPen = new Pen(UiChrome.SurfaceTextPrimary, 2f);
        g.DrawEllipse(borderPen, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
        // Number
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var font = UiChrome.ChromeFont(12f, FontStyle.Bold);
        string text = num.ToString();
        var sz = g.MeasureString(text, font);
        using var textBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.DrawString(text, font, textBrush, pos.X - sz.Width / 2, pos.Y - sz.Height / 2);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
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

        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bgPath = new GraphicsPath())
            {
                bgPath.AddEllipse(new RectangleF(px - 2, py - 2, dstSize + 4, dstSize + 4));
                using var bg = new SolidBrush(Color.FromArgb((int)(200 * opacity), UiChrome.SurfaceElevated.R, UiChrome.SurfaceElevated.G, UiChrome.SurfaceElevated.B));
                g.FillPath(bg, bgPath);
            }

            using var clipPath = new GraphicsPath();
            clipPath.AddEllipse(dstRect);
            g.SetClip(clipPath);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_screenshot, dstRect, srcRect, GraphicsUnit.Pixel);

            int ccx = px + dstSize / 2, ccy = py + dstSize / 2;
            using var crossPen = new Pen(Color.FromArgb((int)(180 * opacity), UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), 1f);
            g.DrawLine(crossPen, ccx - 8, ccy, ccx + 8, ccy);
            g.DrawLine(crossPen, ccx, ccy - 8, ccx, ccy + 8);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var borderPen = new Pen(Color.FromArgb((int)(70 * opacity), UiChrome.SurfaceBorderStrong.R, UiChrome.SurfaceBorderStrong.G, UiChrome.SurfaceBorderStrong.B), 1f);
            g.DrawPath(borderPen, clipPath);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private Graphics GetBlurPreviewGraphics(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        if (_blurPreviewBitmap == null || _blurPreviewSize != size)
        {
            _blurPreviewGraphics?.Dispose();
            _blurPreviewBitmap?.Dispose();
            _blurPreviewBitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            _blurPreviewGraphics = Graphics.FromImage(_blurPreviewBitmap);
            _blurPreviewSize = size;
        }

        return _blurPreviewGraphics!;
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
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle);
            g.DrawPath(border, bgPath);
        }

        // Search bar
        var searchRect = new Rectangle(px + pad, py + pad, pw - pad * 2, searchBarH);
        using (var searchPath = RRect(searchRect, 6))
        {
            using var searchBg = new SolidBrush(UiChrome.SurfaceHover);
            g.FillPath(searchBg, searchPath);
            using var focusBorder = new Pen(UiChrome.SurfaceBorderStrong, 1f);
            g.DrawPath(focusBorder, searchPath);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        string searchDisplay = _fontSearch.Length > 0 ? _fontSearch : "Search fonts...";
        using var searchBrush = new SolidBrush(_fontSearch.Length > 0
            ? UiChrome.SurfaceTextPrimary : UiChrome.SurfaceTextMuted);
        var searchFont = UiChrome.ChromeFont(10f);
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 8, searchRect.Y + 5);
        if (_fontSearch.Length > 0)
        {
            float cursorX = searchRect.X + 8 + g.MeasureString(_fontSearch, searchFont).Width - 2;
            using var cursorPen = new Pen(UiChrome.SurfaceTextPrimary, 1.5f);
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
                using var itemBg = new SolidBrush(Color.FromArgb(alpha, UiChrome.SurfaceHover.R, UiChrome.SurfaceHover.G, UiChrome.SurfaceHover.B));
                g.FillPath(itemBg, itemPath);
            }

            // Cache font objects for perf
            if (!_fontCache.TryGetValue(name, out var font))
            {
                try { font = new Font(name, 11f); }
                catch { font = UiChrome.ChromeFont(11f); }
                _fontCache[name] = font;
            }
            using var brush = new SolidBrush(Color.FromArgb(active ? 255 : 180, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(name, font, brush, px + pad + 6, iy + 4);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
        }

        // Scroll indicator
        if (fonts.Length > visibleCount)
        {
            int trackH = visibleCount * itemH - 4;
            int trackX = px + pw - pad - 3;
            int trackY = listY + 2;
            using var trackBrush = new SolidBrush(UiChrome.SurfaceHover);
            g.FillRectangle(trackBrush, trackX, trackY, 3, trackH);
            int thumbH = Math.Max(10, trackH * visibleCount / fonts.Length);
            int thumbY = maxScroll > 0 ? trackY + (int)((float)_fontPickerScroll / maxScroll * (trackH - thumbH)) : trackY;
            using var thumbBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
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
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            g.DrawPath(border, bgPath);
        }

        // Search bar with focus indicator
        var searchRect = new Rectangle(px + pad, py + pad, pw - pad * 2, searchBarH);
        using (var searchPath = RRect(searchRect, 6))
        {
            using var searchBg = new SolidBrush(UiChrome.SurfaceHover);
            g.FillPath(searchBg, searchPath);
            // Focus border
            using var focusBorder = new Pen(UiChrome.SurfaceBorderStrong, 1f);
            g.DrawPath(focusBorder, searchPath);
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var searchFont = UiChrome.ChromeFont(10f);
        string searchDisplay = _emojiSearch.Length > 0 ? _emojiSearch : "Search emoji...";
        using var searchBrush = new SolidBrush(_emojiSearch.Length > 0
            ? UiChrome.SurfaceTextPrimary
            : UiChrome.SurfaceTextMuted);
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 8, searchRect.Y + 5);
        // Text cursor (always visible when picker is open)
        {
            float cursorX = _emojiSearch.Length > 0
                ? searchRect.X + 8 + g.MeasureString(_emojiSearch, searchFont).Width - 2
                : searchRect.X + 8;
            using var cursorPen = new Pen(UiChrome.SurfaceTextPrimary, 1.5f);
            g.DrawLine(cursorPen, cursorX, searchRect.Y + 7, cursorX, searchRect.Bottom - 7);
        }

        var searchHintFont = UiChrome.ChromeFont(8f);
        using var searchHintBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
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
                using var hoverBg = new SolidBrush(UiChrome.SurfaceHover);
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
            using var trackBrush = new SolidBrush(UiChrome.SurfaceHover);
            g.FillRectangle(trackBrush, trackX, trackY, 3, trackH);
            int thumbH = Math.Max(10, trackH * visibleRows / totalRows);
            int thumbY = trackY + (int)((float)scrollRow / (totalRows - visibleRows) * (trackH - thumbH));
            using var thumbBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
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
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle);
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
                using var selPen = new Pen(UiChrome.SurfaceTextPrimary, 2f);
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

        // Pill background -- solid dark, subtle border and shadow
        PaintShadow(g, r, UiChrome.ToolbarHeight / 2f, 70, 1.2f);
        using (var p = RRect(r, UiChrome.ToolbarHeight / 2))
        {
            using var bg = new SolidBrush(UiChrome.SurfacePill);
            using var border = new Pen(UiChrome.SurfaceBorder, 1f);
            g.FillPath(bg, p);
            g.DrawPath(border, p);
        }

        // Separator lines at group boundaries
        int sepY1 = r.Y + 10;
        int sepY2 = r.Bottom - 10;
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            int sx = _toolbarButtons[idx].Right + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
            using var sepPen = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            g.DrawLine(sepPen, sx, sepY1, sx, sepY2);
        }

        // Check if active mode is a flyout tool (to highlight the "more" button)
        bool flyoutToolActive = _flyoutTools.Any(t => t.Mode == _mode);

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = _toolbarButtons[i];
            bool active = _toolbarModes[i] is { } m && _mode == m;
            if (i == _moreButtonIndex) active = flyoutToolActive; // only highlight if a flyout tool is active, not just open
            bool hover = _hoveredButton == i;

            // Color dot button
            if (_toolbarIcons[i] == "color")
            {
                if (hover)
                {
                    using var hoverBrush = new SolidBrush(UiChrome.SurfaceHover);
                    g.FillEllipse(hoverBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
                }
                int dotSize = 16;
                float dx = btn.X + (btn.Width - dotSize) / 2f;
                float dy = btn.Y + (btn.Height - dotSize) / 2f;
                using var cBrush = new SolidBrush(_toolColor);
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                continue;
            }

            // "More" button: draw three dots instead of icon glyph
            if (_toolbarIcons[i] == "more")
            {
                if (active || hover)
                {
                    var hlColor = active
                        ? Color.FromArgb(32, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B)
                        : UiChrome.SurfaceHover;
                    using var hlBrush = new SolidBrush(hlColor);
                    g.FillEllipse(hlBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
                }
                int dotAlpha = active ? 255 : hover ? 220 : 165;
                using var dotBrush = new SolidBrush(Color.FromArgb(dotAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                float cy = btn.Y + btn.Height / 2f - 1.5f;
                float cx = btn.X + btn.Width / 2f;
                g.FillEllipse(dotBrush, cx - 8f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx - 1.5f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx + 5f, cy, 3f, 3f);
                continue;
            }

            // Active/hover circle highlight
            if (active)
            {
                using var activeBrush = new SolidBrush(Color.FromArgb(32, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                g.FillEllipse(activeBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
            }
            else if (hover)
            {
                using var hoverBrush = new SolidBrush(UiChrome.SurfaceHover);
                g.FillEllipse(hoverBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
            }

            int ia = active ? 255 : hover ? 220 : i >= BtnCount - 1 ? 135 : 165;
            var iconColor = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _toolbarIcons[i], btn, Color.FromArgb(ia, iconColor.R, iconColor.G, iconColor.B));
        }

        // Flyout panel (above toolbar)
        if (_flyoutOpen && _flyoutTools.Length > 0)
        {
            var fr = _flyoutRect;
            PaintShadow(g, fr, UiChrome.ToolbarHeight / 2f, 70, 1.2f);
            using (var fp = RRect(fr, UiChrome.ToolbarHeight / 2))
            {
                using var flyBg = new SolidBrush(UiChrome.SurfacePill);
                using var flyBorder = new Pen(UiChrome.SurfaceBorder, 1f);
                g.FillPath(flyBg, fp);
                g.DrawPath(flyBorder, fp);
            }

            for (int i = 0; i < _flyoutTools.Length; i++)
            {
                var fb = _flyoutButtonRects[i];
                bool fActive = _flyoutTools[i].Mode is { } fm && _mode == fm;
                bool fHover = _hoveredFlyoutButton == i;

                if (fActive)
                {
                    using var ab = new SolidBrush(Color.FromArgb(32, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                    g.FillEllipse(ab, fb.X + 2.5f, fb.Y + 2.5f, fb.Width - 5f, fb.Height - 5f);
                }
                else if (fHover)
                {
                    using var hb = new SolidBrush(UiChrome.SurfaceHover);
                    g.FillEllipse(hb, fb.X + 2.5f, fb.Y + 2.5f, fb.Width - 5f, fb.Height - 5f);
                }

                int fia = fActive ? 255 : fHover ? 220 : 165;
                var fic = UiChrome.SurfaceTextPrimary;
                DrawIcon(g, _flyoutTools[i].Id, fb, Color.FromArgb(fia, fic.R, fic.G, fic.B));
            }
        }

        // Tooltip for main bar or flyout
        string? tipText = null;
        Rectangle tipAnchor = default;
        float tipBelow = r.Bottom + 6;

        if (_hoveredButton >= 0 && _hoveredButton < _toolbarLabels.Length)
        {
            tipText = _toolbarLabels[_hoveredButton];
            tipAnchor = _toolbarButtons[_hoveredButton];

            if (_hoveredButton < _mainBarTools.Length)
            {
                var tool = _mainBarTools[_hoveredButton];
                if (tool.Group == 1 || tool.Group == 0)
                {
                    var hk = Services.SettingsService.LoadStatic()?.GetToolHotkey(tool.Id) ?? (0u, 0u);
                    if (hk.key != 0)
                        tipText += $"  ({Helpers.HotkeyFormatter.Format(hk.mod, hk.key)})";
                }
            }
        }
        else if (_flyoutOpen && _hoveredFlyoutButton >= 0 && _hoveredFlyoutButton < _flyoutTools.Length)
        {
            tipText = _flyoutTools[_hoveredFlyoutButton].Label;
            tipAnchor = _flyoutButtonRects[_hoveredFlyoutButton];
            tipBelow = _flyoutRect.Bottom + 6; // tooltip below the flyout

            var hk = Services.SettingsService.LoadStatic()?.GetToolHotkey(_flyoutTools[_hoveredFlyoutButton].Id) ?? (0u, 0u);
            if (hk.key != 0)
                tipText += $"  ({Helpers.HotkeyFormatter.Format(hk.mod, hk.key)})";
        }

        if (tipText != null)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var tipFont = UiChrome.ChromeFont(8.25f, FontStyle.Regular);
            var sz = g.MeasureString(tipText, tipFont);
            float tx = tipAnchor.X + tipAnchor.Width / 2f - sz.Width / 2f;
            float ty = tipBelow;
            float tipW = sz.Width + 20;
            if (tx - 10 < 4) tx = 14;
            if (tx - 10 + tipW > Width - 4) tx = Width - 4 - tipW + 10;
            var tipRect = new RectangleF(tx - 10, ty - 4, tipW, sz.Height + 8);
            PaintShadow(g, tipRect, tipRect.Height / 2f, 52, 1f);
            using (var tipPath = RRect(tipRect, tipRect.Height / 2f))
            {
                using var tipBg = new SolidBrush(UiChrome.SurfaceTooltip);
                using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                g.FillPath(tipBg, tipPath);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipFg = new SolidBrush(UiChrome.SurfaceTextPrimary);
            g.DrawString(tipText, tipFont, tipFg, tx, ty);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Called by the separate ToolbarForm to paint toolbar, tooltips, and popups.
    /// Graphics is already translated so overlay coordinates map correctly.
    /// </summary>
    public void PaintToolbarTo(Graphics g, Rectangle clip, Point unused)
    {
        ApplyUiGraphics(g);
        var state = g.Save();
        PaintToolbar(g);
        if (_colorPickerOpen) PaintColorPicker(g);
        if (_emojiPickerOpen) PaintEmojiPicker(g);
        if (_fontPickerOpen) PaintFontPicker(g);
        g.Restore(state);
    }

    // Pre-cached annotation fonts (allocated once, reused every frame)
    private static readonly Dictionary<(string, float, FontStyle), Font> _annotationFontCache = new();
    private static Font GetAnnotationFont(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (_annotationFontCache.TryGetValue(key, out var cached))
            return cached;
        Font font;
        try { font = new Font(family, size, style); }
        catch { font = UiChrome.ChromeFont(size, style); }
        _annotationFontCache[key] = font;
        return font;
    }

    private static readonly SolidBrush TextShadowBrush1 = new(Color.FromArgb(50, 0, 0, 0));
    private static readonly SolidBrush TextShadowBrush2 = new(Color.FromArgb(25, 0, 0, 0));
    private static readonly SolidBrush TextStrokeBrush = new(Color.FromArgb(60, 0, 0, 0));

    // Pre-cached fade brushes (allocated once, reused every frame)
    private static SolidBrush?[]? _fadeBrushes;
    private static void EnsureFadeBrushes()
    {
        if (_fadeBrushes != null) return;
        const int bands = 30;
        _fadeBrushes = new SolidBrush?[bands];
        for (int i = 0; i < bands; i++)
        {
            float t = (float)i / bands;
            int alpha = Math.Min(140, (int)((1f - t * t) * 140f));
            _fadeBrushes[i] = alpha >= 1 ? new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)) : null;
        }
    }

    // Fixed button glyphs (not in ToolDef)
    private static readonly Dictionary<string, char> FixedGlyphs = new()
    {
        ["gear"]  = '\uE157', // lucide settings
        ["close"] = '\uE1B1', // lucide x
        ["more"]  = '\uE0D4', // lucide ellipsis (more-horizontal)
    };

    private static Font? _iconFontCached;
    private static Font GetIconFont() => _iconFontCached ??= IconFont.Create(UiChrome.IconGlyphSize);

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
        if (icon == "sticker")
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(b.X + 9.5f, b.Y + 8.5f, b.Width - 19f, b.Height - 19f);
            using var pen = new Pen(c, 1.8f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var path = new GraphicsPath();
            path.AddArc(body.X, body.Y, 6, 6, 180, 90);
            path.AddLine(body.X + 6, body.Y, body.Right - 7, body.Y);
            path.AddLine(body.Right - 7, body.Y, body.Right, body.Y + 7);
            path.AddLine(body.Right, body.Y + 7, body.Right, body.Bottom - 6);
            path.AddArc(body.Right - 6, body.Bottom - 6, 6, 6, 0, 90);
            path.AddArc(body.X, body.Bottom - 6, 6, 6, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);

            g.DrawLine(pen, body.Right - 7, body.Y, body.Right - 7, body.Y + 7);
            g.DrawLine(pen, body.Right - 7, body.Y + 7, body.Right, body.Y + 7);
            g.SmoothingMode = SmoothingMode.Default;
            return;
        }
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
        string text = GetSelectionLabelText(rect, isOcr, isScan);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var font = UiChrome.ChromeFont(10f);
        var lr = GetLabelBounds(rect, isOcr, isScan, text, font, out float lx, out float ly);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = RRect(lr, 8))
        {
            using var lblBg = new SolidBrush(UiChrome.SurfacePill);
            g.FillPath(lblBg, p);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            g.DrawPath(border, p);
        }
        g.SmoothingMode = SmoothingMode.Default;
        using var fg = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.DrawString(text, font, fg, lx, ly);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private static string GetSelectionLabelText(Rectangle rect, bool isOcr, bool isScan)
        => isOcr ? $"OCR  {rect.Width} x {rect.Height}"
        : isScan ? $"SCAN  {rect.Width} x {rect.Height}"
        : $"{rect.Width} x {rect.Height}";

    private RectangleF GetLabelBounds(Rectangle rect, bool isOcr, bool isScan)
    {
        string text = GetSelectionLabelText(rect, isOcr, isScan);
        var font = UiChrome.ChromeFont(10f);
        return GetLabelBounds(rect, isOcr, isScan, text, font, out _, out _);
    }

    private RectangleF GetLabelBounds(Rectangle rect, bool isOcr, bool isScan, string text, Font font, out float lx, out float ly)
    {
        var sz = TextRenderer.MeasureText(text, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        lx = rect.X;
        ly = rect.Bottom + 8;
        if (ly + sz.Height > ClientSize.Height) ly = rect.Y - sz.Height - 8;
        return new RectangleF(lx - 8, ly - 3, sz.Width + 16, sz.Height + 6);
    }

    private void PaintTextToolbar(Graphics g, RectangleF textRect)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float btnH = 26, btnPad = 3, pad = 5, sepW = 6;

        var uiFont = UiChrome.ChromeFont(9f);
        var uiFontBold = UiChrome.ChromeFont(9.5f, FontStyle.Bold);
        var uiFontItalic = UiChrome.ChromeFont(9.5f, FontStyle.Italic);
        var uiFontSmall = UiChrome.ChromeFont(7.5f);

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
            using var bg = new SolidBrush(UiChrome.SurfacePill);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
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
            var bgColor = UiChrome.SurfaceTextPrimary;
            using var btnBg = new SolidBrush(Color.FromArgb(bgAlpha, bgColor.R, bgColor.G, bgColor.B));
            g.FillPath(btnBg, btnPath);
            if (hovered)
            {
                using var hoverBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                g.DrawPath(hoverBorder, btnPath);
            }
            int textAlpha = active ? 255 : hovered ? 200 : 120;
            using var brush = new SolidBrush(Color.FromArgb(textAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
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
            var bgColor = UiChrome.SurfaceTextPrimary;
            using var btnBg = new SolidBrush(Color.FromArgb(bgAlpha, bgColor.R, bgColor.G, bgColor.B));
            g.FillPath(btnBg, btnPath);
            if (fontHovered)
            {
                using var hoverBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                g.DrawPath(hoverBorder, btnPath);
            }
        }
        int fontTextAlpha = _hoveredTextBtn == 4 ? 255 : 200;
        using var fontBrush = new SolidBrush(Color.FromArgb(fontTextAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
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
            var tipFont = UiChrome.ChromeFont(8f);
            var tipSize = g.MeasureString(_textBtnTooltip, tipFont);
            float tipX = hovRect.X + hovRect.Width / 2f - tipSize.Width / 2f - 6;
            float tipY = _textToolbarRect.Y - tipSize.Height - 10;
            var tipRect = new RectangleF(tipX, tipY, tipSize.Width + 12, tipSize.Height + 6);
            PaintShadow(g, tipRect, tipRect.Height / 2f, 40, 1f);
            using var tipPath = RRect(tipRect, tipRect.Height / 2f);
            using var tipBg = new SolidBrush(UiChrome.SurfaceTooltip);
            g.FillPath(tipBg, tipPath);
            using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            g.DrawPath(tipBorder, tipPath);
            using var tipBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
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

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Perpendicular direction for tick marks
        float nx = 0, ny = 0;
        if (dist > 1) { nx = -dy / dist; ny = dx / dist; }
        const float tickHalf = 6f;

        // Shadow line
        using var shadowPen = new Pen(Color.FromArgb(70, 0, 0, 0), 3f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(shadowPen, from.X + 1, from.Y + 1, to.X + 1, to.Y + 1);

        // Main line
        using var pen = new Pen(UiChrome.SurfaceTextPrimary, 1.8f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(pen, from, to);

        // Tick marks at each end (perpendicular to the line)
        using var tickPen = new Pen(UiChrome.SurfaceTextPrimary, 1.8f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(tickPen, from.X - nx * tickHalf, from.Y - ny * tickHalf,
                            from.X + nx * tickHalf, from.Y + ny * tickHalf);
        g.DrawLine(tickPen, to.X - nx * tickHalf, to.Y - ny * tickHalf,
                            to.X + nx * tickHalf, to.Y + ny * tickHalf);

        // Label
        string text = $"{(int)dist}px  ·  {Math.Abs(dx):0} × {Math.Abs(dy):0}  ·  {angle:0.0}°";
        var font = UiChrome.ChromeFont(9.5f);
        var sz = g.MeasureString(text, font);
        var mid = new PointF((from.X + to.X) / 2f, (from.Y + to.Y) / 2f);
        var label = new RectangleF(mid.X - sz.Width / 2f - 10, mid.Y - sz.Height - 14, sz.Width + 20, sz.Height + 10);
        PaintShadow(g, label, 8f, 48, 1f);
        using var path = RRect(label, 8f);
        using var bg = new SolidBrush(UiChrome.SurfacePill);
        using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        using var fg = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.DrawString(text, font, fg, label.X + 10, label.Y + 5);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
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
        var small = GetBlurPreviewGraphics(new Size(sw, sh));
        small.Clear(Color.Transparent);
        small.InterpolationMode = InterpolationMode.Bilinear;
        small.DrawImage(_screenshot, new Rectangle(0, 0, sw, sh), clamped, GraphicsUnit.Pixel);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_blurPreviewBitmap!, clamped);
        g.InterpolationMode = InterpolationMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }


}
