using Xunit;
using OddSnap.Services;
using OddSnap.Models;
using System.Reflection;
using System.Net;
using System.Net.Http;

namespace OddSnap.Tests;

public sealed class UploadServiceTests
{
    [Theory]
    [InlineData(UploadDestination.Imgur, "Imgur")]
    [InlineData(UploadDestination.ImgBB, "ImgBB")]
    [InlineData(UploadDestination.Catbox, "Catbox")]
    [InlineData(UploadDestination.TransferSh, "transfer.sh")]
    [InlineData(UploadDestination.S3Compatible, "S3")]
    [InlineData(UploadDestination.AiChat, "AI Redirects")]
    [InlineData(UploadDestination.TempHosts, "Filter between free/no-setup hosts")]
    [InlineData(UploadDestination.TmpFiles, "tmpfiles.org")]
    [InlineData(UploadDestination.Gofile, "Gofile")]
    [InlineData(UploadDestination.ImgPile, "imgpile")]
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
        Assert.True(UploadService.HasCredentials(UploadDestination.TmpFiles, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.Gofile, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.AiChat, settings));
        Assert.False(UploadService.HasCredentials(UploadDestination.Imgur, settings));
        Assert.False(UploadService.HasCredentials(UploadDestination.ImgPile, settings));
        Assert.False(UploadService.HasCredentials(UploadDestination.Sftp, settings));

