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
    // Cached base layer: screenshot + annotations. Only rebuilt when annotations change.
    private Bitmap? _cachedBase;
    private int _cachedAnnotationCount = -1;

    private void EnsureCachedBase()
    {
        if (_cachedBase != null && _cachedAnnotationCount == _undoStack.Count) return;
        _cachedBase?.Dispose();
        _cachedBase = new Bitmap(_screenshot);
        using var g = Graphics.FromImage(_cachedBase);
        RenderAnnotationsTo(g);
        _cachedAnnotationCount = _undoStack.Count;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureCachedBase();
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        g.PixelOffsetMode = PixelOffsetMode.None;
        g.SmoothingMode = SmoothingMode.None;

        // Cached screenshot + annotations (fast blit)
        var clip = e.ClipRectangle;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_cachedBase!, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        bool isOcr = _mode == CaptureMode.Ocr;
        bool isSelectionMode = _mode == CaptureMode.Rectangle || _mode == CaptureMode.Ocr;

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
        {
            PaintToolbar(g);
            if (_pickerReady) PaintMagnifier(g);
            return;
        }

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
                DrawLabel(g, _selectionRect, isOcr);
                _lastSelectionRect = _selectionRect;
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

        if (!_hasSelection)
            _lastSelectionRect = Rectangle.Empty;

        // Crosshair guidelines
        if (ShowCrosshairGuides && _mode != CaptureMode.ColorPicker)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            // Soft shadow for visibility on light backgrounds
            using var chShadow = new Pen(Color.FromArgb(20, 0, 0, 0), 3f);
            g.DrawLine(chShadow, cur.X + 1, 0, cur.X + 1, ClientSize.Height);
            g.DrawLine(chShadow, 0, cur.Y + 1, ClientSize.Width, cur.Y + 1);
            using var chPen = DashedPen(80, 1f);
            g.DrawLine(chPen, cur.X, 0, cur.X, ClientSize.Height);
            g.DrawLine(chPen, 0, cur.Y, ClientSize.Width, cur.Y);
        }

        PaintToolbar(g);

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Static dashed pen for all selection borders.</summary>
    private static Pen DashedPen(int alpha, float width = 2f) => new Pen(Color.FromArgb(alpha, 255, 255, 255), width)
    {
        DashStyle = DashStyle.Dash,
        DashPattern = new[] { 6f, 4f }
    };

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
        if (_mode == CaptureMode.Line && _isLineDragging)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            SketchRenderer.DrawLine(g, _lineStart, cur, _toolColor, _lineStart.GetHashCode());
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

        // Active text input with selection box
        if (_isTyping)
        {
            var fontStyle = _textBold ? FontStyle.Bold : FontStyle.Regular;
            using var font = new Font(_textFontFamily, _textFontSize, fontStyle);
            string display = _textBuffer.Length > 0 ? _textBuffer : "Type here...";
            string boldIndicator = _textBold ? "B" : "b";
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

            // Font info + font picker button
            using var sizeFont = new Font("Segoe UI", 8f);
            using var sizeBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
            string fontInfo = $"{_textFontFamily}  {(int)_textFontSize}px {boldIndicator}  Ctrl+B  F=Font";
            g.DrawString(fontInfo, sizeFont, sizeBrush, textRect.Right + 4, textRect.Y);
        }

        // Emoji placing preview (follow cursor)
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji && _selectedEmoji != null)
        {
            var cur = PointToClient(System.Windows.Forms.Cursor.Position);
            PaintEmojiAnnotation(g, new Point(cur.X - (int)(_emojiPlaceSize / 2), cur.Y - (int)(_emojiPlaceSize / 2)),
                _selectedEmoji, _emojiPlaceSize, 0.6f);
        }

        // Color picker popup
        if (_colorPickerOpen)
            PaintColorPicker(g);

        // Emoji picker popup
        if (_emojiPickerOpen)
            PaintEmojiPicker(g);

        // Font picker popup
        if (_fontPickerOpen)
            PaintFontPicker(g);
    }

    /// <summary>Excalidraw-style text: clean font, soft shadow, subtle stroke outline.</summary>
    private static void PaintExcalidrawText(Graphics g, Point pos, string text, float fontSize, Color color, bool bold = true, string fontFamily = "Segoe UI")
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = new Font(fontFamily, fontSize, style);

        // Build text path for shadow + fill
        using var textPath = new GraphicsPath();
        textPath.AddString(text, font.FontFamily, (int)font.Style, g.DpiY * fontSize / 72f,
            new PointF(pos.X, pos.Y), StringFormat.GenericDefault);

        // Soft blurred shadow
        SketchRenderer.DrawSoftPathShadow(g, textPath, 1f);

        // Main text fill
        using var fillBrush = new SolidBrush(color);
        g.FillPath(fillBrush, textPath);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
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
        int itemH = 28, pad = 6, visibleCount = 8;
        int pw = 200, ph = visibleCount * itemH + pad * 2;

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
        using (var bgPath = RRect(_fontPickerRect, 10))
        {
            using var bg = new SolidBrush(Color.FromArgb(235, 18, 18, 18));
            g.FillPath(bg, bgPath);
            using var border = new Pen(Color.FromArgb(40, 255, 255, 255));
            g.DrawPath(border, bgPath);
        }

        int maxScroll = Math.Max(0, FontChoices.Length - visibleCount);
        for (int i = 0; i < visibleCount && (_fontPickerScroll + i) < FontChoices.Length; i++)
        {
            int idx = _fontPickerScroll + i;
            string name = FontChoices[idx];
            int iy = py + pad + i * itemH;
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

            using var font = new Font(name, 11f);
            using var brush = new SolidBrush(Color.FromArgb(active ? 255 : 180, 255, 255, 255));
            g.DrawString(name, font, brush, px + pad + 6, iy + 4);
        }

        // Scroll indicator
        if (FontChoices.Length > visibleCount)
        {
            int trackH = ph - pad * 2;
            int trackX = px + pw - pad - 3;
            int trackY = py + pad;
            using var trackBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            g.FillRectangle(trackBrush, trackX, trackY, 3, trackH);
            int thumbH = Math.Max(10, trackH * visibleCount / FontChoices.Length);
            int thumbY = trackY + (int)((float)_fontPickerScroll / maxScroll * (trackH - thumbH));
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

        using (var p = RRect(r, 22))
        {
            using var baseFill = new SolidBrush(Color.FromArgb((int)(t * 240), 28, 28, 28));
            g.FillPath(baseFill, p);
            using var bp = new Pen(Color.FromArgb((int)(t * 25), 255, 255, 255), 1f);
            g.DrawPath(bp, p);
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
            var btn = new Rectangle(_toolbarButtons[i].X, _toolbarButtons[i].Y + oy,
                ButtonSize, ButtonSize);
            bool active = modes[i] is { } m && _mode == m;
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

            if (active)
            {
                using var p = RRect(btn, 7);
                using var bfill = new SolidBrush(Color.FromArgb((int)(t * 50), 255, 255, 255));
                g.FillPath(bfill, p);
            }
            else if (hover)
            {
                using var p = RRect(btn, 7);
                using var bfill = new SolidBrush(Color.FromArgb((int)(t * 25), 255, 255, 255));
                g.FillPath(bfill, p);
            }
            int ia = (int)(t * (active ? 255 : hover ? 230 : i >= BtnCount - 2 ? 160 : 200));
            DrawIcon(g, icons[i], btn, Color.FromArgb(ia, 255, 255, 255));
        }

        if (_hoveredButton >= 0 && _hoveredButton < labels.Length && t > 0.5f)
        {
            string label = labels[_hoveredButton];
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var tipFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            var sz = g.MeasureString(label, tipFont);
            var btnRect = _toolbarButtons[_hoveredButton];
            float tx = btnRect.X + btnRect.Width / 2f - sz.Width / 2f;
            float ty = r.Bottom + 8 + oy;
            var tipRect = new RectangleF(tx - 8, ty - 3, sz.Width + 16, sz.Height + 6);
            using (var tipPath = RRect(tipRect, 8))
            {
                using var tipBg = new SolidBrush(Color.FromArgb((int)(t * 230), 32, 32, 32));
                g.FillPath(tipBg, tipPath);
                using var tipBorder = new Pen(Color.FromArgb((int)(t * 35), 255, 255, 255), 1f);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipBrush = new SolidBrush(Color.FromArgb((int)(t * 220), 255, 255, 255));
            g.DrawString(label, tipFont, tipBrush, tx, ty);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    // PaintTopFade removed - always-on dim provides sufficient contrast for toolbar

    // Fixed button glyphs (not in ToolDef)
    private static readonly Dictionary<string, char> FixedGlyphs = new()
    {
        ["gear"]  = '\uF42B',
        ["close"] = '\uF642',
    };

    private static System.Drawing.Text.PrivateFontCollection? _phosphorFonts;
    private static FontFamily? _phosphorFamily;

    private static FontFamily GetPhosphorFamily()
    {
        if (_phosphorFamily != null) return _phosphorFamily;
        _phosphorFonts = new System.Drawing.Text.PrivateFontCollection();
        string dir = AppContext.BaseDirectory;
        string path = System.IO.Path.Combine(dir, "Phosphor.ttf");
        if (System.IO.File.Exists(path))
            _phosphorFonts.AddFontFile(path);
        _phosphorFamily = _phosphorFonts.Families.Length > 0
            ? _phosphorFonts.Families[0]
            : new FontFamily("Segoe UI");
        return _phosphorFamily;
    }

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c)
    {
        if (icon == "color") return;

        // Look up glyph from ToolDef first, then fixed buttons
        char glyph;
        var toolDef = ToolDef.AllTools.FirstOrDefault(t => t.Id == icon);
        if (toolDef != null)
            glyph = toolDef.Icon;
        else if (!FixedGlyphs.TryGetValue(icon, out glyph))
            return;

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(GetPhosphorFamily(), 14f, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(c);

        string text = glyph.ToString();
        var sz = g.MeasureString(text, font);
        float x = b.X + (b.Width - sz.Width) / 2f;
        float y = b.Y + (b.Height - sz.Height) / 2f;
        g.DrawString(text, font, brush, x, y);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private void DrawLabel(Graphics g, Rectangle rect, bool isOcr)
    {
        string text = isOcr ? $"OCR  {rect.Width} x {rect.Height}" : $"{rect.Width} x {rect.Height}";
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
