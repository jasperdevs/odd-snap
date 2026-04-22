using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Native;

namespace OddSnap.Capture;

public static class ScreenCapture
{
    public static Rectangle GetVirtualScreenBounds()
    {
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        return new Rectangle(left, top, width, height);
    }

    public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreens(bool includeCursor = false)
    {
        var bounds = GetVirtualScreenBounds();
        return CaptureWindowExclusion.RunWithoutIntersectingWindows(bounds, () => CaptureAllScreensCore(includeCursor, bounds));
    }

    private static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreensCore(bool includeCursor, Rectangle bounds)
    {
        try
        {
            var capture = DxgiScreenCapture.CaptureAllScreens();
            if (!IsLikelyInvalidCapture(capture.Bitmap))
            {
                if (includeCursor)
                    DrawCursor(capture.Bitmap, capture.Bounds);
                return capture;
            }

            capture.Bitmap.Dispose();
            DxgiScreenCapture.ResetCache();
        }
        catch
        {
            DxgiScreenCapture.ResetCache();
        }

        return CaptureAllScreensLegacy(includeCursor, bounds);
    }

    /// <summary>Captures only the monitor that currently contains the cursor.</summary>
    public static (Bitmap Bitmap, Rectangle Bounds) CaptureCurrentScreen(bool includeCursor = false)
    {
        Screen screen;
        try { screen = Screen.FromPoint(System.Windows.Forms.Cursor.Position); }
        catch { screen = Screen.PrimaryScreen ?? Screen.AllScreens[0]; }

        var bounds = screen.Bounds;
        var bmp = CaptureRegion(bounds, includeCursor);
        return (bmp, bounds);
    }

    public static Bitmap CaptureRegion(Rectangle region, bool includeCursor = false)
        => CaptureWindowExclusion.RunWithoutIntersectingWindows(region, () => CaptureRegionCore(region, includeCursor));

    private static Bitmap CaptureRegionCore(Rectangle region, bool includeCursor)
    {
        try
        {
            var capture = DxgiScreenCapture.CaptureRegion(region);
            if (!IsLikelyInvalidCapture(capture))
            {
                if (includeCursor)
                    DrawCursor(capture, region);
                return capture;
            }

            capture.Dispose();
            DxgiScreenCapture.ResetCache();
        }
        catch
        {
            DxgiScreenCapture.ResetCache();
        }

        return CaptureRegionLegacy(region, includeCursor);
    }

    /// <summary>
    /// Uses BitBlt directly for sustained frame capture workloads. The current DXGI path
    /// recreates device/duplication resources per call, which is too expensive for recording.
    /// </summary>
    public static Bitmap CaptureRegionForRecording(Rectangle region, bool includeCursor = false)
        => CaptureRegionLegacy(region, includeCursor);

    internal static RecordingFrameCapturer CreateRecordingFrameCapturer(Rectangle region, bool includeCursor = false)
        => new(region, includeCursor);

    private static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreensLegacy(bool includeCursor, Rectangle bounds)
    {
        int left = bounds.Left;
        int top = bounds.Top;
        int width = bounds.Width;
        int height = bounds.Height;
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
            IntPtr hdcDest = IntPtr.Zero;
            try
            {
                hdcDest = graphics.GetHdc();
                bool ok = Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcScreen, left, top, User32.SRCCOPY);
                if (!ok)
                    throw new InvalidOperationException("Screen capture failed (BitBlt returned false).");
            }
            finally
            {
                if (hdcDest != IntPtr.Zero)
                    graphics.ReleaseHdc(hdcDest);
                User32.ReleaseDC(IntPtr.Zero, hdcScreen);
            }

            if (includeCursor)
                DrawCursor(bitmap, bounds);

            return (bitmap, bounds);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Captures a specific screen region directly via BitBlt. Used by GIF recorder.</summary>
    private static Bitmap CaptureRegionLegacy(Rectangle region, bool includeCursor)
    {
        var bmp = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
            IntPtr hdcDest = IntPtr.Zero;
            try
            {
                hdcDest = g.GetHdc();
                bool ok = Gdi32.BitBlt(hdcDest, 0, 0, region.Width, region.Height, hdcScreen, region.X, region.Y, User32.SRCCOPY);
                if (!ok)
                    throw new InvalidOperationException("Screen capture failed (BitBlt returned false).");
            }
            finally
            {
                if (hdcDest != IntPtr.Zero)
                    g.ReleaseHdc(hdcDest);
                User32.ReleaseDC(IntPtr.Zero, hdcScreen);
            }

            if (includeCursor)
                DrawCursor(bmp, region);

            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    public static Bitmap CropRegion(Bitmap fullScreenshot, Rectangle selection)
    {
        // Clamp to bitmap bounds
        int x = Math.Max(0, selection.X);
        int y = Math.Max(0, selection.Y);
        int w = Math.Min(selection.Width, fullScreenshot.Width - x);
        int h = Math.Min(selection.Height, fullScreenshot.Height - y);

        if (w <= 0 || h <= 0)
            return new Bitmap(1, 1);

        var cropRect = new Rectangle(x, y, w, h);
        return fullScreenshot.Clone(cropRect, fullScreenshot.PixelFormat);
    }

    private static bool IsLikelyInvalidCapture(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        int stepX = Math.Max(1, bitmap.Width / 12);
        int stepY = Math.Max(1, bitmap.Height / 12);

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int rowLength = bitmap.Width * 4;
        var row = ArrayPool<byte>.Shared.Rent(rowLength);
        try
        {
            int stride = data.Stride;
            int samples = 0;
            int darkSamples = 0;

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, rowLength);
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    int i = x * 4;
                    byte b = row[i], g = row[i + 1], r = row[i + 2], a = row[i + 3];
                    samples++;
                    if (a <= 4 || (r <= 6 && g <= 6 && b <= 6))
                        darkSamples++;
                }
            }

