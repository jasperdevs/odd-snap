using System.Drawing;
using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class ScrollingCaptureFrameTests
{
    [Fact]
    public void EstimateNewContentHeight_DetectsVerticalScrollDelta()
    {
        using var previous = CreateScrollableFrame(96, 120, yOffset: 0);
        using var current = CreateScrollableFrame(96, 120, yOffset: 40);

        int newContent = ScrollingCaptureForm.EstimateNewContentHeight(previous, current);

        Assert.InRange(newContent, 36, 44);
    }

    [Fact]
    public void TryEstimateNewContentHeight_RejectsUnrelatedFrames()
    {
        using var previous = CreateScrollableFrame(96, 120, yOffset: 0);
        using var current = CreateDifferentFrame(96, 120);

        Assert.False(ScrollingCaptureForm.TryEstimateNewContentHeight(previous, current, out _));
    }

    [Fact]
    public void EstimateNewContentHeight_IgnoresFixedTopHeader()
    {
        using var previous = CreateScrollableFrameWithHeader(96, 120, yOffset: 0, headerHeight: 16);
        using var current = CreateScrollableFrameWithHeader(96, 120, yOffset: 40, headerHeight: 16);

        int newContent = ScrollingCaptureForm.EstimateNewContentHeight(previous, current);

        Assert.InRange(newContent, 36, 44);
    }

    [Fact]
    public void EstimateNewContentHeight_DetectsLargeFastScrollDelta()
    {
        using var previous = CreateScrollableFrame(96, 120, yOffset: 0);
        using var current = CreateScrollableFrame(96, 120, yOffset: 100);

        int newContent = ScrollingCaptureForm.EstimateNewContentHeight(previous, current);

        Assert.InRange(newContent, 96, 104);
    }

    [Fact]
    public void AreFramesDuplicate_ReturnsTrueForIdenticalFrames()
    {
        using var previous = CreateScrollableFrame(96, 120, yOffset: 0);
        using var current = CreateScrollableFrame(96, 120, yOffset: 0);

        Assert.True(ScrollingCaptureForm.AreFramesDuplicate(previous, current));
    }

    [Fact]
    public void AreFramesDuplicate_ReturnsFalseForScrolledFrames()
    {
        using var previous = CreateScrollableFrame(96, 120, yOffset: 0);
        using var current = CreateScrollableFrame(96, 120, yOffset: 40);

        Assert.False(ScrollingCaptureForm.AreFramesDuplicate(previous, current));
    }

    private static Bitmap CreateScrollableFrame(int width, int height, int yOffset)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int absoluteY = y + yOffset;
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(
                    absoluteY % 256,
                    (absoluteY * 2 + x) % 256,
                    (absoluteY * 3 + x * 2) % 256));
            }
        }

        return bitmap;
    }

    private static Bitmap CreateDifferentFrame(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(
                    (x * 11 + y * 7) % 256,
                    (x * 5 + y * 13) % 256,
                    (x * 17 + y * 3) % 256));
            }
        }

        return bitmap;
    }

    private static Bitmap CreateScrollableFrameWithHeader(int width, int height, int yOffset, int headerHeight)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (y < headerHeight)
                {
                    bitmap.SetPixel(x, y, Color.FromArgb(30, 40, 50));
                    continue;
                }

                int absoluteY = y - headerHeight + yOffset;
                bitmap.SetPixel(x, y, Color.FromArgb(
                    absoluteY % 256,
                    (absoluteY * 2 + x) % 256,
                    (absoluteY * 3 + x * 2) % 256));
            }
        }

        return bitmap;
    }
}
