using Xunit;
using OddSnap.Models;

namespace OddSnap.Tests;

public sealed class ToolDefTests
{
    [Fact]
    public void CaptureAndAnnotationToolsStaySeparated()
    {
        Assert.True(ToolDef.IsCaptureTool(CaptureMode.Rectangle));
        Assert.True(ToolDef.IsCaptureTool(CaptureMode.Scan));
        Assert.False(ToolDef.IsCaptureTool(CaptureMode.Text));
        Assert.False(ToolDef.IsCaptureTool(CaptureMode.Draw));

        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.Text));
        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.Draw));
        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.Select));
        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.RectShape));
        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.CircleShape));
        Assert.False(ToolDef.IsAnnotationTool(CaptureMode.Rectangle));
        Assert.False(ToolDef.IsAnnotationTool(CaptureMode.Scan));
    }
}
