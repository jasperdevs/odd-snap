using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private void PaintEmojiPicker(Graphics g)
    {
        // Filter emojis by search
        var filtered = GetFilteredEmojiPalette();

        int cols = 8, emojiSize = 32, pad = 8;
        int visibleRows = 4;
        int totalRows = (filtered.Length + cols - 1) / cols;
        int gridH = visibleRows * (emojiSize + pad);
        int searchBarH = 32;
        int pw = cols * (emojiSize + pad) + pad;
        int ph = searchBarH + pad + gridH + pad;

        var emojiAnchor = _flyoutOpen ? Rectangle.Union(_toolbarRect, _flyoutRect) : _toolbarRect;
        _emojiPickerRect = PositionPopupFromAnchor(emojiAnchor, pw, ph);
        int px = _emojiPickerRect.X;
        int py = _emojiPickerRect.Y;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _emojiPickerRect, 10f, 55, 1.2f);
        using (var bgPath = RRect(_emojiPickerRect, UiChrome.PopupRadius))
        {
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);

            // Fluent gradient highlight (matches toolbar)
            var hlRect = new RectangleF(px + 1f, py + 0.5f, pw - 2f, ph - 1f);
            using var hlPath = RRect(hlRect, UiChrome.PopupRadius - 0.5f);
            using var gradBrush = new LinearGradientBrush(
                new PointF(px, py), new PointF(px, py + ph),
                Color.FromArgb(UiChrome.IsDark ? 40 : 50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var hlPen = new Pen(gradBrush, 1f);
            g.DrawPath(hlPen, hlPath);

            using var border = new Pen(UiChrome.SurfaceBorder, 1f);
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
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var searchFont = UiChrome.ChromeFont(10f);
        string searchDisplay = _emojiSearch.Length > 0 ? _emojiSearch : "Search emoji...";
        using var searchBrush = new SolidBrush(_emojiSearch.Length > 0
            ? UiChrome.SurfaceTextPrimary
            : UiChrome.SurfaceTextMuted);
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 10, searchRect.Y + 7);
        // Text cursor
        {
            float cursorX = _emojiSearch.Length > 0
                ? searchRect.X + 10 + g.MeasureString(_emojiSearch, searchFont).Width - 2
                : searchRect.X + 10;
            using var cursorPen = new Pen(UiChrome.SurfaceTextPrimary, 1.2f);
            g.DrawLine(cursorPen, cursorX, searchRect.Y + 8, cursorX, searchRect.Bottom - 8);
        }

        // Hint text (right aligned)
        var searchHintFont = UiChrome.ChromeFont(8f);
        using var searchHintBrush = new SolidBrush(UiChrome.SurfaceTextMuted);
        var hintSize = g.MeasureString("Type to search", searchHintFont);
        g.DrawString("Type to search", searchHintFont, searchHintBrush, searchRect.Right - hintSize.Width - 6, searchRect.Y + 9);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;

        // Emoji grid
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
                using var hoverPath = RRect(new RectangleF(ex - 3, ey - 3, emojiSize + 6, emojiSize + 6), 6);
                using var hoverBg = new SolidBrush(UiChrome.SurfaceHover);
                g.FillPath(hoverBg, hoverPath);
            }

            var emojiBmp = _emojiRenderer.GetEmoji(filtered[idx].emoji, 22f);
            g.DrawImage(emojiBmp, ex + 2, ey + 2);
        }

        // Scroll indicator (rounded track + thumb)
        if (totalRows > visibleRows)
        {
            int trackH = gridH - 8;
            int trackX = px + pw - pad - 4;
            int trackY = gridY + 4;
            using var trackPath = RRect(new RectangleF(trackX, trackY, 4, trackH), 2);
            using var trackBrush = new SolidBrush(Color.FromArgb(12, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.FillPath(trackBrush, trackPath);
            int thumbH = Math.Max(12, trackH * visibleRows / totalRows);
            int thumbY = trackY + (int)((float)scrollRow / (totalRows - visibleRows) * (trackH - thumbH));
            using var thumbPath = RRect(new RectangleF(trackX, thumbY, 4, thumbH), 2);
            using var thumbBrush = new SolidBrush(Color.FromArgb(80, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.FillPath(thumbBrush, thumbPath);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintFontPicker(Graphics g)
    {
        var fonts = GetFilteredFonts();
        int itemH = 30, pad = 8, visibleCount = 8;
        int searchBarH = 32;
        int bottomPad = pad + 4; // extra space so last item doesn't clip against rounded corners
        int pw = 240, ph = searchBarH + pad + visibleCount * itemH + pad + bottomPad;

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
            var fontAnchor = _flyoutOpen ? Rectangle.Union(_toolbarRect, _flyoutRect) : _toolbarRect;
            var popupRect = PositionPopupFromAnchor(fontAnchor, pw, ph);
            px = popupRect.X;
            py = popupRect.Y;
        }
        _fontPickerRect = new Rectangle(px, py, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _fontPickerRect, 10f, 55, 1.2f);
        using (var bgPath = RRect(_fontPickerRect, UiChrome.PopupRadius))
        {
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);

            // Fluent gradient highlight
            var hlRect = new RectangleF(px + 1f, py + 0.5f, pw - 2f, ph - 1f);
            using var hlPath = RRect(hlRect, UiChrome.PopupRadius - 0.5f);
            using var gradBrush = new LinearGradientBrush(
                new PointF(px, py), new PointF(px, py + ph),
                Color.FromArgb(UiChrome.IsDark ? 40 : 50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var hlPen = new Pen(gradBrush, 1f);
            g.DrawPath(hlPen, hlPath);

            using var border = new Pen(UiChrome.SurfaceBorder, 1f);
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
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        string searchDisplay = _fontSearch.Length > 0 ? _fontSearch : "Search fonts...";
        using var searchBrush = new SolidBrush(_fontSearch.Length > 0
            ? UiChrome.SurfaceTextPrimary : UiChrome.SurfaceTextMuted);
        var searchFont = UiChrome.ChromeFont(10f);
        g.DrawString(searchDisplay, searchFont, searchBrush, searchRect.X + 10, searchRect.Y + 7);
        if (_fontSearch.Length > 0)
        {
            float cursorX = searchRect.X + 10 + g.MeasureString(_fontSearch, searchFont).Width - 2;
            using var cursorPen = new Pen(UiChrome.SurfaceTextPrimary, 1.2f);
            g.DrawLine(cursorPen, cursorX, searchRect.Y + 8, cursorX, searchRect.Bottom - 8);
        }
        g.TextRenderingHint = TextRenderingHint.SystemDefault;

        // Font list — clip to popup bounds so items don't bleed outside rounded corners
        int listY = py + pad + searchBarH + pad;
        int maxScroll = Math.Max(0, fonts.Length - visibleCount);
        var listClipRect = new Rectangle(px, listY, pw, _fontPickerRect.Bottom - listY);
        var clipState = g.Save();
        using (var clipPath = RRect(_fontPickerRect, UiChrome.PopupRadius))
        {
            g.SetClip(clipPath);
        }
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
                if (active)
                {
                    using var activeBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                    g.DrawPath(activeBorder, itemPath);
                }
            }

            // Cache font objects for perf
            if (!_fontCache.TryGetValue(name, out var font))
            {
                try { font = new Font(name, 11f); }
                catch { font = UiChrome.ChromeFont(11f); }
                _fontCache[name] = font;
            }
            int textAlpha = active ? 255 : hovered ? 220 : 160;
            using var brush = new SolidBrush(Color.FromArgb(textAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(name, font, brush, px + pad + 8, iy + 6);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        // Scroll indicator (rounded)
        if (fonts.Length > visibleCount)
        {
            int trackH = visibleCount * itemH - 8;
            int trackX = px + pw - pad - 4;
            int trackY = listY + 4;
            using var trackPath = RRect(new RectangleF(trackX, trackY, 4, trackH), 2);
            using var trackBrush = new SolidBrush(Color.FromArgb(12, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.FillPath(trackBrush, trackPath);
            int thumbH = Math.Max(12, trackH * visibleCount / fonts.Length);
            int thumbY = maxScroll > 0 ? trackY + (int)((float)_fontPickerScroll / maxScroll * (trackH - thumbH)) : trackY;
            using var thumbPath = RRect(new RectangleF(trackX, thumbY, 4, thumbH), 2);
            using var thumbBrush = new SolidBrush(Color.FromArgb(80, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.FillPath(thumbBrush, thumbPath);
        }
        g.Restore(clipState);

        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintTextToolbar(Graphics g, RectangleF textRect)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float btnH = 28, btnPad = 3, pad = 6, sepW = 8;

        var uiFont = UiChrome.ChromeFont(9.5f);
        var uiFontBold = UiChrome.ChromeFont(10f, FontStyle.Bold);
        var uiFontItalic = UiChrome.ChromeFont(10f, FontStyle.Italic);
        var uiFontSmall = UiChrome.ChromeFont(8f);

        string fontLabel = _textFontFamily.Length > 14 ? _textFontFamily[..13] + ".." : _textFontFamily;
        var fontLabelSize = g.MeasureString(fontLabel, uiFont);

        float btnW = 28;
        float fontW = fontLabelSize.Width + 20;
        float totalW = btnW * 5 + btnPad * 4 + sepW + fontW + pad * 2;
        float totalH = btnH + pad * 2;
        _textToolbarRect = GetTextToolbarBounds(textRect, totalW, totalH);
        float tx = _textToolbarRect.X;
        float ty = _textToolbarRect.Y;

        PaintShadow(g, _textToolbarRect, 8f, 55, 1.2f);
        using (var bgPath = RRect(_textToolbarRect, UiChrome.ToolbarCornerRadius))
        {
            using var bg = new SolidBrush(UiChrome.SurfacePill);
            g.FillPath(bg, bgPath);

            // Fluent gradient highlight (matches toolbar)
            var hlRect = new RectangleF(tx + 1f, ty + 0.5f, totalW - 2f, totalH - 1f);
            using var hlPath = RRect(hlRect, UiChrome.ToolbarCornerRadius - 0.5f);
            using var gradBrush = new LinearGradientBrush(
                new PointF(tx, ty), new PointF(tx, ty + totalH),
                Color.FromArgb(UiChrome.IsDark ? 48 : 60, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var hlPen = new Pen(gradBrush, 1f);
            g.DrawPath(hlPen, hlPath);

            using var border = new Pen(UiChrome.SurfaceBorder, 1f);
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
            if (hovered || active)
            {
                using var btnBorder = new Pen(Color.FromArgb(active ? 30 : 18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), 1f);
                g.DrawPath(btnBorder, btnPath);
            }
            int textAlpha = active ? 255 : hovered ? 210 : 130;
            using var brush = new SolidBrush(Color.FromArgb(textAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
            g.DrawString(label, f, brush, rect, _iconFmt);
            btnIdx++;
        }

        // B, I, S(troke), Sh(adow), Bg
        DrawToggleBtn(ref _textBoldBtnRect, cx, "B", uiFontBold, _textBold);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textItalicBtnRect, cx, "I", uiFontItalic, _textItalic);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textStrokeBtnRect, cx, "S", uiFontSmall, _textStroke);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textShadowBtnRect, cx, "Sh", uiFontSmall, _textShadow);
        cx += btnW + btnPad;
        DrawToggleBtn(ref _textBackgroundBtnRect, cx, "Bg", uiFontSmall, _textBackground);
        cx += btnW;

        // Separator between toggle buttons and font selector
        using (var sepPen = new Pen(UiChrome.SurfaceBorderSubtle, 1f))
        {
            float sepX = cx + sepW / 2f;
            g.DrawLine(sepPen, sepX, cy + 5, sepX, cy + btnH - 5);
        }
        cx += sepW;

        // Font selector
        _textFontBtnRect = new RectangleF(cx, cy, fontW, btnH);
        {
            bool fontHovered = _hoveredTextBtn == 5;
            using var btnPath = RRect(_textFontBtnRect, 5);
            int bgAlpha = _fontPickerOpen ? 40 : fontHovered ? 30 : 12;
            var bgColor = UiChrome.SurfaceTextPrimary;
            using var btnBg = new SolidBrush(Color.FromArgb(bgAlpha, bgColor.R, bgColor.G, bgColor.B));
            g.FillPath(btnBg, btnPath);
            if (fontHovered || _fontPickerOpen)
            {
                using var btnBorder = new Pen(Color.FromArgb(_fontPickerOpen ? 30 : 18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B), 1f);
                g.DrawPath(btnBorder, btnPath);
            }
        }
        int fontTextAlpha = _hoveredTextBtn == 5 || _fontPickerOpen ? 255 : 190;
        using var fontBrush = new SolidBrush(Color.FromArgb(fontTextAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
        g.DrawString(fontLabel, uiFont, fontBrush, _textFontBtnRect, _iconFmt);

        // Tooltip for hovered text button
        if (_hoveredTextBtn >= 0 && _textBtnTooltip.Length > 0)
        {
            var hovRect = _hoveredTextBtn switch
            {
                0 => _textBoldBtnRect, 1 => _textItalicBtnRect,
                2 => _textStrokeBtnRect, 3 => _textShadowBtnRect, 4 => _textBackgroundBtnRect,
                _ => _textFontBtnRect
            };
            var tipFont = UiChrome.ChromeFont(8.5f);
            var tipSize = g.MeasureString(_textBtnTooltip, tipFont);
            float tipX = hovRect.X + hovRect.Width / 2f - tipSize.Width / 2f - 8;
            float tipY = _textToolbarRect.Y - tipSize.Height - 12;
            var tipRect = new RectangleF(tipX, tipY, tipSize.Width + 16, tipSize.Height + 8);
            PaintShadow(g, tipRect, 6f, 40, 1f);
            using var tipPath = RRect(tipRect, 6);
            using var tipBg = new SolidBrush(UiChrome.SurfaceTooltip);
            g.FillPath(tipBg, tipPath);
            using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            g.DrawPath(tipBorder, tipPath);
            using var tipBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            g.DrawString(_textBtnTooltip, tipFont, tipBrush, tipX + 8, tipY + 4);
        }

        g.TextRenderingHint = TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
    }

    private RectangleF GetTextToolbarBounds()
        => GetTextToolbarBounds(GetActiveTextRect());

    private RectangleF GetTextToolbarBounds(RectangleF textRect)
    {
        if (textRect.IsEmpty)
            return RectangleF.Empty;

        float btnH = 28, btnPad = 3, pad = 6, sepW = 8;
        var uiFont = UiChrome.ChromeFont(9.5f);
        string fontLabel = _textFontFamily.Length > 14 ? _textFontFamily[..13] + ".." : _textFontFamily;
        using var tmpBmp = new Bitmap(1, 1);
        using var tmpG = Graphics.FromImage(tmpBmp);
        var fontLabelSize = tmpG.MeasureString(fontLabel, uiFont);

        float btnW = 28;
        float fontW = fontLabelSize.Width + 20;
        float totalW = btnW * 5 + btnPad * 4 + sepW + fontW + pad * 2;
        float totalH = btnH + pad * 2;
        return GetTextToolbarBounds(textRect, totalW, totalH);
    }

    private RectangleF GetTextToolbarBounds(RectangleF textRect, float totalW, float totalH)
    {
        float tx = textRect.X;
        float ty = textRect.Y - totalH - 8;
        if (ty < 4) ty = textRect.Bottom + 8;
        tx = Math.Clamp(tx, 4f, Math.Max(4f, ClientSize.Width - totalW - 4f));
        ty = Math.Clamp(ty, 4f, Math.Max(4f, ClientSize.Height - totalH - 4f));
        return new RectangleF(tx, ty, totalW, totalH);
    }
}
