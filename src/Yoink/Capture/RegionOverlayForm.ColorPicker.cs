using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private bool _pickerReady;
    private bool _pickerBusy;
    private int _lastPickedArgb;
    private PickerMagnifierForm? _pickerForm;

    private void OnPickerTick(object? sender, EventArgs e)
    {
        // Timer path disabled; picker is now driven directly from OnMouseMove.
    }

    private void EnsurePickerForm()
    {
        if (_pickerForm != null) return;
        _pickerForm = new PickerMagnifierForm();
        _pickerForm.Show(this);
    }

    internal void UpdateColorPicker(Point overlayPoint)
    {
        if (_pickerBusy) return;
        if (_pickerReady && overlayPoint == _pickerCursorPos) return;

        _pickerBusy = true;
        _pickerReady = true;
        _pickerCursorPos = overlayPoint;
        BuildMagnifier();
        EnsurePickerForm();

        var (mx, my) = MagPos(_pickerCursorPos);
        _pickerForm!.UpdateMagnifier(_magBitmap, _pickerCursorPos, _pickedColor, _hexStr, _rgbStr);
        _pickerForm.Left = mx + _virtualBounds.X - 4;
        _pickerForm.Top = my + _virtualBounds.Y - 4;

        _pickerBusy = false;
    }

    private void CloseMagWindow()
    {
        _pickerForm?.Close();
        _pickerForm?.Dispose();
        _pickerForm = null;
    }

    private void BuildMagnifier()
    {
        int cx = Math.Clamp(_pickerCursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_pickerCursorPos.Y, 0, _bmpH - 1);
        int argb = _pixelData[cy * _bmpW + cx];
        bool colorChanged = argb != _lastPickedArgb;
        _lastPickedArgb = argb;
        _pickedColor = Color.FromArgb(argb);
        if (colorChanged)
        {
            _hexStr = $"{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
            _rgbStr = $"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}";
        }

        // Fill grid pixels directly into the mag bitmap buffer
        Array.Fill(_magPixels, unchecked((int)0xFF202020));

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
                    _magPixels[row + Cell - 1] = Lighten(c, 15);
                }
                int bot = (oy + Cell - 1) * PW + ox;
                int gl = Lighten(c, 15);
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

        // Draw the text layer AFTER copying the pixel buffer, every frame.
        // The buffer copy overwrites the lower info area, so conditional redraw caused blank panels.
        int ty = PPad + Mag + 8;
        using var clearBrush = new SolidBrush(Color.FromArgb(255, 32, 32, 32));
        _magGfx.FillRectangle(clearBrush, PPad + 30, ty - 4, PW - PPad - 32, InfoH);
        _magGfx.DrawString(_hexStr, _hexFont, Brushes.White, PPad + 32, ty - 2);
        _magGfx.DrawString(_rgbStr, _rgbFont, _mutedBrush, PPad + 32, ty + 15);
    }

    // Overlay no longer paints the color picker or its crosshair.

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
}
