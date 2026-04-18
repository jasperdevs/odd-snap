using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OddSnap.Helpers;

public sealed class WindowsToolTip : Form
{
    private const int MaxWidth = 360;
    private const int PadX = 10;
    private const int PadY = 6;
    private readonly Font _font = UiChrome.ChromeFont(8.5f);
    private string _text = "";

    public WindowsToolTip()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = UiChrome.SurfaceTooltip;
        ForeColor = UiChrome.SurfaceTextPrimary;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    public void ShowNear(IWin32Window owner, string text, Rectangle anchorScreenBounds, bool above)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        OddSnap.UI.Theme.Refresh();
        _text = text;
        BackColor = UiChrome.SurfaceTooltip;
        ForeColor = UiChrome.SurfaceTextPrimary;

        var preferred = TextRenderer.MeasureText(
            text,
            _font,
            new Size(MaxWidth - PadX * 2, 0),
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        int width = Math.Min(MaxWidth, Math.Max(1, preferred.Width + PadX * 2));
        int height = Math.Max(1, preferred.Height + PadY * 2);

        int x = anchorScreenBounds.Left + (anchorScreenBounds.Width - width) / 2;
        int y = above
            ? anchorScreenBounds.Top - height - 8
            : anchorScreenBounds.Bottom + 8;

        var screen = Screen.FromRectangle(anchorScreenBounds).WorkingArea;
        x = Math.Clamp(x, screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - width - 4));
        y = Math.Clamp(y, screen.Top + 4, Math.Max(screen.Top + 4, screen.Bottom - height - 4));

        Bounds = new Rectangle(x, y, width, height);
        Region?.Dispose();
        using (var path = WindowsDockRenderer.RoundedRect(new RectangleF(0, 0, width, height), 7f))
            Region = new Region(path);

        if (!Visible)
            Show(owner);

        try
        {
            OddSnap.Native.Dwm.TrySetWindowCornerPreference(Handle, OddSnap.Native.Dwm.DWMWCP_ROUND);
            OddSnap.Native.Dwm.TrySetImmersiveDarkMode(Handle, UiChrome.IsDark);
        }
        catch { }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        WindowsDockRenderer.PaintSurface(g, new RectangleF(0, 0, Width, Height), 7f);

        var textRect = new Rectangle(PadX, PadY, Width - PadX * 2, Height - PadY * 2);
        TextRenderer.DrawText(
            g,
            _text,
            _font,
            textRect,
            UiChrome.SurfaceTextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Region?.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
