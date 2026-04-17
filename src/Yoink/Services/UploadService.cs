using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentFTP;
using Renci.SshNet;
using Yoink.Models;

namespace Yoink.Services;

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
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
    };

    private static readonly UploadDestination[] TemporaryHostFallbacks =
    {
        UploadDestination.Litterbox,
        UploadDestination.TmpFiles,
        UploadDestination.Uguu,
        UploadDestination.Gofile,
        UploadDestination.FileIo
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
        if (provider.Equals("transfer.sh", StringComparison.OrdinalIgnoreCase)) return TransferLogoPath;
        if (provider.Equals("gofile", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (provider.Equals("imgpile", StringComparison.OrdinalIgnoreCase)) return string.Empty;
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
        UploadDestination.TransferSh => TransferLogoPath,
        UploadDestination.Gofile => string.Empty,
        UploadDestination.ImgPile => string.Empty,
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
            UploadDestination.Dropbox => 350L * 1024 * 1024,
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

    /// <summary>Check if the destination has the required credentials configured.</summary>
    public static bool HasCredentials(UploadDestination dest, UploadSettings settings) => dest switch
    {
        UploadDestination.None => false,
        UploadDestination.Imgur => !string.IsNullOrWhiteSpace(settings.ImgurClientId),
        UploadDestination.ImgBB => !string.IsNullOrWhiteSpace(settings.ImgBBApiKey),
        UploadDestination.ImgPile => !string.IsNullOrWhiteSpace(settings.ImgPileApiToken),
        UploadDestination.Gyazo => !string.IsNullOrWhiteSpace(settings.GyazoAccessToken),
        UploadDestination.Dropbox => !string.IsNullOrWhiteSpace(settings.DropboxAccessToken),
        UploadDestination.GoogleDrive => !string.IsNullOrWhiteSpace(settings.GoogleDriveAccessToken),
        UploadDestination.OneDrive => !string.IsNullOrWhiteSpace(settings.OneDriveAccessToken),
        UploadDestination.AzureBlob => !string.IsNullOrWhiteSpace(settings.AzureBlobSasUrl),
        UploadDestination.S3Compatible => !string.IsNullOrWhiteSpace(settings.S3AccessKey),
        UploadDestination.GitHub => !string.IsNullOrWhiteSpace(settings.GitHubToken),
        UploadDestination.Immich => !string.IsNullOrWhiteSpace(settings.ImmichApiKey),
        UploadDestination.Sftp => !string.IsNullOrWhiteSpace(settings.SftpHost)
            && !string.IsNullOrWhiteSpace(settings.SftpHostKeyFingerprint),
        UploadDestination.Ftp => !string.IsNullOrWhiteSpace(settings.FtpUrl),
        UploadDestination.CustomHttp => !string.IsNullOrWhiteSpace(settings.CustomUploadUrl),
        UploadDestination.AiChat => true,
        UploadDestination.TempHosts => true,
        UploadDestination.TmpFiles => true,
        UploadDestination.Gofile => true,
        // These don't need credentials
        UploadDestination.Catbox or UploadDestination.Litterbox or UploadDestination.FileIo
            or UploadDestination.Uguu or UploadDestination.TransferSh => true,
        _ => true,
    };

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

        return null;
    }

    private static async Task<UploadResult> UploadTemporaryHostsAsync(string filePath, UploadSettings settings)
    {
        var errors = new List<string>();
        foreach (var destination in TemporaryHostFallbacks)
        {
            var result = await UploadAsync(filePath, destination, settings);
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

            errors.Add($"{GetName(destination)}: {result.Error}");
        }

        return new UploadResult
        {
            Error = string.Join(" | ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
        };
    }

    public static async Task<UploadResult> UploadAsync(
        string filePath, UploadDestination dest, UploadSettings settings)
    {
        try
        {
            // Check file size limit
            var fileSize = new FileInfo(filePath).Length;
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

            var transportSecurityError = ValidateTransportSecurity(dest, settings);
            if (!string.IsNullOrWhiteSpace(transportSecurityError))
                return new UploadResult { Error = transportSecurityError };

            var result = dest switch
            {
                UploadDestination.Imgur => await UploadImgur(filePath, settings),
                UploadDestination.ImgBB => await UploadImgBB(filePath, settings),
                UploadDestination.Catbox => await UploadCatbox(filePath),
                UploadDestination.Litterbox => await UploadLitterbox(filePath),
                UploadDestination.Gyazo => await UploadGyazo(filePath, settings),
                UploadDestination.FileIo => await UploadFileIo(filePath),
                UploadDestination.Uguu => await UploadUguu(filePath),
                UploadDestination.TransferSh => await UploadTransferSh(filePath),
                UploadDestination.TmpFiles => await UploadTmpFiles(filePath),
                UploadDestination.Gofile => await UploadGofile(filePath),
                UploadDestination.ImgPile => await UploadImgPile(filePath, settings),
                UploadDestination.Dropbox => await UploadDropbox(filePath, settings),
                UploadDestination.GoogleDrive => await UploadGoogleDrive(filePath, settings),
                UploadDestination.OneDrive => await UploadOneDrive(filePath, settings),
                UploadDestination.AzureBlob => await UploadAzureBlob(filePath, settings),
                UploadDestination.GitHub => await UploadGitHub(filePath, settings),
                UploadDestination.Immich => await UploadImmich(filePath, settings),
                UploadDestination.Ftp => await UploadFtp(filePath, settings),
                UploadDestination.Sftp => await UploadSftp(filePath, settings),
                UploadDestination.WebDav => await UploadWebDav(filePath, settings),
                UploadDestination.S3Compatible => await UploadS3(filePath, settings),
                UploadDestination.CustomHttp => await UploadCustom(filePath, settings),
                UploadDestination.TempHosts => await UploadTemporaryHostsAsync(filePath, settings),
                _ => new UploadResult { Error = "No upload destination configured" }
            };

            if (!result.Success)
                LogUploadFailure(dest, filePath, result.Error);

            return result;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("upload.error", ex, $"{GetName(dest)} upload failed for {Path.GetFileName(filePath)}.");
            return new UploadResult { Error = ex.Message };
        }
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
    public string DropboxPathPrefix { get; set; } = "Yoink";

    // Google Drive
    public string GoogleDriveAccessToken { get; set; } = "";
    public string GoogleDriveFolderId { get; set; } = "";

    // OneDrive
    public string OneDriveAccessToken { get; set; } = "";
    public string OneDriveFolder { get; set; } = "Yoink";

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
    public AiChatProvider AiChatProvider { get; set; } = AiChatProvider.ChatGpt;
    public bool AiChatUploadDestinationSynced { get; set; }
    public UploadDestination AiChatUploadDestination { get; set; } = UploadDestination.TempHosts;
}
