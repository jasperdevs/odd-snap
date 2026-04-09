using Xunit;
using Yoink.Services;
using Yoink.Models;

namespace Yoink.Tests;

public sealed class UploadServiceTests
{
    [Theory]
    [InlineData(UploadDestination.Imgur, "Imgur")]
    [InlineData(UploadDestination.ImgBB, "ImgBB")]
    [InlineData(UploadDestination.Catbox, "Catbox")]
    [InlineData(UploadDestination.TransferSh, "transfer.sh")]
    [InlineData(UploadDestination.S3Compatible, "S3")]
    [InlineData(UploadDestination.AiChat, "AI Redirects")]
    [InlineData(UploadDestination.TempHosts, "Filter between free temporary hosts")]
    public void GetName_ReturnsExpectedLabels(UploadDestination destination, string expected)
    {
        Assert.Equal(expected, UploadService.GetName(destination));
    }

    [Theory]
    [InlineData("imgur", "Assets/imgur_sq.png")]
    [InlineData("Google Drive", "Assets/gdrive_sq.png")]
    [InlineData("S3", "Assets/aws_sq.png")]
    [InlineData(null, "")]
    public void GetHistoryLogoPath_ReturnsExpectedAssets(string? provider, string expected)
    {
        Assert.Equal(expected, UploadService.GetHistoryLogoPath(provider));
    }

    [Theory]
    [InlineData(UploadDestination.Imgur, "Assets/imgur_sq.png")]
    [InlineData(UploadDestination.GoogleDrive, "Assets/gdrive_sq.png")]
    [InlineData(UploadDestination.S3Compatible, "Assets/aws_sq.png")]
    [InlineData(UploadDestination.None, "")]
    public void GetUploadsLogoPath_ReturnsExpectedAssets(UploadDestination destination, string expected)
    {
        Assert.Equal(expected, UploadService.GetUploadsLogoPath(destination));
    }

    [Fact]
    public void HasCredentials_HandlesCredentialedAndCredentiallessTargets()
    {
        var settings = new UploadSettings();

        Assert.False(UploadService.HasCredentials(UploadDestination.None, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.Catbox, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.AiChat, settings));
        Assert.False(UploadService.HasCredentials(UploadDestination.Imgur, settings));

        settings.ImgurClientId = "client-id";
        Assert.True(UploadService.HasCredentials(UploadDestination.Imgur, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.AiChat, settings));
    }

    [Theory]
    [InlineData(UploadDestination.Imgur, ".png", 20L * 1024 * 1024)]
    [InlineData(UploadDestination.Imgur, ".gif", 200L * 1024 * 1024)]
    [InlineData(UploadDestination.TransferSh, ".png", 10L * 1024 * 1024 * 1024)]
    public void GetMaxSize_ReflectsDestinationRules(UploadDestination destination, string extension, long expected)
    {
        var path = "sample" + extension;

        Assert.Equal(expected, UploadService.GetMaxSize(destination, path));
    }

    [Theory]
    [InlineData(AiChatProvider.ChatGpt, "https://chatgpt.com/")]
    [InlineData(AiChatProvider.Claude, "https://claude.ai/new")]
    [InlineData(AiChatProvider.ClaudeOpus, "https://claude.ai/new?model=claude-opus-4-1")]
    [InlineData(AiChatProvider.Gemini, "https://gemini.google.com/app")]
    [InlineData(AiChatProvider.GoogleLens, "https://lens.google.com/search?hl=en&country=us")]
    public void BuildAiChatStartUrl_ReturnsProviderSpecificUrl(AiChatProvider provider, string expected)
    {
        var actual = UploadService.BuildAiChatStartUrl(provider);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildGoogleLensUrl_EmbedsHostedImageUrl()
    {
        var actual = UploadService.BuildGoogleLensUrl("https://files.example.com/image.png");

        Assert.Equal("https://lens.google.com/uploadbyurl?url=https%3A%2F%2Ffiles.example.com%2Fimage.png&hl=en&country=us", actual);
    }

    [Theory]
    [InlineData(UploadDestination.None, UploadDestination.Catbox)]
    [InlineData(UploadDestination.AiChat, UploadDestination.Catbox)]
    [InlineData(UploadDestination.Imgur, UploadDestination.Imgur)]
    [InlineData(UploadDestination.Catbox, UploadDestination.Catbox)]
    public void NormalizeAiChatUploadDestination_ReturnsExpectedDestination(UploadDestination input, UploadDestination expected)
    {
        var actual = UploadService.NormalizeAiChatUploadDestination(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldUploadScreenshot_AllowsAiRedirectHotkeyEvenWhenAutoUploadScreenshotsIsOff()
    {
        var settings = new AppSettings
        {
            AutoUploadScreenshots = false,
            ImageUploadDestination = UploadDestination.AiChat,
            AiRedirectHotkeyOnly = true
        };

        Assert.True(UploadService.ShouldUploadScreenshot(settings, hasFilePath: true, useAiRedirect: true));
        Assert.False(UploadService.ShouldUploadScreenshot(settings, hasFilePath: true, useAiRedirect: false));
    }

    [Fact]
    public void ShouldUploadScreenshot_UsesLegacyAutoUploadSettingForNormalDestinations()
    {
        var settings = new AppSettings
        {
            AutoUploadScreenshots = false,
            ImageUploadDestination = UploadDestination.Catbox
        };

        Assert.False(UploadService.ShouldUploadScreenshot(settings, hasFilePath: true, useAiRedirect: false));

        settings.AutoUploadScreenshots = true;
        Assert.True(UploadService.ShouldUploadScreenshot(settings, hasFilePath: true, useAiRedirect: false));
    }

    [Fact]
    public async Task UploadAsync_TransferShFailsFastWithActionableMessage()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "yoink-transfer-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "yoink");

            var result = await UploadService.UploadAsync(filePath, UploadDestination.TransferSh, new UploadSettings());

            Assert.False(result.Success);
            Assert.Contains("transfer.sh", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(filePath); } catch { }
        }
    }
}
