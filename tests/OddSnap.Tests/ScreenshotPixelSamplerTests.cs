using System.Drawing;
using System.Drawing.Imaging;
using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class ScreenshotPixelSamplerTests
{
    [Fact]
    public void CopyArgbRegionClampsToBitmapBounds()
    {
        using var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 0, Color.Green);
        bitmap.SetPixel(0, 1, Color.Blue);
        bitmap.SetPixel(1, 1, Color.White);

        var pixels = ScreenshotPixelSampler.CopyArgbRegion(
            bitmap,
            new Rectangle(-1, -1, 3, 3),
            out var copiedRegion);

        Assert.Equal(new Rectangle(0, 0, 2, 2), copiedRegion);
        Assert.Equal(4, pixels.Length);
        Assert.Equal(Color.Red.ToArgb(), pixels[0]);
        Assert.Equal(Color.Green.ToArgb(), pixels[1]);
        Assert.Equal(Color.Blue.ToArgb(), pixels[2]);
        Assert.Equal(Color.White.ToArgb(), pixels[3]);
    }

    [Fact]
    public void ReadArgbReturnsOpaqueBlackOutsideBitmap()
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);

        Assert.Equal(ScreenshotPixelSampler.OpaqueBlack, ScreenshotPixelSampler.ReadArgb(bitmap, -1, 0));
        Assert.Equal(ScreenshotPixelSampler.OpaqueBlack, ScreenshotPixelSampler.ReadArgb(bitmap, 2, 0));
        Assert.Equal(ScreenshotPixelSampler.OpaqueBlack, ScreenshotPixelSampler.ReadArgb(bitmap, 0, 2));
    }
}
