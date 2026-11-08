using OddSnap.Models;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class SettingsImportExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OddSnapSettingsTransfer_" + Guid.NewGuid().ToString("N"));
    public SettingsImportExportTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ExportAndImportRoundTripPreferencesWithoutExposingOrErasingCredentials()
    {
        var source = new AppSettings
        {
            UiScale = 1.25,
            MuteSounds = true,
            ShowRecentCapturesAfterCapture = true,
            ClosePreviewAfterCopy = true,
            InterfaceLanguage = "ja",
            GoogleTranslateApiKey = "test-export-translation-secret",
            EnabledTools = ["rect", "arrow"]
        };
        source.SetToolHotkey("_toolbar", 3, 0x54);
        source.ToastButtons.ShowCopy = true;
        source.ImageUploadSettings.DropboxAccessToken = "test-export-dropbox-secret";
        source.ImageUploadSettings.CustomHeaders = "Authorization: Bearer test-export-header-secret";
        source.StickerUploadSettings.RemoveBgApiKey = "test-export-sticker-secret";
        source.UpscaleUploadSettings.DeepAiApiKey = "test-export-upscale-secret";
        var path = Path.Combine(_directory, "settings.json");
        await SettingsService.ExportToFileAsync(source, path);
        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("test-export-", json);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));

        var current = new AppSettings { GoogleTranslateApiKey = "test-current-translation" };
        current.ImageUploadSettings.DropboxAccessToken = "test-current-dropbox";
        current.ImageUploadSettings.CustomHeaders = "test-current-headers";
        current.StickerUploadSettings.RemoveBgApiKey = "test-current-sticker";
        current.UpscaleUploadSettings.DeepAiApiKey = "test-current-upscale";
        Assert.True(SettingsService.TryDeserializeImport(json, current, out var imported));
        Assert.Equal(1.25, imported.UiScale);
        Assert.True(imported.MuteSounds);
        Assert.True(imported.ShowRecentCapturesAfterCapture);
        Assert.True(imported.ClosePreviewAfterCopy);
        Assert.True(imported.ToastButtons.ShowCopy);
        Assert.Equal("ja", imported.InterfaceLanguage);
        Assert.Equal((3u, 0x54u), imported.GetToolHotkey("_toolbar"));
        Assert.Equal(new[] { "rect", "arrow" }, imported.EnabledTools);
        Assert.Equal("test-current-translation", imported.GoogleTranslateApiKey);
        Assert.Equal("test-current-dropbox", imported.ImageUploadSettings.DropboxAccessToken);
        Assert.Equal("test-current-headers", imported.ImageUploadSettings.CustomHeaders);
        Assert.Equal("test-current-sticker", imported.StickerUploadSettings.RemoveBgApiKey);
        Assert.Equal("test-current-upscale", imported.UpscaleUploadSettings.DeepAiApiKey);
        Assert.Equal("test-export-dropbox-secret", source.ImageUploadSettings.DropboxAccessToken);

        var savedPath = Path.Combine(_directory, "restored.json");
        using (var service = new SettingsService(savedPath, TimeSpan.FromHours(1)))
        {
            service.Settings = imported;
            service.Save();
            service.FlushPendingWritesOrThrow();
        }
        using var restored = new SettingsService(savedPath);
        restored.Load();
        Assert.Equal(imported.UiScale, restored.Settings.UiScale);
        Assert.Equal(imported.GetToolHotkey("_toolbar"), restored.Settings.GetToolHotkey("_toolbar"));
        Assert.Equal("test-current-dropbox", restored.Settings.ImageUploadSettings.DropboxAccessToken);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"unrelated\":true}")]
    [InlineData("{\"UiScale\":\"invalid\"}")]
    public void ImportRejectsInvalidOrUnrelatedFilesWithoutMutatingCurrentSettings(string json)
    {
        var current = new AppSettings { UiScale = 1.5 };
        Assert.False(SettingsService.TryDeserializeImport(json, current, out _));
        Assert.Equal(1.5, current.UiScale);
    }

    [Fact]
    public void ExplicitImportedCredentialsReplaceExistingValues()
    {
        var current = new AppSettings { GoogleTranslateApiKey = "test-old" };
        Assert.True(SettingsService.TryDeserializeImport("{\"GoogleTranslateApiKey\":\"test-new\"}", current, out var imported));
        Assert.Equal("test-new", imported.GoogleTranslateApiKey);
        Assert.Equal("test-old", current.GoogleTranslateApiKey);
    }

    [Fact]
    public async Task FailedExportLeavesPreviousBackupIntactAndCleansTemporaryFile()
    {
        var path = Path.Combine(_directory, "locked.json");
        await File.WriteAllTextAsync(path, "previous backup", TestContext.Current.CancellationToken);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var failure = await Record.ExceptionAsync(() => SettingsService.ExportToFileAsync(new AppSettings(), path));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal("previous backup", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void FailedImportPersistenceIsReportedToCaller()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");
        using var service = new SettingsService(Path.Combine(blocker, "settings.json"), TimeSpan.FromHours(1));
        service.Save();
        Assert.Throws<IOException>(service.FlushPendingWritesOrThrow);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
