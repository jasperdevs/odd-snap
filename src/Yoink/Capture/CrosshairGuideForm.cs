using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Yoink.Capture;

public sealed class CrosshairGuideForm : Form
{
    private readonly Color _lineColor;
    private Bitmap? _surface;

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

        Bounds = bounds;
        UpdateSurface();
        if (!Visible)
            Show();
    }

    private void UpdateSurface()
    {
        var sz = Size;
        if (sz.Width <= 0 || sz.Height <= 0)
            return;

        if (_surface == null || _surface.Width != sz.Width || _surface.Height != sz.Height)
        {
            _surface?.Dispose();
            _surface = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
        }

        using (var g = Graphics.FromImage(_surface))
        using (var brush = new SolidBrush(_lineColor))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(brush, 0, 0, sz.Width, sz.Height);
        }

        var screenPt = new Native.User32.POINT { X = Left, Y = Top };
        var size = new Native.User32.SIZE { cx = sz.Width, cy = sz.Height };
        var srcPt = new Native.User32.POINT { X = 0, Y = 0 };
        var blend = new Native.User32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        IntPtr hdcScreen = Native.User32.GetDC(IntPtr.Zero);
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        try
        {
            hdcMem = Native.User32.CreateCompatibleDC(hdcScreen);
            hBmp = _surface.GetHbitmap(Color.FromArgb(0));
            hOld = Native.User32.SelectObject(hdcMem, hBmp);
            Native.User32.UpdateLayeredWindow(Handle, hdcScreen, ref screenPt, ref size,
                hdcMem, ref srcPt, 0, ref blend, 2);
        }
        finally
        {
            if (hdcMem != IntPtr.Zero && hOld != IntPtr.Zero)
                Native.User32.SelectObject(hdcMem, hOld);
            if (hBmp != IntPtr.Zero)
                Native.User32.DeleteObject(hBmp);
            if (hdcMem != IntPtr.Zero)
                Native.User32.DeleteDC(hdcMem);
            Native.User32.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _surface?.Dispose();
        base.Dispose(disposing);
    }
}
