using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Native;
using OddSnap.Services;

namespace OddSnap.Capture;

/// <summary>
/// Two-phase scrolling capture:
/// 1. User selects a region on a fullscreen overlay.
/// 2. Overlay hides and a floating control bar appears. User clicks Start,
///    then scrolls the content. Automatic mode captures useful stable scroll deltas;
///    manual mode captures only when the user presses the frame button.
///    User clicks Stop (or presses Escape) when done.
/// 3. Captured frames are stitched into a single tall image via overlap detection.
/// </summary>
public sealed partial class ScrollingCaptureForm : Form
{
    public event Action<Bitmap>? CaptureCompleted;
    public event Action? CaptureCancelled;
    public event Action<string>? CaptureFailed;

    private enum State { Selecting, Capturing, Stitching, Done }

    private Bitmap? _screenshot;
    private readonly Rectangle _virtualBounds;
    private readonly bool _showCursor;
    private readonly ScrollingCaptureMode _captureMode;
    private State _state = State.Selecting;

    // Selection
    private bool _isDragging;
    private Point _dragStart;
    private Point _selectionCursor;
    private Rectangle _selection;

    // Capture
    private Rectangle _screenRegion;
    private const int CaptureIntervalMs = 100;
    private const int MatchStripHeight = 48;
    private const int MinimumAutoNewContentPixels = 24;
    private const double DuplicateThreshold = 0.985;
    private int _initialCaptureFailures;
    private string? _initialCaptureFailureMessage;
    private Bitmap? _pendingAutoFrame;
    private Bitmap? _stitchedResult;
    private Bitmap? _previousCapturedFrame;
    private int _frameCount;
    private int _bestMatchCount;
    private int _bestMatchIndex;
    private int _bestIgnoreBottomOffset;
    private enum FrameCaptureResult { Accepted, Pending, Duplicate, Rejected, Failed }

    // Control bar
    private CaptureControlBar? _controlBar;
    private System.Windows.Forms.Timer? _captureTimer;
    private CaptureEscapeKeyHook? _escapeHook;

    // Magnifier
    private readonly bool _showMagnifier;
    private readonly CaptureMagnifierHelper? _magHelper;
    private LiveSelectionAdornerForm? _selectionAdorner;

    // Cached GDI objects for selection overlay
    private readonly Font _readoutFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _hintBrush = new(UiChrome.SurfaceTextMuted);

    public ScrollingCaptureForm(Bitmap? screenshot, Rectangle virtualBounds, bool showCursor = false,
                                bool showMagnifier = false,
                                ScrollingCaptureMode captureMode = ScrollingCaptureMode.Automatic)
    {
        OddSnap.UI.Theme.Refresh();
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _showCursor = showCursor;
        _captureMode = Enum.IsDefined(captureMode) ? captureMode : ScrollingCaptureMode.Automatic;
        _showMagnifier = showMagnifier;
        if (_showMagnifier && screenshot is not null)
        {
            _magHelper = new CaptureMagnifierHelper();
            _magHelper.CachePixelData(screenshot);
        }

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualBounds.X, virtualBounds.Y, virtualBounds.Width, virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = UiChrome.SurfaceWindowBackground;
        if (screenshot is null)
        {
            Opacity = 0.01;
            _selectionAdorner = new LiveSelectionAdornerForm(_virtualBounds, "Drag to select scrolling area");
        }
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        CaptureWindowExclusion.Apply(this);
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(Handle);
        Activate();
        Focus();
        _escapeHook = CaptureEscapeKeyHook.Install(this, HandleEscape);
        _selectionAdorner?.Show(this);
    }

