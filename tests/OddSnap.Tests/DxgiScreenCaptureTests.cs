using System.Drawing;
using OddSnap.Capture;
using Xunit;

namespace OddSnap.Tests;

public sealed class DxgiScreenCaptureTests
{
    [Fact]
    public void OutputCoverageRequiresEveryMonitorPartOfRequestedRegion()
    {
        var region = new Rectangle(0, 0, 3840, 1080);
        var primaryOnly = new[]
        {
            new Rectangle(0, 0, 1920, 1080)
        };
        var bothDisplays = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        };

        Assert.False(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, primaryOnly));
        Assert.True(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, bothDisplays));
    }

    [Fact]
    public void OutputCoverageHandlesNegativeMonitorCoordinates()
    {
        var region = new Rectangle(-1920, 0, 3840, 1080);
        var leftOnly = new[]
        {
            new Rectangle(-1920, 0, 1920, 1080)
        };
        var bothDisplays = new[]
        {
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1080)
        };

        Assert.False(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, leftOnly));
        Assert.True(DxgiScreenCapture.IsRegionFullyCoveredByOutputs(region, bothDisplays));
    }
}
