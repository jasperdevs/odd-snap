using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

// Pure validation/formatting logic only — no upload method is ever invoked, so no network calls.
public class UploadServiceTests
{
    // ── Transport security (HTTPS enforcement) ──────────────────────

    [Theory]
    [InlineData("https://immich.example.com")]
    [InlineData("https://immich.example.com:2283/api")]
    public void ValidateTransportSecurity_Immich_AcceptsHttps(string url)
    {
        var settings = new UploadSettings { ImmichBaseUrl = url };
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.Immich, settings));
    }

    [Theory]
    [InlineData("http://immich.example.com")]
    [InlineData("ftp://immich.example.com")]
    [InlineData("not a url at all")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    public void ValidateTransportSecurity_Immich_RejectsNonHttps(string url)
    {
        var settings = new UploadSettings { ImmichBaseUrl = url };
        var error = UploadService.ValidateTransportSecurity(UploadDestination.Immich, settings);
        Assert.NotNull(error);
        Assert.Contains("HTTPS", error);
    }

    [Theory]
    [InlineData("http://upload.example.com/api")]
    [InlineData("garbage")]
    [InlineData("//missing-scheme.example.com")]
    public void ValidateTransportSecurity_CustomHttp_RejectsNonHttps(string url)
    {
        var settings = new UploadSettings { CustomUploadUrl = url };
        var error = UploadService.ValidateTransportSecurity(UploadDestination.CustomHttp, settings);
        Assert.NotNull(error);
        Assert.Contains("HTTPS", error);
    }

    [Fact]
    public void ValidateTransportSecurity_CustomHttp_AcceptsHttps()
    {
        var settings = new UploadSettings { CustomUploadUrl = "https://upload.example.com/api" };
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.CustomHttp, settings));
    }

    [Theory]
    [InlineData("http://dav.example.com")]
    [InlineData("nonsense")]
    public void ValidateTransportSecurity_WebDav_RejectsNonHttps(string url)
    {
        var settings = new UploadSettings { WebDavUrl = url };
        Assert.NotNull(UploadService.ValidateTransportSecurity(UploadDestination.WebDav, settings));
    }

    [Fact]
    public void ValidateTransportSecurity_WebDav_AcceptsHttps()
    {
        var settings = new UploadSettings { WebDavUrl = "https://dav.example.com/remote.php" };
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.WebDav, settings));
    }

    [Fact]
    public void ValidateTransportSecurity_S3_HostOnlyEndpointIsAllowed()
    {
        // endpoints without a scheme are normalized elsewhere; only explicit non-https schemes fail
        var settings = new UploadSettings { S3Endpoint = "s3.us-east-1.amazonaws.com" };
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.S3Compatible, settings));
    }

    [Fact]
    public void ValidateTransportSecurity_S3_ExplicitHttpIsRejected()
    {
        var settings = new UploadSettings { S3Endpoint = "http://s3.example.com" };
        Assert.NotNull(UploadService.ValidateTransportSecurity(UploadDestination.S3Compatible, settings));
    }

    [Fact]
    public void ValidateTransportSecurity_S3_ExplicitHttpsIsAccepted()
    {
        var settings = new UploadSettings { S3Endpoint = "https://s3.example.com" };
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.S3Compatible, settings));
    }

    [Fact]
    public void ValidateTransportSecurity_OtherDestinations_AreNotAffected()
    {
        var settings = new UploadSettings();
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.Imgur, settings));
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.Catbox, settings));
        Assert.Null(UploadService.ValidateTransportSecurity(UploadDestination.Ftp, settings));
    }

    // ── Configuration validation ────────────────────────────────────

    [Fact]
    public void GetConfigurationError_None_AsksToChooseDestination()
    {
        Assert.NotNull(UploadService.GetConfigurationError(UploadDestination.None, new UploadSettings()));
    }

    [Fact]
    public void GetConfigurationError_RetiredTransferSh_ExplainsReplacement()
    {
        var error = UploadService.GetConfigurationError(UploadDestination.TransferSh, new UploadSettings());
        Assert.NotNull(error);
        Assert.Contains("no longer supported", error);
        Assert.Contains("Temp Hosts", error);
    }

    [Fact]
    public void GetConfigurationError_ImgurWithoutClientId_ReportsMissingSetting()
    {
        var error = UploadService.GetConfigurationError(UploadDestination.Imgur, new UploadSettings());
        Assert.NotNull(error);
        Assert.Contains("Imgur client ID", error);
    }

    [Fact]
    public void GetConfigurationError_ImgurWithClientId_IsNull()
    {
        var settings = new UploadSettings { ImgurClientId = "abc" };
        Assert.Null(UploadService.GetConfigurationError(UploadDestination.Imgur, settings));
    }

    [Fact]
    public void GetConfigurationError_CatboxNeedsNoCredentials()
    {
        Assert.Null(UploadService.GetConfigurationError(UploadDestination.Catbox, new UploadSettings()));
        Assert.True(UploadService.HasCredentials(UploadDestination.Catbox, new UploadSettings()));
    }

    [Fact]
    public void GetConfigurationError_SftpRequiresHostKeyFingerprint()
    {
        var settings = new UploadSettings { SftpHost = "example.com", SftpUsername = "user" };
        var error = UploadService.GetConfigurationError(UploadDestination.Sftp, settings);
        Assert.NotNull(error);
        Assert.Contains("fingerprint", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetConfigurationError_SftpWith64HexFingerprint_IsAccepted()
    {
        var settings = new UploadSettings
        {
            SftpHost = "example.com",
            SftpUsername = "user",
            // 32 bytes as colon-separated hex = 64 hex digits
            SftpHostKeyFingerprint = string.Join(":", Enumerable.Repeat("ab", 32)),
        };
        Assert.Null(UploadService.GetConfigurationError(UploadDestination.Sftp, settings));
    }

    [Fact]
    public void GetConfigurationError_SftpWithShortFingerprint_IsRejected()
    {
        var settings = new UploadSettings
        {
            SftpHost = "example.com",
            SftpUsername = "user",
            SftpHostKeyFingerprint = "abcd1234",
        };
        Assert.NotNull(UploadService.GetConfigurationError(UploadDestination.Sftp, settings));
    }

    [Fact]
    public void HasCredentials_MatchesGetConfigurationError()
    {
        var settings = new UploadSettings();
        Assert.False(UploadService.HasCredentials(UploadDestination.Imgur, settings));
        settings.ImgurClientId = "abc";
        Assert.True(UploadService.HasCredentials(UploadDestination.Imgur, settings));
    }

    // ── Size limits ─────────────────────────────────────────────────

    [Fact]
    public void GetMaxSize_ImgurGifsGetLargerLimitThanImages()
    {
        Assert.Equal(200L * 1024 * 1024, UploadService.GetMaxSize(UploadDestination.Imgur, @"C:\x\a.gif"));
        Assert.Equal(20L * 1024 * 1024, UploadService.GetMaxSize(UploadDestination.Imgur, @"C:\x\a.png"));
    }

    [Fact]
    public void GetMaxSize_KnownDestinations()
    {
        Assert.Equal(32L * 1024 * 1024, UploadService.GetMaxSize(UploadDestination.ImgBB, @"C:\x\a.png"));
        Assert.Equal(1024L * 1024 * 1024, UploadService.GetMaxSize(UploadDestination.Litterbox, @"C:\x\a.png"));
        Assert.Equal(long.MaxValue, UploadService.GetMaxSize(UploadDestination.AiChat, @"C:\x\a.png"));
    }

    // ── AI chat helpers ─────────────────────────────────────────────

    [Fact]
    public void BuildAiChatStartUrl_KnownProviders()
    {
        Assert.Equal("https://chatgpt.com/", UploadService.BuildAiChatStartUrl(AiChatProvider.ChatGpt));
        Assert.Equal("https://claude.ai/new", UploadService.BuildAiChatStartUrl(AiChatProvider.Claude));
        Assert.Equal("https://claude.ai/new", UploadService.BuildAiChatStartUrl(AiChatProvider.ClaudeOpus));
        Assert.Equal("", UploadService.BuildAiChatStartUrl(AiChatProvider.None));
    }

    [Fact]
    public void GetAiChatProviderName_LegacyClaudeOpusMapsToClaude()
    {
        Assert.Equal("Claude", UploadService.GetAiChatProviderName(AiChatProvider.ClaudeOpus));
        Assert.Equal("Google Lens", UploadService.GetAiChatProviderName(AiChatProvider.GoogleLens));
    }

    [Fact]
    public void BuildGoogleLensUrl_EscapesImageUrl()
    {
        var url = UploadService.BuildGoogleLensUrl("https://i.example.com/shot.png");
        Assert.StartsWith("https://lens.google.com/uploadbyurl?url=", url);
        Assert.Contains(Uri.EscapeDataString("https://i.example.com/shot.png"), url);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.png")]
    public void BuildGoogleLensUrl_NonHttpUrls_Throw(string input)
    {
        Assert.Throws<InvalidOperationException>(() => UploadService.BuildGoogleLensUrl(input));
    }

    // ── tmpfiles.org URL rewriting ──────────────────────────────────

    [Theory]
    [InlineData("https://tmpfiles.org/123/shot.png", "https://tmpfiles.org/dl/123/shot.png")]
    [InlineData("https://tmpfiles.org/dl/123/shot.png", "https://tmpfiles.org/dl/123/shot.png")]
    [InlineData("http://tmpfiles.org/456/x.gif", "https://tmpfiles.org/dl/456/x.gif")]
    public void ToTmpFilesDownloadUrl_RewritesToDownloadForm(string input, string expected)
    {
        Assert.Equal(expected, UploadService.ToTmpFilesDownloadUrl(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void ToTmpFilesDownloadUrl_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(UploadService.ToTmpFilesDownloadUrl(input));
    }
}
