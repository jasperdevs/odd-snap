using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Native;
using Yoink.Services;

namespace Yoink.Capture;

/// <summary>
/// Two-phase scrolling capture:
/// 1. User selects a region on a fullscreen overlay.
/// 2. Overlay hides, then repeatedly captures the region and sends scroll-down
///    events until no new content is detected. All frames are stitched into a
///    single tall image.
/// </summary>
public sealed class ScrollingCaptureForm : Form
{
    public event Action<Bitmap>? CaptureCompleted;
    public event Action? CaptureCancelled;

    private enum State { Selecting, Scrolling, Done }

    private readonly Bitmap _screenshot;
    private readonly Rectangle _virtualBounds;
    private State _state = State.Selecting;

    // Selection
    private bool _isDragging;
    private Point _dragStart;
    private Rectangle _selection;

    // Scrolling capture
    private readonly List<Bitmap> _frames = new();
    private Rectangle _screenRegion;
    private int _scrollCount;
    private int _duplicateCount;
    private const int MaxScrolls = 80;
    private const int ScrollDelayMs = 300;
    private const int ScrollAmount = 120; // one notch = 120 WHEEL_DELTA units
    private const int MatchStripHeight = 48;
    private const double DuplicateThreshold = 0.985;

    // Background worker thread for scrolling (avoids blocking UI/message pump)
    private Thread? _scrollThread;
    private volatile bool _cancelRequested;

    // Cached GDI objects
    private readonly SolidBrush _dimBrush = new(Color.FromArgb(100, 0, 0, 0));
    private readonly Pen _selPen = new(Color.FromArgb(220, 100, 149, 237), 2f) { DashStyle = DashStyle.Dash };
    private readonly Font _labelFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _hintFont = new("Segoe UI", 13f);
    private readonly SolidBrush _hintBrush = new(Color.FromArgb(140, 255, 255, 255));
    private readonly SolidBrush _bgLabelBrush = new(Color.FromArgb(220, 24, 24, 24));
    private readonly SolidBrush _textLabelBrush = new(Color.FromArgb(220, 100, 149, 237));

