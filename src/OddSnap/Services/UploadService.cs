using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using FluentFTP;
using Renci.SshNet;
using OddSnap.Models;

namespace OddSnap.Services;

public enum UploadDestination
{
    None,
    Imgur,
    ImgBB,
    Catbox,
    Litterbox,
    Gyazo,
    FileIo,
    Uguu,
    TransferSh,
    Dropbox,
    GoogleDrive,
    OneDrive,
    AzureBlob,
    GitHub,
    Immich,
    Ftp,
    Sftp,
    WebDav,
    S3Compatible,
    CustomHttp,
    AiChat,
    TempHosts,
    TmpFiles,
    Gofile,
    ImgPile
}

public enum AiChatProvider
{
    None = -1,
    ChatGpt,
    Claude,
    // Legacy saved value; normalized to Claude in current UI/runtime behavior.
    ClaudeOpus,
    Gemini,
    GoogleLens
}

public sealed class UploadResult
{
    public bool Success { get; init; }
    public string Url { get; init; } = "";
    public string DeleteUrl { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsRateLimit { get; init; }
    public string ProviderName { get; init; } = "";
}

/// <summary>
/// Uploads images/GIFs to various hosting services.
/// All methods are static and use a shared HttpClient.
/// </summary>
public static partial class UploadService
{
    private const long MaxUploadTextResponseBytes = 1L * 1024 * 1024;
    private const long MaxUploadErrorResponseBytes = 64L * 1024;
    private const int TemporaryHostsTotalUploadTimeoutSeconds = 90;
    private const int TemporaryHostUploadTimeoutSeconds = 25;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestHeaders = { { "User-Agent", "OddSnap/1.0" } }
    };

    private static readonly UploadDestination[] TemporaryHostFallbacks =
    {
        UploadDestination.Litterbox,
        UploadDestination.TmpFiles,
        UploadDestination.Uguu,
        UploadDestination.Gofile,
        UploadDestination.Catbox
    };

