using System.Drawing;
using System.Windows.Forms;
using OddSnap.Models;

namespace OddSnap.Capture;

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
        if (!IsToolbarInteractive())
            return -1;

        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
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

    private Rectangle GetCenterSelectionRect(Point center, Point current)
    {
        int halfW = Math.Abs(current.X - center.X);
        int halfH = Math.Abs(current.Y - center.Y);

        var aspectRatio = (ModifierKeys & Keys.Shift) != 0
            ? CenterSelectionAspectRatio.Square
            : _centerSelectionAspectRatio;

        if (aspectRatio != CenterSelectionAspectRatio.Free)
        {
            if (halfW == 0 && halfH == 0)
                return Rectangle.Empty;

            ApplyCenterAspectRatio(aspectRatio, ref halfW, ref halfH);
        }

        int maxHalfW = Math.Max(0, Math.Min(center.X, _bmpW - center.X));
        int maxHalfH = Math.Max(0, Math.Min(center.Y, _bmpH - center.Y));
        double scale = Math.Min(1d, Math.Min(maxHalfW / Math.Max(1d, halfW), maxHalfH / Math.Max(1d, halfH)));
        halfW = Math.Max(0, (int)Math.Floor(halfW * scale));
        halfH = Math.Max(0, (int)Math.Floor(halfH * scale));

        return new Rectangle(center.X - halfW, center.Y - halfH, halfW * 2, halfH * 2);
    }

    private static void ApplyCenterAspectRatio(CenterSelectionAspectRatio aspectRatio, ref int halfW, ref int halfH)
    {
        var ratio = aspectRatio switch
        {
            CenterSelectionAspectRatio.Square => 1d,
            CenterSelectionAspectRatio.Widescreen16x9 => 16d / 9d,
            CenterSelectionAspectRatio.Classic4x3 => 4d / 3d,
            CenterSelectionAspectRatio.Photo3x2 => 3d / 2d,
            CenterSelectionAspectRatio.Portrait9x16 => 9d / 16d,
            _ => 0d
        };
        if (ratio <= 0)
            return;

        if (halfW / (double)Math.Max(1, halfH) >= ratio)
            halfH = Math.Max(1, (int)Math.Round(halfW / ratio));
        else
            halfW = Math.Max(1, (int)Math.Round(halfH * ratio));
    }

    private void SetTool(ToolDef tool)
    {
        if (tool.Mode is { } mode)
            SetMode(mode, tool.Id);
    }

    private void SetToolColor(Color color)
    {
        _toolColor = color;
        for (int i = 0; i < ToolColors.Length; i++)
        {
            if (ToolColors[i].ToArgb() == color.ToArgb())
            {
                _toolColorIndex = i;
                return;
            }
        }
    }

    private void SetMode(CaptureMode m, string? toolId = null)
    {
        if (_isTyping) CommitText();
        bool wasEmoji = _mode == CaptureMode.Emoji && _emojiPickerOpen;
        if (_flyoutOpen)
            CloseMoreToolsDropdown();
        _colorPickerOpen = false;
        _fontPickerOpen = false;
        HideFontSearchBox();
        _emojiHovered = -1;
        _mode = m;
        _activeToolId = toolId ?? _visibleTools.FirstOrDefault(t => t.Mode == m)?.Id;
        _hasSelection = false;
        _hasDragged = false;
        _selectionRect = Rectangle.Empty;
        _lastSelectionRect = Rectangle.Empty;
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
        _autoDetectRect = Rectangle.Empty;
        _lastAutoDetectRect = Rectangle.Empty;

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

        // Emoji mode: toggle picker if already in emoji mode
        if (m == CaptureMode.Emoji)
        {
            if (wasEmoji)
            {
                _emojiPickerOpen = false;
                _isPlacingEmoji = false;
                _emojiWarmupPending = false;
                _emojiWarmupIndex = 0;
                HideEmojiSearchBox();
                RefreshToolbar();
                return;
            }
            _emojiPickerOpen = true;
            _isPlacingEmoji = false;
            _selectedEmoji = null;
            _emojiSearch = "";
            _emojiScrollOffset = 0;
            int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding, visibleRows = EmojiPickerVisibleRows;
            int searchBarH = EmojiPickerSearchBarHeight;
            int pw = cols * (emojiSize + pad) + pad;
            int ph = searchBarH + pad + visibleRows * (emojiSize + pad) + pad;
            _emojiPickerRect = PositionPopupFromAnchor(_toolbarRect, pw, ph);
            ShowEmojiSearchBox();
            QueueEmojiWarmup();
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

    private void SwitchModeFromHotkey(CaptureMode mode)
    {
        SetMode(mode);
        Focus();
        Invalidate();
        UpdateToolbarSurfaceOnly();
    }

    private static bool IsOverlaySwitchableMode(CaptureMode mode) =>
        ToolDef.AllTools.Any(tool => tool.Mode == mode);

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
        const int srcSize = 40;
        return GetMagnifierPaintBounds(cursor, new Rectangle(cursor.X - srcSize / 2, cursor.Y - srcSize / 2, srcSize, srcSize), ClientSize);
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

    private Rectangle GetGlobalSnapGuideBounds()
    {
        if (!_snapGuideXVisible && !_snapGuideYVisible)
            return Rectangle.Empty;

        var bounds = Rectangle.Empty;
        int cx = ClientSize.Width / 2;
        int cy = ClientSize.Height / 2;

        if (_snapGuideXVisible)
            bounds = Rectangle.Union(bounds, new Rectangle(cx - 3, 0, 6, ClientSize.Height));
        if (_snapGuideYVisible)
            bounds = Rectangle.Union(bounds, new Rectangle(0, cy - 3, ClientSize.Width, 6));

        return InflateForRepaint(bounds, 6);
    }

    private void SetSnapGuides(bool showVertical, bool showHorizontal)
    {
        if (_snapGuideXVisible == showVertical && _snapGuideYVisible == showHorizontal)
            return;

        var oldBounds = GetGlobalSnapGuideBounds();
        _snapGuideXVisible = showVertical;
        _snapGuideYVisible = showHorizontal;
        var newBounds = GetGlobalSnapGuideBounds();

        if (!oldBounds.IsEmpty && !newBounds.IsEmpty)
            Invalidate(Rectangle.Union(oldBounds, newBounds));
        else if (!oldBounds.IsEmpty)
            Invalidate(oldBounds);
        else if (!newBounds.IsEmpty)
            Invalidate(newBounds);
    }

    private Point SnapPointToGlobalCenter(Rectangle boundsAtDesiredPosition, Point desiredPoint)
    {
        int centerX = ClientSize.Width / 2;
        int centerY = ClientSize.Height / 2;
        int boundsCenterX = boundsAtDesiredPosition.Left + boundsAtDesiredPosition.Width / 2;
        int boundsCenterY = boundsAtDesiredPosition.Top + boundsAtDesiredPosition.Height / 2;
        int snapX = centerX - boundsCenterX;
        int snapY = centerY - boundsCenterY;

        bool snappedX = Math.Abs(snapX) <= GlobalCenterSnapThreshold;
        bool snappedY = Math.Abs(snapY) <= GlobalCenterSnapThreshold;
        SetSnapGuides(snappedX, snappedY);

        return new Point(
            desiredPoint.X + (snappedX ? snapX : 0),
            desiredPoint.Y + (snappedY ? snapY : 0));
    }

    private Point SnapTextPositionToGlobalCenter(Point desiredTextPos)
    {
        var snappedBounds = Rectangle.Round(MeasureTextRect(desiredTextPos, _textBuffer, _textFontSize, _textFontFamily, _textBold, _textItalic, _textBackground));
        return SnapPointToGlobalCenter(snappedBounds, desiredTextPos);
    }

    private Point SnapAnnotationDeltaToGlobalCenter(Rectangle originalBounds, Point desiredDelta)
    {
        var movedBounds = OffsetRect(originalBounds, desiredDelta.X, desiredDelta.Y);
        return SnapPointToGlobalCenter(movedBounds, desiredDelta);
    }

    private Rectangle GetColorPickerBounds()
    {
        int pw = ColorPickerColumns * (ColorPickerSwatchSize + ColorPickerPadding) + ColorPickerPadding;
        int ph = ColorPickerRows * (ColorPickerSwatchSize + ColorPickerPadding) + ColorPickerPadding;
        var colorBtn = _toolbarButtons.Length > ColorButtonIndex ? _toolbarButtons[ColorButtonIndex] : Rectangle.Empty;
        return PositionPopupFromAnchor(colorBtn, pw, ph);
    }

    private Rectangle GetColorPickerSwatchRect(int index)
    {
        if (_colorPickerRect.IsEmpty || index < 0 || index >= ToolColors.Length)
            return Rectangle.Empty;

        int col = index % ColorPickerColumns;
        int row = index / ColorPickerColumns;
        int x = _colorPickerRect.X + ColorPickerPadding + col * (ColorPickerSwatchSize + ColorPickerPadding);
        int y = _colorPickerRect.Y + ColorPickerPadding + row * (ColorPickerSwatchSize + ColorPickerPadding);
        return new Rectangle(x, y, ColorPickerSwatchSize, ColorPickerSwatchSize);
    }

    private Rectangle GetEmojiPickerBounds()
    {
        int cols = EmojiPickerColumns, emojiSize = EmojiPickerIconSize, pad = EmojiPickerPadding;
        int searchBarH = EmojiPickerSearchBarHeight;
        int visibleRows = EmojiPickerVisibleRows;
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
