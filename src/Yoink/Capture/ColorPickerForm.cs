using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Native;

namespace Yoink.Capture;

/// <summary>
/// Full-screen color picker overlay.  Performance-critical path:
///   • Pre-caches all pixel data into a flat int[] (no GetPixel ever).
///   • Pre-renders the dimmed background once.
///   • Pre-renders the magnifier panel into a small bitmap each tick,
///     then blits that single bitmap in OnPaint – no per-cell FillRect,
///     no GraphicsPath, no Region clips per frame.
///   • Only invalidates the two small dirty regions (old + new panel/crosshair).
/// </summary>
public sealed class ColorPickerForm : Form
{
    private readonly Bitmap _dimmed;
    private readonly int[] _pixelData;
    private readonly int _bmpW, _bmpH;
    private readonly Rectangle _virtualBounds;
    private readonly System.Windows.Forms.Timer _trackTimer;

    // Pre-allocated magnifier bitmap (painted once per cursor move, blitted in OnPaint)
    private readonly Bitmap _magBitmap;
    private readonly Graphics _magGfx;
    private readonly int[] _magPixels;   // raw ARGB buffer for the zoom grid

    // Reusable GDI objects
    private readonly Pen _crossPen = new(Color.FromArgb(210, 255, 255, 255), 1f);
    private readonly Font _hexFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _rgbFont = new("Segoe UI", 9f);

    private Point _cursorPos;
    private Point _prevCursorPos;
    private Rectangle _prevDirty;
    private Color _pickedColor = Color.Black;
    private string _hexStr = "#000000";

    private const int GridSize = 9;
    private const int CellPx   = 14;
    private const int MagPx    = GridSize * CellPx;       // 126
    private const int InfoH    = 48;
    private const int Pad      = 10;
    private const int PanelW   = MagPx + Pad * 2;         // 146
    private const int PanelH   = MagPx + InfoH + Pad * 2; // 194
    private const int Offset   = 22;
    private const int DirtyMargin = 6;

    public event Action<string>? ColorPicked;
    public event Action? Cancelled;