    private static JsonNode? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch { return null; }
    }

    private static string BuildHttpError(string service, HttpResponseMessage resp, string body, JsonNode? node = null)
    {
        if ((int)resp.StatusCode == 429)
            return $"{service} rate limit reached";

        string? nodeMsg =
            node?["error"]?["message"]?.GetValue<string>() ??
            node?["data"]?["error"]?.GetValue<string>() ??
            node?["meta"]?["msg"]?.GetValue<string>() ??
            node?["message"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(nodeMsg))
            return nodeMsg;

        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.StartsWith("<", StringComparison.OrdinalIgnoreCase))
        {
            return resp.StatusCode switch
            {
                HttpStatusCode.Forbidden => $"{service} rejected the upload (forbidden or missing approval)",
                HttpStatusCode.Unauthorized => $"{service} rejected the credentials",
                HttpStatusCode.BadRequest => $"{service} rejected the upload request",
                HttpStatusCode.NotFound => $"{service} upload endpoint was not found",
                HttpStatusCode.TooManyRequests => $"{service} rate limit reached",
                _ => $"{service} returned an HTML error page ({(int)resp.StatusCode})"
            };
        }

        if (!string.IsNullOrWhiteSpace(trimmed))
            return trimmed.Length > 180 ? trimmed[..180] : trimmed;

        return $"{service} error: {resp.StatusCode}";
    }

    private static StreamContent CreateFileStreamContent(string filePath, string contentType = "application/octet-stream")
    {
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        var content = new StreamContent(stream);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private static Task<HttpResponseMessage> SendUploadRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostUploadContentAsync(string requestUri, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        return await SendUploadRequestAsync(request, cancellationToken);
    }

    private static Task<string> ReadUploadResponseTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var limit = response.IsSuccessStatusCode
            ? MaxUploadTextResponseBytes
            : MaxUploadErrorResponseBytes;
        return HttpContentReader.ReadLimitedStringAsync(response.Content, limit, cancellationToken);
    }

    /// <summary>Human-readable name for a destination.</summary>
    public static string GetName(UploadDestination dest) => dest switch
    {
        UploadDestination.Imgur => "Imgur",
        UploadDestination.ImgBB => "ImgBB",
        UploadDestination.Catbox => "Catbox",
        UploadDestination.Litterbox => "Litterbox",
        UploadDestination.Gyazo => "Gyazo",
        UploadDestination.FileIo => "file.io",
        UploadDestination.Uguu => "Uguu",
        UploadDestination.TransferSh => "transfer.sh",
        UploadDestination.Dropbox => "Dropbox",
        UploadDestination.GoogleDrive => "Google Drive",
        UploadDestination.OneDrive => "OneDrive",
        UploadDestination.AzureBlob => "Azure Blob",
        UploadDestination.GitHub => "GitHub",
        UploadDestination.Immich => "Immich",
        UploadDestination.Ftp => "FTP",
        UploadDestination.Sftp => "SFTP",
        UploadDestination.WebDav => "WebDAV",
        UploadDestination.S3Compatible => "S3",
        UploadDestination.CustomHttp => "Custom",
        UploadDestination.AiChat => "AI Redirects",
        UploadDestination.TempHosts => "Filter between free/no-setup hosts",
        UploadDestination.TmpFiles => "tmpfiles.org",
        UploadDestination.Gofile => "Gofile",
        UploadDestination.ImgPile => "imgpile",
        _ => ""
    };

    private const string ImgurLogoPath = "Assets/imgur_sq.png";
    private const string ImgBbLogoPath = "Assets/imgbb_sq.png";
    private const string CatboxLogoPath = "Assets/catbox_sq.png";
    private const string LitterboxLogoPath = "Assets/litterbox_sq.png";
    private const string GyazoLogoPath = "Assets/gyazo_sq.png";
    private const string FileIoLogoPath = "Assets/fileio_sq.png";
    private const string UguuLogoPath = "Assets/uguu_sq.png";
    private const string TmpFilesLogoPath = "Assets/tmpfiles_sq.png";
    private const string GofileLogoPath = "Assets/gofile_sq.png";
    private const string ImgPileLogoPath = "Assets/imgpile_sq.png";
    private const string TransferLogoPath = "Assets/transfer_sq.png";
    private const string DropboxLogoPath = "Assets/dropbox_sq.png";
    private const string GoogleDriveLogoPath = "Assets/gdrive_sq.png";
    private const string OneDriveLogoPath = "Assets/onedrive_sq.png";
    private const string AzureLogoPath = "Assets/azure_sq.png";
    private const string GitHubLogoPath = "Assets/github_sq.png";
    private const string ImmichLogoPath = "Assets/immich_sq.png";
    private const string S3LogoPath = "Assets/aws_sq.png";

    public static string GetHistoryLogoPath(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return string.Empty;

        provider = provider.Trim();

        if (provider.Equals("imgur", StringComparison.OrdinalIgnoreCase)) return ImgurLogoPath;
        if (provider.Equals("imgbb", StringComparison.OrdinalIgnoreCase)) return ImgBbLogoPath;
        if (provider.Equals("catbox", StringComparison.OrdinalIgnoreCase)) return CatboxLogoPath;
        if (provider.Equals("litterbox", StringComparison.OrdinalIgnoreCase)) return LitterboxLogoPath;
        if (provider.Equals("gyazo", StringComparison.OrdinalIgnoreCase)) return GyazoLogoPath;
        if (provider.Equals("file.io", StringComparison.OrdinalIgnoreCase)) return FileIoLogoPath;
        if (provider.Equals("uguu", StringComparison.OrdinalIgnoreCase)) return UguuLogoPath;
        if (provider.Equals("tmpfiles.org", StringComparison.OrdinalIgnoreCase)) return TmpFilesLogoPath;
        if (provider.Equals("transfer.sh", StringComparison.OrdinalIgnoreCase)) return TransferLogoPath;
        if (provider.Equals("gofile", StringComparison.OrdinalIgnoreCase)) return GofileLogoPath;
        if (provider.Equals("imgpile", StringComparison.OrdinalIgnoreCase)) return ImgPileLogoPath;
        if (provider.Equals("dropbox", StringComparison.OrdinalIgnoreCase)) return DropboxLogoPath;
        if (provider.Equals("google drive", StringComparison.OrdinalIgnoreCase)) return GoogleDriveLogoPath;
        if (provider.Equals("onedrive", StringComparison.OrdinalIgnoreCase)) return OneDriveLogoPath;
        if (provider.Equals("azure blob", StringComparison.OrdinalIgnoreCase)) return AzureLogoPath;
        if (provider.Equals("github", StringComparison.OrdinalIgnoreCase)) return GitHubLogoPath;
        if (provider.Equals("immich", StringComparison.OrdinalIgnoreCase)) return ImmichLogoPath;
        if (provider.Equals("s3", StringComparison.OrdinalIgnoreCase)) return S3LogoPath;

        return string.Empty;
    }

    public static string GetUploadsLogoPath(UploadDestination dest) => dest switch
    {
        UploadDestination.Imgur => ImgurLogoPath,
        UploadDestination.ImgBB => ImgBbLogoPath,
        UploadDestination.Catbox => CatboxLogoPath,
        UploadDestination.Litterbox => LitterboxLogoPath,
        UploadDestination.Gyazo => GyazoLogoPath,
        UploadDestination.FileIo => FileIoLogoPath,
        UploadDestination.Uguu => UguuLogoPath,
        UploadDestination.TmpFiles => TmpFilesLogoPath,
        UploadDestination.TransferSh => TransferLogoPath,
        UploadDestination.Gofile => GofileLogoPath,
        UploadDestination.ImgPile => ImgPileLogoPath,
        UploadDestination.Dropbox => DropboxLogoPath,
        UploadDestination.GoogleDrive => GoogleDriveLogoPath,
        UploadDestination.OneDrive => OneDriveLogoPath,
        UploadDestination.AzureBlob => AzureLogoPath,
        UploadDestination.GitHub => GitHubLogoPath,
        UploadDestination.Immich => ImmichLogoPath,
        UploadDestination.S3Compatible => S3LogoPath,
        _ => string.Empty
    };

    public static bool IsAiChatDestination(UploadDestination dest) =>
        dest == UploadDestination.AiChat;

    public static bool AiChatProviderRequiresHostedImage(AiChatProvider provider) =>
        provider == AiChatProvider.GoogleLens;

    public static UploadDestination NormalizeAiChatUploadDestination(UploadDestination destination) =>
        destination is UploadDestination.None or UploadDestination.AiChat
            ? UploadDestination.TempHosts
            : destination;

    public static bool ShouldUploadScreenshot(AppSettings settings, bool hasFilePath, bool useAiRedirect)
    {
        if (!hasFilePath || settings.ImageUploadDestination == UploadDestination.None)
            return false;

        if (settings.ImageUploadDestination == UploadDestination.AiChat)
            return useAiRedirect;

        return settings.AutoUploadScreenshots;
    }

    public static string GetAiChatProviderName(AiChatProvider provider) => provider switch
    {
        AiChatProvider.None => "None",
        AiChatProvider.ChatGpt => "ChatGPT",
        AiChatProvider.Claude => "Claude",
        AiChatProvider.ClaudeOpus => "Claude",
        AiChatProvider.Gemini => "Gemini",
        AiChatProvider.GoogleLens => "Google Lens",
        _ => "AI Redirects"
    };

    public static string BuildAiChatStartUrl(AiChatProvider provider)
    {
        return provider switch
        {
            AiChatProvider.ChatGpt => "https://chatgpt.com/",
            AiChatProvider.None => "",
            AiChatProvider.Claude => "https://claude.ai/new",
            AiChatProvider.ClaudeOpus => "https://claude.ai/new",
            AiChatProvider.Gemini => "https://gemini.google.com/app",
            AiChatProvider.GoogleLens => "https://lens.google.com/search?hl=en&country=us",
            _ => "https://chatgpt.com/"
        };
    }

    public static string BuildGoogleLensUrl(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Google Lens needs an absolute image URL.");
        }

        return $"https://lens.google.com/uploadbyurl?url={Uri.EscapeDataString(uri.ToString())}&hl=en&country=us";
    }

    /// <summary>Max file size in bytes per destination.</summary>
    public static long GetMaxSize(UploadDestination dest, string filePath)
    {
        bool isGif = Path.GetExtension(filePath).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        return dest switch
        {
            UploadDestination.Imgur => isGif ? 200L * 1024 * 1024 : 20L * 1024 * 1024,
            UploadDestination.ImgBB => 32L * 1024 * 1024,
            UploadDestination.Catbox => 200L * 1024 * 1024,
            UploadDestination.Litterbox => 1024L * 1024 * 1024,
            UploadDestination.Gyazo => 25L * 1024 * 1024,
            UploadDestination.FileIo => 100L * 1024 * 1024,
            UploadDestination.Uguu => 128L * 1024 * 1024,
            UploadDestination.TransferSh => 10L * 1024 * 1024 * 1024,
            UploadDestination.Dropbox => 150L * 1024 * 1024,
            UploadDestination.GoogleDrive => 5L * 1024 * 1024 * 1024,
            UploadDestination.OneDrive => 250L * 1024 * 1024,
            UploadDestination.AzureBlob => 5L * 1024 * 1024 * 1024,
            UploadDestination.GitHub => 100L * 1024 * 1024,
            UploadDestination.Immich => 5L * 1024 * 1024 * 1024,
            UploadDestination.Ftp => 5L * 1024 * 1024 * 1024,
            UploadDestination.Sftp => 5L * 1024 * 1024 * 1024,
            UploadDestination.WebDav => 5L * 1024 * 1024 * 1024,
            UploadDestination.S3Compatible => 5L * 1024 * 1024 * 1024,
            UploadDestination.AiChat => long.MaxValue,
            UploadDestination.TempHosts => 100L * 1024 * 1024,
            UploadDestination.TmpFiles => 100L * 1024 * 1024,
            UploadDestination.Gofile => long.MaxValue,
            UploadDestination.ImgPile => 100L * 1024 * 1024,
            _ => long.MaxValue
        };
    }

    public static string? GetConfigurationError(UploadDestination dest, UploadSettings settings)
    {
        return dest switch
        {
            UploadDestination.None => "Choose an upload destination in Settings -> Uploads.",
            UploadDestination.Imgur when string.IsNullOrWhiteSpace(settings.ImgurClientId) => MissingUploadSetting("Imgur client ID"),
            UploadDestination.ImgBB when string.IsNullOrWhiteSpace(settings.ImgBBApiKey) => MissingUploadSetting("ImgBB API key"),
            UploadDestination.ImgPile when string.IsNullOrWhiteSpace(settings.ImgPileApiToken) => MissingUploadSetting("imgpile API token"),
            UploadDestination.Gyazo when string.IsNullOrWhiteSpace(settings.GyazoAccessToken) => MissingUploadSetting("Gyazo access token"),
            UploadDestination.Dropbox when string.IsNullOrWhiteSpace(settings.DropboxAccessToken) => MissingUploadSetting("Dropbox access token"),
            UploadDestination.GoogleDrive when string.IsNullOrWhiteSpace(settings.GoogleDriveAccessToken) => MissingUploadSetting("Google Drive access token"),
            UploadDestination.OneDrive when string.IsNullOrWhiteSpace(settings.OneDriveAccessToken) => MissingUploadSetting("OneDrive access token"),
            UploadDestination.AzureBlob when string.IsNullOrWhiteSpace(settings.AzureBlobSasUrl) => MissingUploadSetting("Azure Blob SAS URL"),
            UploadDestination.GitHub when string.IsNullOrWhiteSpace(settings.GitHubToken) => MissingUploadSetting("GitHub token"),
            UploadDestination.GitHub when string.IsNullOrWhiteSpace(settings.GitHubRepo) => MissingUploadSetting("GitHub repo"),
            UploadDestination.Immich when string.IsNullOrWhiteSpace(settings.ImmichBaseUrl) => MissingUploadSetting("Immich base URL"),
            UploadDestination.Immich when string.IsNullOrWhiteSpace(settings.ImmichApiKey) => MissingUploadSetting("Immich API key"),
            UploadDestination.Ftp when string.IsNullOrWhiteSpace(settings.FtpUrl) => MissingUploadSetting("FTP URL"),
            UploadDestination.Ftp when string.IsNullOrWhiteSpace(settings.FtpUsername) => MissingUploadSetting("FTP username"),
            UploadDestination.Sftp when string.IsNullOrWhiteSpace(settings.SftpHost) => MissingUploadSetting("SFTP host"),
            UploadDestination.Sftp when string.IsNullOrWhiteSpace(settings.SftpUsername) => MissingUploadSetting("SFTP username"),
            UploadDestination.Sftp when !TryNormalizeSftpFingerprint(settings.SftpHostKeyFingerprint, out _) => MissingUploadSetting("SFTP host key fingerprint"),
            UploadDestination.WebDav when string.IsNullOrWhiteSpace(settings.WebDavUrl) => MissingUploadSetting("WebDAV URL"),
            UploadDestination.WebDav when string.IsNullOrWhiteSpace(settings.WebDavUsername) => MissingUploadSetting("WebDAV username"),
            UploadDestination.S3Compatible when string.IsNullOrWhiteSpace(settings.S3Endpoint) => MissingUploadSetting("S3 endpoint"),
            UploadDestination.S3Compatible when string.IsNullOrWhiteSpace(settings.S3Bucket) => MissingUploadSetting("S3 bucket"),
            UploadDestination.S3Compatible when string.IsNullOrWhiteSpace(settings.S3AccessKey) => MissingUploadSetting("S3 access key"),
            UploadDestination.S3Compatible when string.IsNullOrWhiteSpace(settings.S3SecretKey) => MissingUploadSetting("S3 secret key"),
            UploadDestination.CustomHttp when string.IsNullOrWhiteSpace(settings.CustomUploadUrl) => MissingUploadSetting("Custom upload URL"),
            _ => null,
        };
    }

    private static string MissingUploadSetting(string settingName)
        => $"{settingName} not configured. Add or update it in Settings -> Uploads.";

    /// <summary>Check if the destination has the required upload configuration.</summary>
    public static bool HasCredentials(UploadDestination dest, UploadSettings settings)
        => GetConfigurationError(dest, settings) is null;

    private static string? ValidateTransportSecurity(UploadDestination dest, UploadSettings settings)
    {
        if (dest == UploadDestination.WebDav)
        {
            if (!Uri.TryCreate(settings.WebDavUrl, UriKind.Absolute, out var webDavUri) ||
                webDavUri.Scheme != Uri.UriSchemeHttps)
            {
                return "WebDAV uploads require an HTTPS URL.";
            }
        }

        if (dest == UploadDestination.S3Compatible &&
            settings.S3Endpoint.Contains("://", StringComparison.Ordinal) &&
            (!Uri.TryCreate(settings.S3Endpoint, UriKind.Absolute, out var s3Uri) ||
             s3Uri.Scheme != Uri.UriSchemeHttps))
        {
            return "S3 uploads require an HTTPS endpoint.";
        }

        if (dest == UploadDestination.Immich)
        {
            if (!Uri.TryCreate(settings.ImmichBaseUrl, UriKind.Absolute, out var immichUri) ||
                immichUri.Scheme != Uri.UriSchemeHttps)
            {
                return "Immich uploads require an HTTPS server URL.";
            }
        }

        if (dest == UploadDestination.CustomHttp)
        {
            if (!Uri.TryCreate(settings.CustomUploadUrl, UriKind.Absolute, out var customUri) ||
                customUri.Scheme != Uri.UriSchemeHttps)
            {
                return "Custom uploads require an HTTPS URL.";
            }
        }

        return null;
    }

    private static async Task<UploadResult> UploadTemporaryHostsAsync(string filePath, UploadSettings settings, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(TimeSpan.FromSeconds(TemporaryHostsTotalUploadTimeoutSeconds));

        foreach (var destination in TemporaryHostFallbacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalTimeout.IsCancellationRequested)
            {
                errors.Add(BuildTemporaryHostsTotalTimeoutMessage());
                break;
            }

            using var hostTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
            hostTimeout.CancelAfter(TimeSpan.FromSeconds(TemporaryHostUploadTimeoutSeconds));

            var result = await UploadAsync(filePath, destination, settings, hostTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();

            if (result.Success)
            {
                return new UploadResult
                {
                    Success = true,
                    Url = result.Url,
                    DeleteUrl = result.DeleteUrl,
                    ProviderName = string.IsNullOrWhiteSpace(result.ProviderName) ? GetName(destination) : result.ProviderName
                };
            }

            if (hostTimeout.IsCancellationRequested)
            {
                errors.Add(totalTimeout.IsCancellationRequested
                    ? BuildTemporaryHostsTotalTimeoutMessage()
                    : $"{GetName(destination)}: timed out after {TemporaryHostUploadTimeoutSeconds} seconds.");

                if (totalTimeout.IsCancellationRequested)
                    break;
                continue;
            }

            errors.Add($"{GetName(destination)}: {result.Error}");
        }

        return new UploadResult
        {
            Error = string.Join(" | ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
        };
    }

    private static string BuildTemporaryHostsTotalTimeoutMessage()
        => $"Temp Hosts timed out after {TemporaryHostsTotalUploadTimeoutSeconds} seconds. Check your connection or choose a specific upload destination in Settings -> Uploads.";

    public static async Task<UploadResult> UploadAsync(
        string filePath, UploadDestination dest, UploadSettings settings, CancellationToken cancellationToken = default)
    {
        var uploadStarted = PerformanceTrace.Timestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new UploadResult
                {
                    Error = "The file to upload is missing. Save the capture again or choose an existing file."
                };
            }

            // Check file size limit
            var fileSize = new FileInfo(filePath).Length;
            if (fileSize <= 0)
            {
                return new UploadResult
                {
                    Error = "The file to upload is empty. Save the capture again and retry."
                };
            }

            var maxSize = GetMaxSize(dest, filePath);
            if (fileSize > maxSize)
            {
                string maxStr = maxSize >= 1024 * 1024
                    ? $"{maxSize / (1024 * 1024)}MB"
                    : $"{maxSize / 1024}KB";
                return new UploadResult { Error = $"File too large ({fileSize / (1024 * 1024)}MB). {GetName(dest)} limit is {maxStr}." };
            }

            if (IsAiChatDestination(dest))
                return new UploadResult { Error = "AI Redirects uses browser redirects instead of host upload." };

            var configurationError = GetConfigurationError(dest, settings);
            if (!string.IsNullOrWhiteSpace(configurationError))
            {
                return new UploadResult
                {
                    Error = configurationError,
                    ProviderName = GetName(dest)
                };
            }

            var transportSecurityError = ValidateTransportSecurity(dest, settings);
            if (!string.IsNullOrWhiteSpace(transportSecurityError))
                return new UploadResult { Error = transportSecurityError };

            var result = dest switch
            {
                UploadDestination.Imgur => await UploadImgur(filePath, settings, cancellationToken),
                UploadDestination.ImgBB => await UploadImgBB(filePath, settings, cancellationToken),
                UploadDestination.Catbox => await UploadCatbox(filePath, cancellationToken),
                UploadDestination.Litterbox => await UploadLitterbox(filePath, cancellationToken),
                UploadDestination.Gyazo => await UploadGyazo(filePath, settings, cancellationToken),
                UploadDestination.FileIo => await UploadFileIo(filePath, cancellationToken),
                UploadDestination.Uguu => await UploadUguu(filePath, cancellationToken),
                UploadDestination.TransferSh => await UploadTransferSh(filePath, cancellationToken),
                UploadDestination.TmpFiles => await UploadTmpFiles(filePath, cancellationToken),
                UploadDestination.Gofile => await UploadGofile(filePath, cancellationToken),
                UploadDestination.ImgPile => await UploadImgPile(filePath, settings, cancellationToken),
                UploadDestination.Dropbox => await UploadDropbox(filePath, settings, cancellationToken),
                UploadDestination.GoogleDrive => await UploadGoogleDrive(filePath, settings, cancellationToken),
                UploadDestination.OneDrive => await UploadOneDrive(filePath, settings, cancellationToken),
                UploadDestination.AzureBlob => await UploadAzureBlob(filePath, settings, cancellationToken),
                UploadDestination.GitHub => await UploadGitHub(filePath, settings, cancellationToken),
                UploadDestination.Immich => await UploadImmich(filePath, settings, cancellationToken),
                UploadDestination.Ftp => await UploadFtp(filePath, settings, cancellationToken),
                UploadDestination.Sftp => await UploadSftp(filePath, settings, cancellationToken),
                UploadDestination.WebDav => await UploadWebDav(filePath, settings, cancellationToken),
                UploadDestination.S3Compatible => await UploadS3(filePath, settings, cancellationToken),
                UploadDestination.CustomHttp => await UploadCustom(filePath, settings, cancellationToken),
                UploadDestination.TempHosts => await UploadTemporaryHostsAsync(filePath, settings, cancellationToken),
                _ => new UploadResult { Error = "No upload destination configured" }
            };

            if (!result.Success)
                LogUploadFailure(dest, filePath, result.Error);

            PerformanceTrace.LogElapsed(
                "perf.upload",
                uploadStarted,
                $"{GetName(dest)} success={result.Success} file={Path.GetFileName(filePath)}");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PerformanceTrace.LogElapsed(
                "perf.upload",
                uploadStarted,
                $"{GetName(dest)} canceled file={Path.GetFileName(filePath)}");
            return new UploadResult { Error = "Upload canceled." };
        }
        catch (Exception ex)
        {
            var errorMessage = BuildUploadExceptionMessage(dest, ex);
            AppDiagnostics.LogError("upload.error", ex, $"{GetName(dest)} upload failed for {Path.GetFileName(filePath)}.");
            PerformanceTrace.LogElapsed(
                "perf.upload",
                uploadStarted,
                $"{GetName(dest)} failed file={Path.GetFileName(filePath)}");
            return new UploadResult { Error = errorMessage };
        }
    }

    private static string BuildUploadExceptionMessage(UploadDestination dest, Exception ex)
    {
        var provider = GetName(dest);
        provider = string.IsNullOrWhiteSpace(provider) ? "Upload provider" : provider;

        return ex switch
        {
            TaskCanceledException => $"{provider} upload timed out. Check your connection and try again.",
            HttpRequestException => $"{provider} could not be reached. Check your connection and try again.",
            FileNotFoundException => "The file to upload is missing. Save the capture again or choose an existing file.",
            DirectoryNotFoundException => "The upload file folder no longer exists. Save the capture again or choose another folder.",
            IOException => "OddSnap could not read the file for upload. Make sure it is not locked by another app and retry.",
            UnauthorizedAccessException => "OddSnap does not have permission to read the file for upload.",
            _ => string.IsNullOrWhiteSpace(ex.Message) ? $"{provider} upload failed." : ex.Message
        };
    }

    private static void LogUploadFailure(UploadDestination dest, string filePath, string error)
    {
        var message = $"{GetName(dest)} upload failed for {Path.GetFileName(filePath)}: {error}";
        AppDiagnostics.LogWarning("upload.failed", message);
    }

}

