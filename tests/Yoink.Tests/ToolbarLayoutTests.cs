using System.Drawing;
using Xunit;
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Tests;

public sealed class ToolbarLayoutTests
{
    [Fact]
    public void GetToolbarRect_AnchorsToolbarToChosenMonitor()
    {
        var virtualBounds = new Rectangle(0, 0, 3840, 1080);
        var rightMonitor = new Rectangle(1920, 0, 1920, 1080);

        var rect = ToolbarLayout.GetToolbarRect(virtualBounds, rightMonitor, 800, 44);

        Assert.True(rect.Left >= 1928);
        Assert.True(rect.Right <= 3840 - 8);
        Assert.Equal(16, rect.Top);
    }

    [Fact]
    public void GetToolbarRect_PlacesBottomDockAboveTaskbar()
    {
        var virtualBounds = new Rectangle(0, 0, 1920, 1080);
        var screen = new Rectangle(0, 0, 1920, 1080);

        var rect = ToolbarLayout.GetToolbarRect(virtualBounds, screen, 800, 44, CaptureDockSide.Bottom);

        Assert.Equal(1080 - 44 - 18, rect.Top);
    }

    [Fact]
    public void GetToolbarRect_PlacesLeftDockOnLeftEdgeAndCentersVertically()
    {
        var virtualBounds = new Rectangle(0, 0, 1920, 1080);
        var screen = new Rectangle(0, 0, 1920, 1080);

        var rect = ToolbarLayout.GetToolbarRect(virtualBounds, screen, 46, 420, CaptureDockSide.Left);

        Assert.Equal(8, rect.Left);
        Assert.Equal((1080 - 420) / 2, rect.Top);
    }
}