    public ColorPickerForm(Bitmap screenshot, Rectangle virtualBounds)
    {
        _virtualBounds = virtualBounds;
        _bmpW = screenshot.Width;
        _bmpH = screenshot.Height;

        // Lock pixels once, copy to managed array, never touch the bitmap again for reads.
        _pixelData = new int[_bmpW * _bmpH];
        var bits = screenshot.LockBits(new Rectangle(0, 0, _bmpW, _bmpH),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bits.Scan0, _pixelData, 0, _pixelData.Length);
        screenshot.UnlockBits(bits);

        // Build dimmed background once.
        _dimmed = new Bitmap(_bmpW, _bmpH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_dimmed))
        {
            g.DrawImage(screenshot, 0, 0);
            using var dim = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, _bmpW, _bmpH);
        }

        // Magnifier panel bitmap – reused every frame.
        _magBitmap = new Bitmap(PanelW, PanelH, PixelFormat.Format32bppArgb);
        _magGfx = Graphics.FromImage(_magBitmap);
        _magGfx.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        _magPixels = new int[PanelW * PanelH];

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualBounds.X, virtualBounds.Y, _bmpW, _bmpH);
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;

        _trackTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _trackTimer.Tick += OnTick;
    }

    // ───────────────────── tick ─────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        User32.GetCursorPos(out var pt);
        var np = new Point(pt.X - _virtualBounds.X, pt.Y - _virtualBounds.Y);
        if (np == _cursorPos) return;

        _prevCursorPos = _cursorPos;
        _cursorPos = np;

        // Build the magnifier bitmap off-screen (cheap: tiny 146x194 bitmap).
        BuildMagnifier();

        // Compute dirty rectangles.
        var oldDirty = _prevDirty;
        var newPanel = PanelScreenRect(_cursorPos);
        var crossOld = CrossRect(_prevCursorPos);
        var crossNew = CrossRect(_cursorPos);
        var newDirty = Rectangle.Union(Rectangle.Union(newPanel, crossNew), crossOld);
        newDirty.Inflate(DirtyMargin, DirtyMargin);
        _prevDirty = newDirty;

        if (!oldDirty.IsEmpty) Invalidate(oldDirty);
        Invalidate(newDirty);
    }

    // ───────────────────── magnifier ─────────────────────

    /// <summary>Renders the magnifier panel into _magBitmap (small fixed-size bitmap).</summary>
    private void BuildMagnifier()
    {
        int cx = Math.Clamp(_cursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_cursorPos.Y, 0, _bmpH - 1);
        _pickedColor = PixelAt(cx, cy);
        _hexStr = $"#{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";

        // ---- Fill magnifier pixels directly into an int[] then blit ----
        // Background color (ARGB for #F5161616 = 245,22,22,22)
        const int bgArgb = unchecked((int)0xF5161616);
        Array.Fill(_magPixels, bgArgb);

        int half = GridSize / 2;
        int gridLineAlpha = 20;

        // Write zoomed pixel cells + grid lines into _magPixels
        for (int gy = 0; gy < GridSize; gy++)
        {
            for (int gx = 0; gx < GridSize; gx++)
            {
                int argb = GetPixelArgb(cx - half + gx, cy - half + gy);
                int cellX = Pad + gx * CellPx;
                int cellY = Pad + gy * CellPx;

                // Fill cell (skip last row/col for gridlines)
                for (int py = 0; py < CellPx - 1; py++)
                {
                    int rowOff = (cellY + py) * PanelW + cellX;
                    for (int px = 0; px < CellPx - 1; px++)
                        _magPixels[rowOff + px] = argb;

                    // Right gridline pixel
                    _magPixels[rowOff + CellPx - 1] = BlendGrid(argb, gridLineAlpha);
                }

                // Bottom gridline row
                int botOff = (cellY + CellPx - 1) * PanelW + cellX;
                int blended = BlendGrid(argb, gridLineAlpha);
                for (int px = 0; px < CellPx; px++)
                    _magPixels[botOff + px] = blended;
            }
        }

        // Center-pixel white border (2px)
        int cX = Pad + half * CellPx;
        int cY = Pad + half * CellPx;
        const int white = unchecked((int)0xFFFFFFFF);
        for (int i = -1; i <= CellPx; i++)
        {
            SetMagPx(cX + i, cY - 1, white);
            SetMagPx(cX + i, cY + CellPx, white);
            SetMagPx(cX - 1, cY + i, white);
            SetMagPx(cX + CellPx, cY + i, white);
        }

        // Write _magPixels into _magBitmap
        var bitsLock = _magBitmap.LockBits(new Rectangle(0, 0, PanelW, PanelH),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        _magBitmap.UnlockBits(bitsLock);

        // Draw text labels with GDI+ (tiny bitmap, fast)
        var g = _magGfx;
        int iy = Pad + MagPx + 8;

        // Color swatch
        using (var sb = new SolidBrush(_pickedColor))
            g.FillRectangle(sb, Pad, iy, 26, 26);

        // Hex + RGB text
        g.DrawString(_hexStr, _hexFont, Brushes.White, Pad + 32, iy - 2);
        using (var mb = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
            g.DrawString($"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}", _rgbFont, mb, Pad + 32, iy + 15);
    }

    private void SetMagPx(int x, int y, int argb)
    {
        if (x >= 0 && x < PanelW && y >= 0 && y < PanelH)
            _magPixels[y * PanelW + x] = argb;
    }

    private static int BlendGrid(int baseArgb, int gridAlpha)
    {
        // Lighten: overlay white at gridAlpha onto the base color.
        int r = ((baseArgb >> 16) & 0xFF) + gridAlpha; if (r > 255) r = 255;
        int gg = ((baseArgb >> 8) & 0xFF) + gridAlpha; if (gg > 255) gg = 255;
        int b = (baseArgb & 0xFF) + gridAlpha;         if (b > 255) b = 255;
        return unchecked((int)0xFF000000) | (r << 16) | (gg << 8) | b;
    }

    // ───────────────────── paint ─────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = e.ClipRectangle;

        // Restore dimmed background for the dirty rect only.
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        // Blit pre-rendered magnifier panel (single DrawImage of a small bitmap).
        var (px, py) = PanelPos(_cursorPos);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_magBitmap, px, py);

        // Panel border (rounded rect – only drawn once, lightweight).
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RRect(new Rectangle(px, py, PanelW, PanelH), 10);
        using var bp = new Pen(Color.FromArgb(45, 255, 255, 255));
        g.DrawPath(bp, path);
        g.SmoothingMode = SmoothingMode.Default;

        // Crosshair
        int mx = _cursorPos.X, my = _cursorPos.Y;
        g.DrawLine(_crossPen, mx - 10, my, mx - 3, my);
        g.DrawLine(_crossPen, mx + 3, my, mx + 10, my);
        g.DrawLine(_crossPen, mx, my - 10, mx, my - 3);
        g.DrawLine(_crossPen, mx, my + 3, mx, my + 10);
    }

    // ───────────────────── geometry helpers ─────────────────────

    private (int px, int py) PanelPos(Point cur)
    {
        int px = cur.X + Offset, py = cur.Y + Offset;
        if (px + PanelW > ClientSize.Width)  px = cur.X - Offset - PanelW;
        if (py + PanelH > ClientSize.Height) py = cur.Y - Offset - PanelH;
        return (Math.Max(4, px), Math.Max(4, py));
    }

    private Rectangle PanelScreenRect(Point cur)
    {
        var (px, py) = PanelPos(cur);
        return new Rectangle(px - DirtyMargin, py - DirtyMargin,
            PanelW + DirtyMargin * 2, PanelH + DirtyMargin * 2);
    }

    private static Rectangle CrossRect(Point c) => new(c.X - 14, c.Y - 14, 28, 28);

    private Color PixelAt(int x, int y)
    {
        if ((uint)x >= (uint)_bmpW || (uint)y >= (uint)_bmpH) return Color.Black;
        return Color.FromArgb(_pixelData[y * _bmpW + x]);
    }

    private int GetPixelArgb(int x, int y)
    {
        if ((uint)x >= (uint)_bmpW || (uint)y >= (uint)_bmpH) return unchecked((int)0xFF000000);
        return _pixelData[y * _bmpW + x];
    }

    // ───────────────────── input ─────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(Handle);
        _trackTimer.Start();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ColorPicked?.Invoke(_hexStr);
        else if (e.Button == MouseButtons.Right)
            Cancelled?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Cancelled?.Invoke();
    }

    // ───────────────────── helpers ─────────────────────

    private static GraphicsPath RRect(Rectangle r, int rad)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trackTimer.Dispose();
            _magGfx.Dispose();
            _magBitmap.Dispose();
            _dimmed.Dispose();
            _crossPen.Dispose();
            _hexFont.Dispose();
            _rgbFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
}
