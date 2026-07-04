using System.Drawing;
using OddSnap.Helpers;
using OddSnap.Models;
using Xunit;

namespace OddSnap.Tests;

public class ToolbarLayoutTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void GetToolbarRect_ZeroOrNegativeSize_ReturnsEmpty()
    {
        Assert.Equal(Rectangle.Empty, ToolbarLayout.GetToolbarRect(Screen, Screen, 0, 50));
        Assert.Equal(Rectangle.Empty, ToolbarLayout.GetToolbarRect(Screen, Screen, 400, 0));
        Assert.Equal(Rectangle.Empty, ToolbarLayout.GetToolbarRect(Screen, Screen, -5, 50));
    }

    [Fact]
    public void GetToolbarRect_TopDock_IsCenteredAtTopMargin()
    {
        var rect = ToolbarLayout.GetToolbarRect(Screen, Screen, 400, 50, CaptureDockSide.Top);
        Assert.Equal(new Rectangle(760, UiChrome.ToolbarTopMargin, 400, 50), rect);
    }

    [Fact]
    public void GetToolbarRect_BottomDock_SitsAboveBottomEdge()
    {
        var rect = ToolbarLayout.GetToolbarRect(Screen, Screen, 400, 50, CaptureDockSide.Bottom);
        Assert.Equal(760, rect.X);
        Assert.Equal(1080 - 50 - 18, rect.Y); // horizontalPadding(8) + 10
    }

    [Fact]
    public void GetToolbarRect_LeftDock_HugsLeftEdgeVerticallyCentered()
    {
        var rect = ToolbarLayout.GetToolbarRect(Screen, Screen, 50, 400, CaptureDockSide.Left);
        Assert.Equal(8, rect.X);
        Assert.Equal((1080 - 400) / 2, rect.Y);
    }

    [Fact]
    public void GetToolbarRect_RightDock_HugsRightEdge()
    {
        var rect = ToolbarLayout.GetToolbarRect(Screen, Screen, 50, 400, CaptureDockSide.Right);
        Assert.Equal(1920 - 50 - 8, rect.X);
        Assert.Equal((1080 - 400) / 2, rect.Y);
    }

    [Fact]
    public void GetToolbarRect_OversizedToolbar_IsClampedToScreen()
    {
        var rect = ToolbarLayout.GetToolbarRect(Screen, Screen, 5000, 50, CaptureDockSide.Top);
        Assert.Equal(1920 - 16, rect.Width);
        Assert.Equal(8, rect.X);
    }

    [Fact]
    public void GetToolbarRect_SecondMonitor_OffsetsIntoVirtualSpace()
    {
        var virtualBounds = new Rectangle(-1920, 0, 3840, 1080);
        var secondScreen = new Rectangle(0, 0, 1920, 1080);
        var rect = ToolbarLayout.GetToolbarRect(virtualBounds, secondScreen, 400, 50, CaptureDockSide.Top);
        Assert.Equal(1920 + 760, rect.X);
        Assert.Equal(UiChrome.ToolbarTopMargin, rect.Y);
    }

    // ── ResolveToolbarAnchorArea ────────────────────────────────────

    [Fact]
    public void ResolveToolbarAnchorArea_CursorScreenWins()
    {
        var overlay = new Rectangle(0, 0, 3840, 1080);
        var screens = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080) };
        var result = ToolbarLayout.ResolveToolbarAnchorArea(overlay, new Point(2000, 500), Rectangle.Empty, screens);
        Assert.Equal(new Rectangle(1920, 0, 1920, 1080), result);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_NoCursor_UsesPersistedAnchorIntersection()
    {
        var overlay = new Rectangle(0, 0, 1920, 1080);
        var last = new Rectangle(100, 100, 800, 600);
        var result = ToolbarLayout.ResolveToolbarAnchorArea(overlay, null, last, Array.Empty<Rectangle>());
        Assert.Equal(last, result);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_CursorOutsideOverlay_FallsBackToLargestScreen()
    {
        var overlay = new Rectangle(0, 0, 3000, 1080);
        var screens = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080) };
        // second screen only intersects for 1080 px width, first fully
        var result = ToolbarLayout.ResolveToolbarAnchorArea(overlay, new Point(9999, 9999), Rectangle.Empty, screens);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), result);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_NothingUsable_ReturnsOverlayBounds()
    {
        var overlay = new Rectangle(0, 0, 1920, 1080);
        var result = ToolbarLayout.ResolveToolbarAnchorArea(overlay, null, Rectangle.Empty, Array.Empty<Rectangle>());
        Assert.Equal(overlay, result);
    }

    [Fact]
    public void ResolveToolbarAnchorArea_PersistedAnchorOutsideOverlay_IsIgnored()
    {
        var overlay = new Rectangle(0, 0, 1920, 1080);
        var stale = new Rectangle(5000, 5000, 100, 100);
        var result = ToolbarLayout.ResolveToolbarAnchorArea(overlay, null, stale, Array.Empty<Rectangle>());
        Assert.Equal(overlay, result);
    }
}
