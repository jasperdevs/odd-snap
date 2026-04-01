using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Native;

namespace Yoink.Capture;

public static class ScreenCapture
{
    public static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreens()
    {
        try
        {
            var capture = DxgiScreenCapture.CaptureAllScreens();
            if (!IsLikelyInvalidCapture(capture.Bitmap))
                return capture;

            capture.Bitmap.Dispose();
        }
        catch
        {
        }

        return CaptureAllScreensLegacy();
    }

    public static Bitmap CaptureRegion(Rectangle region)
    {
        try
        {
            var capture = DxgiScreenCapture.CaptureRegion(region);
            if (!IsLikelyInvalidCapture(capture))
                return capture;

            capture.Dispose();
        }
        catch
        {
        }

        return CaptureRegionLegacy(region);
    }

    /// <summary>
    /// Uses BitBlt directly for sustained frame capture workloads. The current DXGI path
    /// recreates device/duplication resources per call, which is too expensive for recording.
    /// </summary>
    public static Bitmap CaptureRegionForRecording(Rectangle region) => CaptureRegionLegacy(region);

    private static (Bitmap Bitmap, Rectangle Bounds) CaptureAllScreensLegacy()
    {
        // Use GetSystemMetrics for physical pixel bounds (DPI-unaware coordinates)
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        var bounds = new Rectangle(left, top, width, height);
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
        IntPtr hdcDest = graphics.GetHdc();

        Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcScreen, left, top, User32.SRCCOPY);

        graphics.ReleaseHdc(hdcDest);
        User32.ReleaseDC(IntPtr.Zero, hdcScreen);

        return (bitmap, bounds);
    }

    /// <summary>Captures a specific screen region directly via BitBlt. Used by GIF recorder.</summary>
    private static Bitmap CaptureRegionLegacy(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
        IntPtr hdcDest = g.GetHdc();
        Gdi32.BitBlt(hdcDest, 0, 0, region.Width, region.Height, hdcScreen, region.X, region.Y, User32.SRCCOPY);
        g.ReleaseHdc(hdcDest);
        User32.ReleaseDC(IntPtr.Zero, hdcScreen);
        return bmp;
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
        try
        {
            int stride = data.Stride;
            int samples = 0;
            int darkSamples = 0;
            var row = new byte[bitmap.Width * 4];

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, row.Length);
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
            bitmap.UnlockBits(data);
        }
    }
}
