using Xunit;
using OddSnap.UI;

namespace OddSnap.Tests;

public sealed class ToastWindowLayoutTests
{
    [Fact]
    public void ComputeImageOnlyPreviewLayout_UsesConsistentHeightForStandardImages()
    {
        var landscape = ToastWindow.ComputeImageOnlyPreviewLayout(1920, 1080);
        var square = ToastWindow.ComputeImageOnlyPreviewLayout(1080, 1080);

        Assert.False(landscape.Framed);
        Assert.False(square.Framed);
        Assert.InRange(landscape.Height, 180, 188);
        Assert.Equal(188, square.Height);
        Assert.InRange(System.Math.Abs(square.Height - landscape.Height), 0, 8);
    }

    [Fact]
    public void ComputeImageOnlyPreviewLayout_FramesPortraitImages()
    {
        var portrait = ToastWindow.ComputeImageOnlyPreviewLayout(800, 1400);

        Assert.True(portrait.Framed);
        Assert.Equal(188, portrait.Width);
        Assert.Equal(220, portrait.Height);
    }
}