        settings.ImgurClientId = "client-id";
        settings.ImgPileApiToken = "imgpile-token";
        settings.SftpHost = "sftp.example.com";
        settings.SftpHostKeyFingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        Assert.True(UploadService.HasCredentials(UploadDestination.Imgur, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.ImgPile, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.AiChat, settings));
        Assert.True(UploadService.HasCredentials(UploadDestination.Sftp, settings));
    }

    [Theory]
    [InlineData(UploadDestination.Imgur, ".png", 20L * 1024 * 1024)]
    [InlineData(UploadDestination.Imgur, ".gif", 200L * 1024 * 1024)]
    [InlineData(UploadDestination.TransferSh, ".png", 10L * 1024 * 1024 * 1024)]
    [InlineData(UploadDestination.Dropbox, ".png", 150L * 1024 * 1024)]
    public void GetMaxSize_ReflectsDestinationRules(UploadDestination destination, string extension, long expected)
    {
        var path = "sample" + extension;

        Assert.Equal(expected, UploadService.GetMaxSize(destination, path));
    }

    [Fact]
    public void TemporaryHostFallbacks_CoversNoSetupHostsThatStillHaveSupportedUploadPaths()
    {
        var field = typeof(UploadService).GetField("TemporaryHostFallbacks", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var fallbacks = Assert.IsType<UploadDestination[]>(field!.GetValue(null));

        Assert.Contains(UploadDestination.Litterbox, fallbacks);
        Assert.Contains(UploadDestination.TmpFiles, fallbacks);
        Assert.Contains(UploadDestination.Uguu, fallbacks);
        Assert.Contains(UploadDestination.Gofile, fallbacks);
        Assert.Contains(UploadDestination.Catbox, fallbacks);
        Assert.DoesNotContain(UploadDestination.TransferSh, fallbacks);
    }

    [Theory]
    [InlineData(AiChatProvider.ChatGpt, "https://chatgpt.com/")]
    [InlineData(AiChatProvider.Claude, "https://claude.ai/new")]
    [InlineData(AiChatProvider.ClaudeOpus, "https://claude.ai/new")]
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
    [InlineData(UploadDestination.None, UploadDestination.TempHosts)]
    [InlineData(UploadDestination.AiChat, UploadDestination.TempHosts)]
    [InlineData(UploadDestination.Imgur, UploadDestination.Imgur)]
    [InlineData(UploadDestination.Catbox, UploadDestination.Catbox)]
    public void NormalizeAiChatUploadDestination_ReturnsExpectedDestination(UploadDestination input, UploadDestination expected)
    {
        var actual = UploadService.NormalizeAiChatUploadDestination(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldUploadScreenshot_AllowsAiRedirectCaptureWhenAiRedirectDestinationIsUsed()
    {
        var settings = new AppSettings
        {
            AutoUploadScreenshots = false,
            ImageUploadDestination = UploadDestination.AiChat
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
        var filePath = Path.Combine(Path.GetTempPath(), "oddsnap-transfer-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "oddsnap");

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

    [Fact]
    public async Task UploadAsync_WebDavRejectsNonHttpsUrlBeforeNetworkCall()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "oddsnap-webdav-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "oddsnap");
            var settings = new UploadSettings
            {
                WebDavUrl = "http://example.test/uploads",
                WebDavUsername = "user",
                WebDavPassword = "pass"
            };

            var result = await UploadService.UploadAsync(filePath, UploadDestination.WebDav, settings);

            Assert.False(result.Success);
            Assert.Contains("HTTPS", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(filePath); } catch { }
        }
    }

    [Fact]
    public void BuildHttpError_ReportsHtmlOkResponseAsProviderError()
    {
        var method = typeof(UploadService).GetMethod("BuildHttpError", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var message = Assert.IsType<string>(method!.Invoke(null, new object?[]
        {
            "file.io",
            response,
            "<html><title>Error</title></html>",
            null
        }));

        Assert.Contains("HTML error page", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("200", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_S3RejectsExplicitHttpEndpointBeforeNetworkCall()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "oddsnap-s3-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "oddsnap");
            var settings = new UploadSettings
            {
                S3Endpoint = "http://s3.example.test",
                S3Bucket = "bucket",
                S3AccessKey = "access",
                S3SecretKey = "secret"
            };

            var result = await UploadService.UploadAsync(filePath, UploadDestination.S3Compatible, settings);

            Assert.False(result.Success);
            Assert.Contains("HTTPS", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(filePath); } catch { }
        }
    }

    [Fact]
    public void TryBuildAzureBlobUrls_PreservesSasQueryAfterBlobName()
    {
        var method = typeof(UploadService).GetMethod("TryBuildAzureBlobUrls", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var args = new object?[]
        {
            "https://example.blob.core.windows.net/screenshots?sv=2026&sig=abc",
            "screen shot.png",
            null,
            null,
            null
        };

        var ok = Assert.IsType<bool>(method!.Invoke(null, args));

        Assert.True(ok);
        Assert.Equal("https://example.blob.core.windows.net/screenshots/screen%20shot.png?sv=2026&sig=abc", args[2]);
        Assert.Equal("https://example.blob.core.windows.net/screenshots/screen%20shot.png", args[3]);
        Assert.Equal("", args[4]);
    }

    [Fact]
    public void BuildS3ObjectKey_UsesUniqueNameUnderConfiguredPrefix()
    {
        var method = typeof(UploadService).GetMethod("BuildS3ObjectKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var settings = new UploadSettings { S3PathPrefix = "team/uploads/" };

        var first = Assert.IsType<string>(method!.Invoke(null, new object?[] { "screen.png", settings }));
        var second = Assert.IsType<string>(method.Invoke(null, new object?[] { "screen.png", settings }));

        Assert.StartsWith("team/uploads/oddsnap/", first);
        Assert.EndsWith(".png", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("https://s3.example.test", "s3.example.test")]
    [InlineData("https://s3.example.test:9000", "s3.example.test:9000")]
    [InlineData("r2.example.test:9000", "r2.example.test:9000")]
    public void BuildS3SigningHost_PreservesExplicitNonDefaultPorts(string endpoint, string expected)
    {
        Assert.Equal(expected, UploadService.BuildS3SigningHost(endpoint));
    }

    [Theory]
    [InlineData("Authorization: Bearer abc")]
    [InlineData("X-Api-Key: key")]
    public void TryValidateCustomUploadHeader_AllowsSafeHeaders(string line)
    {
        var ok = UploadService.TryValidateCustomUploadHeader(line, out var name, out var value, out var error);

        Assert.True(ok, error);
        Assert.NotEmpty(name);
        Assert.NotEmpty(value);
    }

    [Theory]
    [InlineData("Host: evil.example")]
    [InlineData("Content-Length: 1")]
    [InlineData("Transfer-Encoding: chunked")]
    [InlineData("Bad Header: value")]
    [InlineData("MissingSeparator")]
    public void TryValidateCustomUploadHeader_RejectsUnsafeHeaders(string line)
    {
        var ok = UploadService.TryValidateCustomUploadHeader(line, out _, out _, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateCustomUploadHeader_RejectsControlCharacters()
    {
        var ok = UploadService.TryValidateCustomUploadHeader("X-Test: good\rbad", out _, out _, out var error);

        Assert.False(ok);
        Assert.Contains("control", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://tmpfiles.org/123/name.png", "https://tmpfiles.org/dl/123/name.png")]
    [InlineData("https://tmpfiles.org/dl/123/name.png", "https://tmpfiles.org/dl/123/name.png")]
    public void ToTmpFilesDownloadUrl_ReturnsDirectDownloadUrl(string input, string expected)
    {
        var method = typeof(UploadService).GetMethod("ToTmpFilesDownloadUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = Assert.IsType<string>(method!.Invoke(null, new object?[] { input }));

        Assert.Equal(expected, actual);
    }
}
