using System.Drawing;
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

        int samples = 0;
        int darkSamples = 0;
        int stepX = Math.Max(1, bitmap.Width / 12);
        int stepY = Math.Max(1, bitmap.Height / 12);

        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                samples++;
                if (pixel.A <= 4 || (pixel.R <= 6 && pixel.G <= 6 && pixel.B <= 6))
                    darkSamples++;
            }
        }

        return samples > 0 && darkSamples >= (int)(samples * 0.9);
    }
}
