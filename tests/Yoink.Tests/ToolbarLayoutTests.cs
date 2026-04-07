using System.Drawing;
using Xunit;
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Tests;

public sealed class ToolbarLayoutTests
{
    [Fact]
    public void ResolveToolbarAnchorArea_UsesCursorMonitorWhenCursorIsInsideOverlay()
    {
        var overlayBounds = new Rectangle(0, 0, 5760, 1080);
        var workingAreas = new[]
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 0, 1920, 1040),
            new Rectangle(3840, 0, 1920, 1040),
        };

        var anchor = ToolbarLayout.ResolveToolbarAnchorArea(
            overlayBounds,
            new Point(4500, 400),
            Rectangle.Empty,
            workingAreas);

        Assert.Equal(new Rectangle(3840, 0, 1920, 1040), anchor);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_KeepsLastMonitorWhenCursorLeavesOverlay()
    {
        var overlayBounds = new Rectangle(1920, 0, 1920, 1080);
        var workingAreas = new[]
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 0, 1920, 1040),
            new Rectangle(3840, 0, 1920, 1040),
        };
        var lastAnchor = new Rectangle(1920, 0, 1920, 1040);

        var anchor = ToolbarLayout.ResolveToolbarAnchorArea(
            overlayBounds,
            new Point(3900, 300),
            lastAnchor,
            workingAreas);

        Assert.Equal(lastAnchor, anchor);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_FallsBackToLargestVisibleOverlapWhenNoAnchorExists()
    {
        var overlayBounds = new Rectangle(1000, 0, 2200, 1080);
        var workingAreas = new[]
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 0, 1920, 1040),
            new Rectangle(3840, 0, 1920, 1040),
        };

        var anchor = ToolbarLayout.ResolveToolbarAnchorArea(
            overlayBounds,
            null,
            Rectangle.Empty,
            workingAreas);

        Assert.Equal(new Rectangle(1920, 0, 1280, 1040), anchor);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_SupportsNegativeCoordinateMonitors()
    {
        var overlayBounds = new Rectangle(-1920, 0, 1920, 1080);
        var workingAreas = new[]
        {
            new Rectangle(-1920, 0, 1920, 1040),
            new Rectangle(0, 0, 1920, 1040),
        };

        var anchor = ToolbarLayout.ResolveToolbarAnchorArea(
            overlayBounds,
            new Point(-1500, 300),
            Rectangle.Empty,
            workingAreas);

        Assert.Equal(new Rectangle(-1920, 0, 1920, 1040), anchor);
    }

    [Fact]
    public void GetToolbarRect_AnchorsToolbarToNegativeCoordinateMonitor()
    {
        var virtualBounds = new Rectangle(-1920, 0, 1920, 1080);
        var leftMonitor = new Rectangle(-1920, 0, 1920, 1040);

        var rect = ToolbarLayout.GetToolbarRect(virtualBounds, leftMonitor, 800, 44);

        Assert.True(rect.Left >= 8);
        Assert.True(rect.Right <= 1920 - 8);
        Assert.Equal(16, rect.Top);
    }

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
