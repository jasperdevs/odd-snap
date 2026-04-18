using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Native;
using OddSnap.Services;

namespace OddSnap.Capture;

/// <summary>
/// Two-phase scrolling capture (passive, ShareX-style):
/// 1. User selects a region on a fullscreen overlay.
/// 2. Overlay hides and a floating control bar appears. User clicks Start,
///    then manually scrolls the content. Frames are captured at a regular
///    interval. User clicks Stop (or presses Escape) when done.
/// 3. Captured frames are stitched into a single tall image via overlap detection.
/// </summary>
public sealed partial class ScrollingCaptureForm : Form
{
    public event Action<Bitmap>? CaptureCompleted;
    public event Action? CaptureCancelled;
    public event Action<string>? CaptureFailed;

    private enum State { Selecting, Capturing, Stitching, Done }

    private readonly Bitmap? _screenshot;
    private readonly Rectangle _virtualBounds;
    private readonly bool _showCursor;
    private State _state = State.Selecting;

    // Selection
    private bool _isDragging;
    private Point _dragStart;
    private Rectangle _selection;

    // Capture
    private readonly List<Bitmap> _frames = new();
    private Rectangle _screenRegion;
    private const int CaptureIntervalMs = 400;
    private const int MatchStripHeight = 48;
    private const double DuplicateThreshold = 0.985;
    private int _initialCaptureFailures;
    private string? _initialCaptureFailureMessage;

    // Control bar
    private CaptureControlBar? _controlBar;
    private System.Windows.Forms.Timer? _captureTimer;

    // Magnifier
    private readonly bool _showMagnifier;
    private readonly CaptureMagnifierHelper? _magHelper;
    private LiveSelectionAdornerForm? _selectionAdorner;

