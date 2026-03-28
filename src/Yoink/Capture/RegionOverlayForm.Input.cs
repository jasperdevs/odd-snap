using System.Drawing;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private (string emoji, string name)[] GetFilteredEmojiPalette() =>
        string.IsNullOrEmpty(_emojiSearch)
            ? EmojiPalette
            : EmojiPalette.Where(em => em.name.Contains(_emojiSearch, StringComparison.OrdinalIgnoreCase)).ToArray();

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;

        int btn = GetToolbarButtonAt(e.Location);
        if (btn >= 0)
        {
            int toolCount = _visibleTools.Length;
            if (btn == BtnCount - 1) { Cancel(); return; }     // close
            if (btn == BtnCount - 2)
            {
                _emojiPickerOpen = false;
                _fontPickerOpen = false;
                _colorPickerOpen = false;
                _isPlacingEmoji = false;
                SettingsRequested?.Invoke();
                Cancel();
                return;
            } // gear
            if (btn == BtnCount - 3) { ToggleColorPicker(); return; } // color dot
            if (btn < toolCount && _visibleTools[btn].Mode.HasValue)
                SetMode(_visibleTools[btn].Mode!.Value);
            return;
        }

        // Color picker popup: check if clicked a swatch
        if (_colorPickerOpen)
        {
            if (HandleColorPickerClick(e.Location))
                return;
            _colorPickerOpen = false;
            Invalidate();
        }

        // Font picker popup
        if (_fontPickerOpen)
        {
            if (HandleFontPickerClick(e.Location))
                return;
            _fontPickerOpen = false;
            Invalidate();
        }

        // Emoji picker popup: check if clicked an emoji
        if (_emojiPickerOpen)
        {
            if (HandleEmojiPickerClick(e.Location))
                return;
            // Clicked outside picker
            _emojiPickerOpen = false;
            Invalidate();
        }

        // Emoji placing: click to stamp
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji && _selectedEmoji != null)
        {
            var pos = new Point(e.Location.X - (int)(_emojiPlaceSize / 2), e.Location.Y - (int)(_emojiPlaceSize / 2));
            _undoStack.Add(new EmojiAnnotation(pos, _selectedEmoji, _emojiPlaceSize));
            Invalidate();
            return;
        }

        // If typing text: check if clicking a resize handle first
        if (_isTyping)
        {
            int handle = GetTextHandle(e.Location);
            if (handle >= 0)
            {
                _textResizeHandle = handle;
                _textResizing = true;
                _textResizeStart = e.Location;
                return;
            }
            // Check if clicking inside the text box -- start dragging to move
            var textBox = GetActiveTextRect();
            if (textBox.Contains(e.Location))
            {
                _textDragging = true;
                _textDragOffset = new Point(e.Location.X - _textPos.X, e.Location.Y - _textPos.Y);
                return;
            }
            // Clicked outside -- commit
            CommitText();
            return;
        }

        // In Text mode, check if clicking on an existing committed text to re-edit
        if (_mode == CaptureMode.Text)
        {
            int hitIdx = HitTestText(e.Location);
            if (hitIdx >= 0)
            {
                var ta = GetTextAnnotations()[hitIdx];
                _undoStack.Remove(ta);
                _isTyping = true;
                _textPos = ta.Pos;
                _textBuffer = ta.Text;
                _textFontSize = ta.FontSize;
                _toolColor = ta.Color;
                _textBold = ta.Bold;
                _textFontFamily = ta.FontFamily;
                Invalidate();
                return;
            }
        }

        if (_mode == CaptureMode.ColorPicker)
        {
            ColorPicked?.Invoke(_hexStr);
            return;
        }

        if (_mode == CaptureMode.Eraser)
        {
            // Smart eraser: sample color at click point, start dragging a rect
            int cx = Math.Clamp(e.Location.X, 0, _bmpW - 1);
            int cy = Math.Clamp(e.Location.Y, 0, _bmpH - 1);
            _eraserColor = Color.FromArgb(_pixelData[cy * _bmpW + cx]);
            _eraserStart = e.Location;
            _isEraserDragging = true;
            return;
        }

        _hasDragged = false;
        switch (_mode)
        {
            case CaptureMode.Rectangle:
            case CaptureMode.Ocr:
            case CaptureMode.Scan:
                _isSelecting = true;
                _selectionStart = _selectionEnd = e.Location;
                _hasSelection = false;
                break;
            case CaptureMode.Freeform:
                _isSelecting = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(e.Location);
                break;
            case CaptureMode.Text:
                _isTyping = true;
                _textPos = e.Location;
                _textBuffer = "";
                Invalidate();
                break;
            case CaptureMode.Highlight:
                _isHighlighting = true;
                _highlightStart = e.Location;
                break;
            case CaptureMode.RectShape:
                _isRectShapeDragging = true;
                _shapeStart = e.Location;
                break;
            case CaptureMode.CircleShape:
                _isCircleShapeDragging = true;
                _shapeStart = e.Location;
                break;
            case CaptureMode.StepNumber:
                _undoStack.Add(new StepNumberAnnotation(e.Location, _nextStepNumber, _toolColor));
                _nextStepNumber++;
                Invalidate();
                break;
            case CaptureMode.Magnifier:
                // Place a persistent magnifier at click point
                int srcSz = 40;
                int sx2 = Math.Clamp(e.Location.X - srcSz / 2, 0, _bmpW - srcSz);
                int sy2 = Math.Clamp(e.Location.Y - srcSz / 2, 0, _bmpH - srcSz);
                _undoStack.Add(new MagnifierAnnotation(e.Location, new Rectangle(sx2, sy2, srcSz, srcSz)));
                Invalidate();
                break;
            case CaptureMode.Draw:
                _isSelecting = true;
                _currentStroke = new List<Point> { e.Location };
                break;
            case CaptureMode.Line:
                _isLineDragging = true;
                _lineStart = e.Location;
                break;
            case CaptureMode.Ruler:
                _isRulerDragging = true;
                _rulerStart = e.Location;
                break;
            case CaptureMode.Arrow:
                _isArrowDragging = true;
                _arrowStart = e.Location;
                break;
            case CaptureMode.CurvedArrow:
                _isCurvedArrowDragging = true;
                _currentCurvedArrow = new List<Point> { e.Location };
                break;
            case CaptureMode.Blur:
                _isBlurring = true;
                _blurStart = e.Location;
                break;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        // Double-click on any committed text to edit it (works in any mode)
        int hitIdx = HitTestText(e.Location);
        if (hitIdx >= 0)
        {
            var ta = GetTextAnnotations()[hitIdx];
            _undoStack.Remove(ta);
            _mode = CaptureMode.Text;
            _isTyping = true;
            _textPos = ta.Pos;
            _textBuffer = ta.Text;
            _textFontSize = ta.FontSize;
            _toolColor = ta.Color;
            _textBold = ta.Bold;
            _textFontFamily = ta.FontFamily;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool needsRepaint = false;
        bool toolbarDirty = false;

        // Text move drag
        if (_textDragging && _isTyping)
        {
            _textPos = new Point(e.Location.X - _textDragOffset.X, e.Location.Y - _textDragOffset.Y);
            Invalidate();
            return;
        }

        // Text resize drag
        if (_textResizing && _isTyping)
        {
            float dy = e.Location.Y - _textResizeStart.Y;
            _textFontSize = Math.Clamp(_textFontSize + dy * 0.3f, 10f, 120f);
            _textResizeStart = e.Location;
            Invalidate();
            return;
        }

        int btn = GetToolbarButtonAt(e.Location);
        if (btn != _hoveredButton)
        {
            _hoveredButton = btn;
            toolbarDirty = true;
        }

        // Cursor: show appropriate cursor for context
        if (_isTyping && GetTextHandle(e.Location) >= 0)
            { if (!Cursor.Equals(Cursors.SizeNWSE)) Cursor = Cursors.SizeNWSE; }
        else if (_isTyping && GetActiveTextRect().Contains(e.Location))
            { if (!Cursor.Equals(Cursors.SizeAll)) Cursor = Cursors.SizeAll; }
        else if (_mode == CaptureMode.Text && !_isTyping)
            { if (!Cursor.Equals(Cursors.IBeam)) Cursor = Cursors.IBeam; }
        else if (_emojiPickerOpen)
        {
            int searchBottom = _emojiPickerRect.Y + 6 + 28 + 6;
            if (_emojiPickerRect.Contains(e.Location) && e.Location.Y < searchBottom)
                { if (!Cursor.Equals(Cursors.IBeam)) Cursor = Cursors.IBeam; }
            else
                { if (!Cursor.Equals(Cursors.Default)) Cursor = Cursors.Default; }
        }
        else if (_fontPickerOpen || _colorPickerOpen)
            { if (!Cursor.Equals(Cursors.Default)) Cursor = Cursors.Default; }
        else if (btn >= 0)
            { if (!Cursor.Equals(Cursors.Hand)) Cursor = Cursors.Hand; }
        else if (_mode == CaptureMode.ColorPicker)
            { if (!Cursor.Equals(Cursors.Cross)) Cursor = Cursors.Cross; }
        else
            { if (!Cursor.Equals(Cursors.Cross)) Cursor = Cursors.Cross; }

        _prevCursorPos = _lastCursorPos;
        _lastCursorPos = e.Location;

        if (_mode == CaptureMode.ColorPicker)
        {
            UpdateColorPicker(e.Location);
            return;
        }

        switch (_mode)
        {
            case CaptureMode.Rectangle when !_isSelecting:
            case CaptureMode.Ocr when !_isSelecting:
            case CaptureMode.Scan when !_isSelecting:
                var detected = WindowDetector.GetWindowRectAtPoint(e.Location, _virtualBounds);
                if (detected != _autoDetectRect)
                {
                    _autoDetectRect = detected;
                    _autoDetectActive = detected.Width > 0;
                    needsRepaint = true;
                }
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
            case CaptureMode.Scan when _isSelecting:
                _autoDetectActive = false;
                _selectionEnd = e.Location;
                _selectionRect = NormRect(_selectionStart, _selectionEnd);
                if (_selectionRect.Width > 3 || _selectionRect.Height > 3) _hasDragged = true;
                _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
                needsRepaint = true;
                break;
            case CaptureMode.Freeform when _isSelecting:
                _freeformPoints.Add(e.Location);
                _hasDragged = true;
                needsRepaint = true;
                break;
            case CaptureMode.Highlight when _isHighlighting:
            case CaptureMode.RectShape when _isRectShapeDragging:
            case CaptureMode.CircleShape when _isCircleShapeDragging:
            case CaptureMode.Magnifier:
            case CaptureMode.Line when _isLineDragging:
            case CaptureMode.Ruler when _isRulerDragging:
            case CaptureMode.Arrow when _isArrowDragging:
            case CaptureMode.Blur when _isBlurring:
            case CaptureMode.Eraser when _isEraserDragging:
            case CaptureMode.Emoji when _isPlacingEmoji:
                needsRepaint = true;
                break;
            case CaptureMode.Draw when _isSelecting:
                _currentStroke?.Add(e.Location);
                needsRepaint = true;
                break;
            case CaptureMode.CurvedArrow when _isCurvedArrowDragging:
                _currentCurvedArrow?.Add(e.Location);
                needsRepaint = true;
                break;
        }

        // Font picker hover
        if (_fontPickerOpen)
        {
            int itemH = 28, pad = 6, searchBarH = 28;
            int listY = _fontPickerRect.Y + pad + searchBarH + pad;
            int relY = e.Location.Y - listY;
            int idx = _fontPickerScroll + relY / itemH;
            int newHover = (relY >= 0 && idx < GetFilteredFonts().Length) ? idx : -1;
            if (newHover != _fontPickerHovered) { _fontPickerHovered = newHover; toolbarDirty = true; }
        }

        // Emoji picker hover
        if (_emojiPickerOpen)
        {
            var filtered = GetFilteredEmojiPalette();
            int cols = 8, emojiSize = 32, pad = 6;
            int searchBarH = 28;
            int gridY = _emojiPickerRect.Y + pad + searchBarH + pad;
            int relX = e.Location.X - _emojiPickerRect.X - pad;
            int relY = e.Location.Y - gridY;
            int col = relX / (emojiSize + pad);
            int row = relY / (emojiSize + pad);
            int idx = (_emojiScrollOffset + row) * cols + col;
            int newHover = (col >= 0 && col < cols && relY >= 0 && idx < filtered.Length) ? idx : -1;
            if (newHover != _emojiHovered) { _emojiHovered = newHover; toolbarDirty = true; }
        }

        // Crosshair: partial invalidation (old + new strips)
        if (ShowCrosshairGuides)
        {
            if (_prevCursorPos != Point.Empty && _prevCursorPos != _lastCursorPos)
            {
                Invalidate(new Rectangle(_prevCursorPos.X - 2, 0, 5, ClientSize.Height));
                Invalidate(new Rectangle(0, _prevCursorPos.Y - 2, ClientSize.Width, 5));
            }
            Invalidate(new Rectangle(_lastCursorPos.X - 2, 0, 5, ClientSize.Height));
            Invalidate(new Rectangle(0, _lastCursorPos.Y - 2, ClientSize.Width, 5));
        }

        if (needsRepaint)
            Invalidate();

        if (toolbarDirty)
            RefreshToolbar();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // End text move/resize
        if (_textDragging) { _textDragging = false; return; }
        if (_textResizing) { _textResizing = false; _textResizeHandle = -1; return; }
        switch (_mode)
        {
            case CaptureMode.Highlight when _isHighlighting:
                _isHighlighting = false;
                var hlRect = NormRect(_highlightStart, e.Location);
                if (hlRect.Width > 2 && hlRect.Height > 2)
                    _undoStack.Add(new HighlightAnnotation(hlRect, DefaultHighlightColor));
                Invalidate();
                break;
            case CaptureMode.RectShape when _isRectShapeDragging:
                _isRectShapeDragging = false;
                var rectShape = GetShapeRect(e.Location);
                if (rectShape.Width > 2 && rectShape.Height > 2)
                    _undoStack.Add(new RectShapeAnnotation(rectShape, _toolColor));
                Invalidate();
                break;
            case CaptureMode.CircleShape when _isCircleShapeDragging:
                _isCircleShapeDragging = false;
                var circleShape = GetShapeRect(e.Location);
                if (circleShape.Width > 2 && circleShape.Height > 2)
                    _undoStack.Add(new CircleShapeAnnotation(circleShape, _toolColor));
                Invalidate();
                break;
            case CaptureMode.Magnifier:
                // Click already placed it in OnMouseDown, nothing to do on up
                break;
            case CaptureMode.Draw when _isSelecting:
                _isSelecting = false;
                if (_currentStroke is { Count: >= 2 })
                    _undoStack.Add(new DrawStroke(_currentStroke));
                _currentStroke = null;
                break;
            case CaptureMode.Line when _isLineDragging:
                _isLineDragging = false;
                var lineEnd = e.Location;
                float ldx = lineEnd.X - _lineStart.X;
                float ldy = lineEnd.Y - _lineStart.Y;
                if (MathF.Sqrt(ldx * ldx + ldy * ldy) > 5)
                    _undoStack.Add(new LineAnnotation(_lineStart, lineEnd));
                Invalidate();
                break;
            case CaptureMode.Ruler when _isRulerDragging:
                _isRulerDragging = false;
                var rulerEnd = GetRulerEnd(e.Location);
                float rdx = rulerEnd.X - _rulerStart.X;
                float rdy = rulerEnd.Y - _rulerStart.Y;
                if (MathF.Sqrt(rdx * rdx + rdy * rdy) > 3)
                    _undoStack.Add(new RulerAnnotation(_rulerStart, rulerEnd));
                Invalidate();
                break;
            case CaptureMode.Arrow when _isArrowDragging:
                _isArrowDragging = false;
                var end = e.Location;
                float dx = end.X - _arrowStart.X;
                float dy = end.Y - _arrowStart.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) > 5)
                    _undoStack.Add(new ArrowAnnotation(_arrowStart, end));
                Invalidate();
                break;
            case CaptureMode.CurvedArrow when _isCurvedArrowDragging:
                _isCurvedArrowDragging = false;
                if (_currentCurvedArrow is { Count: >= 2 })
                    _undoStack.Add(new CurvedArrowAnnotation(_currentCurvedArrow));
                _currentCurvedArrow = null;
                Invalidate();
                break;
            case CaptureMode.Blur when _isBlurring:
                _isBlurring = false;
                var blurRect = NormRect(_blurStart, e.Location);
                if (blurRect.Width > 3 && blurRect.Height > 3)
                    _undoStack.Add(new BlurRect(blurRect));
                Invalidate();
                break;
            case CaptureMode.Eraser when _isEraserDragging:
                _isEraserDragging = false;
                var eraserRect = NormRect(_eraserStart, e.Location);
                if (eraserRect.Width > 1 && eraserRect.Height > 1)
                    _undoStack.Add(new EraserFill(eraserRect, _eraserColor));
                Invalidate();
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
            case CaptureMode.Scan when _isSelecting:
                _isSelecting = false;
                bool isOcr = _mode == CaptureMode.Ocr;
                bool isScan = _mode == CaptureMode.Scan;
                if (!_hasDragged)
                {
                    // Use auto-detected window region if available, else fullscreen
                    var clickRect = (_autoDetectActive && _autoDetectRect.Width > 0)
                        ? _autoDetectRect
                        : new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                    if (isOcr) OcrRegionSelected?.Invoke(clickRect);
                    else if (isScan) ScanRegionSelected?.Invoke(clickRect);
                    else RegionSelected?.Invoke(clickRect);
                }
                else if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                {
                    if (isOcr) OcrRegionSelected?.Invoke(_selectionRect);
                    else if (isScan) ScanRegionSelected?.Invoke(_selectionRect);
                    else RegionSelected?.Invoke(_selectionRect);
                }
                else { _hasSelection = false; Invalidate(); }
                break;
            case CaptureMode.Freeform when _isSelecting:
                _isSelecting = false;
                if (!_hasDragged)
                    RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                else if (_freeformPoints.Count > 2) CompleteFreeform();
                break;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredButton = -1;
        _prevCursorPos = _lastCursorPos;
        _lastCursorPos = Point.Empty;
        _lastAutoDetectRect = Rectangle.Empty;
        _autoDetectRect = Rectangle.Empty;
        _autoDetectActive = false;
        Invalidate();
        RefreshToolbar();
    }

    // ProcessCmdKey always receives ESC (OnKeyDown sometimes doesn't)
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            // Close all popups and transient state in one pass
            bool anyClosed = false;
            if (_mode == CaptureMode.ColorPicker) { CloseMagWindow(); anyClosed = true; }
            if (_emojiPickerOpen) { _emojiPickerOpen = false; anyClosed = true; }
            if (_fontPickerOpen) { _fontPickerOpen = false; _fontSearch = ""; _filteredFonts = null; anyClosed = true; }
            if (_colorPickerOpen) { _colorPickerOpen = false; anyClosed = true; }
            if (_isPlacingEmoji) { _isPlacingEmoji = false; _selectedEmoji = null; anyClosed = true; }
            if (_isRulerDragging) { _isRulerDragging = false; anyClosed = true; }
            if (_isSelecting) { _isSelecting = false; _hasSelection = false; anyClosed = true; }
            if (_isArrowDragging) { _isArrowDragging = false; anyClosed = true; }
            if (_isLineDragging) { _isLineDragging = false; anyClosed = true; }
            if (_isCurvedArrowDragging) { _isCurvedArrowDragging = false; anyClosed = true; }
            if (_isBlurring) { _isBlurring = false; anyClosed = true; }
            if (_isEraserDragging) { _isEraserDragging = false; anyClosed = true; }
            if (_isHighlighting) { _isHighlighting = false; anyClosed = true; }
            if (_isRectShapeDragging) { _isRectShapeDragging = false; anyClosed = true; }
            if (_isCircleShapeDragging) { _isCircleShapeDragging = false; anyClosed = true; }
            if (_isTyping)
            {
                // ESC = discard (Enter = commit)
                _isTyping = false;
                _textBuffer = "";
                anyClosed = true;
            }
            if (anyClosed) { Invalidate(); return true; }
            Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Emoji picker search input - handle backspace only, let printable chars go to OnKeyPress
        if (_emojiPickerOpen)
        {
            if (e.KeyCode == Keys.Back && _emojiSearch.Length > 0)
            {
                _emojiSearch = _emojiSearch[..^1]; _emojiScrollOffset = 0; Invalidate();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else
            {
                e.Handled = true; // Mark as handled but don't suppress KeyPress for printable chars
            }
            return;
        }

        // Emoji placing: Tab re-opens picker
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            if (e.KeyCode == Keys.Tab) { _emojiPickerOpen = true; _isPlacingEmoji = false; Invalidate(); }
            return;
        }

        // Font picker search input
        if (_fontPickerOpen)
        {
            if (e.KeyCode == Keys.Back && _fontSearch.Length > 0)
            {
                _fontSearch = _fontSearch[..^1];
                _filteredFonts = null; _fontPickerScroll = 0;
                Invalidate();
            }
            return;
        }

        // Text input mode
        if (_isTyping)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return) { CommitText(); return; }
            if (e.KeyCode == Keys.Back && _textBuffer.Length > 0)
            {
                _textBuffer = _textBuffer[..^1]; Invalidate(); return;
            }
            if (e.KeyCode == Keys.B && e.Control)
            {
                _textBold = !_textBold; Invalidate(); return;
            }
            if (e.KeyCode == Keys.F && e.Control)
            {
                _fontPickerOpen = !_fontPickerOpen; _fontPickerScroll = 0; _fontSearch = ""; _filteredFonts = null; Invalidate(); return;
            }
            return;
        }
        if (TryHandleAnnotationToolNumber(e.KeyCode))
        {
            RefreshToolbar();
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.Z && e.Control && _undoStack.Count > 0)
        {
            var last = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            // Update step counter when undoing a step number
            if (last is StepNumberAnnotation)
            {
                var remaining = _undoStack.OfType<StepNumberAnnotation>().LastOrDefault();
                _nextStepNumber = remaining != null ? remaining.Number + 1 : 1;
            }
            Invalidate();
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_fontPickerOpen && !char.IsControl(e.KeyChar))
        {
            _fontSearch += e.KeyChar;
            _filteredFonts = null; _fontPickerScroll = 0;
            e.Handled = true;
            Invalidate();
            return;
        }
        if (_emojiPickerOpen && !char.IsControl(e.KeyChar))
        {
            _emojiSearch += e.KeyChar;
            _emojiScrollOffset = 0;
            e.Handled = true;
            Invalidate();
            return;
        }
        else if (_isTyping && !char.IsControl(e.KeyChar))
        {
            _textBuffer += e.KeyChar;
            e.Handled = true;
            Invalidate();
            return;
        }
    }

    private void CommitText()
    {
        if (_isTyping && _textBuffer.Length > 0)
            _undoStack.Add(new TextAnnotation(_textPos, _textBuffer, _textFontSize, _toolColor, _textBold, _textFontFamily));
        _isTyping = false;
        _textBuffer = "";
        _fontPickerOpen = false;
        Invalidate();
    }

    private RectangleF GetActiveTextRect()
    {
        if (!_isTyping) return RectangleF.Empty;
        using var font = new Font(_textFontFamily, _textFontSize, _textBold ? FontStyle.Bold : FontStyle.Regular);
        string display = _textBuffer.Length > 0 ? _textBuffer : "Type here...";
        SizeF sz;
        using (var g = CreateGraphics())
            sz = g.MeasureString(display, font);
        return new RectangleF(_textPos.X - 6, _textPos.Y - 4,
            Math.Max(sz.Width + 12, 100), sz.Height + 8);
    }

    private int GetTextHandle(Point p)
    {
        if (!_isTyping) return -1;
        var r = GetActiveTextRect();
        int hs = 10; // hit area size (larger than visual 6px for easier grabbing)
        var handles = new RectangleF[] {
            new(r.X - hs/2, r.Y - hs/2, hs, hs),
            new(r.Right - hs/2, r.Y - hs/2, hs, hs),
            new(r.X - hs/2, r.Bottom - hs/2, hs, hs),
            new(r.Right - hs/2, r.Bottom - hs/2, hs, hs),
        };
        for (int i = 0; i < handles.Length; i++)
            if (handles[i].Contains(p)) return i;
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
            var style = ta.Bold ? FontStyle.Bold : FontStyle.Regular;
            using var font = new Font(ta.FontFamily, ta.FontSize, style);
            SizeF sz;
            using (var g = CreateGraphics())
                sz = g.MeasureString(ta.Text, font);
            var rect = new RectangleF(ta.Pos.X - 6, ta.Pos.Y - 4, sz.Width + 12, sz.Height + 8);
            if (rect.Contains(p)) return i;
        }
        return -1;
    }

    private void ToggleColorPicker()
    {
        _emojiPickerOpen = false;
        _fontPickerOpen = false;
        _isPlacingEmoji = false;
        _colorPickerOpen = !_colorPickerOpen;
        Invalidate();
    }

    private bool HandleColorPickerClick(Point p)
    {
        if (!_colorPickerRect.Contains(p)) return false;

        int swatchSize = 28, pad = 4;
        int relX = p.X - _colorPickerRect.X - pad;
        int relY = p.Y - _colorPickerRect.Y - pad;
        int col = relX / (swatchSize + pad);
        if (col >= 0 && col < ToolColors.Length && relY >= 0 && relY < swatchSize + pad)
        {
            _toolColor = ToolColors[col];
            _toolColorIndex = col;
            _colorPickerOpen = false;
            Invalidate();
            return true;
        }
        return false;
    }

    private bool HandleFontPickerClick(Point p)
    {
        if (!_fontPickerRect.Contains(p)) return false;

        int itemH = 28, pad = 6, searchBarH = 28;
        int listY = _fontPickerRect.Y + pad + searchBarH + pad;
        int relY = p.Y - listY;
        int idx = _fontPickerScroll + relY / itemH;
        var fonts = GetFilteredFonts();

        if (relY >= 0 && idx >= 0 && idx < fonts.Length)
        {
            _textFontFamily = fonts[idx];
            _fontPickerOpen = false;
            _fontSearch = ""; _filteredFonts = null;
            Invalidate();
            return true;
        }
        return true; // absorb click inside picker
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_fontPickerOpen)
        {
            int visibleCount = 8;
            int maxScroll = Math.Max(0, GetFilteredFonts().Length - visibleCount);
            _fontPickerScroll = Math.Clamp(_fontPickerScroll + (e.Delta > 0 ? -1 : 1), 0, maxScroll);
            Invalidate();
        }
        else if (_emojiPickerOpen)
        {
            var filtered = string.IsNullOrEmpty(_emojiSearch)
                ? EmojiPalette
                : EmojiPalette.Where(em => em.name.Contains(_emojiSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
            int cols = 8, visibleRows = 4;
            int totalRows = (filtered.Length + cols - 1) / cols;
            int maxScroll = Math.Max(0, totalRows - visibleRows);
            _emojiScrollOffset = Math.Clamp(_emojiScrollOffset + (e.Delta > 0 ? -1 : 1), 0, maxScroll);
            Invalidate();
        }
        else if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            // Scroll wheel changes emoji size
            _emojiPlaceSize = Math.Clamp(_emojiPlaceSize + (e.Delta > 0 ? 4f : -4f), 16f, 128f);
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    private bool HandleEmojiPickerClick(Point p)
    {
        if (!_emojiPickerRect.Contains(p)) return false;

        var filtered = GetFilteredEmojiPalette();

        int cols = 8, emojiSize = 32, pad = 6;
        int searchBarH = 28;
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
            Invalidate();
            return true;
        }
        return true; // absorb click inside picker
    }

    private int GetToolbarButtonAt(Point p)
    {
        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
        return -1;
    }

    private void SetMode(CaptureMode m)
    {
        if (_isTyping) CommitText();
        _colorPickerOpen = false;
        _fontPickerOpen = false;
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
            int px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
            int py = _toolbarRect.Bottom + 8;
            _emojiPickerRect = new Rectangle(px, py, pw, ph);
        }
        else
        {
            _emojiPickerOpen = false;
            _isPlacingEmoji = false;
        }

        Invalidate();
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

    // Maps keyboard keys to badge labels for annotation tool shortcuts
    internal static readonly (Keys key, string label)[] AnnotationKeyMap = {
        (Keys.D1, "1"), (Keys.D2, "2"), (Keys.D3, "3"),
        (Keys.D4, "4"), (Keys.D5, "5"), (Keys.D6, "6"),
        (Keys.D7, "7"), (Keys.D8, "8"), (Keys.D9, "9"),
        (Keys.D0, "0"), (Keys.OemMinus, "-"), (Keys.Oemplus, "="),
        (Keys.OemOpenBrackets, "["), (Keys.OemCloseBrackets, "]"),
    };

    private bool TryHandleAnnotationToolNumber(Keys keyCode)
    {
        int index = -1;
        for (int i = 0; i < AnnotationKeyMap.Length; i++)
        {
            if (AnnotationKeyMap[i].key == keyCode) { index = i; break; }
        }
        if (index < 0) return false;

        var annotationModes = ToolDef.AllTools
            .Where(t => t.Mode is { } mode && IsAnnotationNumberBadgeMode(mode))
            .Select(t => t.Mode!.Value)
            .Distinct()
            .ToList();

        if (index >= annotationModes.Count) return false;
        SetMode(annotationModes[index]);
        return true;
    }

    private static bool IsAnnotationNumberBadgeMode(CaptureMode mode)
    {
        return mode == CaptureMode.Ruler
            || mode == CaptureMode.Highlight
            || mode == CaptureMode.RectShape
            || mode == CaptureMode.CircleShape
            || mode == CaptureMode.Draw
            || mode == CaptureMode.Line
            || mode == CaptureMode.Arrow
            || mode == CaptureMode.CurvedArrow
            || mode == CaptureMode.Text
            || mode == CaptureMode.StepNumber
            || mode == CaptureMode.Blur
            || mode == CaptureMode.Eraser
            || mode == CaptureMode.Magnifier
            || mode == CaptureMode.Emoji;
    }


}
