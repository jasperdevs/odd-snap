using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Yoink.Capture;

public sealed class PickerMagnifierForm : Form
{
    private const int PickerWidth = 146;
    private const int PickerHeight = 194;
    private Bitmap? _magnifier;
    private string _hex = "000000";
    private string _rgb = "0, 0, 0";
    private Color _picked = Color.Black;

    public PickerMagnifierForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(PickerWidth + 8, PickerHeight + 8);
        BackColor = Color.FromArgb(32, 32, 32);
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        using var path = Rounded(new Rectangle(4, 4, PickerWidth, PickerHeight), 12);
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var rect = new Rectangle(4, 4, PickerWidth, PickerHeight);
        using var path = Rounded(rect, 12);

        // Shadow
        var shadowRect = rect;
        shadowRect.Inflate(2, 2);
        shadowRect.Offset(0, 1);
        using (var sp = Rounded(shadowRect, 14))
        using (var sb = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillPath(sb, sp);

        // Background
        using (var bg = new SolidBrush(Color.FromArgb(255, 32, 32, 32)))
            g.FillPath(bg, path);

        if (_magnifier != null)
        {
            var oldClip = g.Clip;
            g.SetClip(path);
            g.DrawImageUnscaled(_magnifier, 4, 4);
            g.Clip = oldClip;
        }

        using (var pen = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
            g.DrawPath(pen, path);
    }

    private static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