    // Cached GDI objects for selection overlay
    private readonly Pen _selPen = new(Color.FromArgb(255, 255, 255, 255), 2.0f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 3f }, LineJoin = LineJoin.Miter };
    private readonly Font _labelFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _hintBrush = new(UiChrome.SurfaceTextMuted);
    private readonly SolidBrush _bgLabelBrush = new(UiChrome.SurfacePill);
    private readonly SolidBrush _textLabelBrush = new(UiChrome.SurfaceTextPrimary);

    public ScrollingCaptureForm(Bitmap? screenshot, Rectangle virtualBounds, bool showCursor = false,
                                bool showMagnifier = false)
    {
        OddSnap.UI.Theme.Refresh();
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _showCursor = showCursor;
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
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(Handle);
        Activate();
        Focus();
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
        if (_state == State.Capturing && _frames.Count > 1)
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
                _selection = NormRect(_dragStart, e.Location);
                UpdateLiveSelectionAdorner();
                Invalidate();
            }
            _magHelper?.Update(e.Location, this, _virtualBounds);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging && e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _selection = NormRect(_dragStart, e.Location);
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
        _screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        // Make the overlay transparent so the user can see content, but keep the border visible.
        Opacity = 1;
        BackColor = TransKey;
        TransparencyKey = TransKey;

        _controlBar = new CaptureControlBar(_screenRegion);
        _controlBar.StopClicked += () => StopCapturing();
        _controlBar.CancelClicked += () => Cancel();
        _controlBar.Show();
        Invalidate();
        Visible = true;

        // Start capturing immediately (like recording)
        _state = State.Capturing;
        _controlBar.SetCapturing(true);
        Services.SoundService.PlayRecordStartSound();

        CaptureFrame();

        _captureTimer = new System.Windows.Forms.Timer { Interval = CaptureIntervalMs };
        _captureTimer.Tick += (_, _) => CaptureFrame();
        _captureTimer.Start();
    }

    private void CaptureFrame()
    {
        try
        {
            var frame = ScreenCapture.CaptureRegion(_screenRegion, _showCursor);

            // Skip exact duplicates of the last frame (no scroll happened)
            if (_frames.Count > 0 && AreFramesDuplicate(_frames[^1], frame))
            {
                frame.Dispose();
                return;
            }

            _frames.Add(frame);
            _controlBar?.SetFrameCount(_frames.Count);
        }
        catch (Exception ex)
        {
            // Capture can fail transiently; skip this tick.
            // If we never captured a frame at all, surface a failure instead of a silent cancel.
            if (_frames.Count == 0 && _state == State.Capturing)
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
        }
    }

    private void StopCapturing()
    {
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        _captureTimer = null;
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

        if (_frames.Count == 0)
        {
            Fail(_initialCaptureFailureMessage ?? "No frames captured.");
            return;
        }

        if (_frames.Count == 1)
        {
            var frame = _frames[0];
            _frames.RemoveAt(0);
            DisposeFrames();
            CaptureCompleted?.Invoke(frame);
            _state = State.Done;
            Close();
            return;
        }

        var stitched = StitchFrames();
        DisposeFrames();

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
            _captureTimer?.Stop();
            _captureTimer?.Dispose();
            _captureTimer = null;
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

        DisposeFrames();

        try { CaptureFailed?.Invoke(message); } catch { }
        try { CaptureCancelled?.Invoke(); } catch { }
        _state = State.Done;
        try { Close(); } catch { }
    }

    private void Cancel()
    {
        _captureTimer?.Stop();
        _captureTimer?.Dispose();
        _captureTimer = null;
        _controlBar?.Close();
        _controlBar?.Dispose();
        _controlBar = null;
        DisposeFrames();
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
            g.DrawRectangle(_selPen, borderRect);
        }
    }

    private void PaintSelectionPhase(Graphics g)
    {
        if (_screenshot is null)
            g.Clear(UiChrome.SurfaceWindowBackground);
        else
            g.DrawImage(_screenshot, 0, 0);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            if (_screenshot is not null)
                g.DrawImage(_screenshot, _selection, _selection, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawRectangle(_selPen, _selection);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string label = $"Scroll  {_selection.Width} x {_selection.Height}";
            var sz = g.MeasureString(label, _labelFont);
            float lx = _selection.X + _selection.Width / 2f - sz.Width / 2f;
            float ly = _selection.Bottom + 6;
            if (ly + sz.Height > Height - 10) ly = _selection.Y - sz.Height - 6;
            var bgRect = new RectangleF(lx - 8, ly - 2, sz.Width + 16, sz.Height + 4);
            using var bgPath = RRect(bgRect, bgRect.Height / 2f);
            g.FillPath(_bgLabelBrush, bgPath);
            g.DrawString(label, _labelFont, _textLabelBrush, lx, ly);
        }
        else
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
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

        var label = _selection.Width > 2 && _selection.Height > 2
            ? $"Scroll  {_selection.Width} x {_selection.Height}"
            : "";
        _selectionAdorner.SetSelection(_selection, label);
    }

    private static GraphicsPath RRect(RectangleF r, float radius)
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

    private void DisposeFrames()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _magHelper?.Dispose();
            _selectionAdorner?.Dispose();
            _selectionAdorner = null;
            _captureTimer?.Stop();
            _captureTimer?.Dispose();
            _controlBar?.Dispose();
            DisposeFrames();
            _screenshot?.Dispose();
            _selPen.Dispose();
            _labelFont.Dispose();
            _hintFont.Dispose();
            _hintBrush.Dispose();
            _bgLabelBrush.Dispose();
            _textLabelBrush.Dispose();
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

        private const int BarWidth = 320;
        private const int BarHeight = WindowsDockRenderer.SurfaceHeight;
        private const int CornerR = WindowsDockRenderer.SurfaceRadius;

        private int _frameCount;
        private string _status = "Scroll now";

        // Cached GDI objects
        private readonly Font _statusFont = UiChrome.ChromeFont(10f, FontStyle.Bold);
        private readonly Font _btnFont = UiChrome.ChromeFont(9.5f, FontStyle.Bold);

        // Button hit-test rects
        private Rectangle _actionBtnRect;
        private Rectangle _cancelBtnRect;
        private Rectangle? _hoveredBtn;
        private Rectangle _statusRect;

        public CaptureControlBar(Rectangle captureRegion)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(BarWidth, BarHeight);
            BackColor = UiChrome.SurfaceWindowBackground;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Default;

            int x = captureRegion.X + (captureRegion.Width - BarWidth) / 2;
            int y = captureRegion.Y - BarHeight - 12;
            if (y < 0) y = captureRegion.Bottom + 12;
            Location = new Point(Math.Max(4, x), Math.Max(4, y));

            Region = CreateRoundedRegion(BarWidth, BarHeight, CornerR);

            // Button layout
            int btnY = (BarHeight - WindowsDockRenderer.IconButtonSize) / 2;
            _cancelBtnRect = new Rectangle(BarWidth - WindowsDockRenderer.SurfacePadding - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
            _actionBtnRect = new Rectangle(_cancelBtnRect.X - WindowsDockRenderer.ButtonSpacing - WindowsDockRenderer.IconButtonSize, btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
            _statusRect = new Rectangle(16, 0, _actionBtnRect.X - 24, BarHeight);
        }

        public void SetCapturing(bool capturing)
        {
            _status = "Scroll now";
            Invalidate(_statusRect);
        }

        public void SetFrameCount(int count)
        {
            if (InvokeRequired) { BeginInvoke(() => SetFrameCount(count)); return; }
            _frameCount = count;
            _status = $"{count} frames";
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

            // Status text — clip before buttons
            using var statusBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            int maxTextW = _actionBtnRect.X - 24;
            var statusRect = new RectangleF(16, 0, maxTextW, Height);
            var statusFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(_status, _statusFont, statusBrush, statusRect, statusFmt);

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
            if (_actionBtnRect.Contains(e.Location))
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
            if (_actionBtnRect.Contains(e.Location))
                StopClicked?.Invoke();
            else if (_cancelBtnRect.Contains(e.Location))
                CancelClicked?.Invoke();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                if (_frameCount > 1)
                    StopClicked?.Invoke();
                else
                    CancelClicked?.Invoke();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _statusFont.Dispose(); _btnFont.Dispose(); }
            base.Dispose(disposing);
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
