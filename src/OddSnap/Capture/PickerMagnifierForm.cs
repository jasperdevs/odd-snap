using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using OddSnap.Helpers;

namespace OddSnap.Capture;

public sealed class PickerMagnifierForm : Form
{
    public const int LensSize = 110;
    public const int Pad = 5;
    public const int TotalW = LensSize + Pad * 2;

    private const int InfoGap = 10;
    private const int InfoH = 42;
    private const int LensRadius = 14;

    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;
    private bool _showInfo = true;

    private Bitmap? _magnifier;
    private string _hex = "000000";
    private string _rgb = "0, 0, 0";
    private Color _picked = Color.Black;
    private Size _lastSurfaceSize = Size.Empty;
    private bool _lastShowInfo = true;
    private Bitmap? _lastMagnifier;
    private string _lastHex = "";
    private string _lastRgb = "";
    private int _lastPickedArgb;

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
        Size = new Size(TotalW, GetTotalHeight(true));
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CaptureWindowExclusion.Apply(this);
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

    public void UpdateMagnifier(Bitmap magnifier, Point cursor, Color picked, string hex, string rgb, bool showInfo = true)
    {
        var targetSize = new Size(TotalW, GetTotalHeight(showInfo));
        if (_lastSurfaceSize == targetSize &&
            _lastShowInfo == showInfo &&
            ReferenceEquals(_lastMagnifier, magnifier) &&
            _lastPickedArgb == picked.ToArgb() &&
            string.Equals(_lastHex, hex, StringComparison.Ordinal) &&
            string.Equals(_lastRgb, rgb, StringComparison.Ordinal))
        {
            return;
        }

        _magnifier = magnifier;
        _picked = picked;
        _hex = hex;
        _rgb = rgb;
        _showInfo = showInfo;
        if (Size != targetSize)
            Size = targetSize;
        UpdateSurface();
        _lastSurfaceSize = targetSize;
        _lastShowInfo = showInfo;
        _lastMagnifier = magnifier;
        _lastPickedArgb = picked.ToArgb();
        _lastHex = hex;
        _lastRgb = rgb;
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

        int cx = Pad + LensSize / 2;
        int cy = Pad + LensSize / 2;
        var lensRect = new Rectangle(Pad, Pad, LensSize, LensSize);

        var shadowRect = lensRect;
        shadowRect.Inflate(1, 1);
        var shadowPasses = new (int dx, int dy, int a)[]
        {
            (2, 3, 16),
            (1, 2, 30),
            (0, 1, 44),
        };
        foreach (var (dx, dy, a) in shadowPasses)
        {
            var sr = shadowRect;
            sr.Offset(dx, dy);
            using var brush = new SolidBrush(Color.FromArgb(a, 0, 0, 0));
            using var shadowPath = RoundedRect(sr, LensRadius + 2);
            g.FillPath(brush, shadowPath);
        }

        using var lensPath = RoundedRect(lensRect, LensRadius);
        g.FillPath(_bgBrush, lensPath);

        if (_magnifier != null)
        {
            var state = g.Save();
            g.SetClip(lensPath);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_magnifier, lensRect);
            g.Restore(state);
        }

        g.DrawPath(_outerRingPen, lensPath);
        var innerRing = lensRect;
        innerRing.Inflate(-1, -1);
        using (var innerPath = RoundedRect(innerRing, LensRadius - 1))
            g.DrawPath(_ringPen, innerPath);

        int dotSize = 4;
        using var dotFill = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.FillRectangle(dotFill, cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize);
        using var dotBorder = new Pen(UiChrome.SurfaceBorderStrong, 1f);
        g.DrawRectangle(dotBorder, cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize);

        if (_showInfo)
        {
            // Info pill below circle
            string hexLabel = $"#{_hex}";
            string rgbLabel = $"R: {_picked.R}  G: {_picked.G}  B: {_picked.B}";
            var hexSize = g.MeasureString(hexLabel, _hexFont);
            var rgbSize = g.MeasureString(rgbLabel, _rgbFont);
            int pillW = (int)Math.Ceiling(Math.Max(hexSize.Width, rgbSize.Width)) + 20;
            int pillH = InfoH;
            int pillX = cx - pillW / 2;
            int pillY = lensRect.Bottom + InfoGap;
            var pillRect = new RectangleF(pillX, pillY, pillW, pillH);

            using var pillPath = RoundedPill(pillRect, pillH / 2f);
            g.FillPath(_pillBg, pillPath);
            g.DrawPath(_pillBorder, pillPath);

            var hexX = pillX + (pillW - hexSize.Width) / 2f;
            var rgbX = pillX + (pillW - rgbSize.Width) / 2f;
            g.DrawString(hexLabel, _hexFont, _labelBrush, hexX, pillY + 3);
            g.DrawString(rgbLabel, _rgbFont, _labelBrush, rgbX, pillY + 21);
        }

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
        => RoundedRect(r, radius);

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
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

    public static int GetTotalHeight(bool showInfo)
        => LensSize + Pad * 2 + (showInfo ? InfoGap + InfoH : 0);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lastMagnifier = null;
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
