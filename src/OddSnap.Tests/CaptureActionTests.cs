using OddSnap.Helpers;
using OddSnap.Models;
using Xunit;

namespace OddSnap.Tests;

public sealed class CaptureActionTests
{
    [Fact]
    public void MenuActionsFollowConfiguredOrderAndIgnoreUnknownDuplicateOrDisabledTools()
    {
        var settings = new AppSettings
        {
            EnabledTools = ["rect", "ocr"],
            ToolbarToolOrderIds = ["ocr", "unknown", "_record", "ocr", "picker", "rect", "arrow"]
        };
        var tools = ToolbarLayout.GetOrderedCaptureActions(settings);
        Assert.Equal(new[] { "ocr", "_record", "rect" }, tools.Take(3).Select(tool => tool.Id));
        Assert.Equal(tools.Length, tools.Select(tool => tool.Id).Distinct().Count());
        Assert.DoesNotContain(tools, tool => tool.Id is "unknown" or "picker" or "arrow");
        Assert.All(tools, tool => Assert.NotEqual(1, tool.Group));
    }

    [Fact]
    public void DefaultMenuIncludesRecordingAndScrollingCapture()
    {
        var tools = ToolbarLayout.GetOrderedCaptureActions(new AppSettings());
        Assert.Contains(tools, tool => tool.Id == "_record");
        Assert.Contains(tools, tool => tool.Id == "_scrollCapture");
    }

    [Fact]
    public void ToolbarShortcutIsOptInAndParticipatesInConflictDetection()
    {
        var settings = new AppSettings();
        Assert.Equal((0u, 0u), settings.GetToolHotkey("_toolbar"));
        settings.SetToolHotkey("_toolbar", settings.HotkeyModifiers, settings.HotkeyKey);
        var conflict = HotkeyConflictResolver.Find(settings, "rect", settings.HotkeyModifiers, settings.HotkeyKey);
        Assert.Equal("_toolbar", conflict?.ToolId);
    }
}
