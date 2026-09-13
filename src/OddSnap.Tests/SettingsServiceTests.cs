using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public class SettingsServiceTests
{
    private static AppSettings Deserialize(string json)
    {
        Assert.True(SettingsService.TryDeserialize(json, out var settings));
        return settings;
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsFalseWithDefaults()
    {
        Assert.False(SettingsService.TryDeserialize("this is not json", out var settings));
        Assert.NotNull(settings);
        Assert.Equal(AfterCaptureAction.PreviewAndCopy, settings.AfterCapture);
    }

    [Fact]
    public void TryDeserialize_EmptyObject_ProducesNormalizedDefaults()
    {
        var s = Deserialize("{}");
        Assert.Equal(AfterCaptureAction.PreviewAndCopy, s.AfterCapture);
        Assert.Equal(CaptureImageFormat.Png, s.CaptureImageFormat);
        Assert.Equal(FileNameTemplate.DefaultTemplate, s.FileNameTemplate);
        Assert.True(s.SaveToFile);
        Assert.True(s.ShowOddSnapUiInScreenshots);
        Assert.True(s.CreateStartMenuShortcut);
        Assert.Equal(TrayIconAction.AreaCapture, s.TrayLeftClickAction);
        Assert.NotNull(s.ImageUploadSettings);
        Assert.NotNull(s.ToastButtons);
        Assert.False(s.ClosePreviewAfterCopy);
    }

    [Fact]
    public void TryDeserialize_ClosePreviewAfterCopy_PreservesExplicitChoice()
    {
        Assert.True(Deserialize("{\"ClosePreviewAfterCopy\": true}").ClosePreviewAfterCopy);
    }

    [Fact]
    public void TryDeserialize_ShowOddSnapUiInScreenshots_PreservesExplicitChoice()
    {
        Assert.False(Deserialize("{\"ShowOddSnapUiInScreenshots\": false}").ShowOddSnapUiInScreenshots);
    }

    [Fact]
    public void TryDeserialize_CreateStartMenuShortcut_PreservesExplicitChoice()
    {
        Assert.False(Deserialize("{\"CreateStartMenuShortcut\": false}").CreateStartMenuShortcut);
    }

    [Theory]
    [InlineData(0, TrayIconAction.AreaCapture)]
    [InlineData(1, TrayIconAction.History)]
    [InlineData(6, TrayIconAction.Menu)]
    [InlineData(7, TrayIconAction.None)]
    [InlineData(99, TrayIconAction.AreaCapture)]
    public void TryDeserialize_TrayLeftClickAction_IsNormalized(int value, TrayIconAction expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"TrayLeftClickAction\": {value}}}").TrayLeftClickAction);
    }

    [Theory]
    [InlineData(0x70u)]
    [InlineData(0x87u)]
    public void TryDeserialize_ModifierlessFunctionKeyHotkey_IsPreserved(uint key)
    {
        Assert.Equal(key, Deserialize($"{{\"HotkeyModifiers\": 0, \"HotkeyKey\": {key}}}").HotkeyKey);
    }

    [Fact]
    public void TryDeserialize_UnsafeModifierlessLastRegionHotkey_IsCleared()
    {
        var settings = Deserialize("{\"ToolHotkeys\": {\"_lastRegion\": [0, 65]}}");
        Assert.Equal((0u, 0u), settings.GetToolHotkey("_lastRegion"));
    }

    [Theory]
    [InlineData("{\"ToastPosition\": 99}")]
    [InlineData("{\"ToastPosition\": -1}")]
    public void TryDeserialize_OutOfRangeToastPosition_ClampsToDefault(string json)
    {
        Assert.Equal(ToastPosition.Right, Deserialize(json).ToastPosition);
    }

    [Fact]
    public void TryDeserialize_OutOfRangeEnums_ClampToDefaults()
    {
        var s = Deserialize("{\"AfterCapture\": 123, \"RecordingFormat\": -5, \"HistoryRetention\": 77, \"SoundPack\": 9}");
        Assert.Equal(AfterCaptureAction.PreviewAndCopy, s.AfterCapture);
        Assert.Equal(RecordingFormat.MP4, s.RecordingFormat);
        Assert.Equal(HistoryRetentionPeriod.Never, s.HistoryRetention);
        Assert.Equal(SoundPack.Default, s.SoundPack);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 100)]
    [InlineData(85, 85)]
    public void TryDeserialize_JpegQuality_IsClamped(int input, int expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"JpegQuality\": {input}}}").JpegQuality);
    }

    [Theory]
    [InlineData(0, 15)]    // non-positive falls back to default
    [InlineData(-1, 15)]
    [InlineData(1, 5)]     // below min clamps to min
    [InlineData(100, 30)]  // above max clamps to max
    [InlineData(20, 20)]
    public void TryDeserialize_GifFps_IsNormalized(int input, int expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"GifFps\": {input}}}").GifFps);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(2, 5)]
    [InlineData(240, 60)]
    public void TryDeserialize_RecordingFps_IsNormalized(int input, int expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"RecordingFps\": {input}}}").RecordingFps);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(7, 0)]   // only 3/5/10 allowed
    [InlineData(-4, 0)]
    public void TryDeserialize_CaptureDelaySeconds_OnlyAllowsKnownValues(int input, int expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"CaptureDelaySeconds\": {input}}}").CaptureDelaySeconds);
    }

    [Theory]
    [InlineData("100", 30.0)]
    [InlineData("0.1", 0.75)]
    [InlineData("2.5", 2.5)]
    public void TryDeserialize_ToastDuration_IsClamped(string input, double expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"ToastDurationSeconds\": {input}}}").ToastDurationSeconds);
    }

    [Theory]
    [InlineData("9", 1.4)]
    [InlineData("0.1", 0.8)]
    [InlineData("1.0", 1.0)]
    public void TryDeserialize_UiScale_IsClamped(string input, double expected)
    {
        Assert.Equal(expected, Deserialize($"{{\"UiScale\": {input}}}").UiScale);
    }

    [Fact]
    public void TryDeserialize_ModifierlessHotkey_IsClearedForSafety()
    {
        var s = Deserialize("{\"HotkeyModifiers\": 0, \"HotkeyKey\": 65}");
        Assert.Equal(0u, s.HotkeyKey);
    }

    [Fact]
    public void TryDeserialize_ModifierlessPrintScreen_IsAllowed()
    {
        var s = Deserialize("{\"HotkeyModifiers\": 0, \"HotkeyKey\": 44}"); // VK_SNAPSHOT
        Assert.Equal(0x2Cu, s.HotkeyKey);
    }

    [Fact]
    public void TryDeserialize_HotkeyWithModifier_IsPreserved()
    {
        var s = Deserialize("{\"HotkeyModifiers\": 2, \"HotkeyKey\": 65}");
        Assert.Equal(2u, s.HotkeyModifiers);
        Assert.Equal(65u, s.HotkeyKey);
    }

    [Fact]
    public void TryDeserialize_ModifierlessOcrAndPickerHotkeys_AreCleared()
    {
        var s = Deserialize("{\"OcrHotkeyModifiers\": 0, \"OcrHotkeyKey\": 66, \"PickerHotkeyModifiers\": 0, \"PickerHotkeyKey\": 67}");
        Assert.Equal(0u, s.OcrHotkeyKey);
        Assert.Equal(0u, s.PickerHotkeyKey);
    }

    [Fact]
    public void TryDeserialize_LegacyFileNameTemplate_MigratesToNewDefault()
    {
        var json = $"{{\"FileNameTemplate\": \"{FileNameTemplate.LegacyDefaultTemplate}\"}}";
        Assert.Equal(FileNameTemplate.DefaultTemplate, Deserialize(json).FileNameTemplate);
    }

    [Fact]
    public void TryDeserialize_WhitespaceFileNameTemplate_FallsBackToDefault()
    {
        Assert.Equal(FileNameTemplate.DefaultTemplate, Deserialize("{\"FileNameTemplate\": \"  \"}").FileNameTemplate);
    }

    [Fact]
    public void TryDeserialize_CompressHistoryWithPng_SwitchesToJpeg()
    {
        var s = Deserialize("{\"CompressHistory\": true, \"CaptureImageFormat\": 0}");
        Assert.Equal(CaptureImageFormat.Jpeg, s.CaptureImageFormat);
    }

    [Fact]
    public void TryDeserialize_CompressHistoryWithBmp_KeepsBmp()
    {
        var s = Deserialize("{\"CompressHistory\": true, \"CaptureImageFormat\": 2}");
        Assert.Equal(CaptureImageFormat.Bmp, s.CaptureImageFormat);
    }

    [Fact]
    public void TryDeserialize_ImageSearchSources_MaskedToKnownFlags()
    {
        Assert.Equal(ImageSearchSourceOptions.All, Deserialize("{\"ImageSearchSources\": 255}").ImageSearchSources);
    }

    [Fact]
    public void TryDeserialize_RetiredTransferShDestination_MigratesToTempHosts()
    {
        var s = Deserialize($"{{\"ImageUploadDestination\": {(int)UploadDestination.TransferSh}}}");
        Assert.Equal(UploadDestination.TempHosts, s.ImageUploadDestination);
    }

    [Fact]
    public void TryDeserialize_InterfaceLanguage_IsNormalized()
    {
        Assert.Equal("zh-Hant", Deserialize("{\"InterfaceLanguage\": \"zh-TW\"}").InterfaceLanguage);
        Assert.Equal("auto", Deserialize("{\"InterfaceLanguage\": \"klingon\"}").InterfaceLanguage);
    }

    [Fact]
    public void TryDeserialize_SaveDirectory_IsRootedAfterNormalization()
    {
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var s = Deserialize($"{{\"SaveDirectory\": {System.Text.Json.JsonSerializer.Serialize(temp)}}}");
        Assert.True(Path.IsPathRooted(s.SaveDirectory));
        Assert.Equal(Path.GetFullPath(temp), s.SaveDirectory);
    }

    [Fact]
    public void TryDeserialize_EmptySaveDirectory_FallsBackToDefault()
    {
        Assert.Equal(new AppSettings().SaveDirectory, Deserialize("{\"SaveDirectory\": \"\"}").SaveDirectory);
    }

    // ── Tool list normalization ─────────────────────────────────────

    [Fact]
    public void TryDeserialize_EnabledTools_DropsUnknownAndDuplicates()
    {
        var s = Deserialize("{\"EnabledTools\": [\"bogus\", \"rect\", \"RECT\", \"arrow\"]}");
        Assert.Equal(new List<string> { "rect", "arrow" }, s.EnabledTools);
    }

    [Fact]
    public void TryDeserialize_EnabledTools_AnnotationOnlyListGetsCaptureToolInserted()
    {
        var s = Deserialize("{\"EnabledTools\": [\"arrow\"]}");
        Assert.NotNull(s.EnabledTools);
        Assert.Equal("rect", s.EnabledTools![0]);
        Assert.Contains("arrow", s.EnabledTools);
    }

    [Fact]
    public void TryDeserialize_EnabledTools_AllUnknownBecomesNull()
    {
        Assert.Null(Deserialize("{\"EnabledTools\": [\"bogus\", \"nope\"]}").EnabledTools);
    }

    [Fact]
    public void TryDeserialize_ToolbarOrder_AppendsMissingItemsAndKeepsCustomPrefix()
    {
        var s = Deserialize("{\"ToolbarToolOrderIds\": [\"ocr\", \"bogus\"]}");
        Assert.NotNull(s.ToolbarToolOrderIds);
        Assert.Equal("ocr", s.ToolbarToolOrderIds![0]);
        Assert.Equal(ToolDef.AllToolbarItems().Length, s.ToolbarToolOrderIds.Count);
        Assert.DoesNotContain("bogus", s.ToolbarToolOrderIds);
    }

    [Fact]
    public void TryDeserialize_ToolbarPinnedTools_DropsUnknownButKeepsEmptyList()
    {
        Assert.Equal(new List<string> { "free" }, Deserialize("{\"ToolbarPinnedToolIds\": [\"bogus\", \"free\"]}").ToolbarPinnedToolIds);
        Assert.Equal(new List<string>(), Deserialize("{\"ToolbarPinnedToolIds\": []}").ToolbarPinnedToolIds);
    }

    [Fact]
    public void TryDeserialize_ToolLists_CanonicalizeCaseAndWhitespaceConsistently()
    {
        var s = Deserialize(
            "{\"EnabledTools\": [\" RECT \", \"rect\", \" arrow \"]," +
            "\"ToolbarToolOrderIds\": [\" OCR \", \"ocr\"]," +
            "\"ToolbarPinnedToolIds\": [\" FREE \", \"free\"]}");

        Assert.Equal(new List<string> { "rect", "arrow" }, s.EnabledTools);
        Assert.Equal("ocr", s.ToolbarToolOrderIds![0]);
        Assert.Equal(1, s.ToolbarToolOrderIds.Count(id => id.Equals("ocr", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(new List<string> { "free" }, s.ToolbarPinnedToolIds);
    }

    [Fact]
    public void TryDeserialize_ToolHotkeys_KeepsOnlyValidEntries()
    {
        var s = Deserialize("{\"ToolHotkeys\": {\"arrow\": [1, 65], \"bogus\": [1, 66], \"text\": [5]}}");
        Assert.NotNull(s.ToolHotkeys);
        var pair = Assert.Single(s.ToolHotkeys!);
        Assert.Equal("arrow", pair.Key);
        Assert.Equal(new uint[] { 1, 65 }, pair.Value);
    }

    [Fact]
    public void TryDeserialize_ToolHotkeys_AllInvalidBecomesNull()
    {
        Assert.Null(Deserialize("{\"ToolHotkeys\": {\"bogus\": [1, 66]}}").ToolHotkeys);
    }

    // ── Toast button slot dedup ─────────────────────────────────────

    [Fact]
    public void TryDeserialize_DefaultToastButtons_UseUniqueSlots()
    {
        // Default DeleteSlot collides with AiRedirectSlot and is reassigned to
        // BottomInnerRight; Copy retains the available BottomInnerLeft slot.
        var s = Deserialize("{}");
        Assert.Equal(ToastButtonSlot.BottomLeft, s.ToastButtons.AiRedirectSlot);
        Assert.Equal(ToastButtonSlot.BottomInnerRight, s.ToastButtons.DeleteSlot);
        Assert.Equal(ToastButtonSlot.BottomInnerLeft, s.ToastButtons.CopySlot);
    }

    [Fact]
    public void TryDeserialize_ConflictingToastButtonSlots_AreMadeUnique()
    {
        var s = Deserialize("{\"ToastButtons\": {\"CloseSlot\": 3, \"PinSlot\": 3, \"SaveSlot\": 3, \"CopySlot\": 3, \"OfficeSlot\": 3, \"AiRedirectSlot\": 3, \"DeleteSlot\": 3}}");
        var slots = new[]
        {
            s.ToastButtons.CloseSlot, s.ToastButtons.PinSlot, s.ToastButtons.SaveSlot,
            s.ToastButtons.CopySlot, s.ToastButtons.OfficeSlot, s.ToastButtons.AiRedirectSlot, s.ToastButtons.DeleteSlot,
        };
        Assert.Equal(slots.Length, slots.Distinct().Count());
        Assert.Equal(ToastButtonSlot.TopRight, s.ToastButtons.CloseSlot); // first claim wins
    }

    // ── File round trip through a temp path ─────────────────────────

    [Fact]
    public void SaveAndLoad_RoundTripsValuesAndProtectsSecrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OddSnapTests_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            using (var service = new SettingsService(path, TimeSpan.FromMilliseconds(1)))
            {
                service.Settings.JpegQuality = 42;
                service.Settings.ToastPosition = ToastPosition.TopLeft;
                service.Settings.ShowOddSnapUiInScreenshots = false;
                service.Settings.ImageUploadSettings.ImgurClientId = "secret123";
                service.Save();
                service.FlushPendingWrites();
            }

            Assert.True(File.Exists(path));
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("secret123", raw); // DPAPI-protected at rest

            var reloaded = new SettingsService(path);
            reloaded.Load();
            Assert.Equal(42, reloaded.Settings.JpegQuality);
            Assert.Equal(ToastPosition.TopLeft, reloaded.Settings.ToastPosition);
            Assert.False(reloaded.Settings.ShowOddSnapUiInScreenshots);
            Assert.Equal("secret123", reloaded.Settings.ImageUploadSettings.ImgurClientId);
            reloaded.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
