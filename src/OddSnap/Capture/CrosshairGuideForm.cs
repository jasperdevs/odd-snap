using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace OddSnap.Capture;

public sealed class CrosshairGuideForm : Form
{
    private readonly Color _lineColor;
    private Bitmap? _surface;
    private SolidBrush? _brush;
    private IntPtr _cachedHdcMem;
    private IntPtr _cachedHBmp;
    private IntPtr _cachedHOld;
    private Size _cachedSize;
    private bool _zOrderSet;

    public CrosshairGuideForm(Color lineColor)
    {
        _lineColor = lineColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTTRANSPARENT;
            return;
        }

        base.WndProc(ref m);
    }

    public void UpdateLine(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Use SetWindowPos to move + update the layered window in one call.
        // This avoids the expensive Bounds setter which triggers layout/resize events.
        var sz = new Size(bounds.Width, bounds.Height);
        bool sizeChanged = sz != _cachedSize;

        if (sizeChanged)
        {
            _cachedSize = sz;
            RebuildSurface(sz);
        }

        // Move + show via SetWindowPos (combines move, Z-order, and show).
        uint flags = 0x0010; // SWP_NOACTIVATE
        if (!sizeChanged)
            flags |= 0x0001; // SWP_NOSIZE (skip resize if only moving)

        if (!Visible)
        {
            flags |= 0x0040; // SWP_SHOWWINDOW
            Visible = true;
        }

        Native.User32.SetWindowPos(Handle, (IntPtr)(-1) /*HWND_TOPMOST*/,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);

        // Only update the layered content when size changed (the bitmap is the same solid color).
        if (sizeChanged)
            UpdateLayeredContent(bounds);

        if (!_zOrderSet)
        {
            _zOrderSet = true;
            // Extra Z-order nudge on first show.
            Native.User32.SetWindowPos(Handle, (IntPtr)(-1),
                0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOMOVE|NOSIZE|NOACTIVATE
        }
    }

    private void RebuildSurface(Size sz)
    {
        FreeCachedGdi();

        if (_surface == null || _surface.Width != sz.Width || _surface.Height != sz.Height)
        {
            _surface?.Dispose();
            _surface = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
        }

        _brush ??= new SolidBrush(_lineColor);
        var shadowColor = Color.FromArgb(30, 0, 0, 0);
        using var shadowBrush = new SolidBrush(shadowColor);
        using (var g = Graphics.FromImage(_surface))
        {
            g.Clear(Color.Transparent);
            // Draw shadow edges, then main line in center
            if (sz.Width > sz.Height)
            {
                // Horizontal line
                g.FillRectangle(shadowBrush, 0, 0, sz.Width, 1);
                g.FillRectangle(_brush, 0, 1, sz.Width, 1);
                g.FillRectangle(shadowBrush, 0, 2, sz.Width, 1);
            }
            else
            {
                // Vertical line
                g.FillRectangle(shadowBrush, 0, 0, 1, sz.Height);
                g.FillRectangle(_brush, 1, 0, 1, sz.Height);
                g.FillRectangle(shadowBrush, 2, 0, 1, sz.Height);
            }
        }

        // Pre-create the GDI objects for UpdateLayeredWindow.
        IntPtr hdcScreen = Native.User32.GetDC(IntPtr.Zero);
        _cachedHdcMem = Native.User32.CreateCompatibleDC(hdcScreen);
        _cachedHBmp = _surface.GetHbitmap(Color.FromArgb(0));
        _cachedHOld = Native.User32.SelectObject(_cachedHdcMem, _cachedHBmp);
        Native.User32.ReleaseDC(IntPtr.Zero, hdcScreen);
    }

    private void UpdateLayeredContent(Rectangle bounds)
    {
        if (_cachedHdcMem == IntPtr.Zero)
            return;

        var screenPt = new Native.User32.POINT { X = bounds.X, Y = bounds.Y };
        var size = new Native.User32.SIZE { cx = bounds.Width, cy = bounds.Height };
        var srcPt = new Native.User32.POINT { X = 0, Y = 0 };
        var blend = new Native.User32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        IntPtr hdcScreen = Native.User32.GetDC(IntPtr.Zero);
        Native.User32.UpdateLayeredWindow(Handle, hdcScreen, ref screenPt, ref size,
            _cachedHdcMem, ref srcPt, 0, ref blend, 2);
        Native.User32.ReleaseDC(IntPtr.Zero, hdcScreen);
    }

    private void FreeCachedGdi()
    {
        if (_cachedHdcMem != IntPtr.Zero && _cachedHOld != IntPtr.Zero)
            Native.User32.SelectObject(_cachedHdcMem, _cachedHOld);
        if (_cachedHBmp != IntPtr.Zero)
            Native.User32.DeleteObject(_cachedHBmp);
        if (_cachedHdcMem != IntPtr.Zero)
            Native.User32.DeleteDC(_cachedHdcMem);
        _cachedHdcMem = IntPtr.Zero;
        _cachedHBmp = IntPtr.Zero;
        _cachedHOld = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        FreeCachedGdi();
        if (disposing)
        {
            _brush?.Dispose();
            _surface?.Dispose();
        }
        base.Dispose(disposing);
    }
}
