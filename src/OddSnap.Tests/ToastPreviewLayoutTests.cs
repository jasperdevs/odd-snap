using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public class ToastPreviewLayoutTests
{
    private const int MaxWidth = 332;
    private const int MaxHeight = 220;
    private const int ButtonSafeWidth = 152;
    private const int ButtonSafeHeight = 100;

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(800, 600)]
    [InlineData(1000, 500)]
    [InlineData(700, 900)]
    [InlineData(600, 600)]
    public void Layout_PreservesAspectRatioForModerateCaptures(int width, int height)
    {
        var (layoutWidth, layoutHeight, framed) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.False(framed);
        var sourceAspect = width / (double)height;
        var layoutAspect = layoutWidth / (double)layoutHeight;
        Assert.InRange(layoutAspect, sourceAspect * 0.94, sourceAspect * 1.06);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(300, 1000)]
    [InlineData(40, 40)]
    [InlineData(4000, 2000)]
    public void Layout_NeverExceedsTheToastBox(int width, int height)
    {
        var (layoutWidth, layoutHeight, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.InRange(layoutWidth, 1, MaxWidth);
        Assert.InRange(layoutHeight, 1, MaxHeight);
    }

    [Fact]
    public void Layout_WideCaptureFillsTheToastWidth()
    {
        var (layoutWidth, _, _) = ToastWindow.ComputeImageOnlyPreviewLayout(1200, 500);

        Assert.Equal(MaxWidth, layoutWidth);
    }

    [Theory]
    [InlineData(1200, 200)]
    [InlineData(200, 1200)]
    [InlineData(4000, 20)]
    [InlineData(20, 4000)]
    public void Layout_AlwaysLeavesRoomForOverlayButtons(int width, int height)
    {
        var (layoutWidth, layoutHeight, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.True(layoutWidth >= ButtonSafeWidth);
        Assert.True(layoutHeight >= ButtonSafeHeight);
    }

    [Fact]
    public void Layout_MarksPaddedStripsAsFramed()
    {
        var (_, _, framed) = ToastWindow.ComputeImageOnlyPreviewLayout(4000, 20);

        Assert.True(framed);
    }

    [Fact]
    public void Layout_SmallCaptureHasBoundedUpscaling()
    {
        var (layoutWidth, layoutHeight, _) = ToastWindow.ComputeImageOnlyPreviewLayout(80, 60);

        Assert.True(layoutWidth <= 80 * 2);
        Assert.True(layoutHeight <= 60 * 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 10)]
    public void Layout_HandlesDegenerateSizes(int width, int height)
    {
        var (layoutWidth, layoutHeight, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.True(layoutWidth >= 1);
        Assert.True(layoutHeight >= 1);
    }

    [Theory]
    [InlineData(320, 180, 1, 0, 344, 0)]
    [InlineData(320, 180, -1, 0, -344, 0)]
    [InlineData(320, 180, 0, 1, 0, 204)]
    [InlineData(320, 180, 0, -1, 0, -204)]
    public void DismissTravel_TranslatesContentWithoutChangingWindowPlacement(
        double width,
        double height,
        double offsetX,
        double offsetY,
        double expectedX,
        double expectedY)
    {
        var travel = ToastWindow.ComputeDismissTravel(width, height, offsetX, offsetY);

        Assert.Equal(expectedX, travel.X);
        Assert.Equal(expectedY, travel.Y);
    }
}
