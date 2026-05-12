using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    public static void CloseTransientUi()
    {
        var current = _currentOverlay;
        if (current is null)
            return;

        try { current.CloseMagWindow(); } catch { }
        try { current.CloseCaptureMagnifier(); } catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _toolbarAnim = 1f;
        CaptureWindowExclusion.Apply(this);
        WindowDetector.RegisterIgnoredWindow(Handle);
        Native.User32.SetWindowPos(Handle, Native.User32.HWND_TOPMOST,
            0, 0, 0, 0,
            Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_SHOWWINDOW);
        Activate();
        Focus();
        _escapeHook = CaptureEscapeKeyHook.Install(this, Cancel);
        QueueToolbarReady();
        Invalidate();

        WindowDetector.ClearSnapshot();
        if (_windowDetectionMode != WindowDetectionMode.Off)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(220).ConfigureAwait(false);
                    if (IsDisposed || Disposing || !Visible)
                        return;

                    WindowDetector.SnapshotWindows(_virtualBounds);
                }
                catch { }
            });
        }
    }

    private void QueueToolbarReady()
    {
        if (IsDisposed || Disposing)
            return;

        BeginInvoke(new Action(EnsureToolbarReady));
    }

    private void EnsureToolbarReady()
    {
        if (IsDisposed || Disposing || !Visible)
            return;

        if (_isSelecting && ToolDef.IsCaptureTool(_mode))
            return;

        if (_toolbarForm == null || _toolbarForm.IsDisposed)
        {
            _toolbarForm = new ToolbarForm(this);
            PositionToolbarForm();
            var _ = _toolbarForm.Handle;
            WindowDetector.RegisterIgnoredWindow(_toolbarForm.Handle);
            _toolbarForm.Show(this);
        }
        else if (!_toolbarForm.Visible)
        {
            PositionToolbarForm();
            _toolbarForm.Show(this);
        }

        MarkToolbarRenderDirty();
        _toolbarForm.UpdateSurface();
        Invalidate(new Rectangle(_toolbarRect.X - 12, _toolbarRect.Y - 48,
            _toolbarRect.Width + 24, _toolbarRect.Height + 96));
    }

    internal void PrepareFirstMoveChrome()
    {
        if (IsDisposed || Disposing)
            return;

        try
        {
            if (ShowCaptureMagnifier)
                BuildMagnifier();
            WarmSelectionChromeForFirstMove();
        }
        catch (Exception ex)
        {
            OddSnap.Services.AppDiagnostics.LogError("capture.overlay.prepare-first-move-chrome", ex);
        }
    }

    private void WarmSelectionChromeForFirstMove()
    {
        var oldSelecting = _isSelecting;
        var oldSelection = _selectionRect;
        var oldSelectionEnd = _selectionEnd;
        var oldHasSelection = _hasSelection;
        var oldCrosshairVisible = _crosshairVisible;
        var oldCrosshairPoint = _crosshairPoint;
        var oldMagnifierVisible = _captureMagnifierVisible;
        var oldMagnifierBounds = _captureMagnifierBounds;

        using var warmSurface = new Bitmap(160, 120, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(warmSurface);
        ApplyUiGraphics(g);

        try
        {
            var warmSelection = new Rectangle(16, 18, 96, 64);
            var warmCursor = new Point(warmSelection.Right, warmSelection.Bottom);
            _isSelecting = true;
            _selectionRect = warmSelection;
            _selectionEnd = warmCursor;
            _hasSelection = true;
            _crosshairVisible = ShowCrosshairGuides;
            _crosshairPoint = new Point(
                Math.Clamp(80, 1, Math.Max(1, ClientSize.Width - 2)),
                Math.Clamp(60, 1, Math.Max(1, ClientSize.Height - 2)));
            _captureMagnifierVisible = ShowCaptureMagnifier;
            _captureMagnifierBounds = new Rectangle(
                24,
                8,
                PickerMagnifierForm.TotalW,
                PickerMagnifierForm.GetTotalHeight(showInfo: false));

            SelectionFrameRenderer.DrawRectangle(g, warmSelection);
            SelectionSizeReadout.Draw(
                g,
                warmCursor,
                warmSelection,
                _readoutFont,
                new Rectangle(0, 0, warmSurface.Width, warmSurface.Height));
            DrawCrosshairGuides(g);
            DrawCaptureMagnifier(g);
        }
        finally
        {
            _isSelecting = oldSelecting;
            _selectionRect = oldSelection;
            _selectionEnd = oldSelectionEnd;
            _hasSelection = oldHasSelection;
            _crosshairVisible = oldCrosshairVisible;
            _crosshairPoint = oldCrosshairPoint;
            _captureMagnifierVisible = oldMagnifierVisible;
            _captureMagnifierBounds = oldMagnifierBounds;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        if (_allowDeactivation || IsDisposed || Disposing || !Visible)
            return;

        BeginInvoke(new Action(() =>
        {
            if (_allowDeactivation || IsDisposed || Disposing || !Visible)
                return;

            Activate();
            Focus();
            Native.User32.SetForegroundWindow(Handle);
        }));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
    }

    internal void PositionToolbarForm()
    {
        if (_toolbarForm is null) return;
        var uiBounds = GetOverlayUiBounds();
        if (uiBounds.IsEmpty)
            uiBounds = InflateForRepaint(_toolbarRect, 24);

        int marginX = Helpers.UiChrome.ScaleInt(IsVerticalDock ? 120 : 260);
        int marginY = Helpers.UiChrome.ScaleInt(IsVerticalDock ? 120 : 140);
        uiBounds.Inflate(marginX, marginY);
        uiBounds.Intersect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));

        var bounds = new Rectangle(
            _virtualBounds.X + uiBounds.X,
            _virtualBounds.Y + uiBounds.Y,
            Math.Max(1, uiBounds.Width),
            Math.Max(1, uiBounds.Height));
        if (_toolbarForm.Bounds != bounds)
            _toolbarForm.Bounds = bounds;
    }

    private static Rectangle[] GetScreenWorkingAreas()
    {
        var screens = Screen.AllScreens;
        var workingAreas = new Rectangle[screens.Length];
        for (int i = 0; i < screens.Length; i++)
            workingAreas[i] = screens[i].WorkingArea;
        return workingAreas;
    }

    private bool UpdateToolbarAnchorForClientPoint(Point clientPoint)
    {
        var screenPoint = new Point(_virtualBounds.X + clientPoint.X, _virtualBounds.Y + clientPoint.Y);
        var resolved = ToolbarLayout.ResolveToolbarAnchorArea(
            _virtualBounds,
            screenPoint,
            _toolbarAnchorArea,
            GetScreenWorkingAreas());
        if (resolved == _toolbarAnchorArea)
            return false;

        _toolbarAnchorArea = resolved;
        return true;
    }

    private void ShowTextBox()
    {
        if (_textBox == null)
        {
            _textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Multiline = false,
                ScrollBars = ScrollBars.None,
                // Hidden off-screen - we only use it for input handling, not display
                Size = new Size(1, 1),
            };
            _textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitText(); }
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Cancel();
                }
            };
            _textBox.TextChanged += (_, _) =>
            {
                if (_textBox == null) return;
                var oldRect = Rectangle.Round(GetActiveTextRect());
                var oldToolbarRect = Rectangle.Round(GetTextToolbarBounds());
                _textBuffer = _textBox.Text;
                InvalidateActiveTextLayout();
                SyncTextBoxSize();
                var newRect = Rectangle.Round(GetActiveTextRect());
                var newToolbarRect = Rectangle.Round(GetTextToolbarBounds());
                var dirty = Rectangle.Union(
                    Rectangle.Union(InflateForRepaint(oldRect, 16), InflateForRepaint(newRect, 16)),
                    Rectangle.Union(InflateForRepaint(oldToolbarRect, 16), InflateForRepaint(newToolbarRect, 16)));
                RefreshOverlayUiChrome();
                Invalidate(dirty);
            };
            Controls.Add(_textBox);
        }
        _textBox.Text = _textBuffer;
        UpdateTextBoxStyle();
        SyncTextBoxSize();
        RefreshOverlayUiChrome();
        _textBox.Visible = true;
        _textBox.Focus();
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.SelectionLength = 0;
        _textSelectionAnchor = _textBox.SelectionStart;
    }

    private void HideTextBox()
    {
        if (_textBox != null)
        {
            _textBox.Visible = false;
            RefreshOverlayUiChrome();
            Focus();
        }
    }

    private void UpdateTextBoxStyle()
    {
        if (_textBox == null)
            return;

        var fontStyle = FontStyle.Regular;
        if (_textBold) fontStyle |= FontStyle.Bold;
        if (_textItalic) fontStyle |= FontStyle.Italic;

        _textBox.Font = GetAnnotationFont(_textFontFamily, _textFontSize, fontStyle);
    }

    private void SyncTextBoxSize()
    {
        if (_textBox == null)
            return;

        _textBox.Size = new Size(1, 1);
        _textBox.Location = new Point(-10000, -10000);
    }

    private void InvalidateActiveTextLayout()
    {
        _activeTextLayoutDirty = true;
    }

    private void ShowEmojiSearchBox()
    {
        if (_emojiSearchBox == null)
        {
            _emojiSearchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Size = new Size(1, 1),
            };
            _emojiSearchBox.TextChanged += (_, _) =>
            {
                if (_emojiSearchBox == null) return;
                _emojiSearch = _emojiSearchBox.Text;
                _emojiScrollOffset = 0;
                QueueEmojiWarmup();
                UpdateToolbarSurfaceOnly();
            };
            _emojiSearchBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Cancel();
                }
            };
            Controls.Add(_emojiSearchBox);
        }
        _emojiSearchBox.Text = _emojiSearch;
        _emojiSearchBox.Location = new Point(-100, -100);
        _emojiSearchBox.Visible = true;
        _emojiSearchBox.Focus();
    }

    private void HideEmojiSearchBox()
    {
        if (_emojiSearchBox != null) { _emojiSearchBox.Visible = false; Focus(); }
    }

    private void QueueEmojiWarmup()
    {
        _emojiWarmupIndex = 0;
        _emojiWarmupPending = true;
        _pickerTimer.Start();
    }

    private void WarmEmojiPickerCacheBatch()
    {
        if (!_emojiWarmupPending || !_emojiPickerOpen)
            return;

        var filtered = GetFilteredEmojiPalette();
        if (_emojiWarmupIndex >= filtered.Length)
        {
            _emojiWarmupPending = false;
            return;
        }

        const int batchSize = 8;
        int rendered = WarmEmojiRange(filtered, _emojiScrollOffset * EmojiPickerColumns, EmojiPickerVisibleRows * EmojiPickerColumns, batchSize);
        if (rendered < batchSize)
        {
            int end = Math.Min(filtered.Length, _emojiWarmupIndex + batchSize - rendered);
            for (int i = _emojiWarmupIndex; i < end; i++)
                _emojiRenderer.GetEmoji(filtered[i].emoji, EmojiPickerRenderSize);
            _emojiWarmupIndex = end;
        }

        if (_emojiWarmupIndex >= filtered.Length && IsVisibleEmojiRangeWarm(filtered))
            _emojiWarmupPending = false;

        UpdateToolbarSurfaceOnly();
    }

    private int WarmEmojiRange((string emoji, string name)[] filtered, int start, int count, int maxRender)
    {
        int rendered = 0;
        int end = Math.Min(filtered.Length, start + count);
        for (int i = Math.Max(0, start); i < end && rendered < maxRender; i++)
        {
            if (_emojiRenderer.TryGetCachedEmoji(filtered[i].emoji, EmojiPickerRenderSize, out _))
                continue;
            _emojiRenderer.GetEmoji(filtered[i].emoji, EmojiPickerRenderSize);
            rendered++;
        }
        return rendered;
    }

    private bool IsVisibleEmojiRangeWarm((string emoji, string name)[] filtered)
    {
        int start = _emojiScrollOffset * EmojiPickerColumns;
        int end = Math.Min(filtered.Length, start + EmojiPickerVisibleRows * EmojiPickerColumns);
        for (int i = start; i < end; i++)
        {
            if (!_emojiRenderer.TryGetCachedEmoji(filtered[i].emoji, EmojiPickerRenderSize, out _))
                return false;
        }
        return true;
    }

    private void ShowFontSearchBox()
    {
        if (_fontSearchBox == null)
        {
            _fontSearchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Size = new Size(1, 1),
            };
            _fontSearchBox.TextChanged += (_, _) =>
            {
                if (_fontSearchBox == null) return;
                _fontSearch = _fontSearchBox.Text;
                _filteredFonts = null; _fontPickerScroll = 0;
                RefreshToolbar();
            };
            _fontSearchBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    Cancel();
                }
            };
            Controls.Add(_fontSearchBox);
        }
        _fontSearchBox.Text = _fontSearch;
        _fontSearchBox.Location = new Point(-100, -100);
        _fontSearchBox.Visible = true;
        _fontSearchBox.Focus();
    }

    private void HideFontSearchBox()
    {
        if (_fontSearchBox != null) { _fontSearchBox.Visible = false; Focus(); }
    }

    internal void RefreshToolbar()
    {
        var oldUiBounds = _lastOverlayUiBounds;
        CalcToolbar();
        PositionToolbarForm();
        MarkToolbarRenderDirty();
        _toolbarForm?.UpdateSurface();
        var newUiBounds = GetOverlayUiBounds();
        _lastOverlayUiBounds = newUiBounds;
        if (!oldUiBounds.IsEmpty && !newUiBounds.IsEmpty)
            Invalidate(Rectangle.Union(InflateForRepaint(oldUiBounds, 20), InflateForRepaint(newUiBounds, 20)));
        else if (!newUiBounds.IsEmpty)
            Invalidate(InflateForRepaint(newUiBounds, 20));
    }

    internal void UpdateToolbarSurfaceOnly()
    {
        MarkToolbarRenderDirty();
        _toolbarForm?.UpdateSurface();
    }

    private void RefreshOverlayUiChrome()
    {
        var oldUiBounds = _lastOverlayUiBounds;
        PositionToolbarForm();
        MarkToolbarRenderDirty();
        _toolbarForm?.UpdateSurface();
        var newUiBounds = GetOverlayUiBounds();
        _lastOverlayUiBounds = newUiBounds;
        if (!oldUiBounds.IsEmpty && !newUiBounds.IsEmpty)
            Invalidate(Rectangle.Union(InflateForRepaint(oldUiBounds, 20), InflateForRepaint(newUiBounds, 20)));
        else if (!newUiBounds.IsEmpty)
            Invalidate(InflateForRepaint(newUiBounds, 20));
        else if (!oldUiBounds.IsEmpty)
            Invalidate(InflateForRepaint(oldUiBounds, 20));
    }

    private void HideToolbarImmediately()
    {
        HideToolbarTooltip();
        CloseMoreToolsDropdown();
        _colorPickerOpen = false;
        _fontPickerOpen = false;
        _emojiPickerOpen = false;
        HideFontSearchBox();
        HideEmojiSearchBox();
        _hoveredButton = -1;

        if (_toolbarForm is null || _toolbarForm.IsDisposed)
            return;

        _animTimer.Stop();
        _toolbarAnim = 1f;
        _toolbarForm.Hide();
    }

    private void HideToolbarForCaptureTool()
    {
        if (ToolDef.IsCaptureTool(_mode))
            HideToolbarImmediately();
    }

    private void UpdateCrosshairGuides(Point point)
    {
        bool shouldShow = ShowCrosshairGuides
            && _mode != CaptureMode.ColorPicker
            && point != Point.Empty
            && !IsPointInOverlayUi(point);
        if (!shouldShow)
        {
            ClearCrosshairGuides();
            return;
        }

        if (_crosshairVisible && _crosshairPoint == point)
            return;

        if (_crosshairVisible)
            InvalidateCrosshair(_crosshairPoint);

        _crosshairPoint = point;
        _crosshairVisible = true;
        InvalidateCrosshair(point);
    }

    private void ClearCrosshairGuides()
    {
        if (_crosshairVisible)
            InvalidateCrosshair(_crosshairPoint);
        _crosshairVisible = false;
        _crosshairPoint = Point.Empty;
    }

    private void InvalidateCrosshair(Point point)
    {
        if (point == Point.Empty)
            return;

        Invalidate(new Rectangle(Math.Max(0, point.X - 3), 0, 7, ClientSize.Height));
        Invalidate(new Rectangle(0, Math.Max(0, point.Y - 3), ClientSize.Width, 7));
    }

    internal int ToolbarRenderVersion => _toolbarRenderVersion;

    private void MarkToolbarRenderDirty()
    {
        unchecked
        {
            _toolbarRenderVersion++;
        }
    }

    private void EnsureSelectionAdorner()
    {
        if (_selectionAdorner is { IsDisposed: false })
            return;

        _selectionAdorner = new LiveSelectionAdornerForm(_virtualBounds, "");
        var _ = _selectionAdorner.Handle;
        WindowDetector.RegisterIgnoredWindow(_selectionAdorner.Handle);
        _selectionAdorner.Show(this);
    }

    private void UpdateSelectionAdorner()
    {
        EnsureSelectionAdorner();
        _selectionAdorner?.SetSelection(_selectionRect, GetReadoutCursorPoint());
    }

    private void CloseSelectionAdorner()
    {
        if (_selectionAdorner == null)
            return;

        try { WindowDetector.UnregisterIgnoredWindow(_selectionAdorner.Handle); } catch { }
        try { _selectionAdorner.Close(); } catch { }
        try { _selectionAdorner.Dispose(); } catch { }
        _selectionAdorner = null;
    }

    private void Cancel()
    {
        if (_cancelRequested)
            return;

        _cancelRequested = true;
        _allowDeactivation = true;
        try { Hide(); } catch { }
        try { HideToolbarImmediately(); } catch { }
        try { HideTextBox(); } catch { }
        try { HideEmojiSearchBox(); } catch { }
        try { HideFontSearchBox(); } catch { }
        try { CloseMagWindow(); } catch { }
        try { CloseCaptureMagnifier(); } catch { }
        try { CloseSelectionAdorner(); } catch { }
        try { ClearCrosshairGuides(); } catch { }
        try { ResetSelectionDragMoveQueue(); } catch { }
        SelectionCancelled?.Invoke();
        Close();
    }

    internal void CancelFromShortcut() => Cancel();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_currentOverlay == this)
                _currentOverlay = null;
            _escapeHook?.Dispose();
            _escapeHook = null;
            ClearCrosshairGuides();
            CloseSelectionAdorner();
            WindowDetector.UnregisterIgnoredWindow(Handle);
            WindowDetector.ClearSnapshot();
            if (_toolbarForm != null)
                WindowDetector.UnregisterIgnoredWindow(_toolbarForm.Handle);
            CloseMoreToolsDropdown();
            StopMoreToolsMenuMonitor();
            CloseMagWindow();
            CloseCaptureMagnifier();
            _toolbarForm?.Close();
            _toolbarForm?.Dispose();
            _toolbarToolTip?.Dispose();
            _animTimer.Dispose();
            _pickerTimer.Dispose();
            _autoDetectTimer.Dispose();
            _selectionMoveTimer.Dispose();
            _magGfx.Dispose();
            _magBitmap.Dispose();
            _committedAnnotationsBitmap?.Dispose();
            _hexFont.Dispose();
            _rgbFont.Dispose();
            _readoutFont.Dispose();
            _mutedBrush.Dispose();
            _crossPen.Dispose();
            foreach (var f in _fontCache.Values) f?.Dispose();
            _fontCache.Clear();
            foreach (var f in _annotationFontCache.Values) f?.Dispose();
            _annotationFontCache.Clear();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
}