    // ─── Input ───────────────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            HandleEscape();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            HandleEscape();
            return;
        }

        base.OnKeyDown(e);
    }

    private void HandleEscape()
    {
        if (_state == State.Capturing && _frameCount > 1)
            StopCapturing();
        else
            Cancel();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_state == State.Selecting && e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragStart = e.Location;
            _selectionCursor = e.Location;
            _selection = Rectangle.Empty;
            UpdateLiveSelectionAdorner();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_state == State.Selecting)
        {
            if (_isDragging)
            {
                var oldSelection = _selection;
                var oldCursor = _selectionCursor;
                _selection = NormRect(_dragStart, e.Location);
                _selectionCursor = e.Location;
                UpdateLiveSelectionAdorner();
                InvalidateSelectionChrome(oldSelection, oldCursor, _selection, e.Location);
            }
            _magHelper?.Update(e.Location, this, _virtualBounds, _isDragging ? GetMagnifierAvoidBounds() : Rectangle.Empty);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging && e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _selection = NormRect(_dragStart, e.Location);
            _selectionCursor = e.Location;
            UpdateLiveSelectionAdorner();
            if (_selection.Width > 20 && _selection.Height > 20)
                ShowControlBar();
            else
                Invalidate();
        }
    }

    // ─── Control bar — starts capturing instantly (same as recording) ──

    private static readonly Color TransKey = Color.FromArgb(1, 2, 3);

    private void ShowControlBar()
    {
        Visible = false;
        _magHelper?.Close();
        _selectionAdorner?.Close();
        _selectionAdorner?.Dispose();
        _selectionAdorner = null;
        ReleaseSelectionPreview();
        _screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        // Keep the fullscreen selector hidden while capturing so it cannot flash over the page.
        Opacity = 1;
        BackColor = TransKey;
        TransparencyKey = TransKey;

        _controlBar = new CaptureControlBar(_screenRegion, _captureMode);
        _controlBar.StopClicked += () => StopCapturing();
        _controlBar.CancelClicked += () => Cancel();
        _controlBar.ManualFrameClicked += () => CaptureFrame(forceAccept: true);
        _controlBar.Show();

        _state = State.Capturing;
        Services.SoundService.PlayRecordStartSound();

        CaptureFrame(forceAccept: true);
        if (_captureMode == ScrollingCaptureMode.Automatic)
            StartAutomaticTimer();
    }

    private void StartAutomaticTimer()
    {
        if (_captureTimer is not null)
            return;

        _captureTimer = new System.Windows.Forms.Timer { Interval = CaptureIntervalMs };
        _captureTimer.Tick += (_, _) => CaptureFrame(forceAccept: false);
        _captureTimer.Start();
    }

    private void StopAutomaticTimer()
    {
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        _captureTimer = null;
    }

    private FrameCaptureResult CaptureFrame(bool forceAccept)
    {
        try
        {
            var frame = ScreenCapture.CaptureRegion(_screenRegion, _showCursor);

            if (forceAccept || _captureMode == ScrollingCaptureMode.Manual)
            {
                ClearPendingAutoFrame();
                return TryAcceptFrame(frame, forceAccept);
            }

            return ProcessAutomaticFrame(frame);
        }
        catch (Exception ex)
        {
            // Capture can fail transiently; skip this tick.
            // If we never captured a frame at all, surface a failure instead of a silent cancel.
            if (_frameCount == 0 && _state == State.Capturing)
            {
                _initialCaptureFailures++;
                if (string.IsNullOrWhiteSpace(_initialCaptureFailureMessage))
                    _initialCaptureFailureMessage = string.IsNullOrWhiteSpace(ex.Message)
                        ? "Unable to capture this region."
                        : ex.Message;

                // After a few consecutive failures, stop and report a failure to the user.
                if (_initialCaptureFailures >= 3)
                    Fail(_initialCaptureFailureMessage);
            }

            return FrameCaptureResult.Failed;
        }
    }

    private FrameCaptureResult ProcessAutomaticFrame(Bitmap frame)
    {
        if (_previousCapturedFrame is not null && AreFramesDuplicate(_previousCapturedFrame, frame))
        {
            ClearPendingAutoFrame();
            frame.Dispose();
            return FrameCaptureResult.Duplicate;
        }

        if (ShouldKeepFrame(frame, forceAccept: false))
        {
            ClearPendingAutoFrame();
            return TryAcceptFrame(frame, forceAccept: false);
        }

        _pendingAutoFrame?.Dispose();
        _pendingAutoFrame = frame;
        return FrameCaptureResult.Pending;
    }

    private FrameCaptureResult TryAcceptFrame(Bitmap frame, bool forceAccept)
    {
        if (_stitchedResult is null)
        {
            AcceptFirstFrame(frame);
            return FrameCaptureResult.Accepted;
        }

        if (_previousCapturedFrame is not null && AreFramesDuplicate(_previousCapturedFrame, frame))
        {
            frame.Dispose();
            return FrameCaptureResult.Duplicate;
        }

        var match = TryAppendScrollingFrame(_stitchedResult, frame, _bestMatchCount, _bestMatchIndex, _bestIgnoreBottomOffset);
        if (!match.Success)
        {
            frame.Dispose();
            return FrameCaptureResult.Rejected;
        }

        int minimumNewContent = forceAccept || _captureMode == ScrollingCaptureMode.Manual
            ? 1
            : Math.Max(MinimumAutoNewContentPixels, frame.Height / 20);
        if (match.NewContentHeight < minimumNewContent)
        {
            match.Image?.Dispose();
            frame.Dispose();
            return FrameCaptureResult.Rejected;
        }

        bool usedBestGuess = match.UsedBestGuess;
        if (!usedBestGuess)
        {
            _bestMatchCount = Math.Max(_bestMatchCount, match.MatchCount);
            _bestMatchIndex = match.MatchIndex;
            _bestIgnoreBottomOffset = match.IgnoreBottomOffset;
        }

        _stitchedResult.Dispose();
        _stitchedResult = match.Image;
        ReplacePreviousCapturedFrame(frame);
        _frameCount++;
        if (usedBestGuess)
            _controlBar?.SetStatus($"Auto: {_frameCount} frames (partial)");
        else
            _controlBar?.SetFrameCount(_frameCount);
        return FrameCaptureResult.Accepted;
    }

    private void AcceptFirstFrame(Bitmap frame)
    {
        _stitchedResult = (Bitmap)frame.Clone();
        ReplacePreviousCapturedFrame(frame);
        _frameCount = 1;
        _controlBar?.SetFrameCount(_frameCount);
    }

    private void ReplacePreviousCapturedFrame(Bitmap frame)
    {
        _previousCapturedFrame?.Dispose();
        _previousCapturedFrame = frame;
    }

    private bool ShouldKeepFrame(Bitmap frame, bool forceAccept)
    {
        if (_stitchedResult is null)
            return true;

        if (_previousCapturedFrame is not null && AreFramesDuplicate(_previousCapturedFrame, frame))
            return false;

        var match = TryFindScrollingAppend(_stitchedResult, frame, _bestMatchCount, _bestMatchIndex, _bestIgnoreBottomOffset);
        if (!match.Success)
            return false;

        int minimumNewContent = forceAccept || _captureMode == ScrollingCaptureMode.Manual
            ? 1
            : Math.Max(MinimumAutoNewContentPixels, frame.Height / 20);
        return match.NewContentHeight >= minimumNewContent;
    }

    private void StopCapturing()
    {
        StopAutomaticTimer();
        TryAcceptPendingAutoFrame();
        ClearPendingAutoFrame();
        Services.SoundService.PlayRecordStopSound();

        _state = State.Stitching;
        _controlBar?.SetStatus("Stitching...");

        FinishCapture();
    }

    private void FinishCapture()
    {
        _controlBar?.Close();
        _controlBar?.Dispose();
        _controlBar = null;

        if (_stitchedResult is null)
        {
            Fail(_initialCaptureFailureMessage ?? "No frames captured.");
            return;
        }

        if (_frameCount <= 1)
        {
            var singleFrame = _stitchedResult;
            _stitchedResult = null;
            ReleaseCaptureBitmaps();
            CaptureCompleted?.Invoke(singleFrame);
            _state = State.Done;
            Close();
            return;
        }

        var stitched = _stitchedResult;
        _stitchedResult = null;
        ReleaseCaptureBitmaps();

        if (stitched != null)
        {
            CaptureCompleted?.Invoke(stitched);
        }
        else
        {
            CaptureCancelled?.Invoke();
        }
        _state = State.Done;
        Close();
    }

    private void Fail(string message)
    {
        if (_state == State.Done) return;

        try
        {
            StopAutomaticTimer();
            ClearPendingAutoFrame();
        }
        catch { }

        try
        {
            _controlBar?.SetStatus("Capture failed");
        }
        catch { }

        try { _controlBar?.Close(); } catch { }
        try { _controlBar?.Dispose(); } catch { }
        _controlBar = null;

        ReleaseCaptureBitmaps();

        try { CaptureFailed?.Invoke(message); } catch { }
        try { CaptureCancelled?.Invoke(); } catch { }
        _state = State.Done;
        try { Close(); } catch { }
    }

    private void Cancel()
    {
        StopAutomaticTimer();
        ClearPendingAutoFrame();
        _controlBar?.Close();
        _controlBar?.Dispose();
        _controlBar = null;
        ReleaseCaptureBitmaps();
        CaptureCancelled?.Invoke();
        _state = State.Done;
        Close();
    }

    // ─── Paint ───────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_state == State.Selecting)
            PaintSelectionPhase(e.Graphics);
        else if (_state == State.Capturing)
            PaintCapturingPhase(e.Graphics);
    }

    private void PaintCapturingPhase(Graphics g)
    {
        g.Clear(TransKey);
        if (_selection.Width > 2 && _selection.Height > 2)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var borderRect = Rectangle.Inflate(_selection, 2, 2);
            SelectionFrameRenderer.DrawRectangle(g, borderRect, fill: false);
        }
    }

    private void PaintSelectionPhase(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.AssumeLinear;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (_screenshot is null)
            g.Clear(UiChrome.SurfaceWindowBackground);
        else
            g.DrawImage(_screenshot, ClientRectangle,
                new Rectangle(0, 0, _screenshot.Width, _screenshot.Height),
                GraphicsUnit.Pixel);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            SelectionFrameRenderer.DrawRectangle(g, _selection);
        }
        else
        {
            string hint = "Drag to select scrolling area";
            var hintSz = g.MeasureString(hint, _hintFont);
            g.DrawString(hint, _hintFont, _hintBrush,
                Width / 2f - hintSz.Width / 2f, Height / 2f - hintSz.Height / 2f);
        }

    }

    private void UpdateLiveSelectionAdorner()
    {
        if (_selectionAdorner is null)
            return;

        _selectionAdorner.SetSelection(_selection, PointToClient(Cursor.Position));
    }

    private Rectangle GetMagnifierAvoidBounds()
    {
        if (_selection.Width <= 2 || _selection.Height <= 2)
            return Rectangle.Empty;

        var readoutBounds = SelectionSizeReadout.GetBounds(
            PointToClient(Cursor.Position),
            _selection,
            _readoutFont,
            ClientRectangle);
        return readoutBounds.IsEmpty
            ? _selection
            : Rectangle.Union(_selection, InflateForRepaint(readoutBounds, 8));
    }

    private void InvalidateSelectionChrome(Rectangle oldSelection, Point oldCursor, Rectangle newSelection, Point newCursor)
    {
        var oldDirty = GetSelectionChromeBounds(oldSelection, oldCursor);
        var newDirty = GetSelectionChromeBounds(newSelection, newCursor);

        if (!oldDirty.IsEmpty && !newDirty.IsEmpty)
            Invalidate(Rectangle.Union(oldDirty, newDirty));
        else if (!oldDirty.IsEmpty)
            Invalidate(oldDirty);
        else if (!newDirty.IsEmpty)
            Invalidate(newDirty);
    }

    private Rectangle GetSelectionChromeBounds(Rectangle selection, Point cursor)
    {
        if (selection.Width <= 2 || selection.Height <= 2)
            return Rectangle.Empty;

        var dirty = InflateForRepaint(selection, 16);
        var readoutBounds = SelectionSizeReadout.GetBounds(
            cursor,
            selection,
            _readoutFont,
            ClientRectangle);
        if (!readoutBounds.IsEmpty)
            dirty = Rectangle.Union(dirty, InflateForRepaint(readoutBounds, 10));

        return dirty;
    }

    private static Rectangle NormRect(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static Rectangle InflateForRepaint(Rectangle rect, int pad = 8)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return Rectangle.Empty;
        rect.Inflate(pad, pad);
        return rect;
    }

    private void ReleaseCaptureBitmaps()
    {
        _stitchedResult?.Dispose();
        _stitchedResult = null;
        _previousCapturedFrame?.Dispose();
        _previousCapturedFrame = null;
        _frameCount = 0;
        _bestMatchCount = 0;
        _bestMatchIndex = 0;
        _bestIgnoreBottomOffset = 0;
    }

    private void ClearPendingAutoFrame()
    {
        _pendingAutoFrame?.Dispose();
        _pendingAutoFrame = null;
    }

    private void TryAcceptPendingAutoFrame()
    {
        if (_pendingAutoFrame is null)
            return;

        var frame = _pendingAutoFrame;
        _pendingAutoFrame = null;
        TryAcceptFrame(frame, forceAccept: false);
    }

    private void ReleaseSelectionPreview()
    {
        _screenshot?.Dispose();
        _screenshot = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _magHelper?.Dispose();
            _escapeHook?.Dispose();
            _escapeHook = null;
            _selectionAdorner?.Dispose();
            _selectionAdorner = null;
            _captureTimer?.Stop();
            _captureTimer?.Dispose();
            _controlBar?.Dispose();
            ClearPendingAutoFrame();
            ReleaseCaptureBitmaps();
            ReleaseSelectionPreview();
            _readoutFont.Dispose();
            _hintFont.Dispose();
            _hintBrush.Dispose();
        }
        base.Dispose(disposing);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Floating control bar that appears near the selected region
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Floating control bar that uses the shared dock chrome.
    /// </summary>
    private sealed class CaptureControlBar : Form
    {
        public event Action? StopClicked;
        public event Action? CancelClicked;
        public event Action? ManualFrameClicked;

        private static int AutoBarWidth => UiChrome.ScaleInt(320);
        private static int ManualBarWidth => UiChrome.ScaleInt(370);
        private static int BarHeight => WindowsDockRenderer.SurfaceHeight;
        private static int CornerR => WindowsDockRenderer.SurfaceRadius;

        private readonly ScrollingCaptureMode _mode;
        private int _frameCount;
        private string _status;

        // Cached GDI objects
        private readonly Font _statusFont = UiChrome.ChromeFont(10f, FontStyle.Bold);
        private readonly Font _btnFont = UiChrome.ChromeFont(9.5f, FontStyle.Bold);

        // Button hit-test rects
        private Rectangle _manualFrameBtnRect;
        private Rectangle _actionBtnRect;
        private Rectangle _cancelBtnRect;
        private Rectangle? _hoveredBtn;
        private Rectangle _statusRect;

        public CaptureControlBar(Rectangle captureRegion, ScrollingCaptureMode mode)
        {
            _mode = mode;
            _status = mode == ScrollingCaptureMode.Automatic ? "Auto: scroll now" : "Manual: click capture";

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            int barWidth = mode == ScrollingCaptureMode.Manual ? ManualBarWidth : AutoBarWidth;
            Size = new Size(barWidth, BarHeight);
            BackColor = UiChrome.SurfaceWindowBackground;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Default;

            int x = captureRegion.X + (captureRegion.Width - barWidth) / 2;
            var offset = UiChrome.ScaleInt(12);
            int y = captureRegion.Y - BarHeight - offset;
            if (y < 0) y = captureRegion.Bottom + offset;
            var margin = UiChrome.ScaleInt(4);
            Location = new Point(Math.Max(margin, x), Math.Max(margin, y));

            Region = CreateRoundedRegion(barWidth, BarHeight, CornerR);

            int btnY = (BarHeight - WindowsDockRenderer.IconButtonSize) / 2;
            _cancelBtnRect = new Rectangle(barWidth - WindowsDockRenderer.SurfacePadding - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
            _actionBtnRect = new Rectangle(_cancelBtnRect.X - WindowsDockRenderer.ButtonSpacing - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
            _manualFrameBtnRect = mode == ScrollingCaptureMode.Manual
                ? new Rectangle(_actionBtnRect.X - WindowsDockRenderer.ButtonSpacing - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize)
                : Rectangle.Empty;
            int firstButtonX = _manualFrameBtnRect.IsEmpty ? _actionBtnRect.X : _manualFrameBtnRect.X;
            _statusRect = new Rectangle(UiChrome.ScaleInt(16), 0, firstButtonX - UiChrome.ScaleInt(24), BarHeight);
        }

        public void SetFrameCount(int count)
        {
            if (InvokeRequired) { BeginInvoke(() => SetFrameCount(count)); return; }
            _frameCount = count;
            _status = FormatFrameStatus(count);
            Invalidate(_statusRect);
        }

        public void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
            _status = text;
            Invalidate(_statusRect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var barRect = new RectangleF(0, 0, Width, Height);
            WindowsDockRenderer.PaintSurface(g, barRect, CornerR);

            using var statusBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            var statusRect = new RectangleF(UiChrome.ScaleInt(16), 0, _statusRect.Width, Height);
            var statusFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(_status, _statusFont, statusBrush, statusRect, statusFmt);

            if (!_manualFrameBtnRect.IsEmpty)
            {
                DrawIconBtn(g, _manualFrameBtnRect, "record", UiChrome.SurfaceTextPrimary,
                    _hoveredBtn == _manualFrameBtnRect, active: true);
            }
            DrawIconBtn(g, _actionBtnRect, "stopSquare", UiChrome.SurfaceTextPrimary, _hoveredBtn == _actionBtnRect, active: false);
            DrawIconBtn(g, _cancelBtnRect, "close", UiChrome.SurfaceTextPrimary, _hoveredBtn == _cancelBtnRect, active: false);
        }

        private void DrawIconBtn(Graphics g, Rectangle r, string iconId, Color color, bool hovered, bool active)
        {
            WindowsDockRenderer.PaintButton(g, r, active, hovered);
            int alpha = active ? 255 : hovered ? 240 : 200;
            WindowsDockRenderer.PaintIcon(g, iconId, r, Color.FromArgb(alpha, color.R, color.G, color.B), active);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Rectangle? prev = _hoveredBtn;
            if (!_manualFrameBtnRect.IsEmpty && _manualFrameBtnRect.Contains(e.Location))
                _hoveredBtn = _manualFrameBtnRect;
            else if (_actionBtnRect.Contains(e.Location))
                _hoveredBtn = _actionBtnRect;
            else if (_cancelBtnRect.Contains(e.Location))
                _hoveredBtn = _cancelBtnRect;
            else
                _hoveredBtn = null;

            Cursor = _hoveredBtn != null ? Cursors.Hand : Cursors.Default;
            if (_hoveredBtn != prev)
            {
                if (prev.HasValue) Invalidate(prev.Value);
                if (_hoveredBtn.HasValue) Invalidate(_hoveredBtn.Value);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredBtn != null)
            {
                var prev = _hoveredBtn.Value;
                _hoveredBtn = null;
                Invalidate(prev);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!_manualFrameBtnRect.IsEmpty && _manualFrameBtnRect.Contains(e.Location))
            {
                ManualFrameClicked?.Invoke();
            }
            else if (_actionBtnRect.Contains(e.Location))
            {
                StopClicked?.Invoke();
            }
            else if (_cancelBtnRect.Contains(e.Location))
            {
                CancelClicked?.Invoke();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape)
            {
                if (_frameCount > 1)
                    StopClicked?.Invoke();
                else
                    CancelClicked?.Invoke();
                return true;
            }

            if (_mode == ScrollingCaptureMode.Manual && (key == Keys.Space || key == Keys.Enter))
            {
                ManualFrameClicked?.Invoke();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureWindowExclusion.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _statusFont.Dispose(); _btnFont.Dispose(); }
            base.Dispose(disposing);
        }

        private string FormatFrameStatus(int count)
        {
            string label = count == 1 ? "frame" : "frames";
            return _mode == ScrollingCaptureMode.Automatic
                ? $"Auto: {count} {label}"
                : $"Manual: {count} {label}";
        }

        private static Region CreateRoundedRegion(int w, int h, int r)
        {
            using var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(w - d, 0, d, d, 270, 90);
            path.AddArc(w - d, h - d, d, d, 0, 90);
            path.AddArc(0, h - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        private static GraphicsPath CreateRoundedPath(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
