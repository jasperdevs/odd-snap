using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private bool _pickerReady;
    private bool _pickerBusy;
    private int _lastPickedArgb;
    private Point _lastRenderedPickerPoint = Point.Empty;
    private Point _lastRenderedCapturePickerPoint = Point.Empty;
    private Point _pendingPickerPoint;
    private bool _pickerUpdateQueued;
    private readonly System.Diagnostics.Stopwatch _pickerStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private PickerMagnifierForm? _pickerForm;
    private Point _pendingCapturePickerPoint;
    private bool _capturePickerUpdateQueued;
    private readonly System.Diagnostics.Stopwatch _capturePickerStopwatch = System.Diagnostics.Stopwatch.StartNew();

    private void OnPickerTick(object? sender, EventArgs e)
    {
        if (_pickerBusy)
            return;

        bool didWork = false;

        if (_pickerUpdateQueued && _pickerStopwatch.ElapsedMilliseconds >= 16)
        {
            _pickerUpdateQueued = false;
            RenderColorPickerFrame(_pendingPickerPoint);
            didWork = true;
        }

        if (_capturePickerUpdateQueued && _capturePickerStopwatch.ElapsedMilliseconds >= 16)
        {
            _capturePickerUpdateQueued = false;
            RenderCaptureMagnifierFrame(_pendingCapturePickerPoint);
            didWork = true;
        }

        if (_emojiWarmupPending && _emojiPickerOpen)
        {
            WarmEmojiPickerCacheBatch();
            didWork = true;
        }

        if (!didWork && !_pickerUpdateQueued && !_emojiWarmupPending)
            _pickerTimer.Stop();
    }

    private void EnsurePickerForm()
    {
        if (_pickerForm != null) return;
        _pickerForm = new PickerMagnifierForm();
        var _ = _pickerForm.Handle;
    }

    private void EnsureCaptureMagnifierForm()
    {
        if (_captureMagnifierForm != null) return;
        _captureMagnifierForm = new PickerMagnifierForm();
        var _ = _captureMagnifierForm.Handle;
    }

    internal void UpdateColorPicker(Point overlayPoint)
    {
        _pendingPickerPoint = overlayPoint;
        _pickerUpdateQueued = true;
        if (!_pickerBusy)
            RenderColorPickerFrame(overlayPoint);
    }

    private void RenderColorPickerFrame(Point overlayPoint)
    {
        if (_pickerBusy) return;
        if (_pickerReady && overlayPoint == _lastRenderedPickerPoint && _pickerForm != null)
            return;

        _pickerBusy = true;
        try
        {
            _pickerReady = true;
            _pickerCursorPos = overlayPoint;
            BuildMagnifier();
            EnsurePickerForm();
            var pickerForm = _pickerForm;
            if (pickerForm is null)
                return;

            var (mx, my) = MagPos(_pickerCursorPos, showInfo: true);
            pickerForm.Left = mx + _virtualBounds.X - 4;
            pickerForm.Top = my + _virtualBounds.Y - 4;
            if (!pickerForm.Visible)
                pickerForm.Show(this);
            pickerForm.UpdateMagnifier(_magBitmap, _pickerCursorPos, _pickedColor, _hexStr, _rgbStr);
            _lastRenderedPickerPoint = overlayPoint;
            _pickerStopwatch.Restart();
        }
        finally
        {
            _pickerBusy = false;
        }
    }

    internal void UpdateCaptureMagnifier(Point overlayPoint)
    {
        if (!ShouldShowCaptureMagnifierAt(overlayPoint))
        {
            CloseCaptureMagnifier();
            return;
        }

        _pendingCapturePickerPoint = overlayPoint;
        _capturePickerUpdateQueued = true;
        bool isSelectingCapture = _isSelecting &&
            (_mode is CaptureMode.Rectangle or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker);

        if (!_pickerBusy && (!isSelectingCapture || _capturePickerStopwatch.ElapsedMilliseconds >= 16))
        {
            RenderCaptureMagnifierFrame(overlayPoint);
        }
        else if (!_pickerTimer.Enabled)
        {
            _pickerTimer.Start();
        }
    }

    private void RenderCaptureMagnifierFrame(Point overlayPoint)
    {
        if (!ShouldShowCaptureMagnifierAt(overlayPoint))
        {
            CloseCaptureMagnifier();
            return;
        }

        if (_lastRenderedCapturePickerPoint == overlayPoint && _captureMagnifierForm != null)
            return;

        _pickerCursorPos = overlayPoint;
        BuildMagnifier();
        EnsureCaptureMagnifierForm();
        var magForm = _captureMagnifierForm;
        if (magForm is null)
            return;

        var (mx, my) = MagPos(_pickerCursorPos, showInfo: false);
        magForm.Left = mx + _virtualBounds.X - 4;
        magForm.Top = my + _virtualBounds.Y - 4;
        if (!magForm.Visible)
            magForm.Show(this);
        magForm.UpdateMagnifier(_magBitmap, _pickerCursorPos, _pickedColor, _hexStr, _rgbStr, showInfo: false);
        _lastRenderedCapturePickerPoint = overlayPoint;
        _capturePickerStopwatch.Restart();
    }

    private void CloseMagWindow()
    {
        _pickerForm?.Close();
        _pickerForm?.Dispose();
        _pickerForm = null;
        _pickerUpdateQueued = false;
        _pickerReady = false;
        _capturePickerUpdateQueued = false;
    }

    private void CloseCaptureMagnifier()
    {
        _captureMagnifierForm?.Close();
        _captureMagnifierForm?.Dispose();
        _captureMagnifierForm = null;
        _capturePickerUpdateQueued = false;
        _lastRenderedCapturePickerPoint = Point.Empty;
        _lastMagnifierSamplePoint = new Point(-1, -1);
        _capturePickerStopwatch.Reset();
        _capturePickerStopwatch.Start();
    }

    private void BuildMagnifier()
    {
        int cx = Math.Clamp(_pickerCursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_pickerCursorPos.Y, 0, _bmpH - 1);
        var samplePoint = new Point(cx, cy);
        int argb = _pixelData[cy * _bmpW + cx];
        bool colorChanged = argb != _lastPickedArgb;
        bool sampleChanged = samplePoint != _lastMagnifierSamplePoint;
        _lastPickedArgb = argb;
        _pickedColor = Color.FromArgb(argb);
        if (colorChanged)
        {
            _hexStr = $"{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
            _rgbStr = $"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}";
        }

        if (!sampleChanged)
            return;

        _lastMagnifierSamplePoint = samplePoint;

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

        var bitsLock = _magBitmap.LockBits(new Rectangle(0, 0, PW, PH),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        _magBitmap.UnlockBits(bitsLock);
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

    private (int, int) MagPos(Point c, bool showInfo = true)
    {
        int formW = 152;
        int formH = showInfo ? 196 : 152;
        int margin = 12;
        int px = c.X + MagOff, py = c.Y + MagOff;
        if (px + formW > ClientSize.Width - margin) px = c.X - MagOff - formW;
        if (py + formH > ClientSize.Height - margin) py = c.Y - MagOff - formH;
        px = Math.Clamp(px, margin, Math.Max(margin, ClientSize.Width - formW - margin));
        py = Math.Clamp(py, margin, Math.Max(margin, ClientSize.Height - formH - margin));
        return (px, py);
    }
}
