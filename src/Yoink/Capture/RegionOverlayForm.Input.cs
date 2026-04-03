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
            Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
        }

        // Font picker popup
        if (_fontPickerOpen)
        {
            if (HandleFontPickerClick(e.Location))
                return;
            _fontPickerOpen = false;
            HideFontSearchBox();
            Invalidate(InflateForRepaint(GetFontPickerBounds(), 12));
        }

        // Emoji picker popup: check if clicked an emoji
        if (_emojiPickerOpen)
        {
            if (HandleEmojiPickerClick(e.Location))
                return;
            // Clicked outside picker
            _emojiPickerOpen = false;
            HideEmojiSearchBox();
            Invalidate(InflateForRepaint(GetEmojiPickerBounds(), 12));
        }

        // Emoji placing: click to stamp
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji && _selectedEmoji != null)
        {
            var pos = new Point(e.Location.X - (int)(_emojiPlaceSize / 2), e.Location.Y - (int)(_emojiPlaceSize / 2));
            AddAnnotation(new EmojiAnnotation(pos, _selectedEmoji, _emojiPlaceSize));
            Invalidate(InflateForRepaint(GetEmojiPreviewRect(e.Location)));
            return;
        }

        // If typing text: check toolbar buttons, resize handles, drag, or commit
        if (_isTyping)
        {
            // Text formatting toolbar buttons
            if (_textBoldBtnRect.Contains(e.Location))
            {
                _textBold = !_textBold;
                InvalidateActiveTextLayout();
                UpdateTextBoxStyle(); SyncTextBoxSize();
                Invalidate();
                return;
            }
            if (_textItalicBtnRect.Contains(e.Location))
            {
                _textItalic = !_textItalic;
                InvalidateActiveTextLayout();
                UpdateTextBoxStyle(); SyncTextBoxSize();
                Invalidate();
                return;
            }
            if (_textStrokeBtnRect.Contains(e.Location))
            {
                _textStroke = !_textStroke;
                Invalidate();
                return;
            }
            if (_textShadowBtnRect.Contains(e.Location))
            {
                _textShadow = !_textShadow;
                Invalidate();
                return;
            }
            if (_textFontBtnRect.Contains(e.Location))
            {
                _fontPickerOpen = !_fontPickerOpen;
                _fontPickerScroll = 0; _fontSearch = ""; _filteredFonts = null;
                if (_fontPickerOpen) ShowFontSearchBox(); else HideFontSearchBox();
                Invalidate();
                RefreshToolbar();
                return;
            }
            // Absorb click on the toolbar background
            if (_textToolbarRect.Contains(e.Location))
                return;

            int handle = GetTextHandle(e.Location);
            if (handle >= 0)
            {
                _textResizeHandle = handle;
                _textResizing = true;
                _textResizeStart = e.Location;
                Invalidate();
                return;
            }
            // Check if clicking inside the text box -- start dragging to move
            var textBox = GetActiveTextRect();
            if (textBox.Contains(e.Location))
            {
                _textDragging = true;
                _textDragOffset = new Point(e.Location.X - _textPos.X, e.Location.Y - _textPos.Y);
                Invalidate();
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
                var oldTextRect = InflateForRepaint(Rectangle.Round(MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic)));
                RemoveAnnotation(ta);
                _isTyping = true;
                _textPos = ta.Pos;
                _textBuffer = ta.Text;
                _textFontSize = ta.FontSize;
                _toolColor = ta.Color;
                _textBold = ta.Bold;
                _textItalic = ta.Italic;
                _textStroke = ta.Stroke;
                _textShadow = ta.Shadow;
                _textFontFamily = ta.FontFamily;
                InvalidateActiveTextLayout();
                ShowTextBox();
                Invalidate();
                return;
            }
        }

        // Select tool: check resize handles first, then hit-test annotations
        if (_mode == CaptureMode.Select)
        {
            // Check resize handles on already-selected annotation
            int handle = GetSelectHandle(e.Location);
            if (handle >= 0 && _selectedAnnotationIndex >= 0)
            {
                _isSelectResizing = true;
                _selectResizeHandle = handle;
                _selectDragStart = e.Location;
                _selectHandleBounds = GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]);
                Invalidate();
                return;
            }

            int hit = HitTestAnnotation(e.Location);
            if (hit >= 0)
            {
                _selectedAnnotationIndex = hit;
                _isSelectDragging = true;
                _selectDragStart = e.Location;
                Invalidate();
            }
            else
            {
                _selectedAnnotationIndex = -1;
                Invalidate();
            }
            return;
        }

        if (_mode == CaptureMode.ColorPicker)
        {
            ColorPicked?.Invoke(_hexStr);
            return;
        }

        _hasDragged = false;
        switch (_mode)
        {
            case CaptureMode.Rectangle:
            case CaptureMode.Ocr:
            case CaptureMode.Scan:
            case CaptureMode.Sticker:
                HideToolbarForCaptureTool();
                _autoDetectRect = WindowDetector.GetDetectionRectAtPoint(
                    e.Location, _virtualBounds, _windowDetectionMode);
                _autoDetectActive = _autoDetectRect.Width > 0 && _autoDetectRect.Height > 0;
                _isSelecting = true;
                _selectionStart = _selectionEnd = e.Location;
                _hasSelection = false;
                break;
            case CaptureMode.Freeform:
                HideToolbarForCaptureTool();
                _isSelecting = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(e.Location);
                break;
            case CaptureMode.Text:
                HideToolbarForCaptureTool();
                _isTyping = true;
                _textPos = e.Location;
                _textBuffer = "";
                InvalidateActiveTextLayout();
                ShowTextBox();
                Invalidate(InflateForRepaint(Rectangle.Round(MeasureTextRect(_textPos, "", _textFontSize, _textFontFamily, _textBold, _textItalic))));
                break;
            case CaptureMode.Highlight:
                HideToolbarForCaptureTool();
                _isHighlighting = true;
                _highlightStart = e.Location;
                break;
            case CaptureMode.RectShape:
                HideToolbarForCaptureTool();
                _isRectShapeDragging = true;
                _shapeStart = e.Location;
                break;
            case CaptureMode.CircleShape:
                HideToolbarForCaptureTool();
                _isCircleShapeDragging = true;
                _shapeStart = e.Location;
                break;
            case CaptureMode.StepNumber:
                HideToolbarForCaptureTool();
                AddAnnotation(new StepNumberAnnotation(e.Location, _nextStepNumber, _toolColor));
                _nextStepNumber++;
                Invalidate(InflateForRepaint(new Rectangle(e.Location.X - 16, e.Location.Y - 16, 32, 32)));
                break;
            case CaptureMode.Magnifier:
                HideToolbarForCaptureTool();
                // Place a persistent magnifier at click point
                int srcSz = 40;
                int sx2 = Math.Clamp(e.Location.X - srcSz / 2, 0, _bmpW - srcSz);
                int sy2 = Math.Clamp(e.Location.Y - srcSz / 2, 0, _bmpH - srcSz);
                AddAnnotation(new MagnifierAnnotation(e.Location, new Rectangle(sx2, sy2, srcSz, srcSz)));
                Invalidate(InflateForRepaint(GetMagnifierPreviewRect(e.Location)));
                break;
            case CaptureMode.Draw:
                HideToolbarForCaptureTool();
                _isSelecting = true;
                _currentStroke = new List<Point> { e.Location };
                break;
            case CaptureMode.Line:
                HideToolbarForCaptureTool();
                _isLineDragging = true;
                _lineStart = e.Location;
                break;
            case CaptureMode.Ruler:
                HideToolbarForCaptureTool();
                _isRulerDragging = true;
                _rulerStart = e.Location;
                break;
            case CaptureMode.Arrow:
                HideToolbarForCaptureTool();
                _isArrowDragging = true;
                _arrowStart = e.Location;
                break;
            case CaptureMode.CurvedArrow:
                HideToolbarForCaptureTool();
                _isCurvedArrowDragging = true;
                _currentCurvedArrow = new List<Point> { e.Location };
                break;
            case CaptureMode.Blur:
                HideToolbarForCaptureTool();
                _isBlurring = true;
                _blurStart = e.Location;
                break;
            case CaptureMode.Eraser:
                HideToolbarForCaptureTool();
                int cx = Math.Clamp(e.Location.X, 0, _bmpW - 1);
                int cy = Math.Clamp(e.Location.Y, 0, _bmpH - 1);
                _eraserColor = Color.FromArgb(_pixelData[cy * _bmpW + cx]);
                _eraserStart = e.Location;
                _isEraserDragging = true;
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
            var oldTextRect = InflateForRepaint(Rectangle.Round(MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic)));
            RemoveAnnotation(ta);
            _mode = CaptureMode.Text;
            _isTyping = true;
            _textPos = ta.Pos;
            _textBuffer = ta.Text;
            _textFontSize = ta.FontSize;
            _toolColor = ta.Color;
            _textBold = ta.Bold;
            _textItalic = ta.Italic;
            _textStroke = ta.Stroke;
            _textShadow = ta.Shadow;
            _textFontFamily = ta.FontFamily;
            InvalidateActiveTextLayout();
            ShowTextBox();
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
            InvalidateActiveTextLayout();
            Invalidate();
            return;
        }

        // Text resize drag - each handle pulls in its own direction
        if (_textResizing && _isTyping)
        {
            float dx = e.Location.X - _textResizeStart.X;
            float dy = e.Location.Y - _textResizeStart.Y;
            // Scale factor depends on which corner: outward = bigger, inward = smaller
            float delta = _textResizeHandle switch
            {
                0 => (-dx - dy) * 0.15f, // TL: pull up-left = bigger
                1 => (dx - dy) * 0.15f,  // TR: pull up-right = bigger
                2 => (-dx + dy) * 0.15f, // BL: pull down-left = bigger
                3 => (dx + dy) * 0.15f,  // BR: pull down-right = bigger
                _ => 0
            };
            _textFontSize = Math.Clamp(_textFontSize + delta, 10f, 120f);
            _textResizeStart = e.Location;
            InvalidateActiveTextLayout();
            Invalidate();
            return;
        }

        int btn = GetToolbarButtonAt(e.Location);
        if (btn != _hoveredButton)
        {
            _hoveredButton = btn;
            toolbarDirty = true;
        }

        // Text toolbar button hover tracking
        int prevTextBtn = _hoveredTextBtn;
        _hoveredTextBtn = -1;
        if (_isTyping)
        {
            if (_textBoldBtnRect.Contains(e.Location)) _hoveredTextBtn = 0;
            else if (_textItalicBtnRect.Contains(e.Location)) _hoveredTextBtn = 1;
            else if (_textStrokeBtnRect.Contains(e.Location)) _hoveredTextBtn = 2;
            else if (_textShadowBtnRect.Contains(e.Location)) _hoveredTextBtn = 3;
            else if (_textFontBtnRect.Contains(e.Location)) _hoveredTextBtn = 4;
        }
        if (_hoveredTextBtn != prevTextBtn)
        {
            _textBtnTooltip = _hoveredTextBtn switch
            {
                0 => "Bold", 1 => "Italic", 2 => "Stroke", 3 => "Shadow", 4 => _textFontFamily, _ => ""
            };
            needsRepaint = true;
        }

        // Select tool resize
        if (_isSelectResizing && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            int dx = e.Location.X - _selectDragStart.X;
            int dy = e.Location.Y - _selectDragStart.Y;
            var ob = _selectHandleBounds;
            Rectangle nb = _selectResizeHandle switch
            {
                0 => Rectangle.FromLTRB(ob.Left + dx, ob.Top + dy, ob.Right, ob.Bottom),  // TL
                1 => Rectangle.FromLTRB(ob.Left, ob.Top + dy, ob.Right + dx, ob.Bottom),  // TR
                2 => Rectangle.FromLTRB(ob.Left + dx, ob.Top, ob.Right, ob.Bottom + dy),  // BL
                3 => Rectangle.FromLTRB(ob.Left, ob.Top, ob.Right + dx, ob.Bottom + dy),  // BR
                _ => ob
            };
            if (nb.Width > 5 && nb.Height > 5)
            {
                var scaled = ScaleAnnotation(_undoStack[_selectedAnnotationIndex], ob, nb);
                _undoStack[_selectedAnnotationIndex] = scaled;
                _selectHandleBounds = nb;
                _selectDragStart = e.Location;
                MarkCommittedAnnotationsDirty();
                Invalidate();
            }
            return;
        }

        // Select tool move drag
        if (_isSelectDragging && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            int dx = e.Location.X - _selectDragStart.X;
            int dy = e.Location.Y - _selectDragStart.Y;
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
            {
                var moved = MoveAnnotation(_undoStack[_selectedAnnotationIndex], dx, dy);
                _undoStack[_selectedAnnotationIndex] = moved;
                MarkCommittedAnnotationsDirty();
                _selectDragStart = e.Location;
                Invalidate();
            }
            return;
        }

        // Cursor: show appropriate cursor for context
        System.Windows.Forms.Cursor target;
        if (_isTyping && _hoveredTextBtn >= 0)
            target = Cursors.Hand;
        else if (_isTyping && _textToolbarRect.Contains(e.Location))
            target = Cursors.Default;
        else if (_isTyping)
        {
            int h = GetTextHandle(e.Location);
            if (h >= 0) target = h is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW;
            else if (GetActiveTextRect().Contains(e.Location)) target = Cursors.SizeAll;
            else target = Cursors.Cross;
        }
        else if (_emojiPickerOpen && _emojiPickerRect.Contains(e.Location))
        {
            int searchBottom = _emojiPickerRect.Y + 6 + 28 + 6;
            target = e.Location.Y < searchBottom ? Cursors.IBeam : (_emojiHovered >= 0 ? Cursors.Hand : Cursors.Default);
        }
        else if (_fontPickerOpen && _fontPickerRect.Contains(e.Location))
        {
            int searchBarH = 28, pad = 6;
            int searchBottom = _fontPickerRect.Y + pad + searchBarH;
            target = e.Location.Y < searchBottom ? Cursors.IBeam : (_fontPickerHovered >= 0 ? Cursors.Hand : Cursors.Default);
        }
        else if (_colorPickerOpen && _colorPickerRect.Contains(e.Location))
            target = Cursors.Hand;
        else if (_mode == CaptureMode.Select)
        {
            int sh = GetSelectHandle(e.Location);
            if (sh >= 0) target = sh is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW;
            else if (_selectedAnnotationIndex >= 0 && GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]).Contains(e.Location))
                target = Cursors.SizeAll;
            else
            {
                int h = HitTestAnnotation(e.Location);
                target = h >= 0 ? Cursors.Hand : Cursors.Default;
            }
        }
        else if (_mode == CaptureMode.Text && !_isTyping)
            target = Cursors.IBeam;
        else if (btn >= 0)
            target = Cursors.Hand;
        else
            target = Cursors.Cross;

        if (!Cursor.Equals(target)) Cursor = target;

        _prevCursorPos = _lastCursorPos;
        var prevCursor = _lastCursorPos;
        var oldCursor = prevCursor == Point.Empty ? e.Location : prevCursor;
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
                var oldDetect = _autoDetectRect;
                var detected = WindowDetector.GetDetectionRectAtPoint(
                    e.Location, _virtualBounds, _windowDetectionMode);

                if (detected != _autoDetectRect)
                {
                    _autoDetectRect = detected;
                    _autoDetectActive = detected.Width > 0;
                    Invalidate(Rectangle.Union(InflateForRepaint(oldDetect), InflateForRepaint(detected)));
                }
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
            case CaptureMode.Scan when _isSelecting:
            case CaptureMode.Sticker when _isSelecting:
                _autoDetectActive = false;
                _selectionEnd = e.Location;
                _selectionRect = _mode == CaptureMode.Rectangle && (ModifierKeys & Keys.Shift) != 0
                    ? GetSquareSelectionRect(_selectionStart, _selectionEnd)
                    : NormRect(_selectionStart, _selectionEnd);
                if (_selectionRect.Width > 3 || _selectionRect.Height > 3) _hasDragged = true;
                _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
                Invalidate();
                break;
            case CaptureMode.Freeform when _isSelecting:
                _freeformPoints.Add(e.Location);
                _hasDragged = true;
                Invalidate();
                break;
            case CaptureMode.Highlight when _isHighlighting:
                Invalidate();
                break;
            case CaptureMode.RectShape when _isRectShapeDragging:
                Invalidate();
                break;
            case CaptureMode.CircleShape when _isCircleShapeDragging:
                Invalidate();
                break;
            case CaptureMode.Magnifier:
                Invalidate();
                break;
            case CaptureMode.Line when _isLineDragging:
                Invalidate();
                break;
            case CaptureMode.Ruler when _isRulerDragging:
                Invalidate();
                break;
            case CaptureMode.Arrow when _isArrowDragging:
                Invalidate();
                break;
            case CaptureMode.Blur when _isBlurring:
                Invalidate();
                break;
            case CaptureMode.Eraser when _isEraserDragging:
                Invalidate();
                break;
            case CaptureMode.Emoji when _isPlacingEmoji:
                Invalidate();
                break;
            case CaptureMode.Draw when _isSelecting:
                if (_currentStroke is { Count: > 0 })
                {
                    if ((ModifierKeys & Keys.Shift) != 0)
                    {
                        var start = _currentStroke[0];
                        var constrained = GetConstrainedDrawPoint(e.Location);
                        _currentStroke.Clear();
                        _currentStroke.Add(start);
                        _currentStroke.Add(constrained);
                    }
                    else
                    {
                        _currentStroke.Add(e.Location);
                    }
                    Invalidate();
                }
                break;
            case CaptureMode.CurvedArrow when _isCurvedArrowDragging:
                _currentCurvedArrow?.Add(e.Location);
                Invalidate();
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
            Invalidate();
        }

        if (needsRepaint)
            Invalidate();

        if (toolbarDirty)
            RefreshToolbar();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // End select drag/resize
        if (_isSelectResizing) { _isSelectResizing = false; _selectResizeHandle = -1; Invalidate(); return; }
        if (_isSelectDragging) { _isSelectDragging = false; Invalidate(); return; }
        // End text move/resize
        if (_textDragging) { _textDragging = false; return; }
        if (_textResizing) { _textResizing = false; _textResizeHandle = -1; return; }
        switch (_mode)
        {
            case CaptureMode.Highlight when _isHighlighting:
                _isHighlighting = false;
                var hlRect = NormRect(_highlightStart, e.Location);
                if (hlRect.Width > 2 && hlRect.Height > 2)
                    AddAnnotation(new HighlightAnnotation(hlRect, DefaultHighlightColor));
                Invalidate(InflateForRepaint(hlRect));
                break;
            case CaptureMode.RectShape when _isRectShapeDragging:
                _isRectShapeDragging = false;
                var rectShape = GetShapeRect(e.Location);
                if (rectShape.Width > 2 && rectShape.Height > 2)
                    AddAnnotation(new RectShapeAnnotation(rectShape, _toolColor));
                Invalidate(InflateForRepaint(rectShape));
                break;
            case CaptureMode.CircleShape when _isCircleShapeDragging:
                _isCircleShapeDragging = false;
                var circleShape = GetShapeRect(e.Location);
                if (circleShape.Width > 2 && circleShape.Height > 2)
                    AddAnnotation(new CircleShapeAnnotation(circleShape, _toolColor));
                Invalidate(InflateForRepaint(circleShape));
                break;
            case CaptureMode.Magnifier:
                // Click already placed it in OnMouseDown, nothing to do on up
                break;
            case CaptureMode.Draw when _isSelecting:
                _isSelecting = false;
                if (_currentStroke is { Count: >= 2 })
                {
                    if ((ModifierKeys & Keys.Shift) != 0)
                    {
                        var start = _currentStroke[0];
                        var constrainedEnd = GetConstrainedDrawPoint(e.Location);
                        _currentStroke.Clear();
                        _currentStroke.Add(start);
                        _currentStroke.Add(constrainedEnd);
                    }
                    AddAnnotation(new DrawStroke(_currentStroke, _toolColor));
                    Invalidate(InflateForRepaint(BoundsOfPoints(_currentStroke, 6)));
                }
                _currentStroke = null;
                break;
            case CaptureMode.Line when _isLineDragging:
                _isLineDragging = false;
                var lineEnd = e.Location;
                float ldx = lineEnd.X - _lineStart.X;
                float ldy = lineEnd.Y - _lineStart.Y;
                if (MathF.Sqrt(ldx * ldx + ldy * ldy) > 5)
                    AddAnnotation(new LineAnnotation(_lineStart, lineEnd, _toolColor));
                Invalidate(InflateForRepaint(NormRect(_lineStart, lineEnd)));
                break;
            case CaptureMode.Ruler when _isRulerDragging:
                _isRulerDragging = false;
                var rulerEnd = GetRulerEnd(e.Location);
                float rdx = rulerEnd.X - _rulerStart.X;
                float rdy = rulerEnd.Y - _rulerStart.Y;
                if (MathF.Sqrt(rdx * rdx + rdy * rdy) > 3)
                    AddAnnotation(new RulerAnnotation(_rulerStart, rulerEnd));
                Invalidate(InflateForRepaint(NormRect(_rulerStart, rulerEnd)));
                break;
            case CaptureMode.Arrow when _isArrowDragging:
                _isArrowDragging = false;
                var end = e.Location;
                float dx = end.X - _arrowStart.X;
                float dy = end.Y - _arrowStart.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) > 5)
                    AddAnnotation(new ArrowAnnotation(_arrowStart, end, _toolColor));
                Invalidate(InflateForRepaint(NormRect(_arrowStart, end)));
                break;
            case CaptureMode.CurvedArrow when _isCurvedArrowDragging:
                _isCurvedArrowDragging = false;
                if (_currentCurvedArrow is { Count: >= 2 })
                {
                    AddAnnotation(new CurvedArrowAnnotation(_currentCurvedArrow, _toolColor));
                    Invalidate(InflateForRepaint(BoundsOfPoints(_currentCurvedArrow, 10)));
                }
                _currentCurvedArrow = null;
                break;
            case CaptureMode.Blur when _isBlurring:
                _isBlurring = false;
                var blurRect = NormRect(_blurStart, e.Location);
                if (blurRect.Width > 3 && blurRect.Height > 3)
                    AddAnnotation(new BlurRect(blurRect));
                Invalidate(InflateForRepaint(blurRect));
                break;
            case CaptureMode.Eraser when _isEraserDragging:
                _isEraserDragging = false;
                var eraserRect = NormRect(_eraserStart, e.Location);
                if (eraserRect.Width > 1 && eraserRect.Height > 1)
                    AddAnnotation(new EraserFill(eraserRect, _eraserColor));
                Invalidate(InflateForRepaint(eraserRect));
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
            case CaptureMode.Scan when _isSelecting:
            case CaptureMode.Sticker when _isSelecting:
                _isSelecting = false;
                bool isOcr = _mode == CaptureMode.Ocr;
                bool isScan = _mode == CaptureMode.Scan;
                bool isSticker = _mode == CaptureMode.Sticker;
                if (!_hasDragged)
                {
                    var detectedAtRelease = WindowDetector.GetDetectionRectAtPoint(
                        e.Location, _virtualBounds, _windowDetectionMode);
                    if (detectedAtRelease.Width > 0 && detectedAtRelease.Height > 0)
                        _autoDetectRect = detectedAtRelease;

                    // Use auto-detected window region if available, else fullscreen
                    var clickRect = (_autoDetectRect.Width > 0 && _autoDetectRect.Height > 0)
                        ? _autoDetectRect
                        : new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                    if (isOcr) OcrRegionSelected?.Invoke(clickRect);
                    else if (isScan) ScanRegionSelected?.Invoke(clickRect);
                    else if (isSticker) StickerRegionSelected?.Invoke(clickRect);
                    else RegionSelected?.Invoke(clickRect);
                }
                else if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                {
                    if (isOcr) OcrRegionSelected?.Invoke(_selectionRect);
                    else if (isScan) ScanRegionSelected?.Invoke(_selectionRect);
                    else if (isSticker) StickerRegionSelected?.Invoke(_selectionRect);
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
            if (_emojiPickerOpen) { _emojiPickerOpen = false; HideEmojiSearchBox(); anyClosed = true; }
            if (_fontPickerOpen) { _fontPickerOpen = false; _fontSearch = ""; _filteredFonts = null; HideFontSearchBox(); anyClosed = true; }
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
            if (_isSelectDragging) { _isSelectDragging = false; anyClosed = true; }
            if (_isSelectResizing) { _isSelectResizing = false; _selectResizeHandle = -1; anyClosed = true; }
            if (_selectedAnnotationIndex >= 0) { _selectedAnnotationIndex = -1; anyClosed = true; }
            if (_isTyping)
            {
                _isTyping = false;
                _textBuffer = "";
                HideTextBox();
                anyClosed = true;
            }
            if (anyClosed)
            {
                Invalidate();
                RefreshToolbar();
                return true;
            }
            Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // All search/text input is handled by off-screen TextBoxes
        if (_emojiPickerOpen) return;

        // Emoji placing: Tab re-opens picker
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            if (e.KeyCode == Keys.Tab) { _emojiPickerOpen = true; _isPlacingEmoji = false; ShowEmojiSearchBox(); RefreshToolbar(); }
            return;
        }

        if (_fontPickerOpen) return;
        if (_isTyping) return;
        if (TryHandleAnnotationToolNumber(e.KeyCode))
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            RefreshToolbar();
            Invalidate();
            return;
        }

        // Delete selected annotation
        if (e.KeyCode == Keys.Delete && _mode == CaptureMode.Select && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            var bounds = InflateForRepaint(GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]));
            _undoStack.RemoveAt(_selectedAnnotationIndex);
            MarkCommittedAnnotationsDirty();
            _selectedAnnotationIndex = -1;
            Invalidate(bounds);
            return;
        }

        if (e.KeyCode == Keys.Z && e.Control && _undoStack.Count > 0)
        {
            var last = RemoveLastAnnotation();
            // Update step counter when undoing a step number
            if (last is StepNumberAnnotation)
            {
                var remaining = _undoStack.OfType<StepNumberAnnotation>().LastOrDefault();
                _nextStepNumber = remaining != null ? remaining.Number + 1 : 1;
            }
            Invalidate(InflateForRepaint(GetAnnotationBounds(last)));
        }
    }

    // All text input is handled by off-screen TextBox controls

    private void CommitText()
    {
        // Sync from TextBox before committing
        if (_textBox != null && _textBox.Visible)
            _textBuffer = _textBox.Text;
        if (_isTyping && _textBuffer.Length > 0)
            AddAnnotation(new TextAnnotation(_textPos, _textBuffer, _textFontSize, _toolColor, _textBold, _textItalic, _textStroke, _textShadow, _textFontFamily));
        _isTyping = false;
        _textBuffer = "";
        InvalidateActiveTextLayout();
        _fontPickerOpen = false;
        HideTextBox();
        Invalidate();
    }

    private RectangleF MeasureTextRect(Point pos, string text, float fontSize, string fontFamily, bool bold, bool italic)
    {
        var style = FontStyle.Regular;
        if (bold) style |= FontStyle.Bold;
        if (italic) style |= FontStyle.Italic;
        var font = GetAnnotationFont(fontFamily, fontSize, style);
        string display = text.Length > 0 ? text : "Type here...";
        var size = TextRenderer.MeasureText(display, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return new RectangleF(pos.X - 6, pos.Y - 4, Math.Max(size.Width + 12, 100), size.Height + 8);
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
            _activeTextRectCache = MeasureTextRect(_textPos, _textBuffer, _textFontSize, _textFontFamily, _textBold, _textItalic);
            _activeTextMeasureWidth = measured.Width;
            const int hs = 10;
            _activeTextHandleCache[0] = new RectangleF(_activeTextRectCache.X - hs / 2f, _activeTextRectCache.Y - hs / 2f, hs, hs);
            _activeTextHandleCache[1] = new RectangleF(_activeTextRectCache.Right - hs / 2f, _activeTextRectCache.Y - hs / 2f, hs, hs);
            _activeTextHandleCache[2] = new RectangleF(_activeTextRectCache.X - hs / 2f, _activeTextRectCache.Bottom - hs / 2f, hs, hs);
            _activeTextHandleCache[3] = new RectangleF(_activeTextRectCache.Right - hs / 2f, _activeTextRectCache.Bottom - hs / 2f, hs, hs);
            _activeTextLayoutDirty = false;
        }
        return _activeTextRectCache;
    }

    private int GetTextHandle(Point p)
    {
        if (!_isTyping) return -1;
        _ = GetActiveTextRect();
        for (int i = 0; i < _activeTextHandleCache.Length; i++)
            if (_activeTextHandleCache[i].Contains(p)) return i;
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
            var rect = MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic);
            if (rect.Contains(p)) return i;
        }
        return -1;
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

        int swatchSize = 28, pad = 4;
        int relX = p.X - _colorPickerRect.X - pad;
        int relY = p.Y - _colorPickerRect.Y - pad;
        int col = relX / (swatchSize + pad);
        if (col >= 0 && col < ToolColors.Length && relY >= 0 && relY < swatchSize + pad)
        {
            _toolColor = ToolColors[col];
            _toolColorIndex = col;
            _colorPickerOpen = false;
            Invalidate(InflateForRepaint(GetColorPickerBounds(), 12));
            RefreshToolbar();
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
            InvalidateActiveTextLayout();
            UpdateTextBoxStyle(); SyncTextBoxSize();
            Invalidate(InflateForRepaint(GetFontPickerBounds(), 12));
            RefreshToolbar();
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
            RefreshToolbar();
        }
        else if (_emojiPickerOpen)
        {
            var filtered = GetFilteredEmojiPalette();
            int cols = 8, visibleRows = 4;
            int totalRows = (filtered.Length + cols - 1) / cols;
            int maxScroll = Math.Max(0, totalRows - visibleRows);
            _emojiScrollOffset = Math.Clamp(_emojiScrollOffset + (e.Delta > 0 ? -1 : 1), 0, maxScroll);
            RefreshToolbar();
        }
        else if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            // Scroll wheel changes emoji size
            var oldPreview = GetEmojiPreviewRect(_lastCursorPos);
            _emojiPlaceSize = Math.Clamp(_emojiPlaceSize + (e.Delta > 0 ? 4f : -4f), 16f, 128f);
            Invalidate(Rectangle.Union(InflateForRepaint(oldPreview), InflateForRepaint(GetEmojiPreviewRect(_lastCursorPos))));
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
            HideEmojiSearchBox();
            Invalidate(InflateForRepaint(GetEmojiPickerBounds(), 12));
            RefreshToolbar();
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
            int px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
            int py = _toolbarRect.Bottom + 8;
            _emojiPickerRect = new Rectangle(px, py, pw, ph);
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
        int px = colorBtn.X + colorBtn.Width / 2 - pw / 2;
        int py = colorBtn.Y + colorBtn.Height + 8;
        return new Rectangle(px, py, pw, ph);
    }

    private Rectangle GetEmojiPickerBounds()
    {
        int cols = 8, emojiSize = 32, pad = 6;
        int searchBarH = 28;
        int visibleRows = 4;
        int pw = cols * (emojiSize + pad) + pad;
        int ph = searchBarH + pad + visibleRows * (emojiSize + pad) + pad;
        int px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
        int py = _toolbarRect.Bottom + 8;
        return new Rectangle(px, py, pw, ph);
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
            px = _toolbarRect.X + _toolbarRect.Width / 2 - pw / 2;
            py = _toolbarRect.Bottom + 8;
        }
        return new Rectangle(px, py, pw, ph);
    }

    // Maps keyboard keys to badge labels for annotation tool shortcuts
    internal static readonly (Keys key, string label)[] AnnotationKeyMap = {
        (Keys.D1, "1"), (Keys.D2, "2"), (Keys.D3, "3"),
        (Keys.D4, "4"), (Keys.D5, "5"), (Keys.D6, "6"),
        (Keys.D7, "7"), (Keys.D8, "8"), (Keys.D9, "9"),
        (Keys.D0, "0"), (Keys.OemMinus, "-"), (Keys.Oemplus, "="),
        (Keys.OemOpenBrackets, "["), (Keys.OemCloseBrackets, "]"),
        (Keys.OemPipe, "\\"),
    };

    private bool TryHandleAnnotationToolNumber(Keys keyCode)
    {
        int index = -1;
        for (int i = 0; i < AnnotationKeyMap.Length; i++)
        {
            if (AnnotationKeyMap[i].key == keyCode) { index = i; break; }
        }
        if (index < 0) return false;

        var modes = _visibleTools
            .Where(t => t.Mode.HasValue && t.Group == 1)
            .Select(t => t.Mode!.Value)
            .ToList();

        if (index >= modes.Count) return false;
        SetMode(modes[index]);
        return true;
    }

}
