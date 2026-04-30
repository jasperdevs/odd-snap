using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    // All text input is handled by off-screen TextBox controls

    private void CommitText()
    {
        // Sync from TextBox before committing
        if (_textBox != null && _textBox.Visible)
            _textBuffer = _textBox.Text;
        if (_isTyping && _textBuffer.Length > 0)
            AddAnnotation(new TextAnnotation(_textPos, _textBuffer, _textFontSize, _toolColor, _textBold, _textItalic, _textStroke, _textShadow, _textBackground, _textFontFamily));
        _isTyping = false;
        SetSnapGuides(false, false);
        _textBuffer = "";
        InvalidateActiveTextLayout();
        _fontPickerOpen = false;
        HideTextBox();
        RefreshOverlayUiChrome();
        Invalidate();
    }

    private static RectangleF MeasureTextRect(Point pos, string text, float fontSize, string fontFamily, bool bold, bool italic, bool background = false)
    {
        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(fontFamily, fontSize, style);
        string display = text.Length > 0 ? text : "Type here...";
        using var path = new GraphicsPath();
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces
        };
        path.AddString(
            display,
            font.FontFamily,
            (int)font.Style,
            font.SizeInPoints * 96f / 72f,
            new PointF(pos.X, pos.Y),
            format);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            var size = TextRenderer.MeasureText(display, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            bounds = new RectangleF(pos.X, pos.Y, Math.Max(1, size.Width), Math.Max(1, size.Height));
        }

        int padX = background ? 16 : 8;
        int padY = background ? 12 : 8;
        return new RectangleF(
            bounds.X - (padX / 2f),
            bounds.Y - (padY / 2f),
            bounds.Width + padX,
            bounds.Height + padY);
    }

    private RectangleF GetActiveTextRect()
    {
        if (!_isTyping) return RectangleF.Empty;
        if (_activeTextLayoutDirty)
        {
            var style = FontStyle.Regular;
            if (_textBold) style |= FontStyle.Bold;
            if (_textItalic) style |= FontStyle.Italic;
            var font = GetAnnotationFont(_textFontFamily, _textFontSize, style);
            string display = _textBuffer.Length > 0 ? _textBuffer : "Type here...";
            var measured = TextRenderer.MeasureText(display, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            _activeTextRectCache = MeasureTextRect(_textPos, _textBuffer, _textFontSize, _textFontFamily, _textBold, _textItalic, _textBackground);
            _activeTextMeasureWidth = measured.Width;
            _activeTextHandleCache[0] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.X, _activeTextRectCache.Y));
            _activeTextHandleCache[1] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.Right, _activeTextRectCache.Y));
            _activeTextHandleCache[2] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.X, _activeTextRectCache.Bottom));
            _activeTextHandleCache[3] = WindowsHandleRenderer.CenteredAt(new PointF(_activeTextRectCache.Right, _activeTextRectCache.Bottom));
            _activeTextLayoutDirty = false;
        }
        return _activeTextRectCache;
    }

    private int GetTextHandle(Point p)
    {
        if (!_isTyping) return -1;
        _ = GetActiveTextRect();
        for (int i = 0; i < _activeTextHandleCache.Length; i++)
        {
            var h = Rectangle.Round(_activeTextHandleCache[i]);
            h.Inflate((WindowsHandleRenderer.HitSize - h.Width) / 2, (WindowsHandleRenderer.HitSize - h.Height) / 2);
            if (h.Contains(p)) return i;
        }
        return -1;
    }

    private List<TextAnnotation> GetTextAnnotations() =>
        _undoStack.OfType<TextAnnotation>().ToList();

    private int HitTestText(Point p)
    {
        var texts = GetTextAnnotations();
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var ta = texts[i];
            var rect = MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic, ta.Background);
            if (rect.Contains(p)) return i;
        }
        return -1;
    }

    private float MeasureTextPrefixWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0f;

        length = Math.Min(length, text.Length);
        var size = TextRenderer.MeasureText(text[..length], font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return size.Width;
    }

    private int GetTextCharIndexAt(Point p)
    {
        if (!_isTyping)
            return 0;

        var style = FontStyle.Regular;
        if (_textBold) style |= FontStyle.Bold;
        if (_textItalic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(_textFontFamily, _textFontSize, style);
        string text = _textBuffer ?? string.Empty;
        float x = p.X - _textPos.X;
        if (x <= 0 || text.Length == 0)
            return 0;

        for (int i = 1; i <= text.Length; i++)
        {
            float width = MeasureTextPrefixWidth(text, i, font);
            float prevWidth = MeasureTextPrefixWidth(text, i - 1, font);
            if (x <= ((prevWidth + width) / 2f))
                return i - 1;
        }

        return text.Length;
    }

    private void ToggleColorPicker()
    {
        _emojiPickerOpen = false;
        _fontPickerOpen = false;
        HideEmojiSearchBox();
        HideFontSearchBox();
        _isPlacingEmoji = false;
        _colorPickerOpen = !_colorPickerOpen;
        Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
        RefreshToolbar();
    }

    private bool HandleColorPickerClick(Point p)
    {
        if (!_colorPickerRect.Contains(p)) return false;

        for (int i = 0; i < ToolColors.Length; i++)
        {
            if (!GetColorPickerSwatchRect(i).Contains(p))
                continue;

            SetToolColor(ToolColors[i]);
            _activeToolId = _visibleTools.FirstOrDefault(t => t.Mode == _mode)?.Id ?? _activeToolId;
            _colorPickerOpen = false;
            Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
            RefreshToolbar();
            return true;
        }

        return true; // absorb clicks inside the popup even between swatches
    }

    private bool HandleFontPickerClick(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;

        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int relY = p.Y - listY;
        var fonts = GetFilteredFonts();
        int visibleCount = 8;
        int maxScroll = Math.Max(0, fonts.Length - visibleCount);
        int trackH = visibleCount * itemH - 8;
        int trackX = _fontPickerRect.Right - pad - 4;
        int trackY = listY + 4;
        var trackRect = new Rectangle(trackX - 4, trackY, 12, trackH);
        if (trackRect.Contains(p) && fonts.Length > visibleCount)
        {
            int thumbH = Math.Max(12, trackH * visibleCount / fonts.Length);
            int thumbTravel = Math.Max(1, trackH - thumbH);
            int target = p.Y - trackY - (thumbH / 2);
            target = Math.Clamp(target, 0, thumbTravel);
            _fontPickerScroll = (int)Math.Round((double)target / thumbTravel * maxScroll);
            RefreshToolbar();
            return true;
        }

        int idx = _fontPickerScroll + relY / itemH;

        if (relY >= 0 && idx >= 0 && idx < fonts.Length)
        {
            var oldTextRect = Rectangle.Round(GetActiveTextRect());
            var oldToolbarRect = Rectangle.Round(GetTextToolbarBounds());
            var oldPickerRect = InflateForRepaint(GetFontPickerBounds(), 12);
            _textFontFamily = fonts[idx];
            _fontPickerOpen = false;
            _fontSearch = ""; _filteredFonts = null;
            InvalidateActiveTextLayout();
            UpdateTextBoxStyle(); SyncTextBoxSize();
            var newTextRect = Rectangle.Round(GetActiveTextRect());
            var newToolbarRect = Rectangle.Round(GetTextToolbarBounds());
            RefreshOverlayUiChrome();
            Invalidate(Rectangle.Union(
                Rectangle.Union(InflateForRepaint(oldTextRect, 16), InflateForRepaint(newTextRect, 16)),
                Rectangle.Union(Rectangle.Union(InflateForRepaint(oldToolbarRect, 16), InflateForRepaint(newToolbarRect, 16)), oldPickerRect)));
            RefreshToolbar();
            return true;
        }
        return true; // absorb click inside picker
    }

    private bool IsPointInFontPickerSearch(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        int searchBarH = 32, pad = 8;
        int searchBottom = _fontPickerRect.Y + pad + searchBarH;
        return p.Y < searchBottom;
    }

    private bool IsPointInFontPickerScrollbar(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        var fonts = GetFilteredFonts();
        int visibleCount = 8;
        if (fonts.Length <= visibleCount) return false;

        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int trackH = visibleCount * itemH - 8;
        int trackX = _fontPickerRect.Right - pad - 4;
        int trackY = listY + 4;
        var trackRect = new Rectangle(trackX - 4, trackY, 12, trackH);
        return trackRect.Contains(p);
    }

    private bool IsPointInFontPickerList(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;
        int itemH = 30, pad = 8, searchBarH = 32;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int relY = p.Y - listY;
        int idx = _fontPickerScroll + relY / itemH;
        return relY >= 0 && idx >= 0 && idx < GetFilteredFonts().Length;
    }

    private bool IsPointInEmojiPickerSearch(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;
        int pad = EmojiPickerPadding, searchBarH = EmojiPickerSearchBarHeight;
        int searchBottom = _emojiPickerRect.Y + pad + searchBarH + pad;
        return p.Y < searchBottom;
    }

    private bool IsPointInEmojiPickerItem(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;

        var filtered = GetFilteredEmojiPalette();
        int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding;
        int searchBarH = EmojiPickerSearchBarHeight;
        int gridY = _emojiPickerRect.Y + pad + searchBarH + pad;
        int relX = p.X - _emojiPickerRect.X - pad;
        int relY = p.Y - gridY;
        int col = relX / (emojiSize + pad);
        int row = relY / (emojiSize + pad);
        int idx = (_emojiScrollOffset + row) * cols + col;
        return col >= 0 && col < cols && row >= 0 && idx >= 0 && idx < filtered.Length;
    }

    private bool IsPointInColorPickerSwatch(Point p)
    {
        if (!_colorPickerRect.Contains(p)) return false;
        for (int i = 0; i < ToolColors.Length; i++)
            if (GetColorPickerSwatchRect(i).Contains(p))
                return true;
        return false;
    }

    private bool HandleEmojiPickerClick(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;

        var filtered = GetFilteredEmojiPalette();

        int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding;
        int searchBarH = EmojiPickerSearchBarHeight;
        int gridY = _emojiPickerRect.Y + pad + searchBarH + pad;

        // Check if clicking in search bar area (just keep focus, absorb click)
        if (p.Y < gridY) return true;

        int relX = p.X - _emojiPickerRect.X - pad;
        int relY = p.Y - gridY;
        int col = relX / (emojiSize + pad);
        int row = relY / (emojiSize + pad);
        int idx = (_emojiScrollOffset + row) * cols + col;

        if (col >= 0 && col < cols && row >= 0 && idx < filtered.Length)
        {
            _selectedEmoji = filtered[idx].emoji;
            _isPlacingEmoji = true;
            _emojiPickerOpen = false;
            _fontPickerOpen = false;
            HideEmojiSearchBox();
            Invalidate(InflateForRepaint(GetEmojiPickerBounds(), 12));
            RefreshToolbar();
            return true;
        }
        return true; // absorb click inside picker
    }
}
