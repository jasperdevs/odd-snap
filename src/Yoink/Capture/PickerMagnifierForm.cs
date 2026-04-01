using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Yoink.Capture;

public sealed class PickerMagnifierForm : Form
{
    private const int CircleDiameter = 140;
    private const int InfoGap = 10;
    private const int InfoH = 28;
    private const int Pad = 6;
    private const int TotalW = CircleDiameter + Pad * 2;
    private const int TotalH = CircleDiameter + InfoGap + InfoH + Pad * 2;

    private Bitmap? _magnifier;
    private string _hex = "000000";
    private string _rgb = "0, 0, 0";
    private Color _picked = Color.Black;

    // Cached GDI objects
    private readonly Font _labelFont = new("Segoe UI", 9f);
    private readonly SolidBrush _labelBrush = new(Color.FromArgb(220, 255, 255, 255));
    private readonly SolidBrush _bgBrush = new(Color.FromArgb(255, 32, 32, 32));
    private readonly SolidBrush _shadowBrush = new(Color.FromArgb(50, 0, 0, 0));
    private readonly Pen _ringPen = new(Color.FromArgb(40, 255, 255, 255), 1.5f);
    private readonly Pen _outerRingPen = new(Color.FromArgb(15, 255, 255, 255), 1f);
    private readonly SolidBrush _pillBg = new(Color.FromArgb(220, 32, 32, 32));
    private readonly Pen _pillBorder = new(Color.FromArgb(30, 255, 255, 255), 1f);

    public PickerMagnifierForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(TotalW, TotalH);
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // TOOLWINDOW
            cp.ExStyle |= 0x08000000; // NOACTIVATE
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
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Magenta); // transparency key
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        int cx = Pad + CircleDiameter / 2;
        int cy = Pad + CircleDiameter / 2;
        var circleRect = new Rectangle(Pad, Pad, CircleDiameter, CircleDiameter);

        // Shadow behind circle
        var shadowRect = circleRect;
        shadowRect.Inflate(2, 2);
        shadowRect.Offset(0, 2);
        g.FillEllipse(_shadowBrush, shadowRect);

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
        using var dotBorder = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
        g.DrawRectangle(dotBorder, cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize);

        // Info pill below circle
        string label = $"R: {_picked.R}  G: {_picked.G}  B: {_picked.B}";
        var labelSize = g.MeasureString(label, _labelFont);
        int pillW = (int)labelSize.Width + 20;
        int pillH = InfoH;
        int pillX = cx - pillW / 2;
        int pillY = circleRect.Bottom + InfoGap;
        var pillRect = new RectangleF(pillX, pillY, pillW, pillH);

        using var pillPath = RoundedPill(pillRect, pillH / 2f);
        g.FillPath(_pillBg, pillPath);
        g.DrawPath(_pillBorder, pillPath);

        var labelX = pillX + (pillW - labelSize.Width) / 2f;
        var labelY = pillY + (pillH - labelSize.Height) / 2f;
        g.DrawString(label, _labelFont, _labelBrush, labelX, labelY);
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
            _labelFont.Dispose();
            _labelBrush.Dispose();
            _bgBrush.Dispose();
            _shadowBrush.Dispose();
            _ringPen.Dispose();
            _outerRingPen.Dispose();
            _pillBg.Dispose();
            _pillBorder.Dispose();
        }
        base.Dispose(disposing);
    }
}
