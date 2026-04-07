using System.Drawing;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
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
        if (_mode == CaptureMode.Freeform && _isSelecting)
        {
            CloseCaptureMagnifier();
            ClearCrosshairGuides();

            if (_freeformPoints.Count == 0)
            {
                _freeformPoints.Add(e.Location);
            }
            else
            {
                var last = _freeformPoints[^1];
                int dx = e.Location.X - last.X;
                int dy = e.Location.Y - last.Y;
                if ((dx * dx) + (dy * dy) >= 9)
                {
                    _freeformPoints.Add(e.Location);
                    _hasDragged = true;
                    Invalidate(InflateForRepaint(RectFromPoints(last, e.Location, 10)));
                }
            }
            return;
        }

        bool needsRepaint = false;
        bool toolbarDirty = false;

        if (UpdateToolbarAnchorForClientPoint(e.Location))
            toolbarDirty = true;

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

        // Flyout hover tracking
        if (_flyoutOpen && _flyoutTools.Length > 0)
        {
            int fb = GetFlyoutButtonAt(e.Location);
            if (fb != _hoveredFlyoutButton)
            {
                _hoveredFlyoutButton = fb;
                toolbarDirty = true;
            }
        }
        else if (_hoveredFlyoutButton >= 0)
        {
            _hoveredFlyoutButton = -1;
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
        else if (_flyoutOpen && GetFlyoutButtonAt(e.Location) >= 0)
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

        bool isSelectingCapture = _isSelecting &&
            (_mode is CaptureMode.Rectangle or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker);

        if (ShowCaptureMagnifier && ToolDef.IsCaptureTool(_mode) && ShouldShowCaptureMagnifierAt(e.Location))
            UpdateCaptureMagnifier(e.Location);
        else if (_captureMagnifierForm != null && (!ShowCaptureMagnifier || !ToolDef.IsCaptureTool(_mode)))
            CloseCaptureMagnifier();
        else if (isSelectingCapture || IsPointInOverlayUi(e.Location))
            CloseCaptureMagnifier();

        switch (_mode)
        {
            case CaptureMode.Rectangle when !_isSelecting:
            case CaptureMode.Ocr when !_isSelecting:
            case CaptureMode.Scan when !_isSelecting:
            case CaptureMode.Sticker when !_isSelecting:
                if (IsPointInOverlayUi(e.Location))
                {
                    var oldDetect = _autoDetectRect;
                    _autoDetectRect = Rectangle.Empty;
                    _autoDetectActive = false;
                    _autoDetectTimer.Stop();
                    InvalidateAutoDetectChrome(oldDetect, Rectangle.Empty);
                }
                else
                {
                    UpdateAutoDetectRect(e.Location);
                }
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
            case CaptureMode.Scan when _isSelecting:
            case CaptureMode.Sticker when _isSelecting:
                var oldSelectionRect = _selectionRect;
                bool wasOcrSelection = _mode == CaptureMode.Ocr;
                bool wasScanSelection = _mode == CaptureMode.Scan;
                _autoDetectActive = false;
                _autoDetectTimer.Stop();
                _selectionEnd = e.Location;
                _selectionRect = _mode == CaptureMode.Rectangle && (ModifierKeys & Keys.Shift) != 0
                    ? GetSquareSelectionRect(_selectionStart, _selectionEnd)
                    : NormRect(_selectionStart, _selectionEnd);
                if (_selectionRect.Width > 3 || _selectionRect.Height > 3) _hasDragged = true;
                _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
                var oldDirty = GetSelectionOverlayBounds(oldSelectionRect, wasOcrSelection, wasScanSelection);
                var newDirty = GetSelectionOverlayBounds(_selectionRect, _mode == CaptureMode.Ocr, _mode == CaptureMode.Scan);
                if (oldDirty.IsEmpty)
                    Invalidate(newDirty);
                else if (newDirty.IsEmpty)
                    Invalidate(oldDirty);
                else
                    Invalidate(Rectangle.Union(oldDirty, newDirty));
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

        UpdateCrosshairGuides(_lastCursorPos);

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
                    if (_windowDetectionMode != WindowDetectionMode.Off)
                    {
                        var detectedAtRelease = WindowDetector.GetDetectionRectAtPoint(
                            e.Location, _virtualBounds, _windowDetectionMode);
                        if (detectedAtRelease.Width > 0 && detectedAtRelease.Height > 0)
                            _autoDetectRect = detectedAtRelease;
                    }
                    else
                    {
                        _autoDetectRect = Rectangle.Empty;
                        _autoDetectActive = false;
                    }

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
                    _autoDetectRect = Rectangle.Empty;
                    _autoDetectActive = false;
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

        // Check if the cursor actually left the form area. Child/overlay windows
        // (toolbar, crosshair guides) trigger spurious mouse-leave events while
        // the cursor is still logically within our bounds.
        var screenPos = System.Windows.Forms.Cursor.Position;
        var clientPos = PointToClient(screenPos);
        bool actuallyLeft = clientPos.X < 0 || clientPos.Y < 0
            || clientPos.X >= ClientSize.Width || clientPos.Y >= ClientSize.Height;

        _hoveredButton = -1;
        CloseCaptureMagnifier();
        _autoDetectTimer.Stop();

        if (actuallyLeft)
        {
            ClearCrosshairGuides();
            _prevCursorPos = _lastCursorPos;
            _lastCursorPos = Point.Empty;
            _lastAutoDetectRect = Rectangle.Empty;
            _autoDetectRect = Rectangle.Empty;
            _autoDetectActive = false;
        }

        Invalidate();
        RefreshToolbar();
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
}
