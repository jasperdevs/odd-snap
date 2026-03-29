using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Yoink.Native;
using Yoink.Services;

namespace Yoink.Capture;

/// <summary>
/// Two-phase form: first shows fullscreen overlay for region selection,
/// then stays fullscreen but transparent during recording to show the
/// dashed border around the capture region plus a floating toolbar.
/// </summary>
public sealed class RecordingForm : Form
{
    public event Action<string>? RecordingCompleted;
    public event Action? RecordingCancelled;

    private enum State { Selecting, Recording, Encoding }

    private readonly Bitmap _screenshot;
    private readonly Rectangle _virtualBounds;
    private State _state = State.Selecting;

    // Selection
    private bool _isDragging;
    private Point _dragStart;
    private Rectangle _selection;

    // Recording
    private GifRecorder? _recorder;
    private readonly int _fps;
    private readonly int _maxDuration;
    private System.Windows.Forms.Timer? _tickTimer;
    private readonly string _savePath;

    // Screen-relative selection (stays valid after phase change)
    private Rectangle _recordRegion; // in form coords, persisted

    // Toolbar (recording phase) - positioned relative to form
    private Rectangle _toolbarRect;
    private Rectangle _stopBtn;
    private Rectangle _discardBtn;
    private int _hoveredBtn = -1; // 0=stop, 1=discard

    // TransparencyKey color - any color that won't appear in UI
    private static readonly Color TransKey = Color.FromArgb(1, 2, 3);

    public RecordingForm(Bitmap screenshot, Rectangle virtualBounds, int fps, int maxDuration, string savePath)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _fps = fps;
        _maxDuration = maxDuration;
        _savePath = savePath;

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

