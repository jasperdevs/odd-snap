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
    /// <summary>Fires with (filePath, firstFrameBitmap). Caller must dispose the bitmap.</summary>
    public event Action<string, Bitmap?>? RecordingCompleted;
    public event Action? RecordingCancelled;

    /// <summary>Static reference to the current recording form for external stop control.</summary>
    public static RecordingForm? Current { get; private set; }

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
    private VideoRecorder? _videoRecorder;
    private readonly int _fps;
    private readonly int _maxDuration;
    private readonly Models.RecordingFormat _format;
    private readonly int _maxHeight;
    private readonly bool _recordMic;
    private readonly string? _micDeviceId;
    private readonly bool _recordDesktop;
    private readonly string? _desktopDeviceId;
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

    // Cached GDI objects for paint
    private readonly SolidBrush _dimBrush = new(Color.FromArgb(100, 0, 0, 0));
    private readonly Pen _selPen = new(Color.FromArgb(220, 239, 68, 68), 2f) { DashStyle = DashStyle.Dash };
    private readonly Font _labelFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _hintFont = new("Segoe UI", 13f);
    private readonly SolidBrush _hintBrush = new(Color.FromArgb(140, 255, 255, 255));
    private readonly SolidBrush _bgLabelBrush = new(Color.FromArgb(220, 24, 24, 24));
    private readonly SolidBrush _textLabelBrush = new(Color.FromArgb(220, 239, 68, 68));
    private readonly Pen _borderPen = new(Color.FromArgb(200, 239, 68, 68), 2f) { DashStyle = DashStyle.Dash };
    private readonly SolidBrush _cornerBrush = new(Color.FromArgb(220, 239, 68, 68));
    private readonly SolidBrush _shadowBrush = new(Color.FromArgb(60, 0, 0, 0));
    private readonly SolidBrush _toolbarBgBrush = new(Color.FromArgb(252, 28, 28, 28));
    private readonly Pen _toolbarBorderPen = new(Color.FromArgb(30, 255, 255, 255), 1f);
    private readonly SolidBrush _dotBrush = new(Color.FromArgb(240, 239, 68, 68));
    private readonly Pen _ringPen = new(Color.FromArgb(80, 239, 68, 68), 1.5f);
    private readonly Font _timeFont = new("Segoe UI Variable Text", 11f, FontStyle.Bold);
    private readonly SolidBrush _timeBrush = new(Color.FromArgb(220, 255, 255, 255));
    private readonly Font _btnFont = new("Segoe UI Variable Text", 9.5f, FontStyle.Bold);
    private readonly Font _encFont = new("Segoe UI", 10f, FontStyle.Bold);
    private readonly SolidBrush _encTextBrush = new(Color.FromArgb(200, 255, 255, 255));
    private readonly SolidBrush _spinBrush = new(Color.FromArgb(200, 239, 68, 68));
    private readonly SolidBrush _encBgBrush = new(Color.FromArgb(250, 24, 24, 24));
    private readonly Pen _encBorderPen = new(Color.FromArgb(25, 255, 255, 255), 1f);

    public RecordingForm(Bitmap screenshot, Rectangle virtualBounds, int fps, string savePath,
                         Models.RecordingFormat format = Models.RecordingFormat.GIF, int maxHeight = 0,
                         bool recordMic = false, string? micDeviceId = null,
                         bool recordDesktop = false, string? desktopDeviceId = null)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _fps = fps;
        _maxDuration = 3600; // effectively unlimited - user stops manually
        _savePath = savePath;
        _format = format;
        _maxHeight = maxHeight;
        _recordMic = recordMic;
        _micDeviceId = micDeviceId;
        _recordDesktop = recordDesktop;
        _desktopDeviceId = desktopDeviceId;

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

        if (_format == Models.RecordingFormat.GIF)
        {
            _recorder = new GifRecorder(screenRegion, _fps, _maxDuration);
        }
        else
        {
            var vfmt = _format switch
            {
                Models.RecordingFormat.WebM => VideoRecorder.Format.WebM,
                Models.RecordingFormat.MKV => VideoRecorder.Format.MKV,
                _ => VideoRecorder.Format.MP4
            };
            _videoRecorder = new VideoRecorder(screenRegion, vfmt, _fps, _maxDuration, _maxHeight,
                _recordMic, _micDeviceId, _recordDesktop, _desktopDeviceId);
            _videoRecorder.Start(_savePath);
        }
        _state = State.Recording;
        Cursor = Cursors.Default;

        // Switch to transparent mode: form stays fullscreen but only the
        // dashed border + toolbar are visible. Everything else is see-through.
        BackColor = TransKey;
        TransparencyKey = TransKey;

        // Exclude this form from capture so the border/toolbar don't appear in the GIF
        User32.SetWindowDisplayAffinity(Handle, User32.WDA_EXCLUDEFROMCAPTURE);

        CalcToolbarLayout();

        Current = this;
        SoundService.PlayRecordStartSound();
        _recorder?.Start();

        _tickTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _tickTimer.Tick += (_, _) =>
        {
            if ((_recorder != null && !_recorder.IsRecording) || (_videoRecorder != null && !_videoRecorder.IsRecording))
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
        if (_state != State.Recording) return;
        if (_recorder == null && _videoRecorder == null) return;
        _tickTimer?.Stop();

        var gifRec = _recorder; _recorder = null;
        var vidRec = _videoRecorder; _videoRecorder = null;
        SoundService.PlayRecordStopSound();
        Close();

        // Finalize the recording in the background after the UI closes.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Bitmap? firstFrame = gifRec?.GetFirstFrame();
            try
            {
                gifRec?.StopAndEncode(_savePath);
                vidRec?.StopAndEncode(_savePath);
                RecordingCompleted?.Invoke(_savePath, firstFrame);
            }
            catch
            {
                firstFrame?.Dispose();
            }
            finally
            {
                gifRec?.Dispose();
                vidRec?.Dispose();
            }
        });
    }

    private void DiscardRecording()
    {
        _tickTimer?.Stop();
        if (_recorder != null) { _recorder.Discard(); _recorder.Dispose(); _recorder = null; }
        if (_videoRecorder != null) { _videoRecorder.Discard(); _videoRecorder.Dispose(); _videoRecorder = null; }
        RecordingCancelled?.Invoke();
        Close();
    }

    private void CalcToolbarLayout()
    {
        int tw = 300, th = 48;
        // Try to place above the recording region
        int tx = _recordRegion.X + _recordRegion.Width / 2 - tw / 2;
        int ty = _recordRegion.Y - th - 14;

        // If off-screen (fullscreen recording), place at bottom center of screen
        if (ty < 4 || _recordRegion.Height > Height - 100)
            ty = Height - th - 40; // 40px from bottom edge
        if (tx < 4) tx = 4;
        if (tx + tw > Width - 4) tx = Width - 4 - tw;

        _toolbarRect = new Rectangle(tx, ty, tw, th);

        int btnY = _toolbarRect.Y + 10;
        int btnH = 28;
        _stopBtn = new Rectangle(_toolbarRect.X + 100, btnY, 80, btnH);
        _discardBtn = new Rectangle(_stopBtn.Right + 8, btnY, 80, btnH);
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
        g.FillRectangle(_dimBrush, 0, 0, Width, Height);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            g.DrawImage(_screenshot, _selection, _selection, GraphicsUnit.Pixel);
            g.DrawRectangle(_selPen, _selection);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string label = $"GIF  {_selection.Width} x {_selection.Height}";
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
            string hint = "Drag to select recording area";
            var hintSz = g.MeasureString(hint, _hintFont);
            g.DrawString(hint, _hintFont, _hintBrush,
                Width / 2f - hintSz.Width / 2f, Height / 2f - hintSz.Height / 2f);
        }
    }

    private void PaintRecordingPhase(Graphics g)
    {
        g.Clear(TransKey);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var borderRect = Rectangle.Inflate(_recordRegion, 2, 2);
        g.DrawRectangle(_borderPen, borderRect);

        int cm = 6;
        g.FillRectangle(_cornerBrush, borderRect.X - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.Right - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.X - cm / 2, borderRect.Bottom - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.Right - cm / 2, borderRect.Bottom - cm / 2, cm, cm);

        var shadowRect = RectangleF.Inflate(new RectangleF(_toolbarRect.X, _toolbarRect.Y, _toolbarRect.Width, _toolbarRect.Height), 3, 3);
        shadowRect.Offset(0, 2);
        using (var shadowPath = RRect(shadowRect, 16))
            g.FillPath(_shadowBrush, shadowPath);
        using (var tbPath = RRect(_toolbarRect, 14))
        {
            g.FillPath(_toolbarBgBrush, tbPath);
            g.DrawPath(_toolbarBorderPen, tbPath);
        }

        var elapsed = _recorder?.Elapsed ?? _videoRecorder?.Elapsed ?? TimeSpan.Zero;

        float dotX = _toolbarRect.X + 16;
        float dotY = _toolbarRect.Y + _toolbarRect.Height / 2f - 5;
        bool dotVisible = (int)(elapsed.TotalMilliseconds / 500) % 2 == 0;
        if (dotVisible)
            g.FillEllipse(_dotBrush, dotX, dotY, 10, 10);
        g.DrawEllipse(_ringPen, dotX, dotY, 10, 10);

        string time = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        g.DrawString(time, _timeFont, _timeBrush, dotX + 18, _toolbarRect.Y + 13);

        DrawBtn(g, _stopBtn, "\u25A0  Stop", _hoveredBtn == 0,
            Color.FromArgb(255, 239, 68, 68), Color.FromArgb(50, 239, 68, 68));
        DrawBtn(g, _discardBtn, "Discard", _hoveredBtn == 1,
            Color.FromArgb(180, 255, 255, 255), Color.FromArgb(18, 255, 255, 255));

        if (_state == State.Encoding)
        {
            using var encPath = RRect(_toolbarRect, 12);
            g.FillPath(_encBgBrush, encPath);
            g.DrawPath(_encBorderPen, encPath);

            float spinX = _toolbarRect.X + 14;
            float spinY = _toolbarRect.Y + _toolbarRect.Height / 2f - 4;
            g.FillEllipse(_spinBrush, spinX, spinY, 8, 8);

            string encLabel = _format == Models.RecordingFormat.GIF ? "Encoding GIF..." : "Saving...";
            g.DrawString(encLabel, _encFont, _encTextBrush, spinX + 16, _toolbarRect.Y + 12);
        }
    }

    private void DrawBtn(Graphics g, Rectangle rect, string text, bool hovered,
        Color textColor, Color bgColor)
    {
        using var path = RRect(rect, 8);
        int alpha = hovered ? Math.Clamp((int)(bgColor.A * 2.5), 0, 255) : bgColor.A;
        using var bg = new SolidBrush(Color.FromArgb(alpha, bgColor.R, bgColor.G, bgColor.B));
        g.FillPath(bg, path);
        if (hovered)
        {
            using var hBorder = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.DrawPath(hBorder, path);
        }
        using var brush = new SolidBrush(textColor);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, _btnFont, brush, rect, fmt);
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

    /// <summary>External stop (called from tray menu).</summary>
    public void RequestStop()
    {
        if (_state == State.Recording)
            BeginInvoke(new Action(StopRecording));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Current = null;
            _tickTimer?.Dispose();
            _recorder?.Dispose();
            _videoRecorder?.Dispose();
            _screenshot.Dispose();
            _dimBrush.Dispose(); _selPen.Dispose(); _labelFont.Dispose();
            _hintFont.Dispose(); _hintBrush.Dispose(); _bgLabelBrush.Dispose();
            _textLabelBrush.Dispose(); _borderPen.Dispose(); _cornerBrush.Dispose();
            _shadowBrush.Dispose(); _toolbarBgBrush.Dispose(); _toolbarBorderPen.Dispose();
            _dotBrush.Dispose(); _ringPen.Dispose(); _timeFont.Dispose();
            _timeBrush.Dispose(); _btnFont.Dispose(); _encFont.Dispose();
            _encTextBrush.Dispose(); _spinBrush.Dispose(); _encBgBrush.Dispose();
            _encBorderPen.Dispose();
        }
        base.Dispose(disposing);
    }
}
