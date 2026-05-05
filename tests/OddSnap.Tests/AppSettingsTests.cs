using Xunit;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void GetToolHotkey_ReturnsDedicatedDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal((0x0001u, 0xC0u), settings.GetToolHotkey("rect"));
        Assert.Equal((0u, 0u), settings.GetToolHotkey("center"));
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

    [Fact]
    public void CaptureDockSide_DefaultsToTop()
    {
        var settings = new AppSettings();

        Assert.Equal(CaptureDockSide.Top, settings.CaptureDockSide);
    }

    [Fact]
    public void ScrollingCaptureMode_DefaultsToAutomatic()
    {
        var settings = new AppSettings();

        Assert.Equal(ScrollingCaptureMode.Automatic, settings.ScrollingCaptureMode);
    }

    [Fact]
    public void OverlayCaptureAllMonitors_DefaultsToEnabled()
    {
        var settings = new AppSettings();

        Assert.True(settings.OverlayCaptureAllMonitors);
    }

    [Fact]
    public void ToastButtons_DefaultToVisibleCornerLayout()
    {
        var settings = new AppSettings();

        Assert.True(settings.ToastButtons.ShowClose);
        Assert.True(settings.ToastButtons.ShowPin);
        Assert.True(settings.ToastButtons.ShowSave);
        Assert.False(settings.ToastButtons.ShowOffice);
        Assert.False(settings.ToastButtons.ShowDelete);
        Assert.Equal(ToastButtonSlot.TopRight, settings.ToastButtons.CloseSlot);
        Assert.Equal(ToastButtonSlot.TopLeft, settings.ToastButtons.PinSlot);
        Assert.Equal(ToastButtonSlot.BottomRight, settings.ToastButtons.SaveSlot);
        Assert.Equal(ToastButtonSlot.TopInnerLeft, settings.ToastButtons.OfficeSlot);
        Assert.Equal(ToastButtonSlot.BottomLeft, settings.ToastButtons.DeleteSlot);
    }

    [Fact]
    public void RecordingDefaults_EnableDesktopAudio()
    {
        var settings = new AppSettings();

        Assert.True(settings.RecordDesktopAudio);
        Assert.False(settings.RecordMicrophone);
    }

    [Fact]
    public void SaveInMonthlyFolders_DefaultsToEnabled()
    {
        var settings = new AppSettings();

        Assert.True(settings.SaveInMonthlyFolders);
    }

    [Fact]
    public void FileNameTemplate_DefaultsToHumanReadableScreenshotName()
    {
        var settings = new AppSettings();

        Assert.Equal(FileNameTemplate.DefaultTemplate, settings.FileNameTemplate);
    }

    [Fact]
    public void InterfaceLanguage_DefaultsToAuto()
    {
        var settings = new AppSettings();

        Assert.Equal("auto", settings.InterfaceLanguage);
    }

    [Fact]
    public void UiScale_DefaultsToNormal()
    {
        var settings = new AppSettings();

        Assert.Equal(1.0, settings.UiScale);
    }

    [Fact]
    public void CenterSelectionAspectRatio_DefaultsToFree()
    {
        var settings = new AppSettings();

        Assert.Equal(CenterSelectionAspectRatio.Free, settings.CenterSelectionAspectRatio);
    }

    [Fact]
    public void TranslationTarget_DefaultsToAuto()
    {
        var settings = new AppSettings();

        Assert.Equal("auto", settings.OcrDefaultTranslateTo);
    }

    [Fact]
    public void TryDeserialize_NormalizesUnsupportedInterfaceLanguageToAuto()
    {
        var json = """
            {
              "InterfaceLanguage": "zz"
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal("auto", settings.InterfaceLanguage);
    }

    [Fact]
    public void TryDeserialize_MigratesLegacyDefaultFileNameTemplate()
    {
        var json = $$"""
            {
              "FileNameTemplate": "{{FileNameTemplate.LegacyDefaultTemplate}}"
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(FileNameTemplate.DefaultTemplate, settings.FileNameTemplate);
    }

    [Fact]
    public void CaptureMode_PreservesPersistedNumericValues()
    {
        Assert.Equal(0, (int)CaptureMode.Rectangle);
        Assert.Equal(1, (int)CaptureMode.Freeform);
        Assert.Equal(22, (int)CaptureMode.Center);
    }

    [Fact]
    public void TryDeserialize_PreservesLegacyFreeformDefaultCaptureMode()
    {
        var json = """
            {
              "DefaultCaptureMode": 1
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(CaptureMode.Freeform, settings.DefaultCaptureMode);
    }

    [Fact]
    public void TryDeserialize_NormalizesInvalidScrollingCaptureModeToAutomatic()
    {
        var json = """
            {
              "ScrollingCaptureMode": 99
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(ScrollingCaptureMode.Automatic, settings.ScrollingCaptureMode);
    }

    [Fact]
    public void TryDeserialize_NormalizesInvalidEnumsToSafeDefaults()
    {
        var json = """
            {
              "AfterCapture": 99,
              "CaptureImageFormat": 99,
              "LastCaptureMode": 999,
              "DefaultCaptureMode": 999,
              "WindowDetection": 99,
              "CaptureDockSide": 99,
              "HistoryRetention": 99,
              "ToastPosition": 99,
              "SoundPack": 99,
              "RecordingFormat": 99,
              "RecordingQuality": 99,
              "CenterSelectionAspectRatio": 99,
              "ImageUploadDestination": 99,
              "ImageUploadSettings": {
                "AiChatProvider": 99,
                "AiChatUploadDestination": 99
              },
              "StickerUploadSettings": {
                "Provider": 99,
                "LocalEngine": 99,
                "LocalCpuEngine": 99,
                "LocalGpuEngine": 99,
                "LocalExecutionProvider": 99
              },
              "UpscaleUploadSettings": {
                "Provider": 99,
                "LocalEngine": 99,
                "LocalCpuEngine": 99,
                "LocalGpuEngine": 99,
                "LocalExecutionProvider": 99
              }
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(AfterCaptureAction.PreviewAndCopy, settings.AfterCapture);
        Assert.Equal(CaptureImageFormat.Png, settings.CaptureImageFormat);
        Assert.Equal(CaptureMode.Rectangle, settings.LastCaptureMode);
        Assert.Equal(CaptureMode.Rectangle, settings.DefaultCaptureMode);
        Assert.Equal(WindowDetectionMode.WindowOnly, settings.WindowDetection);
        Assert.Equal(CaptureDockSide.Top, settings.CaptureDockSide);
        Assert.Equal(HistoryRetentionPeriod.Never, settings.HistoryRetention);
        Assert.Equal(ToastPosition.Right, settings.ToastPosition);
        Assert.Equal(SoundPack.Default, settings.SoundPack);
        Assert.Equal(RecordingFormat.MP4, settings.RecordingFormat);
        Assert.Equal(RecordingQuality.Original, settings.RecordingQuality);
        Assert.Equal(CenterSelectionAspectRatio.Free, settings.CenterSelectionAspectRatio);
        Assert.Equal(UploadDestination.None, settings.ImageUploadDestination);
        Assert.Equal(AiChatProvider.GoogleLens, settings.ImageUploadSettings.AiChatProvider);
        Assert.Equal(UploadDestination.TempHosts, settings.ImageUploadSettings.AiChatUploadDestination);
        Assert.Equal(StickerProvider.LocalCpu, settings.StickerUploadSettings.Provider);
        Assert.Equal(LocalStickerEngine.U2Netp, settings.StickerUploadSettings.LocalEngine);
        Assert.Equal(LocalStickerEngine.U2Netp, settings.StickerUploadSettings.LocalCpuEngine);
        Assert.Equal(LocalStickerEngine.BiRefNetLite, settings.StickerUploadSettings.LocalGpuEngine);
        Assert.Equal(StickerExecutionProvider.Cpu, settings.StickerUploadSettings.LocalExecutionProvider);
        Assert.Equal(UpscaleProvider.Local, settings.UpscaleUploadSettings.Provider);
        Assert.Equal(LocalUpscaleEngine.SwinIrRealWorld, settings.UpscaleUploadSettings.LocalEngine);
        Assert.Equal(LocalUpscaleEngine.SwinIrRealWorld, settings.UpscaleUploadSettings.LocalCpuEngine);
        Assert.Equal(LocalUpscaleEngine.RealEsrganX4Plus, settings.UpscaleUploadSettings.LocalGpuEngine);
        Assert.Equal(UpscaleExecutionProvider.Cpu, settings.UpscaleUploadSettings.LocalExecutionProvider);
    }

    [Theory]
    [InlineData(0.25, 0.8)]
    [InlineData(1.2, 1.2)]
    [InlineData(3.0, 1.4)]
    public void TryDeserialize_ClampsUiScale(double input, double expected)
    {
        var json = $$"""
            {
              "UiScale": {{input.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
            }
            """;

        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        Assert.Equal(expected, settings.UiScale, precision: 3);
    }
}
