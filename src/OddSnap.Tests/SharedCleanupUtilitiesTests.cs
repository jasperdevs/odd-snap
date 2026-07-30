using System.Drawing;
using OddSnap.Capture;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public class SharedCleanupUtilitiesTests
{
    [Fact]
    public void LimitedTextBuffer_KeepsNewestTextWithinLimit()
    {
        var buffer = new LimitedTextBuffer(256);

        buffer.AppendLine(new string('a', 200));
        buffer.AppendLine(new string('b', 100));

        var text = buffer.ToString();
        Assert.Equal(256, text.Length);
        Assert.EndsWith(new string('b', 100), text);
    }

    [Fact]
    public void LimitedTextBuffer_IgnoresEmptyLines()
    {
        var buffer = new LimitedTextBuffer(256);

        buffer.AppendLine("diagnostic");
        buffer.AppendLine("");

        Assert.Equal("diagnostic", buffer.ToString());
    }

    [Fact]
    public void OverlayPlacement_UsesFirstCandidateThatAvoidsBlockedArea()
    {
        Point[] candidates = [new(10, 10), new(50, 50)];

        var result = OverlayPlacement.Resolve(
            candidates,
            new Size(100, 100),
            new Size(20, 20),
            margin: 5,
            avoidRect: new Rectangle(5, 5, 30, 30),
            preferredIndex: 0);

        Assert.Equal(new Point(50, 50), result.Position);
        Assert.Equal(1, result.Index);
    }

    [Fact]
    public void OverlayPlacement_ClampsFallbackWithinClientArea()
    {
        Point[] candidates = [new(90, 90)];

        var result = OverlayPlacement.Resolve(
            candidates,
            new Size(100, 100),
            new Size(20, 20),
            margin: 5,
            avoidRect: new Rectangle(0, 0, 100, 100),
            preferredIndex: 0);

        Assert.Equal(new Point(75, 75), result.Position);
        Assert.Equal(0, result.Index);
    }

    [Fact]
    public void HotkeyConflictResolver_ClearsAndRestoresToolConflict()
    {
        var settings = new AppSettings();
        settings.SetToolHotkey("_record", 2, 0x52);

        var conflict = HotkeyConflictResolver.Find(settings, "rect", 2, 0x52);

        Assert.NotNull(conflict);
        Assert.Equal("_record", conflict.ToolId);
        var previous = HotkeyConflictResolver.Clear(settings, conflict);
        Assert.Equal((0u, 0u), settings.GetToolHotkey("_record"));

        HotkeyConflictResolver.Restore(settings, conflict, previous);
        Assert.Equal((2u, 0x52u), settings.GetToolHotkey("_record"));
    }

    [Fact]
    public void HotkeyConflictResolver_ClearsAndRestoresAiRedirectConflict()
    {
        var settings = new AppSettings
        {
            AiRedirectHotkeyModifiers = 2,
            AiRedirectHotkeyKey = 0x41
        };

        var conflict = HotkeyConflictResolver.Find(settings, "rect", 2, 0x41);

        Assert.NotNull(conflict);
        Assert.True(conflict.IsAiRedirect);
        var previous = HotkeyConflictResolver.Clear(settings, conflict);
        Assert.Equal(0u, settings.AiRedirectHotkeyKey);

        HotkeyConflictResolver.Restore(settings, conflict, previous);
        Assert.Equal(2u, settings.AiRedirectHotkeyModifiers);
        Assert.Equal(0x41u, settings.AiRedirectHotkeyKey);
    }

    [Fact]
    public void RuntimeProbeCache_EnforcesReadinessAndExpiry()
    {
        var now = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var cache = new RuntimeProbeCache<string>(TimeSpan.FromMinutes(10), () => now);
        cache.Update("cpu", ready: false, "Not installed");

        Assert.False(cache.TryGet("cpu", requireReady: true, out _, out _));
        Assert.True(cache.TryGet("cpu", requireReady: false, out var ready, out var status));
        Assert.False(ready);
        Assert.Equal("Not installed", status);

        now = now.AddMinutes(11);
        Assert.False(cache.TryGet("cpu", requireReady: false, out _, out _));
    }
}
