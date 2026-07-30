using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Native;

namespace OddSnap.Capture;

internal sealed class RecordingBorderForm : Form
{
    private readonly Rectangle _recordingScreenBounds;
    private readonly int _pad;
    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;

    public RecordingBorderForm(Rectangle recordingScreenBounds)
    {
        _recordingScreenBounds = recordingScreenBounds;
        _pad = UiChrome.ScaleInt(10);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = Rectangle.Inflate(recordingScreenBounds, _pad, _pad);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= User32.WS_EX_TOOLWINDOW;
            cp.ExStyle |= User32.WS_EX_NOACTIVATE;
            cp.ExStyle |= User32.WS_EX_LAYERED;
            cp.ExStyle |= User32.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CaptureWindowExclusion.Apply(this);
    }

    public void UpdateSurface()
    {
        var sz = Size;
        if (sz.Width <= 0 || sz.Height <= 0 || IsDisposed || !IsHandleCreated)
            return;

        if (_surface == null || _surface.Width != sz.Width || _surface.Height != sz.Height)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _surface = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
            _surfaceGraphics = Graphics.FromImage(_surface);
        }

        var g = _surfaceGraphics!;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float lineWidth = UiChrome.ScaleFloat(2f);
        float inset = Math.Max(2f, UiChrome.ScaleFloat(3f));
        var borderRect = new RectangleF(
            _pad - inset,
            _pad - inset,
            _recordingScreenBounds.Width + inset * 2,
            _recordingScreenBounds.Height + inset * 2);

        using (var shadowPen = new Pen(Color.FromArgb(80, 0, 0, 0), lineWidth + 2f)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 4f, 3f },
            LineJoin = LineJoin.Miter
        })
        {
            g.DrawRectangle(shadowPen, borderRect.X, borderRect.Y, borderRect.Width, borderRect.Height);
        }

        using (var borderPen = new Pen(Color.FromArgb(230, 239, 68, 68), lineWidth)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 4f, 3f },
            LineJoin = LineJoin.Miter
        })
        {
            g.DrawRectangle(borderPen, borderRect.X, borderRect.Y, borderRect.Width, borderRect.Height);
        }

        float corner = UiChrome.ScaleFloat(7f);
        using var cornerBrush = new SolidBrush(Color.FromArgb(235, 239, 68, 68));
        FillCorner(g, cornerBrush, borderRect.Left, borderRect.Top, corner);
        FillCorner(g, cornerBrush, borderRect.Right, borderRect.Top, corner);
        FillCorner(g, cornerBrush, borderRect.Left, borderRect.Bottom, corner);
        FillCorner(g, cornerBrush, borderRect.Right, borderRect.Bottom, corner);

        g.Flush(FlushIntention.Sync);
        UpdateLayeredWindowSurface(sz);
    }

    private void UpdateLayeredWindowSurface(Size sz)
    {
        LayeredWindowSurface.Update(Handle, _surface!, new Rectangle(Left, Top, sz.Width, sz.Height));
    }

    private static void FillCorner(Graphics g, Brush brush, float x, float y, float size)
        => g.FillRectangle(brush, x - size / 2f, y - size / 2f, size, size);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsHandleCreated)
                CaptureWindowExclusion.Unregister(Handle);
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
        }

        base.Dispose(disposing);
    }
}
