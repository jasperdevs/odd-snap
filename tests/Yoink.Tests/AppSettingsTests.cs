using Xunit;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void GetToolHotkey_ReturnsDedicatedDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal((0x0001u, 0xC0u), settings.GetToolHotkey("rect"));
        Assert.Equal((0u, 0x31u), settings.GetToolHotkey("select"));
        Assert.Equal((0u, 0x32u), settings.GetToolHotkey("arrow"));
        Assert.Equal((0u, 0x30u), settings.GetToolHotkey("ruler"));
    }

    [Fact]
    public void GetToolHotkey_HonorsDisabledTools()
    {
        var settings = new AppSettings
        {
            EnabledTools = new List<string> { "select" }
        };

        Assert.Equal((0u, 0x31u), settings.GetToolHotkey("select"));
        Assert.Equal((0u, 0u), settings.GetToolHotkey("arrow"));
    }

    [Fact]
    public void SetToolHotkey_StoresGenericMappings()
    {
        var settings = new AppSettings();

        settings.SetToolHotkey("custom", 0x0002u, 0x43);

        Assert.Equal((0x0002u, 0x43u), settings.GetToolHotkey("custom"));
    }

    [Fact]
    public void FindAnnotationToolId_UsesStableDefaultsInsteadOfVisibleOrder()
    {
        var settings = new AppSettings
        {
            EnabledTools = new List<string> { "arrow", "draw" }
        };

        Assert.Equal("arrow", settings.FindAnnotationToolId(0u, 0x32u, settings.EnabledTools));
        Assert.Null(settings.FindAnnotationToolId(0u, 0x31u, settings.EnabledTools));
    }

    [Fact]
    public void FindAnnotationToolId_HonorsCustomMappings()
    {
        var settings = new AppSettings();
        settings.SetToolHotkey("arrow", 0u, 0x38u);

        Assert.Equal("arrow", settings.FindAnnotationToolId(0u, 0x38u));
        Assert.Null(settings.FindAnnotationToolId(0u, 0x32u));
    }

    [Fact]
    public void StickerDefaults_ToLocal()
    {
        var settings = new AppSettings();

        Assert.Equal(StickerProvider.LocalCpu, settings.StickerUploadSettings.Provider);
    }
}
