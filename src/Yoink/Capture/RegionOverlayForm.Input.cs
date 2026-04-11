using System.Drawing;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;

        // Flyout button click
        if (_flyoutOpen && _flyoutTools.Length > 0)
        {
            int fb = GetFlyoutButtonAt(e.Location);
            if (fb >= 0 && _flyoutTools[fb].Mode.HasValue)
            {
                SetMode(_flyoutTools[fb].Mode!.Value);
                return;
            }
        }

        int btn = GetToolbarButtonAt(e.Location);
        if (btn >= 0)
        {
            if (btn == BtnCount - 1) { Cancel(); return; }     // close
            if (btn == BtnCount - 2) { ToggleColorPicker(); return; } // color dot
            if (_moreButtonIndex >= 0 && btn == _moreButtonIndex)
            {
                SetFlyoutOpen(!_flyoutOpen);
                return;
            }
            if (btn < _mainBarTools.Length && _mainBarTools[btn].Mode.HasValue)
                SetMode(_mainBarTools[btn].Mode!.Value);
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
            if (_textBackgroundBtnRect.Contains(e.Location))
            {
                _textBackground = !_textBackground;
                InvalidateActiveTextLayout();
                UpdateTextBoxStyle(); SyncTextBoxSize();
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
                ClearCrosshairGuides();
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
                var oldTextRect = InflateForRepaint(Rectangle.Round(MeasureTextRect(ta.Pos, ta.Text, ta.FontSize, ta.FontFamily, ta.Bold, ta.Italic, ta.Background)));
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
                _textBackground = ta.Background;
                _textFontFamily = ta.FontFamily;
                InvalidateActiveTextLayout();
                ShowTextBox();
                _textDragging = true;
                _textDragOffset = new Point(e.Location.X - _textPos.X, e.Location.Y - _textPos.Y);
                RefreshOverlayUiChrome();
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
                ClearCrosshairGuides();
                Invalidate();
                return;
            }

            int hit = HitTestAnnotation(e.Location);
            if (hit >= 0)
            {
                _selectedAnnotationIndex = hit;
                _isSelectDragging = true;
                var bounds = GetAnnotationBounds(_undoStack[hit]);
                _selectDragStart = e.Location;
                _selectDragOffset = new Point(e.Location.X - bounds.X, e.Location.Y - bounds.Y);
                ClearCrosshairGuides();
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
                if (_windowDetectionMode == WindowDetectionMode.Off)
                {
                    _autoDetectRect = Rectangle.Empty;
                    _autoDetectActive = false;
                }
                else
                {
                    _autoDetectRect = WindowDetector.GetDetectionRectAtPoint(
                        e.Location, _virtualBounds, _windowDetectionMode);
                    _autoDetectActive = _autoDetectRect.Width > 0 && _autoDetectRect.Height > 0;
                }
                _isSelecting = true;
                _selectionStart = _selectionEnd = e.Location;
                _selectionRect = Rectangle.Empty;
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
                RefreshOverlayUiChrome();
                Invalidate(InflateForRepaint(Rectangle.Round(MeasureTextRect(_textPos, "", _textFontSize, _textFontFamily, _textBold, _textItalic, _textBackground))));
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
                var pixelData = GetPixelData();
                int cx = Math.Clamp(e.Location.X, 0, _bmpW - 1);
                int cy = Math.Clamp(e.Location.Y, 0, _bmpH - 1);
                _eraserColor = Color.FromArgb(pixelData[cy * _bmpW + cx]);
                _eraserStart = e.Location;
                _isEraserDragging = true;
                break;
        }
    }

}
