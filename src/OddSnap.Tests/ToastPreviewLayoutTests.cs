using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

/// <summary>
/// Image-only toasts must take the shape of the capture. A narrow strip used to be forced into a
/// fixed 280x176 box, which showed as a border around the image.
/// </summary>
public class ToastPreviewLayoutTests
{
    private const int MaxWidth = 332;
    private const int MaxHeight = 220;

    // Smallest toast that still leaves room for the 40px overlay buttons in its corners.
    private const int ButtonSafeWidth = 152;
    private const int ButtonSafeHeight = 100;

    [Theory]
    [InlineData(1920, 1080)] // widescreen
    [InlineData(800, 600)]   // 4:3
    [InlineData(1000, 500)]  // wide
    [InlineData(700, 900)]   // tall
    [InlineData(600, 600)]   // square
    // Ratios here are all moderate enough that the button-safe minimums don't kick in; more extreme
    // strips are deliberately padded (see Layout_AlwaysLeavesRoomForTheOverlayButtons).
    public void Layout_PreservesAspectRatio(int width, int height)
    {
        var (w, h, framed) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.False(framed);
        var sourceAspect = width / (double)height;
        var layoutAspect = w / (double)h;
        // Integer rounding of the final size allows a little drift.
        Assert.InRange(layoutAspect, sourceAspect * 0.94, sourceAspect * 1.06);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(300, 1000)]
    [InlineData(40, 40)]
    [InlineData(4000, 2000)]
    public void Layout_NeverExceedsTheToastBox(int width, int height)
    {
        var (w, h, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.InRange(w, 1, MaxWidth);
        Assert.InRange(h, 1, MaxHeight);
    }

    [Fact]
    public void Layout_WideCaptureFillsTheToastWidth()
    {
        // The case that regressed: a wide capture used to come back as a fixed 280x176 frame.
        var (w, _, _) = ToastWindow.ComputeImageOnlyPreviewLayout(1200, 500);

        Assert.Equal(MaxWidth, w);
    }

    [Theory]
    [InlineData(1200, 200)]  // wide strip
    [InlineData(200, 1200)]  // tall strip
    [InlineData(4000, 20)]   // extreme sliver
    [InlineData(20, 4000)]
    public void Layout_AlwaysLeavesRoomForTheOverlayButtons(int width, int height)
    {
        var (w, h, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.True(w >= ButtonSafeWidth, $"width {w} is too narrow for the overlay buttons");
        Assert.True(h >= ButtonSafeHeight, $"height {h} is too short for the overlay buttons");
    }

    [Fact]
    public void Layout_MarksPaddedStripsAsFramed()
    {
        // Padding to the button-safe size means the image no longer fills the toast edge to edge.
        var (_, _, framed) = ToastWindow.ComputeImageOnlyPreviewLayout(4000, 20);

        Assert.True(framed);
    }

    [Fact]
    public void Layout_SmallCaptureIsNotUpscaledIntoMush()
    {
        var (w, h, _) = ToastWindow.ComputeImageOnlyPreviewLayout(80, 60);

        Assert.True(w <= 80 * 2);
        Assert.True(h <= 60 * 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 10)]
    public void Layout_HandlesDegenerateSizes(int width, int height)
    {
        var (w, h, _) = ToastWindow.ComputeImageOnlyPreviewLayout(width, height);

        Assert.True(w >= 1);
        Assert.True(h >= 1);
    }
}
