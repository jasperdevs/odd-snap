using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    private bool _pickerReady;
    private bool _pickerBusy;
    private int _lastPickedArgb;
    private int _lastRenderedPickerArgb;
    private int _lastRenderedCapturePickerArgb;
    private Point _lastRenderedPickerPoint = Point.Empty;
    private Point _lastRenderedCapturePickerPoint = Point.Empty;
    private Point _pendingPickerPoint;
    private bool _pickerUpdateQueued;
    private readonly System.Diagnostics.Stopwatch _pickerStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private PickerMagnifierForm? _pickerForm;
    private Point _pendingCapturePickerPoint;
    private bool _capturePickerUpdateQueued;
    private int _captureMagnifierPlacementIndex;
    private int _captureMagnifierDragQuadrant;
    private readonly System.Diagnostics.Stopwatch _capturePickerStopwatch = System.Diagnostics.Stopwatch.StartNew();

    private void OnPickerTick(object? sender, EventArgs e)
    {
        if (_pickerBusy)
            return;

        bool didWork = false;

        if (_pickerUpdateQueued && _pickerStopwatch.ElapsedMilliseconds >= UiChrome.FrameIntervalMs)
        {
            _pickerUpdateQueued = false;
            RenderColorPickerFrame(_pendingPickerPoint);
            didWork = true;
        }

        if (_capturePickerUpdateQueued && _capturePickerStopwatch.ElapsedMilliseconds >= UiChrome.FrameIntervalMs)
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

        if (!didWork && !_pickerUpdateQueued && !_capturePickerUpdateQueued && !_emojiWarmupPending)
            _pickerTimer.Stop();
    }

    private void EnsurePickerForm()
    {
        if (_pickerForm != null) return;
        _pickerForm = new PickerMagnifierForm();
        var _ = _pickerForm.Handle;
        WindowDetector.RegisterIgnoredWindow(_pickerForm.Handle);
    }

    private void EnsureCaptureMagnifierForm()
    {
        if (_captureMagnifierForm != null) return;
        _captureMagnifierForm = new PickerMagnifierForm();
        var _ = _captureMagnifierForm.Handle;
        WindowDetector.RegisterIgnoredWindow(_captureMagnifierForm.Handle);
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
        if (_pickerReady &&
            overlayPoint == _lastRenderedPickerPoint &&
            _lastPickedArgb == _lastRenderedPickerArgb &&
            _pickerForm != null)
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
            _lastRenderedPickerArgb = _lastPickedArgb;
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
            (_mode is CaptureMode.Rectangle or CaptureMode.Center or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker or CaptureMode.Upscale or CaptureMode.Freeform);

        if (isSelectingCapture)
            ResetCaptureMagnifierPlacementOnDragReversal(overlayPoint);

        if (!_pickerBusy && (!isSelectingCapture || _capturePickerStopwatch.ElapsedMilliseconds >= UiChrome.FrameIntervalMs))
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

        if (_lastRenderedCapturePickerPoint == overlayPoint &&
            _lastPickedArgb == _lastRenderedCapturePickerArgb &&
            _captureMagnifierForm != null)
            return;

        _pickerCursorPos = overlayPoint;
        BuildMagnifier();
        EnsureCaptureMagnifierForm();
        var magForm = _captureMagnifierForm;
        if (magForm is null)
            return;

        var avoidRect = GetCaptureMagnifierAvoidBounds();
        var (mx, my) = MagPos(_pickerCursorPos, showInfo: false, avoidRect);
        magForm.Left = mx + _virtualBounds.X - 4;
        magForm.Top = my + _virtualBounds.Y - 4;
        if (!magForm.Visible)
            magForm.Show(this);
        magForm.UpdateMagnifier(_magBitmap, _pickerCursorPos, _pickedColor, _hexStr, _rgbStr, showInfo: false);
        Native.User32.SetWindowPos(magForm.Handle, Native.User32.HWND_TOPMOST, 0, 0, 0, 0,
            Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_NOACTIVATE | Native.User32.SWP_SHOWWINDOW);
        _lastRenderedCapturePickerPoint = overlayPoint;
        _lastRenderedCapturePickerArgb = _lastPickedArgb;
        _capturePickerStopwatch.Restart();
    }

    private void CloseMagWindow()
    {
        if (_pickerForm != null)
            WindowDetector.UnregisterIgnoredWindow(_pickerForm.Handle);
        _pickerForm?.Close();
        _pickerForm?.Dispose();
        _pickerForm = null;
        _pickerUpdateQueued = false;
        _pickerReady = false;
        _capturePickerUpdateQueued = false;
    }

    private void CloseCaptureMagnifier()
    {
        if (_captureMagnifierForm != null)
            WindowDetector.UnregisterIgnoredWindow(_captureMagnifierForm.Handle);
        _captureMagnifierForm?.Close();
        _captureMagnifierForm?.Dispose();
        _captureMagnifierForm = null;
        _capturePickerUpdateQueued = false;
        _lastRenderedCapturePickerPoint = Point.Empty;
        _lastMagnifierSamplePoint = new Point(-1, -1);
        _captureMagnifierPlacementIndex = 0;
        _captureMagnifierDragQuadrant = 0;
        _capturePickerStopwatch.Reset();
        _capturePickerStopwatch.Start();
    }

    private void ResetCaptureMagnifierDragPlacement()
    {
        _captureMagnifierPlacementIndex = 0;
        _captureMagnifierDragQuadrant = 0;
        _lastRenderedCapturePickerPoint = Point.Empty;
    }

    private void ResetCaptureMagnifierPlacementOnDragReversal(Point overlayPoint)
    {
        var quadrant = GetDragQuadrant(_selectionStart, overlayPoint);
        if (quadrant == 0 || quadrant == _captureMagnifierDragQuadrant)
            return;

        _captureMagnifierPlacementIndex = 0;
        _lastRenderedCapturePickerPoint = Point.Empty;
        _captureMagnifierDragQuadrant = quadrant;
    }

    private static int GetDragQuadrant(Point start, Point current)
    {
        int dx = current.X - start.X;
        int dy = current.Y - start.Y;
        if (Math.Abs(dx) < 24 && Math.Abs(dy) < 24)
            return 0;

        int sx = dx < 0 ? -1 : 1;
        int sy = dy < 0 ? -1 : 1;
        return (sx < 0 ? 1 : 2) | (sy < 0 ? 4 : 8);
    }

    private Rectangle GetCaptureMagnifierAvoidBounds()
    {
        if (!_isSelecting)
            return Rectangle.Empty;

        Rectangle selectionBounds = Rectangle.Empty;
        if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
            selectionBounds = _selectionRect;
        else if (_mode == CaptureMode.Freeform && ShouldFillFreeformPreview(_freeformPoints))
            selectionBounds = GetFreeformBounds(_freeformPoints);

        if (selectionBounds.IsEmpty)
            return Rectangle.Empty;

        var readoutBounds = SelectionSizeReadout.GetBounds(
            GetReadoutCursorPoint(),
            selectionBounds,
            _readoutFont,
            ClientRectangle);
        return readoutBounds.IsEmpty
            ? selectionBounds
            : Rectangle.Union(selectionBounds, InflateForRepaint(readoutBounds, 8));
    }

    private void BuildMagnifier()
    {
        var pixelData = GetPixelData();
        int cx = Math.Clamp(_pickerCursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_pickerCursorPos.Y, 0, _bmpH - 1);
        var samplePoint = new Point(cx, cy);
        int argb = pixelData[cy * _bmpW + cx];
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
                    ? pixelData[sy * _bmpW + sx] : unchecked((int)0xFF000000);

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
        try
        {
            Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        }
        finally
        {
            _magBitmap.UnlockBits(bitsLock);
        }
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

    private (int, int) MagPos(Point c, bool showInfo = true, Rectangle avoidRect = default)
    {
        int formW = PickerMagnifierForm.TotalW;
        int formH = PickerMagnifierForm.GetTotalHeight(showInfo);
        int margin = 12;
        int preferredIndex = _captureMagnifierPlacementIndex;
        var (px, py, index) = ResolveMagnifierPosition(c, ClientSize, formW, formH, margin, MagOff, avoidRect, preferredIndex);
        _captureMagnifierPlacementIndex = index;
        return (px, py);
    }

    private static (int x, int y, int index) ResolveMagnifierPosition(
        Point cursor,
        Size clientSize,
        int formW,
        int formH,
        int margin,
        int offset,
        Rectangle avoidRect,
        int preferredIndex)
    {
        var candidates = new[]
        {
            new Point(cursor.X + offset, cursor.Y + offset),
            new Point(cursor.X - offset - formW, cursor.Y + offset),
            new Point(cursor.X + offset, cursor.Y - offset - formH),
            new Point(cursor.X - offset - formW, cursor.Y - offset - formH),
            avoidRect.IsEmpty ? Point.Empty : new Point(avoidRect.Right + offset, cursor.Y - formH / 2),
            avoidRect.IsEmpty ? Point.Empty : new Point(avoidRect.Left - offset - formW, cursor.Y - formH / 2),
            avoidRect.IsEmpty ? Point.Empty : new Point(cursor.X - formW / 2, avoidRect.Bottom + offset),
            avoidRect.IsEmpty ? Point.Empty : new Point(cursor.X - formW / 2, avoidRect.Top - offset - formH)
        };

        if (TryResolveCandidate(preferredIndex, candidates, clientSize, formW, formH, margin, avoidRect, out var preferred))
            return (preferred.X, preferred.Y, preferredIndex);

        for (int i = 0; i < candidates.Length; i++)
        {
            if (i == preferredIndex)
                continue;

            if (TryResolveCandidate(i, candidates, clientSize, formW, formH, margin, avoidRect, out var resolved))
                return (resolved.X, resolved.Y, i);
        }

        var fallback = ClampMagnifier(candidates[0], clientSize, formW, formH, margin);
        return (fallback.X, fallback.Y, 0);
    }

    private static bool TryResolveCandidate(
        int index,
        IReadOnlyList<Point> candidates,
        Size clientSize,
        int formW,
        int formH,
        int margin,
        Rectangle avoidRect,
        out Point resolved)
    {
        resolved = Point.Empty;
        if ((uint)index >= (uint)candidates.Count)
            return false;

        var candidate = candidates[index];
        if (candidate == Point.Empty)
            return false;

        var clamped = ClampMagnifier(candidate, clientSize, formW, formH, margin);
        var rect = new Rectangle(clamped.X, clamped.Y, formW, formH);
        if (!avoidRect.IsEmpty && rect.IntersectsWith(avoidRect))
            return false;

        resolved = clamped;
        return true;
    }

    private static Point ClampMagnifier(Point point, Size clientSize, int formW, int formH, int margin)
        => new(
            Math.Clamp(point.X, margin, Math.Max(margin, clientSize.Width - formW - margin)),
            Math.Clamp(point.Y, margin, Math.Max(margin, clientSize.Height - formH - margin)));
}
