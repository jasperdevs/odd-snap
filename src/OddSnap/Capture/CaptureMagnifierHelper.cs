using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OddSnap.Helpers;
using OddSnap.Native;

namespace OddSnap.Capture;

/// <summary>
/// Shared magnifier logic used across all capture forms (region overlay, recording, scrolling).
/// Manages a single PickerMagnifierForm instance and builds the magnifier bitmap from screenshot data.
/// </summary>
internal sealed class CaptureMagnifierHelper : IDisposable
{
    private const int Grid = 11, Cell = 14, Mag = Grid * Cell;
    private const int PW = Mag, PH = Mag;

    private readonly Bitmap _magBitmap = new(PW, PH, PixelFormat.Format32bppArgb);
    private readonly int[] _magPixels = new int[PW * PH];
    private readonly System.Diagnostics.Stopwatch _throttle = System.Diagnostics.Stopwatch.StartNew();
    private PickerMagnifierForm? _form;
    private Point _lastSamplePoint = new(-1, -1);
    private int[]? _pixelData;
    private int _bmpW, _bmpH;
    private Color _pickedColor;
    private string _hexStr = "";
    private string _rgbStr = "";

    /// <summary>
    /// Caches pixel data from the screenshot for fast magnifier rendering.
    /// Call once after creating the helper with the screenshot bitmap.
    /// </summary>
    public void CachePixelData(Bitmap screenshot)
    {
        _bmpW = screenshot.Width;
        _bmpH = screenshot.Height;
        _pixelData = new int[_bmpW * _bmpH];
        var bits = screenshot.LockBits(new Rectangle(0, 0, _bmpW, _bmpH),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bits.Scan0, _pixelData, 0, _pixelData.Length);
        }
        finally
        {
            screenshot.UnlockBits(bits);
        }
    }

    /// <summary>
    /// Shows or updates the magnifier at the given cursor position (in form/overlay coords).
    /// </summary>
    public void Update(Point cursorInForm, Form owner, Rectangle virtualBounds)
    {
        if (_pixelData is null) return;
        if (_throttle.ElapsedMilliseconds < UiChrome.FrameIntervalMs && _form?.Visible == true) return;

        int cx = Math.Clamp(cursorInForm.X, 0, _bmpW - 1);
        int cy = Math.Clamp(cursorInForm.Y, 0, _bmpH - 1);
        var samplePoint = new Point(cx, cy);

        int argb = _pixelData[cy * _bmpW + cx];
        _pickedColor = Color.FromArgb(argb);
        _hexStr = $"{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
        _rgbStr = $"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}";

        if (samplePoint != _lastSamplePoint)
        {
            _lastSamplePoint = samplePoint;
            BuildMagnifierBitmap(cx, cy);
        }

        EnsureForm(owner);
        var form = _form!;

        var (mx, my) = CalcPosition(cursorInForm, owner.ClientSize);
        form.Left = mx + virtualBounds.X - 4;
        form.Top = my + virtualBounds.Y - 4;
        if (!form.Visible)
            form.Show(owner);
        form.UpdateMagnifier(_magBitmap, cursorInForm, _pickedColor, _hexStr, _rgbStr, showInfo: false);
        _throttle.Restart();
    }

    /// <summary>
    /// Hides and disposes the magnifier window.
    /// </summary>
    public void Close()
    {
        if (_form != null)
            WindowDetector.UnregisterIgnoredWindow(_form.Handle);
        _form?.Close();
        _form?.Dispose();
        _form = null;
        _lastSamplePoint = new Point(-1, -1);
    }

    public bool IsVisible => _form?.Visible == true;

    private void EnsureForm(Form owner)
    {
        if (_form != null) return;
        _form = new PickerMagnifierForm();
        _ = _form.Handle;
        WindowDetector.RegisterIgnoredWindow(_form.Handle);
    }

    private void BuildMagnifierBitmap(int cx, int cy)
    {
        var pixelData = _pixelData!;
        Array.Fill(_magPixels, unchecked((int)0xFF202020));

        int half = Grid / 2;
        for (int gy = 0; gy < Grid; gy++)
        {
            int sy = cy - half + gy;
            for (int gx = 0; gx < Grid; gx++)
            {
                int sx = cx - half + gx;
                int c = ((uint)sx < (uint)_bmpW && (uint)sy < (uint)_bmpH)
                    ? pixelData[sy * _bmpW + sx] : unchecked((int)0xFF000000);

                int ox = gx * Cell;
                int oy = gy * Cell;
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

        // Center pixel border (white)
        int bx = half * Cell, by2 = half * Cell;
        const int w = unchecked((int)0xFFFFFFFF);
        for (int i = -1; i <= Cell; i++)
        {
            SetPx(bx + i, by2 - 1, w); SetPx(bx + i, by2 + Cell, w);
            SetPx(bx - 1, by2 + i, w); SetPx(bx + Cell, by2 + i, w);
        }

        var bitsLock = _magBitmap.LockBits(new Rectangle(0, 0, PW, PH),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        _magBitmap.UnlockBits(bitsLock);
    }

    private void SetPx(int x, int y, int v)
    {
        if ((uint)x < (uint)PW && (uint)y < (uint)PH)
            _magPixels[y * PW + x] = v;
    }

    private static int Lighten(int c, int amt)
    {
        int r = Math.Min(((c >> 16) & 0xFF) + amt, 255);
        int g = Math.Min(((c >> 8) & 0xFF) + amt, 255);
        int b = Math.Min((c & 0xFF) + amt, 255);
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    private static (int x, int y) CalcPosition(Point cursor, Size clientSize)
    {
        int formW = 152, formH = 152;
        int margin = 12, offset = 8;
        int px = cursor.X + offset + 4, py = cursor.Y + offset + 4;
        if (px + formW > clientSize.Width - margin) px = cursor.X - offset - formW;
        if (py + formH > clientSize.Height - margin) py = cursor.Y - offset - formH;
        px = Math.Clamp(px, margin, Math.Max(margin, clientSize.Width - formW - margin));
        py = Math.Clamp(py, margin, Math.Max(margin, clientSize.Height - formH - margin));
        return (px, py);
    }

    public void Dispose()
    {
        Close();
        _magBitmap.Dispose();
    }
}