    // ─── Selection phase ──────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_state == State.Recording)
            {
                DiscardRecording();
                return true;
            }
            RecordingCancelled?.Invoke();
            Close();
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
        else if (_state == State.Recording && e.Button == MouseButtons.Left)
        {
            if (_stopBtn.Contains(e.Location))
                StopRecording();
            else if (_discardBtn.Contains(e.Location))
                DiscardRecording();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging)
        {
            _selection = NormRect(_dragStart, e.Location);
            Invalidate();
        }
        else if (_state == State.Recording)
        {
            int prev = _hoveredBtn;
            _hoveredBtn = _stopBtn.Contains(e.Location) ? 0
                        : _discardBtn.Contains(e.Location) ? 1
                        : -1;
            Cursor = _hoveredBtn >= 0 ? Cursors.Hand : Cursors.Default;
            if (_hoveredBtn != prev) Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging && e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _selection = NormRect(_dragStart, e.Location);
            if (_selection.Width > 10 && _selection.Height > 10)
                StartRecording();
            else
                Invalidate();
        }
    }

    // ─── Recording lifecycle ──────────────────────────────────────────

    private void StartRecording()
    {
        _recordRegion = _selection;

        // Convert selection from form coords to screen coords
        var screenRegion = new Rectangle(
            _selection.X + _virtualBounds.X,
            _selection.Y + _virtualBounds.Y,
            _selection.Width, _selection.Height);

        _recorder = new GifRecorder(screenRegion, _fps, _maxDuration);
        _state = State.Recording;
        Cursor = Cursors.Default;

        // Switch to transparent mode: form stays fullscreen but only the
        // dashed border + toolbar are visible. Everything else is see-through.
        BackColor = TransKey;
        TransparencyKey = TransKey;

        // Exclude this form from capture so the border/toolbar don't appear in the GIF
        User32.SetWindowDisplayAffinity(Handle, User32.WDA_EXCLUDEFROMCAPTURE);

        CalcToolbarLayout();

        SoundService.PlayRecordStartSound();
        _recorder.Start();

        _tickTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _tickTimer.Tick += (_, _) =>
        {
            if (_recorder != null && !_recorder.IsRecording)
            {
                StopRecording();
                return;
            }
            Invalidate();
        };
        _tickTimer.Start();
        Invalidate();
    }

    private void StopRecording()
    {
        if (_state != State.Recording || _recorder == null) return;
        _state = State.Encoding;
        _tickTimer?.Stop();
        Invalidate();

        var recorder = _recorder;
        _recorder = null;
        var thread = new Thread(() =>
        {
            try
            {
                string outputPath = recorder.StopAndEncode(_savePath);
                recorder.Dispose();
                BeginInvoke(() =>
                {
                    SoundService.PlayRecordStopSound();
                    RecordingCompleted?.Invoke(outputPath);
                    Close();
                });
            }
            catch
            {
                recorder.Dispose();
                BeginInvoke(() =>
                {
                    RecordingCancelled?.Invoke();
                    Close();
                });
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    private void DiscardRecording()
    {
        _tickTimer?.Stop();
        _recorder?.Discard();
        _recorder?.Dispose();
        _recorder = null;
        RecordingCancelled?.Invoke();
        Close();
    }

    private void CalcToolbarLayout()
    {
        int tw = 240, th = 40;
        int tx = _recordRegion.X + _recordRegion.Width / 2 - tw / 2;
        int ty = _recordRegion.Y - th - 12;
        if (ty < 4) ty = _recordRegion.Bottom + 12;
        // Clamp to screen
        if (tx < 4) tx = 4;
        if (tx + tw > Width - 4) tx = Width - 4 - tw;
        _toolbarRect = new Rectangle(tx, ty, tw, th);

        int btnY = _toolbarRect.Y + 8;
        int btnH = 24;
        // Layout: [dot 8px] [timer 54px] [gap 8] [Stop btn 64] [gap 6] [Discard btn 72]
        _stopBtn = new Rectangle(_toolbarRect.X + 78, btnY, 64, btnH);
        _discardBtn = new Rectangle(_stopBtn.Right + 6, btnY, 72, btnH);
    }

    // ─── Paint ────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        if (_state == State.Selecting)
            PaintSelectionPhase(g);
        else
            PaintRecordingPhase(g);
    }

    private void PaintSelectionPhase(Graphics g)
    {
        g.DrawImage(_screenshot, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        g.FillRectangle(dim, 0, 0, Width, Height);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            g.DrawImage(_screenshot, _selection, _selection, GraphicsUnit.Pixel);

            using var pen = new Pen(Color.FromArgb(220, 239, 68, 68), 2f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, _selection);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string label = $"GIF  {_selection.Width} x {_selection.Height}";
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            var sz = g.MeasureString(label, font);
            float lx = _selection.X + _selection.Width / 2f - sz.Width / 2f;
            float ly = _selection.Bottom + 6;
            if (ly + sz.Height > Height - 10) ly = _selection.Y - sz.Height - 6;
            var bgRect = new RectangleF(lx - 8, ly - 2, sz.Width + 16, sz.Height + 4);
            using var bgPath = RRect(bgRect, bgRect.Height / 2f);
            using var bgBrush = new SolidBrush(Color.FromArgb(220, 24, 24, 24));
            g.FillPath(bgBrush, bgPath);
            using var textBrush = new SolidBrush(Color.FromArgb(220, 239, 68, 68));
            g.DrawString(label, font, textBrush, lx, ly);
        }
        else
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var hintFont = new Font("Segoe UI", 13f);
            string hint = "Drag to select recording area";
            var hintSz = g.MeasureString(hint, hintFont);
            using var hintBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255));
            g.DrawString(hint, hintFont, hintBrush,
                Width / 2f - hintSz.Width / 2f, Height / 2f - hintSz.Height / 2f);
        }
    }

    private void PaintRecordingPhase(Graphics g)
    {
        // Fill with transparency key - everything this color becomes click-through
        g.Clear(TransKey);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // ── Dashed recording border around the captured region ──
        var borderRect = Rectangle.Inflate(_recordRegion, 2, 2);
        using var borderPen = new Pen(Color.FromArgb(200, 239, 68, 68), 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(borderPen, borderRect);

        // Corner markers (solid red squares at each corner for visibility)
        int cm = 6;
        using var cornerBrush = new SolidBrush(Color.FromArgb(220, 239, 68, 68));
        g.FillRectangle(cornerBrush, borderRect.X - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(cornerBrush, borderRect.Right - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(cornerBrush, borderRect.X - cm / 2, borderRect.Bottom - cm / 2, cm, cm);
        g.FillRectangle(cornerBrush, borderRect.Right - cm / 2, borderRect.Bottom - cm / 2, cm, cm);

        // ── Toolbar background (rounded, dark) ──
        using (var tbPath = RRect(_toolbarRect, 12))
        {
            using var bg = new SolidBrush(Color.FromArgb(245, 24, 24, 24));
            g.FillPath(bg, tbPath);
            using var border = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
            g.DrawPath(border, tbPath);
        }

        var elapsed = _recorder?.Elapsed ?? TimeSpan.Zero;

        // ── Blinking red dot (LEFT side of toolbar) ──
        float dotX = _toolbarRect.X + 12;
        float dotY = _toolbarRect.Y + _toolbarRect.Height / 2f - 4;
        bool dotVisible = (int)(elapsed.TotalMilliseconds / 500) % 2 == 0;
        if (dotVisible)
        {
            using var dotBrush = new SolidBrush(Color.FromArgb(230, 239, 68, 68));
            g.FillEllipse(dotBrush, dotX, dotY, 8, 8);
        }
        // Dim ring always visible
        using var ringPen = new Pen(Color.FromArgb(60, 239, 68, 68), 1f);
        g.DrawEllipse(ringPen, dotX, dotY, 8, 8);

        // ── Timer (next to dot) ──
        string time = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        using var timeFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var timeBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.DrawString(time, timeFont, timeBrush, dotX + 16, _toolbarRect.Y + 11);

        // ── Stop button (red accent) ──
        DrawBtn(g, _stopBtn, "\u25A0  Stop", _hoveredBtn == 0,
            Color.FromArgb(255, 239, 68, 68), Color.FromArgb(40, 239, 68, 68));

        // ── Discard button ──
        DrawBtn(g, _discardBtn, "Discard", _hoveredBtn == 1,
            Color.FromArgb(160, 255, 255, 255), Color.FromArgb(15, 255, 255, 255));

        // ── Encoding overlay ──
        if (_state == State.Encoding)
        {
            // Cover the entire toolbar with a solid fill, then centered text
            using var encPath = RRect(_toolbarRect, 12);
            using var encBg = new SolidBrush(Color.FromArgb(250, 24, 24, 24));
            g.FillPath(encBg, encPath);
            using var encBorder = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
            g.DrawPath(encBorder, encPath);

            // Spinner dot
            float spinX = _toolbarRect.X + 14;
            float spinY = _toolbarRect.Y + _toolbarRect.Height / 2f - 4;
            using var spinBrush = new SolidBrush(Color.FromArgb(200, 239, 68, 68));
            g.FillEllipse(spinBrush, spinX, spinY, 8, 8);

            using var encFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var encBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            g.DrawString("Encoding GIF...", encFont, encBrush, spinX + 16, _toolbarRect.Y + 10);
        }
    }

    private static void DrawBtn(Graphics g, Rectangle rect, string text, bool hovered,
        Color textColor, Color bgColor)
    {
        using var path = RRect(rect, 6);
        int alpha = hovered ? Math.Clamp((int)(bgColor.A * 2.5), 0, 255) : bgColor.A;
        using var bg = new SolidBrush(Color.FromArgb(alpha, bgColor.R, bgColor.G, bgColor.B));
        g.FillPath(bg, path);
        if (hovered)
        {
            using var hBorder = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.DrawPath(hBorder, path);
        }
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rect, fmt);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tickTimer?.Dispose();
            _recorder?.Dispose();
            _screenshot.Dispose();
        }
        base.Dispose(disposing);
    }
}
