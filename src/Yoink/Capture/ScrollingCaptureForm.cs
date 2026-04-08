using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Helpers;
using Yoink.Native;
using Yoink.Services;

namespace Yoink.Capture;

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

    private readonly Bitmap _screenshot;
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

    // Cached GDI objects for selection overlay
    private readonly Pen _selPen = new(Color.FromArgb(220, 100, 149, 237), 2f) { DashStyle = DashStyle.Dash };
    private readonly Font _labelFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _hintBrush = new(UiChrome.SurfaceTextMuted);
    private readonly SolidBrush _bgLabelBrush = new(Color.FromArgb(220, 24, 24, 24));
    private readonly SolidBrush _textLabelBrush = new(Color.FromArgb(220, 100, 149, 237));

    public ScrollingCaptureForm(Bitmap screenshot, Rectangle virtualBounds, bool showCursor = false)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _showCursor = showCursor;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualBounds.X, virtualBounds.Y, virtualBounds.Width, virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = UiChrome.SurfaceWindowBackground;
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
    }

    // ─── Input ───────────────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_state == State.Capturing)
                StopCapturing();
            else
                Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_state == State.Selecting && e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragStart = e.Location;
            _selection = Rectangle.Empty;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging)
        {
            _selection = NormRect(_dragStart, e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging && e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _selection = NormRect(_dragStart, e.Location);
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
        _screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        // Make the overlay transparent so the user can see content, but keep the border visible.
        BackColor = TransKey;
        TransparencyKey = TransKey;
        Invalidate();

        _controlBar = new CaptureControlBar(_screenRegion);
        _controlBar.StopClicked += () => StopCapturing();
        _controlBar.CancelClicked += () => Cancel();
        _controlBar.Show();

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
            SoundService.PlayCaptureSound();
            CaptureCompleted?.Invoke(frame);
            _state = State.Done;
            Close();
            return;
        }

        var stitched = StitchFrames();
        DisposeFrames();

        if (stitched != null)
        {
            SoundService.PlayCaptureSound();
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
        g.DrawImage(_screenshot, 0, 0);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            g.DrawImage(_screenshot, _selection, _selection, GraphicsUnit.Pixel);
            g.DrawRectangle(_selPen, _selection);

            g.SmoothingMode = SmoothingMode.AntiAlias;
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
            _captureTimer?.Stop();
            _captureTimer?.Dispose();
            _controlBar?.Dispose();
            DisposeFrames();
            _screenshot.Dispose();
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
    /// Floating control bar matching the RecordingForm visual style:
    /// dark background, subtle white border, red accent, custom painted.
    /// </summary>
    private sealed class CaptureControlBar : Form
    {
        public event Action? StopClicked;
        public event Action? CancelClicked;

        private const int BarWidth = 320;
        private const int BarHeight = 48;
        private const int CornerR = 14;

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
            _cancelBtnRect = new Rectangle(BarWidth - 82, 10, 68, 28);
            _actionBtnRect = new Rectangle(BarWidth - 156, 10, 68, 28);
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

            // Shadow (dark rect behind, offset)
            var shadowPasses = new (float dx, float dy, int a)[]
            {
                (0f, 3f, 24),
                (0f, 1.5f, 36),
            };
            foreach (var (dx, dy, a) in shadowPasses)
            {
                using var shadowPath = CreateRoundedPath(new RectangleF(2 + dx, 4 + dy, Width - 4, Height - 2), CornerR);
                using var shadowBrush = new SolidBrush(Color.FromArgb(a, 0, 0, 0));
                g.FillPath(shadowBrush, shadowPath);
            }

            // Background
            using (var bgBrush = new SolidBrush(UiChrome.SurfacePill))
            {
                using var bgPath = CreateRoundedPath(new RectangleF(0, 0, Width, Height), CornerR);
                g.FillPath(bgBrush, bgPath);
            }

            // Subtle white border
            using (var borderPen = new Pen(UiChrome.SurfaceBorder, 1f))
            {
                using var borderPath = CreateRoundedPath(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CornerR);
                g.DrawPath(borderPen, borderPath);
            }

            // Status text — clip before buttons
            using var statusBrush = new SolidBrush(UiChrome.SurfaceTextPrimary);
            int maxTextW = _actionBtnRect.X - 24;
            var statusRect = new RectangleF(16, 0, maxTextW, Height);
            var statusFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(_status, _statusFont, statusBrush, statusRect, statusFmt);

            // Draw buttons — always Stop + Discard (starts instantly like recording)
            DrawBtn(g, _actionBtnRect, "Stop",
                Color.FromArgb(239, 68, 68), UiChrome.SurfaceTextPrimary, _hoveredBtn == _actionBtnRect);
            DrawBtn(g, _cancelBtnRect, "Discard",
                UiChrome.SurfaceHover, UiChrome.SurfaceTextPrimary, _hoveredBtn == _cancelBtnRect);
        }

        private void DrawBtn(Graphics g, Rectangle r, string text, Color bg, Color fg, bool hovered)
        {
            int alpha = hovered ? (int)(bg.A * 2.5) : bg.A;
            alpha = Math.Min(255, alpha);
            using var brush = new SolidBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
            using var path = CreateRoundedPath(new RectangleF(r.X, r.Y, r.Width, r.Height), 8);
            g.FillPath(brush, path);

            if (hovered)
            {
                using var hoverBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                g.DrawPath(hoverBorder, path);
            }

            using var textBrush = new SolidBrush(fg);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, _btnFont, textBrush, r, sf);
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
            if (keyData == Keys.Escape)
            {
                StopClicked?.Invoke();
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
