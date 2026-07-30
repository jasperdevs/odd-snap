using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OddSnap.Capture;

/// <summary>
/// Separate borderless Form that renders the toolbar, tooltips, and popups.
/// Uses UpdateLayeredWindow for per-pixel alpha -- no TransparencyKey needed.
/// Owned by RegionOverlayForm and positioned over it.
/// Having its own HWND means DWM composites it independently -- no tearing.
/// </summary>
public sealed class ToolbarForm : Form
{
    private readonly RegionOverlayForm _owner;
    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;
    private int _lastRenderVersion = int.MinValue;
    private Size _lastRenderSize;
    private Point _lastRenderLocation;

    public ToolbarForm(RegionOverlayForm owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = owner.TopMost;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT (click-through)
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CaptureWindowExclusion.Apply(this);
    }

    public void UpdateSurface()
    {
        var sz = Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

        var location = Location;
        var renderVersion = _owner.ToolbarRenderVersion;
        if (_surface is not null &&
            _lastRenderVersion == renderVersion &&
            _lastRenderSize == sz &&
            _lastRenderLocation == location)
        {
            return;
        }

        if (_surface == null || _surface.Width != sz.Width || _surface.Height != sz.Height)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _surface = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
            _surfaceGraphics = Graphics.FromImage(_surface);
        }

        // _owner paints using overlay-client coordinates (e.g. _toolbarRect).
        // This form is positioned at screen coords; the overlay's screen origin
        // is _owner.Left, _owner.Top.  So translate = overlayScreenOrigin - thisScreenOrigin.
        int dx = _owner.Left - Left;
        int dy = _owner.Top - Top;

        var g = _surfaceGraphics!;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(dx, dy);
        _owner.PaintToolbarTo(g);
        g.ResetTransform();
        g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);

        LayeredWindowSurface.Update(Handle, _surface!, new Rectangle(Left, Top, sz.Width, sz.Height));
        _lastRenderVersion = renderVersion;
        _lastRenderSize = sz;
        _lastRenderLocation = location;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            _owner.CancelFromShortcut();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

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