    public ScrollingCaptureForm(Bitmap screenshot, Rectangle virtualBounds)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualBounds.X, virtualBounds.Y, virtualBounds.Width, virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
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
            _cancelRequested = true;
            if (_state == State.Selecting)
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
                StartScrollingCapture();
            else
                Invalidate();
        }
    }

    // ─── Scrolling capture ──────────────────────────────────────────

    private void StartScrollingCapture()
    {
        _state = State.Scrolling;
        _scrollCount = 0;
        _duplicateCount = 0;
        _cancelRequested = false;

        _screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        // Hide overlay so we can capture the actual content
        Hide();

        // Run scrolling on a background thread to avoid blocking the message pump
        _scrollThread = new Thread(ScrollCaptureLoop) { IsBackground = true, Name = "ScrollCapture" };
        _scrollThread.Start();
    }

    private void ScrollCaptureLoop()
    {
        try
        {
            // Wait for overlay to fully hide
            Thread.Sleep(200);

            // Capture first frame
            _frames.Add(ScreenCapture.CaptureRegion(_screenRegion));

            while (_scrollCount < MaxScrolls && !_cancelRequested)
            {
                // Send scroll at center of region
                int cx = _screenRegion.X + _screenRegion.Width / 2;
                int cy = _screenRegion.Y + _screenRegion.Height / 2;
                SendMouseScroll(cx, cy, -ScrollAmount);

                // Wait for content to render after scroll
                Thread.Sleep(ScrollDelayMs);
                if (_cancelRequested) break;

                // Capture new frame
                var frame = ScreenCapture.CaptureRegion(_screenRegion);
                _frames.Add(frame);
                _scrollCount++;

                // Check if we've hit the bottom (consecutive duplicate detection)
                if (_frames.Count >= 2)
                {
                    var prev = _frames[^2];
                    var curr = _frames[^1];
                    if (AreFramesDuplicate(prev, curr))
                    {
                        _frames.RemoveAt(_frames.Count - 1);
                        curr.Dispose();
                        _duplicateCount++;
                        // Require 2 consecutive duplicates to confirm we're at the bottom
                        // (handles cases where content briefly looks the same during animations)
                        if (_duplicateCount >= 2)
                            break;
                    }
                    else
                    {
                        _duplicateCount = 0;
                    }
                }
            }
        }
        catch
        {
            // Capture failed
        }

        // Finish on UI thread
        try
        {
            BeginInvoke(new Action(FinishCapture));
        }
        catch
        {
            // Form may be disposed
        }
    }

    private void FinishCapture()
    {
        if (_cancelRequested || _frames.Count == 0)
        {
            Cancel();
            return;
        }

        if (_frames.Count == 1)
        {
            var clone = new Bitmap(_frames[0]);
            DisposeFrames();
            SoundService.PlayCaptureSound();
            CaptureCompleted?.Invoke(clone);
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
        Close();
    }

    private void Cancel()
    {
        _cancelRequested = true;
        DisposeFrames();
        CaptureCancelled?.Invoke();
        Close();
    }

    // ─── Frame stitching ────────────────────────────────────────────

    private Bitmap? StitchFrames()
    {
        if (_frames.Count == 0) return null;
        if (_frames.Count == 1) return new Bitmap(_frames[0]);

        int frameW = _frames[0].Width;
        int frameH = _frames[0].Height;
        int stripH = Math.Min(MatchStripHeight, frameH / 4);

        // For each consecutive pair, find the overlap and compute Y offset
        var yPositions = new List<int> { 0 };
        int runningY = 0;

        for (int i = 1; i < _frames.Count; i++)
        {
            int overlap = FindOverlap(_frames[i - 1], _frames[i], stripH);
            int newContent = frameH - overlap;
            if (newContent <= 0) newContent = 1;
            runningY += newContent;
            yPositions.Add(runningY);
        }

        int totalHeight = runningY + frameH;

        // Cap at 32000 pixels tall (GDI+ limit is 65535 but let's be safe)
        if (totalHeight > 32000)
            totalHeight = 32000;

        var result = new Bitmap(frameW, totalHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;

        for (int i = 0; i < _frames.Count; i++)
        {
            int y = yPositions[i];
            if (y >= totalHeight) break;
            int drawH = Math.Min(frameH, totalHeight - y);
            g.DrawImage(_frames[i], new Rectangle(0, y, frameW, drawH),
                new Rectangle(0, 0, frameW, drawH), GraphicsUnit.Pixel);
        }

        return result;
    }

    /// <summary>
    /// Finds the vertical overlap between two frames by sliding a horizontal strip
    /// from the bottom of the previous frame over the top of the current frame.
    /// </summary>
    private static int FindOverlap(Bitmap prev, Bitmap curr, int stripHeight)
    {
        int w = Math.Min(prev.Width, curr.Width);
        int h = Math.Min(prev.Height, curr.Height);
        if (w <= 0 || h <= 0) return 0;

        var prevData = prev.LockBits(new Rectangle(0, 0, prev.Width, prev.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currData = curr.LockBits(new Rectangle(0, 0, curr.Width, curr.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int bestOverlap = 0;
            double bestScore = 0;

            // Test overlaps from large to small, coarse then fine
            int maxOverlap = (int)(h * 0.85);
            int minOverlap = Math.Min(stripHeight, h / 8);

            // Coarse pass: step by 8 pixels to find approximate overlap
            int coarseBest = 0;
            double coarseBestScore = 0;
            for (int overlap = maxOverlap; overlap >= minOverlap; overlap -= 8)
            {
                double score = CompareRegions(prevData, currData, w,
                    prev.Height - overlap, 0, Math.Min(stripHeight, overlap));
                if (score > coarseBestScore)
                {
                    coarseBestScore = score;
                    coarseBest = overlap;
                }
                if (score > 0.998) break;
            }

            // Fine pass: refine around the coarse best
            if (coarseBestScore > 0.9)
            {
                int lo = Math.Max(minOverlap, coarseBest - 10);
                int hi = Math.Min(maxOverlap, coarseBest + 10);
                for (int overlap = hi; overlap >= lo; overlap--)
                {
                    double score = CompareRegions(prevData, currData, w,
                        prev.Height - overlap, 0, Math.Min(stripHeight, overlap));

                    if (score > DuplicateThreshold)
                    {
                        // Verify with a second strip deeper in the overlap
                        int midCheck = overlap / 2;
                        if (midCheck > stripHeight)
                        {
                            double midScore = CompareRegions(prevData, currData, w,
                                prev.Height - overlap + midCheck, midCheck,
                                Math.Min(stripHeight, overlap - midCheck));
                            score = (score + midScore) / 2.0;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestOverlap = overlap;
                        }
                        if (score > 0.998) break;
                    }
                }
            }

            return bestOverlap;
        }
        finally
        {
            prev.UnlockBits(prevData);
            curr.UnlockBits(currData);
        }
    }

    /// <summary>Compares a horizontal strip between two locked bitmaps. Returns 0-1 similarity.</summary>
    private static unsafe double CompareRegions(BitmapData prevData, BitmapData currData,
        int width, int prevY, int currY, int height)
    {
        if (height <= 0) return 0;

        int matches = 0;
        int total = 0;
        int step = Math.Max(1, width / 80);

        for (int row = 0; row < height; row++)
        {
            int py = prevY + row;
            int cy = currY + row;
            if (py < 0 || py >= prevData.Height || cy < 0 || cy >= currData.Height) continue;

            byte* prevRow = (byte*)prevData.Scan0 + py * prevData.Stride;
            byte* currRow = (byte*)currData.Scan0 + cy * currData.Stride;

            for (int x = 0; x < width; x += step)
            {
                int off = x * 4;
                total++;
                int dr = prevRow[off + 2] - currRow[off + 2];
                int dg = prevRow[off + 1] - currRow[off + 1];
                int db = prevRow[off] - currRow[off];
                if (dr * dr + dg * dg + db * db < 100)
                    matches++;
            }
        }

        return total > 0 ? (double)matches / total : 0;
    }

    private static bool AreFramesDuplicate(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;

        var aData = a.LockBits(new Rectangle(0, 0, a.Width, a.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bData = b.LockBits(new Rectangle(0, 0, b.Width, b.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            return CompareRegions(aData, bData, a.Width, 0, 0, a.Height) > DuplicateThreshold;
        }
        finally
        {
            a.UnlockBits(aData);
            b.UnlockBits(bData);
        }
    }

    // ─── Mouse scroll via SendInput ─────────────────────────────────

    [DllImport("user32.dll")]
    private static extern void SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private static void SendMouseScroll(int screenX, int screenY, int delta)
    {
        SetCursorPos(screenX, screenY);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = delta,
                    dwFlags = MOUSEEVENTF_WHEEL
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // ─── Paint ───────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_state == State.Selecting)
            PaintSelectionPhase(e.Graphics);
    }

    private void PaintSelectionPhase(Graphics g)
    {
        g.DrawImage(_screenshot, 0, 0);
        g.FillRectangle(_dimBrush, 0, 0, Width, Height);

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

    private void DisposeFrames()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelRequested = true;
            _scrollThread?.Join(2000);
            DisposeFrames();
            _screenshot.Dispose();
            _dimBrush.Dispose();
            _selPen.Dispose();
            _labelFont.Dispose();
            _hintFont.Dispose();
            _hintBrush.Dispose();
            _bgLabelBrush.Dispose();
            _textLabelBrush.Dispose();
        }
        base.Dispose(disposing);
    }
}
