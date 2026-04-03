using System.Drawing;
using Xunit;
using Yoink.Helpers;

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
}
