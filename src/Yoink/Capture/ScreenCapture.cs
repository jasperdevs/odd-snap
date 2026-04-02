using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Native;

namespace Yoink.Capture;

public static class ScreenCapture
{
    public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreens(bool includeCursor = false)
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

        return CaptureAllScreensLegacy(includeCursor);
    }

    public static Bitmap CaptureRegion(Rectangle region, bool includeCursor = false)
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

    private static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreensLegacy(bool includeCursor)
    {
        // Use GetSystemMetrics for physical pixel bounds (DPI-unaware coordinates)
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        var bounds = new Rectangle(left, top, width, height);
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
            using var g = Graphics.FromImage(bitmap);
            IntPtr hdc = g.GetHdc();
            try
            {
                int x = cursorInfo.ptScreenPos.X - captureBounds.X - (int)iconInfo.xHotspot;
                int y = cursorInfo.ptScreenPos.Y - captureBounds.Y - (int)iconInfo.yHotspot;
                User32.DrawIconEx(hdc, x, y, cursorInfo.hCursor, 0, 0, 0, IntPtr.Zero, User32.DI_NORMAL);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        finally
        {
            if (iconInfo.hbmMask != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero)
                Gdi32.DeleteObject(iconInfo.hbmColor);
        }
    }
}
