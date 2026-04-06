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

        int cols = 8, emojiSize = 32, pad = 6;
        int visibleRows = 4;
        int totalRows = (filtered.Length + cols - 1) / cols;
        int gridH = visibleRows * (emojiSize + pad);
        int searchBarH = 28;
        int pw = cols * (emojiSize + pad) + pad;
        int ph = searchBarH + pad + gridH + pad;

        _emojiPickerRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
        int px = _emojiPickerRect.X;
        int py = _emojiPickerRect.Y;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _emojiPickerRect, 12f, 58, 1f);
        using (var bgPath = RRect(_emojiPickerRect, 12))
        {
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
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
            var popupRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
            px = popupRect.X;
            py = popupRect.Y;
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
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
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
                using var hoverBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
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
                using var hoverBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
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
            using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
            g.DrawPath(tipBorder, tipPath);
            using var tipBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            g.DrawString(_textBtnTooltip, tipFont, tipBrush, tipX + 6, tipY + 3);
        }

        g.TextRenderingHint = TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
    }
}
