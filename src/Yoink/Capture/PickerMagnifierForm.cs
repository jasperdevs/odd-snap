using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using Yoink.Helpers;

namespace Yoink.Capture;

public sealed class PickerMagnifierForm : Form
{
    private const int CircleDiameter = 140;
    private const int InfoGap = 10;
    private const int InfoH = 42;
    private const int Pad = 6;
    private const int TotalW = CircleDiameter + Pad * 2;
    private const int TotalH = CircleDiameter + InfoGap + InfoH + Pad * 2;

    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;

    private Bitmap? _magnifier;
    private string _hex = "000000";
    private string _rgb = "0, 0, 0";
    private Color _picked = Color.Black;

    // Cached GDI objects
    private readonly Font _hexFont = UiChrome.ChromeFont(9.5f, FontStyle.Bold);
    private readonly Font _rgbFont = UiChrome.ChromeFont(8.5f);
    private readonly SolidBrush _labelBrush = new(UiChrome.SurfaceTextPrimary);
    private readonly SolidBrush _bgBrush = new(UiChrome.SurfaceElevated);
    private readonly Pen _ringPen = new(UiChrome.SurfaceBorderStrong, 1.5f);
    private readonly Pen _outerRingPen = new(UiChrome.SurfaceBorderSubtle, 1f);
    private readonly SolidBrush _pillBg = new(UiChrome.SurfacePill);
    private readonly Pen _pillBorder = new(UiChrome.SurfaceBorderSubtle, 1f);

    public PickerMagnifierForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(TotalW, TotalH);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
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

    public void UpdateMagnifier(Bitmap magnifier, Point cursor, Color picked, string hex, string rgb)
    {
        _magnifier = magnifier;
        _picked = picked;
        _hex = hex;
        _rgb = rgb;
        UpdateSurface();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateSurface();
    }

    private void UpdateSurface()
    {
        var sz = Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

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
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        int cx = Pad + CircleDiameter / 2;
        int cy = Pad + CircleDiameter / 2;
        var circleRect = new Rectangle(Pad, Pad, CircleDiameter, CircleDiameter);

        // Shadow behind circle
        var shadowRect = circleRect;
        shadowRect.Inflate(2, 2);
        var shadowPasses = new (int dx, int dy, int a)[]
        {
            (4, 5, 14),
            (2, 3, 24),
            (1, 2, 38),
            (0, 2, 55),
        };
        foreach (var (dx, dy, a) in shadowPasses)
        {
            var sr = shadowRect;
            sr.Offset(dx, dy);
            using var brush = new SolidBrush(Color.FromArgb(a, 0, 0, 0));
            g.FillEllipse(brush, sr);
        }

        // Dark background circle
        g.FillEllipse(_bgBrush, circleRect);

        // Draw magnified pixels clipped to circle
        if (_magnifier != null)
        {
            var state = g.Save();
            using var clipPath = new GraphicsPath();
            clipPath.AddEllipse(circleRect);
            g.SetClip(clipPath);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_magnifier, circleRect);
            g.Restore(state);
        }

        // Circle border rings
        g.DrawEllipse(_outerRingPen, circleRect);
        var innerRing = circleRect;
        innerRing.Inflate(-1, -1);
        g.DrawEllipse(_ringPen, innerRing);

        // Center crosshair dot
        int dotSize = 4;
        g.FillRectangle(Brushes.White, cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize);
        using var dotBorder = new Pen(UiChrome.SurfaceBorderStrong, 1f);
        g.DrawRectangle(dotBorder, cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize);

        // Info pill below circle
        string hexLabel = $"#{_hex}";
        string rgbLabel = $"R: {_picked.R}  G: {_picked.G}  B: {_picked.B}";
        var hexSize = g.MeasureString(hexLabel, _hexFont);
        var rgbSize = g.MeasureString(rgbLabel, _rgbFont);
        int pillW = (int)Math.Ceiling(Math.Max(hexSize.Width, rgbSize.Width)) + 20;
        int pillH = InfoH;
        int pillX = cx - pillW / 2;
        int pillY = circleRect.Bottom + InfoGap;
        var pillRect = new RectangleF(pillX, pillY, pillW, pillH);

        using var pillPath = RoundedPill(pillRect, pillH / 2f);
        g.FillPath(_pillBg, pillPath);
        g.DrawPath(_pillBorder, pillPath);

        var hexX = pillX + (pillW - hexSize.Width) / 2f;
        var rgbX = pillX + (pillW - rgbSize.Width) / 2f;
        g.DrawString(hexLabel, _hexFont, _labelBrush, hexX, pillY + 3);
        g.DrawString(rgbLabel, _rgbFont, _labelBrush, rgbX, pillY + 21);

        g.Flush(FlushIntention.Sync);

        var screenPt = new Native.User32.POINT { X = Left, Y = Top };
        var size = new Native.User32.SIZE { cx = sz.Width, cy = sz.Height };
        var srcPt = new Native.User32.POINT { X = 0, Y = 0 };
        var blend = new Native.User32.BLENDFUNCTION
        {
            BlendOp = 0, // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1  // AC_SRC_ALPHA
        };

        IntPtr hdcScreen = Native.User32.GetDC(IntPtr.Zero);
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        try
        {
            hdcMem = Native.User32.CreateCompatibleDC(hdcScreen);
            hBmp = _surface!.GetHbitmap(Color.FromArgb(0));
            hOld = Native.User32.SelectObject(hdcMem, hBmp);
            Native.User32.UpdateLayeredWindow(Handle, hdcScreen, ref screenPt, ref size,
                hdcMem, ref srcPt, 0, ref blend, 2 /* ULW_ALPHA */);
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

    private static GraphicsPath RoundedPill(RectangleF r, float radius)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _hexFont.Dispose();
            _rgbFont.Dispose();
            _labelBrush.Dispose();
            _bgBrush.Dispose();
            _ringPen.Dispose();
            _outerRingPen.Dispose();
            _pillBg.Dispose();
            _pillBorder.Dispose();
        }
        base.Dispose(disposing);
    }
}