/// <summary>Settings for upload destinations. Stored as part of AppSettings.</summary>
public sealed class UploadSettings
{
    // Imgur
    public string ImgurClientId { get; set; } = "";
    public string ImgurAccessToken { get; set; } = "";

    // ImgBB
    public string ImgBBApiKey { get; set; } = "";

    // imgpile
    public string ImgPileApiToken { get; set; } = "";

    // Gyazo
    public string GyazoAccessToken { get; set; } = "";

    // Dropbox
    public string DropboxAccessToken { get; set; } = "";
    public string DropboxPathPrefix { get; set; } = "OddSnap";

    // Google Drive
    public string GoogleDriveAccessToken { get; set; } = "";
    public string GoogleDriveFolderId { get; set; } = "";

    // OneDrive
    public string OneDriveAccessToken { get; set; } = "";
    public string OneDriveFolder { get; set; } = "OddSnap";

    // Azure Blob
    public string AzureBlobSasUrl { get; set; } = "";

    // GitHub
    public string GitHubToken { get; set; } = "";
    public string GitHubRepo { get; set; } = "";
    public string GitHubBranch { get; set; } = "main";
    public string GitHubPathPrefix { get; set; } = "uploads";

    // Immich
    public string ImmichBaseUrl { get; set; } = "";
    public string ImmichApiKey { get; set; } = "";