            return samples > 0 && darkSamples >= (int)(samples * 0.9);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
            bitmap.UnlockBits(data);
        }
    }

    private static void DrawCursor(Bitmap bitmap, Rectangle captureBounds)
    {
        var cursorInfo = new User32.CURSORINFO
        {
            cbSize = Marshal.SizeOf<User32.CURSORINFO>()
        };

        if (!User32.GetCursorInfo(ref cursorInfo))
            return;

        if ((cursorInfo.flags & User32.CURSOR_SHOWING) == 0 || cursorInfo.hCursor == IntPtr.Zero)
            return;

        if (cursorInfo.ptScreenPos.X < captureBounds.Left || cursorInfo.ptScreenPos.X >= captureBounds.Right ||
            cursorInfo.ptScreenPos.Y < captureBounds.Top || cursorInfo.ptScreenPos.Y >= captureBounds.Bottom)
            return;

        if (!User32.GetIconInfo(cursorInfo.hCursor, out var iconInfo))
            return;

        try
        {
            using var cursorIcon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(cursorInfo.hCursor).Clone();
            using var cursorBmp = cursorIcon.ToBitmap();
            using var g = Graphics.FromImage(bitmap);
            int x = cursorInfo.ptScreenPos.X - captureBounds.X - (int)iconInfo.xHotspot;
            int y = cursorInfo.ptScreenPos.Y - captureBounds.Y - (int)iconInfo.yHotspot;
            g.DrawImageUnscaled(cursorBmp, x, y);
        }
        finally
        {
            if (iconInfo.hbmMask != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmColor);
        }
    }

    private static void DrawCursor(IntPtr hdc, Rectangle captureBounds)
    {
        var cursorInfo = new User32.CURSORINFO
        {
            cbSize = Marshal.SizeOf<User32.CURSORINFO>()
        };

        if (!User32.GetCursorInfo(ref cursorInfo))
            return;

        if ((cursorInfo.flags & User32.CURSOR_SHOWING) == 0 || cursorInfo.hCursor == IntPtr.Zero)
            return;

        if (cursorInfo.ptScreenPos.X < captureBounds.Left || cursorInfo.ptScreenPos.X >= captureBounds.Right ||
            cursorInfo.ptScreenPos.Y < captureBounds.Top || cursorInfo.ptScreenPos.Y >= captureBounds.Bottom)
            return;

        if (!User32.GetIconInfo(cursorInfo.hCursor, out var iconInfo))
            return;

        try
        {
            int x = cursorInfo.ptScreenPos.X - captureBounds.X - (int)iconInfo.xHotspot;
            int y = cursorInfo.ptScreenPos.Y - captureBounds.Y - (int)iconInfo.yHotspot;
            User32.DrawIconEx(hdc, x, y, cursorInfo.hCursor, 0, 0, 0, IntPtr.Zero, User32.DI_NORMAL);
        }
        finally
        {
            if (iconInfo.hbmMask != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmColor);
        }
    }

    internal sealed class RecordingFrameCapturer : IDisposable
    {
        private readonly Rectangle _region;
        private readonly bool _includeCursor;
        private readonly Bitmap _bitmap;
        private readonly Graphics _graphics;
        private readonly IntPtr _hdcScreen;
        private bool _disposed;

        public RecordingFrameCapturer(Rectangle region, bool includeCursor)
        {
            _region = region;
            _includeCursor = includeCursor;
            _bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            _graphics = Graphics.FromImage(_bitmap);
            _hdcScreen = User32.GetDC(IntPtr.Zero);
            if (_hdcScreen == IntPtr.Zero)
                throw new InvalidOperationException("Screen capture failed (GetDC returned null).");
        }

        public int BufferByteCount => _region.Width * _region.Height * 4;

        public byte[] CaptureToBuffer(byte[]? buffer)
        {
            ThrowIfDisposed();
            if (buffer is null || buffer.Length != BufferByteCount)
                buffer = new byte[BufferByteCount];

            CaptureCurrentFrame();

            var rect = new Rectangle(0, 0, _bitmap.Width, _bitmap.Height);
            var data = _bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int byteCount = data.Stride * data.Height;
                if (buffer.Length != byteCount)
                    buffer = new byte[byteCount];
                Marshal.Copy(data.Scan0, buffer, 0, byteCount);
                return buffer;
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }
        }

        private void CaptureCurrentFrame()
        {
            IntPtr hdcDest = IntPtr.Zero;
            try
            {
                hdcDest = _graphics.GetHdc();
                bool ok = Gdi32.BitBlt(hdcDest, 0, 0, _region.Width, _region.Height, _hdcScreen, _region.X, _region.Y, User32.SRCCOPY);
                if (!ok)
                    throw new InvalidOperationException("Screen capture failed (BitBlt returned false).");

                if (_includeCursor)
                    DrawCursor(hdcDest, _region);
            }
            finally
            {
                if (hdcDest != IntPtr.Zero)
                    _graphics.ReleaseHdc(hdcDest);
            }
        }

        public Bitmap CloneCurrentFrame()
        {
            ThrowIfDisposed();
            return new Bitmap(_bitmap);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_hdcScreen != IntPtr.Zero)
                User32.ReleaseDC(IntPtr.Zero, _hdcScreen);
            _graphics.Dispose();
            _bitmap.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecordingFrameCapturer));
        }
    }
}
