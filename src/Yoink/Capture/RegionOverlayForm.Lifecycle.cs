using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Capture;

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
        WindowDetector.RegisterIgnoredWindow(Handle);
        Native.User32.SetWindowPos(Handle, Native.User32.HWND_TOPMOST,
            0, 0, 0, 0,
            Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_SHOWWINDOW);
        Activate();
        Focus();
        EnsureToolbarReady();
        Invalidate();
        Update();

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

    private void EnsureToolbarReady()
    {
        if (IsDisposed || Disposing || !Visible)
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

        _toolbarForm.UpdateSurface();
        Invalidate(new Rectangle(_toolbarRect.X - 12, _toolbarRect.Y - 48,
            _toolbarRect.Width + 24, _toolbarRect.Height + 96));
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

    internal void PositionToolbarForm()
    {
        if (_toolbarForm is null) return;
        var uiBounds = GetOverlayUiBounds();
        if (uiBounds.IsEmpty)
            uiBounds = InflateForRepaint(_toolbarRect, 24);

        int marginX = IsVerticalDock ? 120 : 260;
        int marginY = IsVerticalDock ? 120 : 140;
        uiBounds.Inflate(marginX, marginY);
        uiBounds.Intersect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));

        var bounds = new Rectangle(
            _virtualBounds.X + uiBounds.X,
            _virtualBounds.Y + uiBounds.Y,
            Math.Max(1, uiBounds.Width),
            Math.Max(1, uiBounds.Height));
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
                RefreshToolbar();
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

        int batchSize = 4;
        int end = Math.Min(filtered.Length, _emojiWarmupIndex + batchSize);
        for (int i = _emojiWarmupIndex; i < end; i++)
            _emojiRenderer.GetEmoji(filtered[i].emoji, 22f);

        _emojiWarmupIndex = end;
        if (_emojiWarmupIndex >= filtered.Length)
            _emojiWarmupPending = false;

        Invalidate(InflateForRepaint(GetEmojiPickerBounds(), 12));
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
        _toolbarForm?.UpdateSurface();
    }

    private void RefreshOverlayUiChrome()
    {
        var oldUiBounds = _lastOverlayUiBounds;
        PositionToolbarForm();
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

    private void SetFlyoutOpen(bool open)
    {
        _flyoutOpen = open;
        _flyoutAnimStart = _flyoutAnim;
        _flyoutAnimTarget = open ? 1f : 0f;
        _flyoutAnimStartedAt = DateTime.UtcNow;
        if (!open)
            _hoveredFlyoutButton = -1;

        if (!_animTimer.Enabled)
            _animTimer.Start();

        RefreshToolbar();
        Invalidate(new Rectangle(_toolbarRect.X - 12, _toolbarRect.Y - 48,
            _toolbarRect.Width + 24, _toolbarRect.Height + 160));
    }

    private void HideToolbarImmediately()
    {
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

    private void EnsureCrosshairForms()
    {
        if (_verticalCrosshairForm != null && _horizontalCrosshairForm != null)
            return;

        var color = Color.FromArgb(72, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B);
        if (_verticalCrosshairForm == null)
        {
            _verticalCrosshairForm = new CrosshairGuideForm(color);
            var _ = _verticalCrosshairForm.Handle;
            WindowDetector.RegisterIgnoredWindow(_verticalCrosshairForm.Handle);
        }

        if (_horizontalCrosshairForm == null)
        {
            _horizontalCrosshairForm = new CrosshairGuideForm(color);
            var _ = _horizontalCrosshairForm.Handle;
            WindowDetector.RegisterIgnoredWindow(_horizontalCrosshairForm.Handle);
        }
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

        EnsureCrosshairForms();
        if (_verticalCrosshairForm is null || _horizontalCrosshairForm is null)
            return;

        int screenX = _virtualBounds.X + point.X;
        int screenY = _virtualBounds.Y + point.Y;
        _verticalCrosshairForm.UpdateLine(new Rectangle(screenX - 1, _virtualBounds.Top, 3, _virtualBounds.Height));
        _horizontalCrosshairForm.UpdateLine(new Rectangle(_virtualBounds.Left, screenY - 1, _virtualBounds.Width, 3));
    }

    private void ClearCrosshairGuides()
    {
        _verticalCrosshairForm?.Hide();
        _horizontalCrosshairForm?.Hide();
    }

    private void Cancel()
    {
        if (_cancelRequested)
            return;

        _cancelRequested = true;
        _allowDeactivation = true;
        try { HideTextBox(); } catch { }
        try { HideEmojiSearchBox(); } catch { }
        try { HideFontSearchBox(); } catch { }
        try { CloseMagWindow(); } catch { }
        try { CloseCaptureMagnifier(); } catch { }
        try { ClearCrosshairGuides(); } catch { }
        SelectionCancelled?.Invoke();
    }

    internal void CancelFromShortcut() => Cancel();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_currentOverlay == this)
                _currentOverlay = null;
            ClearCrosshairGuides();
            if (_verticalCrosshairForm != null)
                WindowDetector.UnregisterIgnoredWindow(_verticalCrosshairForm.Handle);
            _verticalCrosshairForm?.Close();
            _verticalCrosshairForm?.Dispose();
            if (_horizontalCrosshairForm != null)
                WindowDetector.UnregisterIgnoredWindow(_horizontalCrosshairForm.Handle);
            _horizontalCrosshairForm?.Close();
            _horizontalCrosshairForm?.Dispose();
            WindowDetector.UnregisterIgnoredWindow(Handle);
            WindowDetector.ClearSnapshot();
            if (_toolbarForm != null)
                WindowDetector.UnregisterIgnoredWindow(_toolbarForm.Handle);
            CloseMagWindow();
            CloseCaptureMagnifier();
            _toolbarForm?.Close();
            _toolbarForm?.Dispose();
            _animTimer.Dispose();
            _pickerTimer.Dispose();
            _autoDetectTimer.Dispose();
            _magGfx.Dispose();
            _magBitmap.Dispose();
            _committedAnnotationsBitmap?.Dispose();
            _hexFont.Dispose();
            _rgbFont.Dispose();
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