    // FTP
    public string FtpUrl { get; set; } = "";
    public string FtpUsername { get; set; } = "";
    public string FtpPassword { get; set; } = "";
    public string FtpPublicUrl { get; set; } = "";

    // SFTP
    public string SftpHost { get; set; } = "";
    public int SftpPort { get; set; } = 22;
    public string SftpUsername { get; set; } = "";
    public string SftpPassword { get; set; } = "";
    public string SftpRemotePath { get; set; } = "/";
    public string SftpPublicUrl { get; set; } = "";
    public string SftpHostKeyFingerprint { get; set; } = "";

    // WebDAV
    public string WebDavUrl { get; set; } = "";
    public string WebDavUsername { get; set; } = "";
    public string WebDavPassword { get; set; } = "";
    public string WebDavPublicUrl { get; set; } = "";

    // S3-Compatible (AWS, R2, B2, etc.)
    public string S3Endpoint { get; set; } = "";
    public string S3Region { get; set; } = "auto";
    public string S3Bucket { get; set; } = "";
    public string S3AccessKey { get; set; } = "";
    public string S3SecretKey { get; set; } = "";
    public string S3PathPrefix { get; set; } = "";
    public string S3PublicUrl { get; set; } = "";

    // Custom HTTP
    public string CustomUploadUrl { get; set; } = "";
    public string CustomFileFormName { get; set; } = "file";
    public string CustomResponseUrlPath { get; set; } = "url";
    public string CustomHeaders { get; set; } = "";

    // AI Chat
    public AiChatProvider AiChatProvider { get; set; } = AiChatProvider.GoogleLens;
    public bool AiChatUploadDestinationSynced { get; set; }
    public UploadDestination AiChatUploadDestination { get; set; } = UploadDestination.TempHosts;
}
