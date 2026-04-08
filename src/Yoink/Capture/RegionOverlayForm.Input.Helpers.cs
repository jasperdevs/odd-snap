using System.Drawing;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private (string emoji, string name)[]? _filteredEmojis;
    private string _lastEmojiSearch = "";

    private (string emoji, string name)[] GetFilteredEmojiPalette()
    {
        if (_filteredEmojis != null && _lastEmojiSearch == _emojiSearch)
            return _filteredEmojis;
        _lastEmojiSearch = _emojiSearch;
        _filteredEmojis = string.IsNullOrEmpty(_emojiSearch)
            ? EmojiPalette
            : EmojiPalette.Where(em => em.name.Contains(_emojiSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
        return _filteredEmojis;
    }

    private void InvalidateEmojiCache()
    {
        _filteredEmojis = null;
    }

    private static Rectangle InflateForRepaint(Rectangle rect, int pad = 8)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return Rectangle.Empty;
        rect.Inflate(pad, pad);
        return rect;
    }

    private int GetToolbarButtonAt(Point p)
    {
        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
        return -1;
    }

    private int GetFlyoutButtonAt(Point p)
    {
        if (!_flyoutOpen || _flyoutButtonRects == null) return -1;
        for (int i = 0; i < _flyoutButtonRects.Length; i++)
            if (_flyoutButtonRects[i].Contains(p)) return i;
        return -1;
    }

    private static Rectangle GetSquareSelectionRect(Point start, Point current)
    {
        int dx = current.X - start.X;
        int dy = current.Y - start.Y;
        int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int x2 = start.X + Math.Sign(dx == 0 ? 1 : dx) * size;
        int y2 = start.Y + Math.Sign(dy == 0 ? 1 : dy) * size;
        return NormRect(start, new Point(x2, y2));
    }

    private void SetMode(CaptureMode m)
    {
        if (ToolDef.IsCaptureTool(m))
            _lastCaptureMode = m;

        if (_isTyping) CommitText();
        _colorPickerOpen = false;
        _fontPickerOpen = false;
        HideFontSearchBox();
        _emojiHovered = -1;
        _mode = m;
        _hasSelection = false;
        _hasDragged = false;
        _freeformPoints.Clear();
        _isSelecting = false;
        _isBlurring = false;
        _isHighlighting = false;
        _isRectShapeDragging = false;
        _isCircleShapeDragging = false;
        _isArrowDragging = false;
        _isLineDragging = false;
        _isRulerDragging = false;
        _isCurvedArrowDragging = false;
        _isEraserDragging = false;
        _autoDetectActive = false;

        if (m == CaptureMode.ColorPicker)
        {
            _pickerTimer.Stop();
            _pickerReady = false;
        }
        else
        {
            _pickerTimer.Stop();
            CloseMagWindow();
        }

        // Emoji mode: always open picker
        if (m == CaptureMode.Emoji)
        {
            _emojiPickerOpen = true;
            _isPlacingEmoji = false;
            _selectedEmoji = null;
            _emojiSearch = "";
            _emojiScrollOffset = 0;
            int cols = 8, emojiSize = 32, pad = 6, visibleRows = 4;
            int searchBarH = 28;
            int pw = cols * (emojiSize + pad) + pad;
            int ph = searchBarH + pad + visibleRows * (emojiSize + pad) + pad;
            _emojiPickerRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
            ShowEmojiSearchBox();
            _emojiWarmupIndex = 0;
            _emojiWarmupPending = true;
            _pickerTimer.Start();
        }
        else
        {
            _emojiPickerOpen = false;
            _isPlacingEmoji = false;
            _emojiWarmupPending = false;
            _emojiWarmupIndex = 0;
            HideEmojiSearchBox();
        }

        Invalidate(Rectangle.Union(InflateForRepaint(GetEmojiPickerBounds(), 12), InflateForRepaint(GetColorPickerBounds(), 12)));
        RefreshToolbar();
    }

    private Point GetRulerEnd(Point current)
    {
        if ((ModifierKeys & Keys.Shift) == 0) return current;

        int dx = current.X - _rulerStart.X;
        int dy = current.Y - _rulerStart.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return new Point(current.X, _rulerStart.Y);
        return new Point(_rulerStart.X, current.Y);
    }

    private Point GetConstrainedDrawPoint(Point current)
    {
        if ((ModifierKeys & Keys.Shift) == 0 || _currentStroke is not { Count: > 0 })
            return current;

        var start = _currentStroke[0];
        int dx = current.X - start.X;
        int dy = current.Y - start.Y;
        return Math.Abs(dx) >= Math.Abs(dy)
            ? new Point(current.X, start.Y)
            : new Point(start.X, current.Y);
    }

    private Rectangle GetShapeRect(Point current)
    {
        if ((ModifierKeys & Keys.Shift) == 0)
            return NormRect(_shapeStart, current);

        int dx = current.X - _shapeStart.X;
        int dy = current.Y - _shapeStart.Y;
        int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int x2 = _shapeStart.X + Math.Sign(dx == 0 ? 1 : dx) * size;
        int y2 = _shapeStart.Y + Math.Sign(dy == 0 ? 1 : dy) * size;
        return NormRect(_shapeStart, new Point(x2, y2));
    }

    private Rectangle GetMagnifierPreviewRect(Point cursor)
    {
        if (cursor == Point.Empty)
            return Rectangle.Empty;
        return new Rectangle(cursor.X - 60, cursor.Y - 60, 160, 160);
    }

    private Rectangle GetEmojiPreviewRect(Point cursor)
    {
        if (cursor == Point.Empty)
            return Rectangle.Empty;
        int size = (int)Math.Ceiling(_emojiPlaceSize);
        int x = cursor.X - size / 2;
        int y = cursor.Y - size / 2;
        return new Rectangle(x - 8, y - 8, size + 16, size + 16);
    }

    private Rectangle GetColorPickerBounds()
    {
        int cols = 6, rows = 1, swatchSize = 28, pad = 4;
        int pw = cols * (swatchSize + pad) + pad;
        int ph = rows * (swatchSize + pad) + pad;
        int colorBtnIdx = BtnCount - 3;
        var colorBtn = _toolbarButtons.Length > colorBtnIdx ? _toolbarButtons[colorBtnIdx] : Rectangle.Empty;
        return PositionPopupFromAnchor(colorBtn, pw, ph);
    }

    private Rectangle GetEmojiPickerBounds()
    {
        int cols = 8, emojiSize = 32, pad = 6;
        int searchBarH = 28;
        int visibleRows = 4;
        int pw = cols * (emojiSize + pad) + pad;
        int ph = searchBarH + pad + visibleRows * (emojiSize + pad) + pad;
        return PositionPopupFromAnchor(_toolbarRect, pw, ph);
    }

    private Rectangle GetFontPickerBounds()
    {
        int pad = 6, visibleCount = 8, itemH = 28, searchBarH = 28;
        int pw = 240, ph = searchBarH + pad + visibleCount * itemH + pad * 2;
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
        return new Rectangle(px, py, pw, ph);
    }
}
