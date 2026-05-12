using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OddSnap.Capture;

internal static class ScreenshotPixelSampler
{
    public const int OpaqueBlack = unchecked((int)0xFF000000);

    public static int ReadArgb(Bitmap bitmap, int x, int y)
    {
        if ((uint)x >= (uint)bitmap.Width || (uint)y >= (uint)bitmap.Height)
            return OpaqueBlack;

        var rect = new Rectangle(x, y, 1, 1);
        try
        {
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                return Marshal.ReadInt32(data.Scan0);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch
        {
            try { return bitmap.GetPixel(x, y).ToArgb(); }
            catch { return OpaqueBlack; }
        }
    }

    public static int[] CopyArgbRegion(Bitmap bitmap, Rectangle requestedRegion, out Rectangle copiedRegion)
    {
        copiedRegion = Rectangle.Intersect(
            requestedRegion,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (copiedRegion.Width <= 0 || copiedRegion.Height <= 0)
            return Array.Empty<int>();

        var pixels = new int[copiedRegion.Width * copiedRegion.Height];
        try
        {
            var data = bitmap.LockBits(copiedRegion, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < copiedRegion.Height; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(data.Scan0, y * data.Stride),
                        pixels,
                        y * copiedRegion.Width,
                        copiedRegion.Width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch
        {
            try
            {
                for (int y = 0; y < copiedRegion.Height; y++)
                {
                    for (int x = 0; x < copiedRegion.Width; x++)
                    {
                        pixels[(y * copiedRegion.Width) + x] = bitmap.GetPixel(
                            copiedRegion.X + x,
                            copiedRegion.Y + y).ToArgb();
                    }
                }
            }
            catch
            {
                Array.Fill(pixels, OpaqueBlack);
            }
        }

        return pixels;
    }
}
