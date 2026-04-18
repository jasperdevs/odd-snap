using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

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

        _emojiPickerRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
        int px = _emojiPickerRect.X;
        int py = _emojiPickerRect.Y;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        WindowsDockRenderer.PaintSurface(g, _emojiPickerRect);

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
            var popupRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
            px = popupRect.X;
            py = popupRect.Y;
        }
        _fontPickerRect = new Rectangle(px, py, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        WindowsDockRenderer.PaintSurface(g, _fontPickerRect);

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

        WindowsDockRenderer.PaintSurface(g, _textToolbarRect);

        float cx = tx + pad;
        float cy = ty + pad;

        int btnIdx = 0;
        void DrawToggleBtn(ref RectangleF rect, float x, string label, Font f, bool active)
        {
            rect = new RectangleF(x, cy, btnW, btnH);
            bool hovered = _hoveredTextBtn == btnIdx;
            WindowsDockRenderer.PaintButton(g, rect, active, hovered);
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
            WindowsDockRenderer.PaintButton(g, _textFontBtnRect, _fontPickerOpen, fontHovered);
        }
        int fontTextAlpha = _hoveredTextBtn == 5 || _fontPickerOpen ? 255 : 190;
        using var fontBrush = new SolidBrush(Color.FromArgb(fontTextAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
        g.DrawString(fontLabel, uiFont, fontBrush, _textFontBtnRect, _iconFmt);

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
