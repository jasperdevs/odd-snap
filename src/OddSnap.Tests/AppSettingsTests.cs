using OddSnap.Models;
using OddSnap.Helpers;
using System.Drawing;
using Xunit;

namespace OddSnap.Tests;

public class AppSettingsTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    [Fact]
    public void Defaults_MainHotkeyIsAltBacktick()
    {
        var s = new AppSettings();
        Assert.Equal(ModAlt, s.HotkeyModifiers);
        Assert.Equal(0xC0u, s.HotkeyKey);
    }

    [Fact]
    public void Defaults_OddSnapUiRemainsVisibleInScreenshotsForCompatibility()
    {
        Assert.True(new AppSettings().ShowOddSnapUiInScreenshots);
    }

    [Fact]
    public void Defaults_StartMenuShortcutRemainsEnabledForCompatibility()
    {
        Assert.True(new AppSettings().CreateStartMenuShortcut);
    }

    [Theory]
    [InlineData("rect", ModAlt, 0xC0u)]
    [InlineData("ocr", ModAlt | ModShift, 0xC0u)]
    [InlineData("picker", ModAlt, 0x43u)]
    public void GetToolHotkey_NamedTools_ReturnDedicatedProperties(string toolId, uint expectedMod, uint expectedKey)
    {
        var s = new AppSettings();
        Assert.Equal((expectedMod, expectedKey), s.GetToolHotkey(toolId));
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("sticker")]
    [InlineData("upscale")]
    [InlineData("center")]
    [InlineData("_fullscreen")]
    [InlineData("_activeWindow")]
    [InlineData("_scrollCapture")]
    [InlineData("_record")]
    [InlineData("_lastRegion")]
    public void GetToolHotkey_OptionalTools_DisabledByDefault(string toolId)
    {
        Assert.Equal((0u, 0u), new AppSettings().GetToolHotkey(toolId));
    }

    [Theory]
    [InlineData("select", 0x31u)]      // '1'
    [InlineData("arrow", 0x32u)]       // '2'
    [InlineData("ruler", 0x30u)]       // '0' (10th annotation tool)
    [InlineData("magnifier", 0xBDu)]   // '-'
    [InlineData("eraser", 0xDCu)]      // '\'
    public void GetToolHotkey_AnnotationTools_HaveBareKeyDefaults(string toolId, uint expectedKey)
    {
        Assert.Equal((0u, expectedKey), new AppSettings().GetToolHotkey(toolId));
    }

    [Fact]
    public void SetToolHotkey_NamedTool_UpdatesDedicatedProperties()
    {
        var s = new AppSettings();
        s.SetToolHotkey("rect", ModControl, 0x41);
        Assert.Equal(ModControl, s.HotkeyModifiers);
        Assert.Equal(0x41u, s.HotkeyKey);
        Assert.Equal((ModControl, 0x41u), s.GetToolHotkey("rect"));
    }

    [Fact]
    public void SetToolHotkey_GenericTool_StoredInDictionary()
    {
        var s = new AppSettings();
        s.SetToolHotkey("arrow", ModShift, 0x46);
        Assert.NotNull(s.ToolHotkeys);
        Assert.Equal((ModShift, 0x46u), s.GetToolHotkey("arrow"));
    }

    [Fact]
    public void LastCaptureRegion_StoresAndClipsToCurrentDesktop()
    {
        var settings = new AppSettings();
        LastCaptureRegion.Store(settings, new Rectangle(-100, 40, 500, 300));

        Assert.Equal(new Rectangle(0, 40, 400, 300),
            LastCaptureRegion.Resolve(settings, new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void LastCaptureRegion_RejectsMissingOrDegenerateSelection()
    {
        var settings = new AppSettings { LastCaptureRegionWidth = 1, LastCaptureRegionHeight = 200 };
        Assert.Equal(Rectangle.Empty, LastCaptureRegion.Resolve(settings, new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void LastCaptureRegion_HandlesExtremePersistedCoordinatesWithoutOverflow()
    {
        var settings = new AppSettings
        {
            LastCaptureRegionX = int.MaxValue - 10,
            LastCaptureRegionY = int.MaxValue - 10,
            LastCaptureRegionWidth = int.MaxValue,
            LastCaptureRegionHeight = int.MaxValue,
        };

        Assert.Equal(Rectangle.Empty, LastCaptureRegion.Resolve(settings, new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void SetToolHotkey_ExplicitClear_OverridesAnnotationDefault()
    {
        var s = new AppSettings();
        s.SetToolHotkey("arrow", 0, 0);
        Assert.Equal((0u, 0u), s.GetToolHotkey("arrow"));
    }

    [Fact]
    public void GetToolHotkey_DisabledAnnotationTool_HasNoDefaultHotkey()
    {
        var s = new AppSettings { EnabledTools = new List<string> { "rect" } };
        Assert.Equal((0u, 0u), s.GetToolHotkey("arrow"));
    }

    [Fact]
    public void GetToolHotkey_UnknownToolId_ReturnsNone()
    {
        Assert.Equal((0u, 0u), new AppSettings().GetToolHotkey("definitely-not-a-tool"));
    }

    [Fact]
    public void FindAnnotationToolId_MatchesDefaultBareKey()
    {
        var s = new AppSettings();
        Assert.Equal("select", s.FindAnnotationToolId(0, 0x31));
        Assert.Equal("eraser", s.FindAnnotationToolId(0, 0xDC));
    }

    [Fact]
    public void FindAnnotationToolId_ZeroKey_ReturnsNull()
    {
        Assert.Null(new AppSettings().FindAnnotationToolId(0, 0));
    }

    [Fact]
    public void FindAnnotationToolId_RespectsVisibleFilter()
    {
        var s = new AppSettings();
        Assert.Null(s.FindAnnotationToolId(0, 0x31, new[] { "arrow" }));
        Assert.Equal("arrow", s.FindAnnotationToolId(0, 0x32, new[] { "arrow" }));
    }

    [Fact]
    public void FindAnnotationToolId_CaptureToolHotkeysAreIgnored()
    {
        // "rect" (Group 0) defaults to Alt+backtick; only Group 1 tools should match
        Assert.Null(new AppSettings().FindAnnotationToolId(ModAlt, 0xC0));
    }

    // ── ToolDef catalog invariants ──────────────────────────────────

    [Fact]
    public void AllTools_IdsAreUnique()
    {
        var ids = ToolDef.AllTools.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AllTools_HaveNonEmptyLabelsAndIcons()
    {
        Assert.All(ToolDef.AllTools, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id));
            Assert.False(string.IsNullOrWhiteSpace(t.Label));
            Assert.NotEqual('\0', t.Icon);
        });
    }

    [Fact]
    public void AllTools_GroupsAreCaptureOrAnnotation()
    {
        Assert.All(ToolDef.AllTools, t => Assert.True(t.Group is 0 or 1));
        Assert.Contains(ToolDef.AllTools, t => t.Group == 0);
        Assert.Contains(ToolDef.AllTools, t => t.Group == 1);
    }

    [Fact]
    public void AllToolbarItems_OrderIsCaptureThenActionsThenAnnotation()
    {
        var items = ToolDef.AllToolbarItems();
        var groups = items.Select(t => t.Group).ToList();
        // groups appear as a run of 0s, then 2s, then 1s
        var expected = ToolDef.AllTools.Where(t => t.Group == 0).Select(t => t.Group)
            .Concat(ToolDef.ToolbarActions.Select(t => t.Group))
            .Concat(ToolDef.AllTools.Where(t => t.Group == 1).Select(t => t.Group))
            .ToList();
        Assert.Equal(expected, groups);
        Assert.Equal(items.Length, items.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DefaultPinnedToolbarIds_AreExactlyCaptureTools()
    {
        var expected = ToolDef.AllTools.Where(t => t.Group == 0).Select(t => t.Id).ToList();
        Assert.Equal(expected, ToolDef.DefaultPinnedToolbarIds());
    }

    [Fact]
    public void DefaultToolbarOrderIds_MatchAllToolbarItems()
    {
        Assert.Equal(ToolDef.AllToolbarItems().Select(t => t.Id).ToList(), ToolDef.DefaultToolbarOrderIds());
    }

    [Fact]
    public void FlyoutToolIds_AreAnnotationTools()
    {
        var flyout = ToolDef.FlyoutToolIds();
        Assert.Equal(ToolDef.AllTools.Count(t => t.Group == 1), flyout.Count);
        Assert.Contains("arrow", flyout);
        Assert.DoesNotContain("rect", flyout);
    }

    [Fact]
    public void IsCaptureTool_And_IsAnnotationTool_AreDisjoint()
    {
        Assert.True(ToolDef.IsCaptureTool(CaptureMode.Rectangle));
        Assert.False(ToolDef.IsAnnotationTool(CaptureMode.Rectangle));
        Assert.True(ToolDef.IsAnnotationTool(CaptureMode.Arrow));
        Assert.False(ToolDef.IsCaptureTool(CaptureMode.Arrow));
    }
}
