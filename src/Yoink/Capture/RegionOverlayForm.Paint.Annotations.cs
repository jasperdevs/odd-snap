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

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
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
            int selectionStart = _textBox?.SelectionStart ?? 0;
            int selectionLength = _textBox?.SelectionLength ?? 0;

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

            if (_textBuffer.Length > 0 && selectionLength > 0)
            {
                float selX = _textPos.X + MeasureTextPrefixWidth(_textBuffer, selectionStart, font);
                float selW = Math.Max(2f, MeasureTextPrefixWidth(_textBuffer, selectionStart + selectionLength, font) - MeasureTextPrefixWidth(_textBuffer, selectionStart, font));
                var selRect = new RectangleF(selX - 1, textRect.Y + 3, selW + 2, Math.Max(16f, textRect.Height - 6));
                using var selBrush = new SolidBrush(Color.FromArgb(90, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                g.FillRectangle(selBrush, selRect);
            }

            // Render text with stroke/shadow
            if (_textBuffer.Length > 0)
            {
                PaintExcalidrawText(g, _textPos, _textBuffer, _textFontSize, _toolColor,
                    _textBold, _textItalic, _textStroke, _textShadow, _textBackground, _textFontFamily);
            }
            else
            {
                using var placeholderBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
                if (_textBackground)
                {
                    var bgRect = GetActiveTextRect();
                    using var bgPath = SketchRenderer.RoundedRect(bgRect, 8f);
                    using var bgBrush = new SolidBrush(_toolColor);
                    g.FillPath(bgBrush, bgPath);
                    using var bgStroke = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
                    g.DrawPath(bgStroke, bgPath);
                }
                g.DrawString(display, font, placeholderBrush, _textPos.X, _textPos.Y);
            }

            // Blinking caret: draw a standard I-beam inside the text frame, not inside glyph strokes.
            if (selectionLength == 0)
            {
                float cursorX;
                int caretIndex = _textBox?.SelectionStart ?? _textBuffer.Length;
                if (_textBuffer.Length > 0)
                {
                    cursorX = _textPos.X + MeasureTextPrefixWidth(_textBuffer, caretIndex, font) - 1;
                }
                else
                {
                    cursorX = _textPos.X;
                }

                float blinkAlpha = (float)(Math.Sin(Environment.TickCount64 / 400.0 * Math.PI) * 0.5 + 0.5);
                int alpha = (int)(blinkAlpha * 220);
                var caretColor = Color.FromArgb(alpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B);
                float caretTop = textRect.Y + 3;
                float caretBottom = textRect.Bottom - 3;
                using var cursorPen = new Pen(caretColor, 1.6f);
                g.DrawLine(cursorPen, cursorX, caretTop, cursorX, caretBottom);
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

        PaintGlobalSnapGuides(g);

        // Color/emoji/font picker popups are painted on the separate ToolbarForm
    }

    private void PaintGlobalSnapGuides(Graphics g)
    {
        if (!_snapGuideXVisible && !_snapGuideYVisible)
            return;

        g.SmoothingMode = SmoothingMode.None;
        int centerX = ClientSize.Width / 2;
        int centerY = ClientSize.Height / 2;

        if (_snapGuideXVisible)
        {
            using var shadowPen = new Pen(Color.FromArgb(28, 0, 0, 0), 3f);
            using var guidePen = new Pen(Color.FromArgb(150, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), 1f);
            guidePen.DashStyle = DashStyle.Dash;
            guidePen.DashPattern = new[] { 6f, 4f };
            g.DrawLine(shadowPen, centerX + 1, 0, centerX + 1, ClientSize.Height);
            g.DrawLine(guidePen, centerX, 0, centerX, ClientSize.Height);
        }

        if (_snapGuideYVisible)
        {
            using var shadowPen = new Pen(Color.FromArgb(28, 0, 0, 0), 3f);
            using var guidePen = new Pen(Color.FromArgb(150, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), 1f);
            guidePen.DashStyle = DashStyle.Dash;
            guidePen.DashPattern = new[] { 6f, 4f };
            g.DrawLine(shadowPen, 0, centerY + 1, ClientSize.Width, centerY + 1);
            g.DrawLine(guidePen, 0, centerY, ClientSize.Width, centerY);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
    }

    /// <summary>Text annotation: uses DrawString for correct kerning. Shadow and stroke via offset draws.</summary>
    private static void PaintExcalidrawText(Graphics g, Point pos, string text, float fontSize, Color color,
        bool bold = true, bool italic = false, bool stroke = true, bool shadow = true, bool background = false, string fontFamily = UiChrome.DefaultFontFamily)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(fontFamily, fontSize, style);
        {
            if (background)
            {
                var size = TextRenderer.MeasureText(text, font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                var bgRect = new RectangleF(pos.X - 8, pos.Y - 6, Math.Max(size.Width + 16, 110), size.Height + 12);
                using var bgPath = SketchRenderer.RoundedRect(bgRect, 8f);
                if (shadow)
                {
                    using var shadowBrush = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
                    using var shadowPath = SketchRenderer.RoundedRect(new RectangleF(bgRect.X + 2, bgRect.Y + 2, bgRect.Width, bgRect.Height), 8f);
                    g.FillPath(shadowBrush, shadowPath);
                }
                using var bgBrush = new SolidBrush(color);
                g.FillPath(bgBrush, bgPath);
                if (stroke)
                {
                    using var bgStroke = new Pen(Color.FromArgb(60, 0, 0, 0), 1.25f);
                    g.DrawPath(bgStroke, bgPath);
                }
                color = Color.White;
            }

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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var font = UiChrome.ChromeFont(11f, FontStyle.Bold);
        string text = num.ToString();
        var sz = g.MeasureString(text, font);

        // Size the badge to fit the number with padding
        float padX = 8f, padY = 4f;
        float w = Math.Max(sz.Width + padX * 2, sz.Height + padY * 2); // at least circular
        float h = sz.Height + padY * 2;
        float r = h / 2f; // fully rounded ends
        var rect = new RectangleF(pos.X - w / 2f, pos.Y - h / 2f, w, h);

        // Shadow
        using var shadowPath = SketchRenderer.RoundedRect(
            new RectangleF(rect.X + 1, rect.Y + 2, rect.Width, rect.Height), r);
        using var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));
        g.FillPath(shadowBrush, shadowPath);

        // Badge fill
        using var bgPath = SketchRenderer.RoundedRect(rect, r);
        using var bgBrush = new SolidBrush(color);
        g.FillPath(bgBrush, bgPath);

        // Subtle bright inner edge
        using var borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
        g.DrawPath(borderPen, bgPath);

        // Number text — white or black based on badge brightness
        int luma = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        var textColor = luma > 140 ? Color.FromArgb(20, 20, 20) : Color.FromArgb(255, 255, 255);
        using var textBrush = new SolidBrush(textColor);
        g.DrawString(text, font, textBrush, rect.X + (rect.Width - sz.Width) / 2f, rect.Y + (rect.Height - sz.Height) / 2f);

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
}
