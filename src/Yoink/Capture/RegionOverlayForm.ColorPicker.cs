using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private bool _pickerReady;

    private void OnPickerTick(object? sender, EventArgs e)
    {
        Native.User32.GetCursorPos(out var pt);
        var np = new Point(pt.X - _virtualBounds.X, pt.Y - _virtualBounds.Y);
        if (_pickerReady && np == _pickerCursorPos) return;
        _pickerReady = true;
        _pickerCursorPos = np;
        BuildMagnifier();

        var newDirty = PickerDirtyRect(_pickerCursorPos);
        if (!_pickerPrevDirty.IsEmpty) Invalidate(_pickerPrevDirty);
        Invalidate(newDirty);
        _pickerPrevDirty = newDirty;
    }

    private void BuildMagnifier()
    {
        int cx = Math.Clamp(_pickerCursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_pickerCursorPos.Y, 0, _bmpH - 1);
        int argb = _pixelData[cy * _bmpW + cx];
        _pickedColor = Color.FromArgb(argb);
        _hexStr = $"{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
        _rgbStr = $"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}";

        // Transparent background so glass shows through in info area
        Array.Fill(_magPixels, 0);

        int half = Grid / 2;
        for (int gy = 0; gy < Grid; gy++)
        {
            int sy = cy - half + gy;
            for (int gx = 0; gx < Grid; gx++)
            {
                int sx = cx - half + gx;
                int c = ((uint)sx < (uint)_bmpW && (uint)sy < (uint)_bmpH)
                    ? _pixelData[sy * _bmpW + sx] : unchecked((int)0xFF000000);

                int ox = PPad + gx * Cell;
                int oy = PPad + gy * Cell;
                for (int py = 0; py < Cell - 1; py++)
                {
                    int row = (oy + py) * PW + ox;
                    for (int px = 0; px < Cell - 1; px++)
                        _magPixels[row + px] = c;
                    _magPixels[row + Cell - 1] = Lighten(c, 20);
                }
                int bot = (oy + Cell - 1) * PW + ox;
                int gl = Lighten(c, 20);
                for (int px = 0; px < Cell; px++)
                    _magPixels[bot + px] = gl;
            }
        }

        // Center pixel border
        int bx = PPad + half * Cell, by = PPad + half * Cell;
        const int w = unchecked((int)0xFFFFFFFF);
        for (int i = -1; i <= Cell; i++)
        {
            SetMagPx(bx + i, by - 1, w); SetMagPx(bx + i, by + Cell, w);
            SetMagPx(bx - 1, by + i, w); SetMagPx(bx + Cell, by + i, w);
        }

        // Color swatch
        int swY = PPad + Mag + 8;
        int swArgb = _pickedColor.ToArgb();
        for (int py = 0; py < 26; py++)
        {
            int row = (swY + py) * PW + PPad;
            for (int px = 0; px < 26; px++)
                _magPixels[row + px] = swArgb;
        }

        var bitsLock = _magBitmap.LockBits(new Rectangle(0, 0, PW, PH),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        _magBitmap.UnlockBits(bitsLock);

        int ty = PPad + Mag + 8;
        _magGfx.DrawString(_hexStr, _hexFont, Brushes.White, PPad + 32, ty - 2);
        _magGfx.DrawString(_rgbStr, _rgbFont, _mutedBrush, PPad + 32, ty + 15);
    }

    private void PaintMagnifier(Graphics g)
    {
        var (px, py) = MagPos(_pickerCursorPos);
        var magRect = new Rectangle(px, py, PW, PH);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RRect(magRect, 14);

        // Glass backdrop: blurred screenshot clipped to panel shape
        var oldClip = g.Clip;
        using (var magRegion = new Region(path))
        {
            g.Clip = magRegion;
            if (_blurred != null)
                g.DrawImage(_blurred, magRect, magRect, GraphicsUnit.Pixel);
        }
        g.Clip = oldClip;

        // Dark tint
        using (var tint = new SolidBrush(Color.FromArgb(140, 15, 15, 15)))
            g.FillPath(tint, path);

        // Magnifier pixels on top
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImageUnscaled(_magBitmap, px, py);

        // Border
        using var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;

        int mx = _pickerCursorPos.X, my = _pickerCursorPos.Y;
        g.DrawLine(_crossPen, mx - 10, my, mx - 3, my);
        g.DrawLine(_crossPen, mx + 3, my, mx + 10, my);
        g.DrawLine(_crossPen, mx, my - 10, mx, my - 3);
        g.DrawLine(_crossPen, mx, my + 3, mx, my + 10);
    }

    private void SetMagPx(int x, int y, int v)
    {
        if ((uint)x < (uint)PW && (uint)y < (uint)PH)
            _magPixels[y * PW + x] = v;
    }

    private static int Lighten(int c, int amt)
    {
        int r = Math.Min(((c >> 16) & 0xFF) + amt, 255);
        int gg = Math.Min(((c >> 8) & 0xFF) + amt, 255);
        int b = Math.Min((c & 0xFF) + amt, 255);
        return unchecked((int)0xFF000000) | (r << 16) | (gg << 8) | b;
    }

    private (int, int) MagPos(Point c)
    {
        int px = c.X + MagOff, py = c.Y + MagOff;
        if (px + PW > ClientSize.Width) px = c.X - MagOff - PW;
        if (py + PH > ClientSize.Height) py = c.Y - MagOff - PH;
        return (Math.Max(4, px), Math.Max(4, py));
    }

    private Rectangle PickerDirtyRect(Point cur)
    {
        var (px, py) = MagPos(cur);
        int left = Math.Min(px, cur.X - 14) - MagMargin;
        int top = Math.Min(py, cur.Y - 14) - MagMargin;
        int right = Math.Max(px + PW, cur.X + 14) + MagMargin;
        int bottom = Math.Max(py + PH, cur.Y + 14) + MagMargin;
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
