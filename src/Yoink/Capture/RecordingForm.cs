using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using Yoink.Native;
using Yoink.Helpers;
using Yoink.Services;
using Yoink.UI;

namespace Yoink.Capture;

/// <summary>
/// Two-phase form: first shows fullscreen overlay for region selection,
/// then stays fullscreen but transparent during recording to show the
/// dashed border around the capture region plus a floating toolbar.
/// </summary>
public sealed partial class RecordingForm : Form
{
    private const int RecordingWarmupDelayMs = 260;

    /// <summary>Fires with (filePath, firstFrameBitmap). Caller must dispose the bitmap.</summary>
    public event Action<string, Bitmap?>? RecordingCompleted;
    public event Action<Exception>? RecordingFailed;
    public event Action? RecordingCancelled;

    /// <summary>Static reference to the current recording form for external stop control.</summary>
    public static RecordingForm? Current { get; private set; }

    private enum State { Selecting, Recording, Encoding }

    private Bitmap? _screenshot;
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
    private readonly bool _showCursor;
    private readonly bool _recordMic;
    private readonly string? _micDeviceId;
    private readonly bool _recordDesktop;
    private readonly string? _desktopDeviceId;
    private readonly bool _showMagnifier;
    private readonly CaptureMagnifierHelper? _magHelper;
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
    private readonly Pen _selPen = new(Color.FromArgb(255, 255, 255, 255), 2.0f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 3f }, LineJoin = LineJoin.Miter };
    private readonly Font _labelFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _hintBrush = new(UiChrome.SurfaceTextMuted);
    private readonly SolidBrush _bgLabelBrush = new(UiChrome.SurfacePill);
    private readonly SolidBrush _textLabelBrush = new(UiChrome.SurfaceTextPrimary);
    private readonly Pen _borderPen = new(Color.FromArgb(200, 239, 68, 68), 2.0f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 3f }, LineJoin = LineJoin.Miter };
    private readonly SolidBrush _cornerBrush = new(Color.FromArgb(220, 239, 68, 68));
    private readonly SolidBrush _shadowBrush = new(Color.FromArgb(60, 0, 0, 0));
    private readonly SolidBrush _toolbarBgBrush = new(UiChrome.SurfacePill);
    private readonly Pen _toolbarBorderPen = new(UiChrome.SurfaceBorder, 1f);
    private readonly SolidBrush _dotBrush = new(Color.FromArgb(240, 239, 68, 68));
    private readonly Pen _ringPen = new(Color.FromArgb(80, 239, 68, 68), 1.5f);
    private readonly Font _timeFont = UiChrome.ChromeFont(UiChrome.ChromeTitleSize, FontStyle.Bold);
    private readonly SolidBrush _timeBrush = new(UiChrome.SurfaceTextPrimary);
    private readonly Font _btnFont = UiChrome.ChromeFont(9.5f, FontStyle.Bold);
    private readonly Font _encFont = UiChrome.ChromeFont(10f, FontStyle.Bold);
    private readonly SolidBrush _encTextBrush = new(UiChrome.SurfaceTextSecondary);
    private readonly SolidBrush _spinBrush = new(Color.FromArgb(200, 239, 68, 68));
    private readonly SolidBrush _encBgBrush = new(UiChrome.SurfacePill);
    private readonly Pen _encBorderPen = new(UiChrome.SurfaceBorderSubtle, 1f);

    public RecordingForm(Bitmap screenshot, Rectangle virtualBounds, int fps, string savePath,
                         Models.RecordingFormat format = Models.RecordingFormat.GIF, int maxHeight = 0,
                         bool showCursor = false,
                         bool recordMic = false, string? micDeviceId = null,
                         bool recordDesktop = false, string? desktopDeviceId = null,
                         bool showMagnifier = false)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _fps = fps;
        _maxDuration = 3600; // effectively unlimited - user stops manually
        _savePath = savePath;
        _format = format;
        _maxHeight = maxHeight;
        _showCursor = showCursor;
        _recordMic = recordMic;
        _micDeviceId = micDeviceId;
        _recordDesktop = recordDesktop;
        _desktopDeviceId = desktopDeviceId;
        _showMagnifier = showMagnifier;
        if (_showMagnifier)
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
        if (_state == State.Selecting)
        {
            if (_isDragging)
            {
                _selection = NormRect(_dragStart, e.Location);
                Invalidate();
            }
            _magHelper?.Update(e.Location, this, _virtualBounds);
            return;
        }
        if (_state == State.Recording)
        {
            int prev = _hoveredBtn;
            _hoveredBtn = _stopBtn.Contains(e.Location) ? 0
                        : _discardBtn.Contains(e.Location) ? 1
                        : -1;
            Cursor = _hoveredBtn >= 0 ? Cursors.Hand : Cursors.Default;
            if (_hoveredBtn != prev) Invalidate(_toolbarRect);
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
            {
                Invalidate();
            }
        }
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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var screenshot = _screenshot;
        if (screenshot is null)
            return;

        g.DrawImage(screenshot, 0, 0);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            g.DrawImage(screenshot, _selection, _selection, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawRectangle(_selPen, _selection);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string label = $"{GetRecordingFormatLabel()}  {_selection.Width} x {_selection.Height}  {_fps} FPS";
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
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var borderRect = Rectangle.Inflate(_recordRegion, 2, 2);
        g.DrawRectangle(_borderPen, borderRect);

        int cm = 6;
        g.FillRectangle(_cornerBrush, borderRect.X - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.Right - cm / 2, borderRect.Y - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.X - cm / 2, borderRect.Bottom - cm / 2, cm, cm);
        g.FillRectangle(_cornerBrush, borderRect.Right - cm / 2, borderRect.Bottom - cm / 2, cm, cm);

        // Fluent 2-layer shadow: ambient + directional
        var tbRectF = new RectangleF(_toolbarRect.X, _toolbarRect.Y, _toolbarRect.Width, _toolbarRect.Height);
        var ambient = tbRectF;
        ambient.Inflate(8f, 8f);
        ambient.Offset(0, 2.2f);
        using (var shadowPath = RRect(ambient, 8f + 8f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(6, 0, 0, 0)))
            g.FillPath(shadowBrush, shadowPath);
        var directional = tbRectF;
        directional.Inflate(3f, 3f);
        directional.Offset(0, 5.2f);
        using (var shadowPath = RRect(directional, 8f + 3f))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(12, 0, 0, 0)))
            g.FillPath(shadowBrush, shadowPath);

        using (var tbPath = RRect(_toolbarRect, 8f))
        {
            g.FillPath(_toolbarBgBrush, tbPath);

            // Fluent gradient highlight
            var hlRect = new RectangleF(_toolbarRect.X + 1f, _toolbarRect.Y + 0.5f, _toolbarRect.Width - 2f, _toolbarRect.Height - 1f);
            using var hlPath = RRect(hlRect, 7.5f);
            using var gradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new PointF(_toolbarRect.X, _toolbarRect.Y),
                new PointF(_toolbarRect.X, _toolbarRect.Bottom),
                Color.FromArgb(UiChrome.IsDark ? 48 : 60, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var hlPen = new Pen(gradBrush, 1f);
            g.DrawPath(hlPen, hlPath);

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
        g.DrawString(time, _timeFont, _timeBrush, dotX + 18, _toolbarRect.Y + 21);

        DrawBtn(g, _stopBtn, "\u25A0  Stop", _hoveredBtn == 0,
            Color.FromArgb(255, 239, 68, 68), Color.FromArgb(50, 239, 68, 68));
        DrawBtn(g, _discardBtn, "Discard", _hoveredBtn == 1,
            UiChrome.SurfaceTextPrimary, UiChrome.SurfaceHover);

        if (_state == State.Encoding)
        {
            using var encPath = RRect(_toolbarRect, 8);
            g.FillPath(_encBgBrush, encPath);
            g.DrawPath(_encBorderPen, encPath);

            float spinX = _toolbarRect.X + 14;
            float spinY = _toolbarRect.Y + _toolbarRect.Height / 2f - 4;
            g.FillEllipse(_spinBrush, spinX, spinY, 8, 8);

            string encLabel = _format == Models.RecordingFormat.GIF ? "Encoding GIF..." : "Saving...";
            g.DrawString(encLabel, _encFont, _encTextBrush, spinX + 16, _toolbarRect.Y + 22);
        }
    }

    private void DrawBtn(Graphics g, Rectangle rect, string text, bool hovered,
        Color textColor, Color bgColor)
    {
        using var path = RRect(rect, 5);
        int alpha = hovered ? Math.Clamp((int)(bgColor.A * 2.5), 0, 255) : bgColor.A;
        using var bg = new SolidBrush(Color.FromArgb(alpha, bgColor.R, bgColor.G, bgColor.B));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(bg, path);
        if (hovered)
        {
            using var hBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
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

    private string GetRecordingFormatLabel() => _format switch
    {
        Models.RecordingFormat.MP4 => "MP4",
        Models.RecordingFormat.WebM => "WebM",
        Models.RecordingFormat.MKV => "MKV",
        _ => "GIF"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Current = null;
            _tickTimer?.Dispose();
            _recorder?.Dispose();
            _videoRecorder?.Dispose();
            _magHelper?.Dispose();
            _screenshot?.Dispose();
            _screenshot = null;
            _selPen.Dispose(); _labelFont.Dispose();
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
