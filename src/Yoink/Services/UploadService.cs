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
    CustomHttp
}

public sealed class UploadResult
{
    public bool Success { get; init; }
    public string Url { get; init; } = "";
    public string DeleteUrl { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsRateLimit { get; init; }
}

/// <summary>
/// Uploads images/GIFs to various hosting services.
/// All methods are static and use a shared HttpClient.
/// </summary>
public static class UploadService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
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
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
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
        UploadDestination.Dropbox => DropboxLogoPath,
        UploadDestination.GoogleDrive => GoogleDriveLogoPath,
        UploadDestination.OneDrive => OneDriveLogoPath,
        UploadDestination.AzureBlob => AzureLogoPath,
        UploadDestination.GitHub => GitHubLogoPath,
        UploadDestination.Immich => ImmichLogoPath,
        UploadDestination.S3Compatible => S3LogoPath,
        _ => string.Empty
    };

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
            _ => long.MaxValue
        };
    }

    /// <summary>Check if the destination has the required credentials configured.</summary>
    public static bool HasCredentials(UploadDestination dest, UploadSettings settings) => dest switch
    {
        UploadDestination.None => false,
        UploadDestination.Imgur => !string.IsNullOrWhiteSpace(settings.ImgurClientId),
        UploadDestination.ImgBB => !string.IsNullOrWhiteSpace(settings.ImgBBApiKey),
        UploadDestination.Gyazo => !string.IsNullOrWhiteSpace(settings.GyazoAccessToken),
        UploadDestination.Dropbox => !string.IsNullOrWhiteSpace(settings.DropboxAccessToken),
        UploadDestination.GoogleDrive => !string.IsNullOrWhiteSpace(settings.GoogleDriveAccessToken),
        UploadDestination.OneDrive => !string.IsNullOrWhiteSpace(settings.OneDriveAccessToken),
        UploadDestination.AzureBlob => !string.IsNullOrWhiteSpace(settings.AzureBlobSasUrl),
        UploadDestination.S3Compatible => !string.IsNullOrWhiteSpace(settings.S3AccessKey),
        UploadDestination.GitHub => !string.IsNullOrWhiteSpace(settings.GitHubToken),
        UploadDestination.Immich => !string.IsNullOrWhiteSpace(settings.ImmichApiKey),
        UploadDestination.Sftp => !string.IsNullOrWhiteSpace(settings.SftpHost),
        UploadDestination.Ftp => !string.IsNullOrWhiteSpace(settings.FtpUrl),
        UploadDestination.CustomHttp => !string.IsNullOrWhiteSpace(settings.CustomUploadUrl),
        // These don't need credentials
        UploadDestination.Catbox or UploadDestination.Litterbox or UploadDestination.FileIo
            or UploadDestination.Uguu or UploadDestination.TransferSh => true,
        _ => true,
    };

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

            return dest switch
            {
                UploadDestination.Imgur => await UploadImgur(filePath, settings),
                UploadDestination.ImgBB => await UploadImgBB(filePath, settings),
                UploadDestination.Catbox => await UploadCatbox(filePath),
                UploadDestination.Litterbox => await UploadLitterbox(filePath),
                UploadDestination.Gyazo => await UploadGyazo(filePath, settings),
                UploadDestination.FileIo => await UploadFileIo(filePath),
                UploadDestination.Uguu => await UploadUguu(filePath),
                UploadDestination.TransferSh => await UploadTransferSh(filePath),
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
                _ => new UploadResult { Error = "No upload destination configured" }
            };
        }
        catch (Exception ex)
        {
            return new UploadResult { Error = ex.Message };
        }
    }

    // ─── Imgur ────────────────────────────────────────────────────────

    private static async Task<UploadResult> UploadImgur(string filePath, UploadSettings s)
    {
        string clientId = string.IsNullOrWhiteSpace(s.ImgurClientId)
            ? "546c25a59c58ad7"
            : s.ImgurClientId;

        using var content = new MultipartFormDataContent();
        content.Add(CreateFileStreamContent(filePath), "image", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.imgur.com/3/image");
        if (!string.IsNullOrWhiteSpace(s.ImgurAccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ImgurAccessToken);
        else
            request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", clientId);

        request.Content = content;
        var resp = await Http.SendAsync(request);
        var json = await resp.Content.ReadAsStringAsync();
        var node = TryParseJson(json);

        if (node?["success"]?.GetValue<bool>() == true)
        {
            return new UploadResult
            {
                Success = true,
                Url = node["data"]?["link"]?.GetValue<string>() ?? "",
                DeleteUrl = $"https://imgur.com/delete/{node["data"]?["deletehash"]?.GetValue<string>()}"
            };
        }

        return new UploadResult { Error = BuildHttpError("Imgur", resp, json, node), IsRateLimit = (int)resp.StatusCode == 429 };
    }

    // ─── ImgBB ───────────────────────────────────────────────────────

    private static async Task<UploadResult> UploadImgBB(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.ImgBBApiKey))
            return new UploadResult { Error = "ImgBB API key not configured. Get one free at api.imgbb.com" };

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var base64 = Convert.ToBase64String(fileBytes);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["key"] = s.ImgBBApiKey,
            ["image"] = base64,
            ["name"] = Path.GetFileNameWithoutExtension(filePath)
        });

        var resp = await Http.PostAsync("https://api.imgbb.com/1/upload", content);
        var json = await resp.Content.ReadAsStringAsync();
        var node = TryParseJson(json);

        if (node?["success"]?.GetValue<bool>() == true)
        {
            return new UploadResult
            {
                Success = true,
                Url = node["data"]?["url"]?.GetValue<string>() ?? "",
                DeleteUrl = node["data"]?["delete_url"]?.GetValue<string>() ?? ""
            };
        }

        return new UploadResult { Error = BuildHttpError("ImgBB", resp, json, node), IsRateLimit = (int)resp.StatusCode == 429 };
    }

    // ─── Catbox.moe ──────────────────────────────────────────────────

    private static async Task<UploadResult> UploadCatbox(string filePath)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("fileupload"), "reqtype");
        content.Add(CreateFileStreamContent(filePath), "fileToUpload", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://catbox.moe/user/api.php", content);
        var url = (await resp.Content.ReadAsStringAsync()).Trim();

        if (url.StartsWith("https://"))
            return new UploadResult { Success = true, Url = url };

        return new UploadResult { Error = $"Catbox error: {url}" };
    }

    // ─── Litterbox (temporary Catbox) ────────────────────────────────

    private static async Task<UploadResult> UploadLitterbox(string filePath)
    {
        if (!File.Exists(filePath))
            return new UploadResult { Error = "Litterbox upload file not found" };

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("fileupload"), "reqtype");
        content.Add(new StringContent("72h"), "time");
        content.Add(CreateFileStreamContent(filePath), "fileToUpload", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://litterbox.catbox.moe/resources/internals/api.php", content);
        var url = (await resp.Content.ReadAsStringAsync()).Trim();

        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"Litterbox error ({resp.StatusCode}): {url}" };

        if (url.StartsWith("https://"))
            return new UploadResult { Success = true, Url = url };

        return new UploadResult { Error = $"Litterbox error: {url}" };
    }

    // ─── Gyazo ───────────────────────────────────────────────────────

    private static async Task<UploadResult> UploadGyazo(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.GyazoAccessToken))
            return new UploadResult { Error = "Gyazo access token not configured" };

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(s.GyazoAccessToken), "access_token");
        content.Add(CreateFileStreamContent(filePath), "imagedata", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://upload.gyazo.com/api/upload", content);
        var json = await resp.Content.ReadAsStringAsync();
        var node = TryParseJson(json);

        var url = node?["permalink_url"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(url))
            return new UploadResult { Success = true, Url = url };

        return new UploadResult { Error = BuildHttpError("Gyazo", resp, json, node), IsRateLimit = (int)resp.StatusCode == 429 };
    }

    // ─── file.io ─────────────────────────────────────────────────────

    private static async Task<UploadResult> UploadFileIo(string filePath)
    {
        using var content = new MultipartFormDataContent();
        content.Add(CreateFileStreamContent(filePath), "file", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://file.io", content);
        var json = await resp.Content.ReadAsStringAsync();
        var node = TryParseJson(json);

        if (node?["success"]?.GetValue<bool>() == true)
        {
            return new UploadResult
            {
                Success = true,
                Url = node["link"]?.GetValue<string>() ?? ""
            };
        }

        return new UploadResult { Error = BuildHttpError("file.io", resp, json, node), IsRateLimit = (int)resp.StatusCode == 429 };
    }

    // ─── Uguu.se ─────────────────────────────────────────────────────

    private static async Task<UploadResult> UploadUguu(string filePath)
    {
        using var content = new MultipartFormDataContent();
        content.Add(CreateFileStreamContent(filePath), "files[]", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://uguu.se/upload.php?output=text", content);
        var url = (await resp.Content.ReadAsStringAsync()).Trim();

        if (url.StartsWith("https://") || url.StartsWith("http://"))
            return new UploadResult { Success = true, Url = url };

        return new UploadResult { Error = $"Uguu error: {url}" };
    }

    // ─── transfer.sh ────────────────────────────────────────────────

    private static async Task<UploadResult> UploadTransferSh(string filePath)
    {
        using var content = new MultipartFormDataContent();
        content.Add(CreateFileStreamContent(filePath), "file", Path.GetFileName(filePath));

        var resp = await Http.PostAsync("https://transfer.sh", content);
        var url = (await resp.Content.ReadAsStringAsync()).Trim();

        if (url.StartsWith("https://") || url.StartsWith("http://"))
            return new UploadResult { Success = true, Url = url };

        return new UploadResult { Error = $"transfer.sh error: {url}" };
    }

    private static async Task<UploadResult> UploadDropbox(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DropboxAccessToken))
            return new UploadResult { Error = "Dropbox access token not configured" };

        string remotePath = $"/{(string.IsNullOrWhiteSpace(s.DropboxPathPrefix) ? "Yoink" : s.DropboxPathPrefix.Trim('/'))}/{Path.GetFileName(filePath)}";

        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
        uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.DropboxAccessToken);
        uploadReq.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = remotePath, mode = "add", autorename = true, mute = false }));
        uploadReq.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        uploadReq.Content = CreateFileStreamContent(filePath);
        var uploadResp = await Http.SendAsync(uploadReq);
        if (!uploadResp.IsSuccessStatusCode)
            return new UploadResult { Error = $"Dropbox upload failed: {uploadResp.StatusCode}" };

        using var shareReq = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/create_shared_link_with_settings");
        shareReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.DropboxAccessToken);
        shareReq.Content = new StringContent(JsonSerializer.Serialize(new { path = remotePath }), Encoding.UTF8, "application/json");
        var shareResp = await Http.SendAsync(shareReq);
        var shareBody = await shareResp.Content.ReadAsStringAsync();
        if (!shareResp.IsSuccessStatusCode && shareBody.Contains("shared_link_already_exists"))
        {
            using var listReq = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/list_shared_links");
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.DropboxAccessToken);
            listReq.Content = new StringContent(JsonSerializer.Serialize(new { path = remotePath, direct_only = true }), Encoding.UTF8, "application/json");
            var listResp = await Http.SendAsync(listReq);
            var listBody = await listResp.Content.ReadAsStringAsync();
            var listNode = TryParseJson(listBody);
            var existing = listNode?["links"]?.AsArray().FirstOrDefault()?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existing))
                return new UploadResult { Success = true, Url = existing.Replace("?dl=0", "?raw=1") };
        }
        else if (shareResp.IsSuccessStatusCode)
        {
            var shareNode = TryParseJson(shareBody);
            var url = shareNode?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(url))
                return new UploadResult { Success = true, Url = url.Replace("?dl=0", "?raw=1") };
        }

        return new UploadResult { Error = "Dropbox share link creation failed" };
    }

    private static async Task<UploadResult> UploadGoogleDrive(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.GoogleDriveAccessToken))
            return new UploadResult { Error = "Google Drive access token not configured" };

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string mimeType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

        var metadata = new JsonObject { ["name"] = Path.GetFileName(filePath) };
        if (!string.IsNullOrWhiteSpace(s.GoogleDriveFolderId))
            metadata["parents"] = new JsonArray(s.GoogleDriveFolderId);

        using var content = new MultipartFormDataContent("foo_bar_baz");
        content.Add(new StringContent(metadata.ToJsonString(), Encoding.UTF8, "application/json"), "metadata");
        content.Add(CreateFileStreamContent(filePath, mimeType), "file", Path.GetFileName(filePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink,webContentLink");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.GoogleDriveAccessToken);
        req.Content = content;
        var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"Google Drive upload failed: {resp.StatusCode}" };

        var node = TryParseJson(body);
        var id = node?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            return new UploadResult { Error = "Google Drive returned no file ID" };

        using var permReq = new HttpRequestMessage(HttpMethod.Post, $"https://www.googleapis.com/drive/v3/files/{id}/permissions");
        permReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.GoogleDriveAccessToken);
        permReq.Content = new StringContent("{\"role\":\"reader\",\"type\":\"anyone\"}", Encoding.UTF8, "application/json");
        await Http.SendAsync(permReq);

        return new UploadResult { Success = true, Url = $"https://drive.google.com/file/d/{id}/view" };
    }

    private static async Task<UploadResult> UploadOneDrive(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.OneDriveAccessToken))
            return new UploadResult { Error = "OneDrive access token not configured" };

        string folder = string.IsNullOrWhiteSpace(s.OneDriveFolder) ? "Yoink" : s.OneDriveFolder.Trim('/');
        string url = $"https://graph.microsoft.com/v1.0/me/drive/root:/{folder}/{Path.GetFileName(filePath)}:/content";

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.OneDriveAccessToken);
        req.Content = CreateFileStreamContent(filePath);
        var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"OneDrive upload failed: {resp.StatusCode}" };

        var node = TryParseJson(body);
        var itemId = node?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(itemId))
            return new UploadResult { Error = "OneDrive returned no item ID" };

        using var shareReq = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/me/drive/items/{itemId}/createLink");
        shareReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.OneDriveAccessToken);
        shareReq.Content = new StringContent("{\"type\":\"view\",\"scope\":\"anonymous\"}", Encoding.UTF8, "application/json");
        var shareResp = await Http.SendAsync(shareReq);
        var shareBody = await shareResp.Content.ReadAsStringAsync();
        if (!shareResp.IsSuccessStatusCode)
            return new UploadResult { Error = $"OneDrive share failed: {shareResp.StatusCode}" };
        var shareNode = TryParseJson(shareBody);
        var link = shareNode?["link"]?["webUrl"]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(link)
            ? new UploadResult { Success = true, Url = link }
            : new UploadResult { Error = "OneDrive returned no share URL" };
    }

    private static async Task<UploadResult> UploadAzureBlob(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.AzureBlobSasUrl))
            return new UploadResult { Error = "Azure Blob SAS base URL not configured" };

        string baseUrl = s.AzureBlobSasUrl.TrimEnd('/');
        string url = $"{baseUrl}/{Path.GetFileName(filePath)}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        req.Content = CreateFileStreamContent(filePath);
        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"Azure Blob upload failed: {resp.StatusCode}" };
        return new UploadResult { Success = true, Url = url.Split('?')[0] };
    }

    private static async Task<UploadResult> UploadGitHub(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.GitHubToken) || string.IsNullOrWhiteSpace(s.GitHubRepo))
            return new UploadResult { Error = "GitHub token or repo not configured" };

        string path = string.IsNullOrWhiteSpace(s.GitHubPathPrefix)
            ? Path.GetFileName(filePath)
            : $"{s.GitHubPathPrefix.Trim('/')}/{Path.GetFileName(filePath)}";
        string apiUrl = $"https://api.github.com/repos/{s.GitHubRepo}/contents/{path}";
        string branch = string.IsNullOrWhiteSpace(s.GitHubBranch) ? "main" : s.GitHubBranch;
        var base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(filePath));

        using var req = new HttpRequestMessage(HttpMethod.Put, apiUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.GitHubToken);
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            message = $"Upload {Path.GetFileName(filePath)}",
            content = base64,
            branch
        }), Encoding.UTF8, "application/json");

        var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"GitHub upload failed: {resp.StatusCode}" };

        return new UploadResult { Success = true, Url = $"https://raw.githubusercontent.com/{s.GitHubRepo}/{branch}/{path}" };
    }

    private static async Task<UploadResult> UploadImmich(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.ImmichBaseUrl) || string.IsNullOrWhiteSpace(s.ImmichApiKey))
            return new UploadResult { Error = "Immich base URL or API key not configured" };

        using var content = new MultipartFormDataContent();
        content.Add(CreateFileStreamContent(filePath), "assetData", Path.GetFileName(filePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, s.ImmichBaseUrl.TrimEnd('/') + "/api/assets");
        req.Headers.TryAddWithoutValidation("x-api-key", s.ImmichApiKey);
        req.Content = content;
        var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"Immich upload failed: {resp.StatusCode}" };

        var node = TryParseJson(body);
        var id = node?["id"]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(id)
            ? new UploadResult { Success = true, Url = s.ImmichBaseUrl.TrimEnd('/') + "/photos/" + id }
            : new UploadResult { Error = "Immich returned no asset ID" };
    }

    private static async Task<UploadResult> UploadFtp(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.FtpUrl) || string.IsNullOrWhiteSpace(s.FtpUsername))
            return new UploadResult { Error = "FTP URL or username not configured" };

        var baseUri = new Uri(s.FtpUrl.EndsWith('/') ? s.FtpUrl : s.FtpUrl + "/");
        string fileName = Path.GetFileName(filePath);
        string remotePath = baseUri.AbsolutePath.TrimEnd('/') + "/" + fileName;
        string url = new Uri(baseUri, Uri.EscapeDataString(fileName)).ToString();

        var config = new FtpConfig
        {
            EncryptionMode = FtpEncryptionMode.None,
            DataConnectionType = FtpDataConnectionType.AutoPassive,
            ValidateAnyCertificate = true
        };

        using var client = new AsyncFtpClient(baseUri.Host, s.FtpUsername, s.FtpPassword ?? string.Empty, baseUri.Port > 0 ? baseUri.Port : 21, config);
        await client.Connect();
        await client.UploadFile(filePath, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true);
        await client.Disconnect();

        return new UploadResult
        {
            Success = true,
            Url = string.IsNullOrWhiteSpace(s.FtpPublicUrl) ? url : s.FtpPublicUrl.TrimEnd('/') + "/" + fileName
        };
    }

    private static Task<UploadResult> UploadSftp(string filePath, UploadSettings s)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(s.SftpHost) || string.IsNullOrWhiteSpace(s.SftpUsername))
                return new UploadResult { Error = "SFTP host or username not configured" };

            using var client = new SftpClient(s.SftpHost, s.SftpPort <= 0 ? 22 : s.SftpPort, s.SftpUsername, s.SftpPassword ?? string.Empty);
            client.Connect();
            string remoteDir = string.IsNullOrWhiteSpace(s.SftpRemotePath) ? "/" : s.SftpRemotePath;
            string remotePath = remoteDir.TrimEnd('/') + "/" + Path.GetFileName(filePath);
            using var fs = File.OpenRead(filePath);
            client.UploadFile(fs, remotePath, true);
            client.Disconnect();
            string publicUrl = string.IsNullOrWhiteSpace(s.SftpPublicUrl)
                ? $"sftp://{s.SftpHost}{remotePath}"
                : s.SftpPublicUrl.TrimEnd('/') + "/" + Path.GetFileName(filePath);
            return new UploadResult { Success = true, Url = publicUrl };
        });
    }

    private static async Task<UploadResult> UploadWebDav(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.WebDavUrl) || string.IsNullOrWhiteSpace(s.WebDavUsername))
            return new UploadResult { Error = "WebDAV URL or username not configured" };

        string url = s.WebDavUrl.TrimEnd('/') + "/" + Path.GetFileName(filePath);
        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.WebDavUsername}:{s.WebDavPassword}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        req.Content = CreateFileStreamContent(filePath);
        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = $"WebDAV upload failed: {resp.StatusCode}" };
        return new UploadResult { Success = true, Url = string.IsNullOrWhiteSpace(s.WebDavPublicUrl) ? url : s.WebDavPublicUrl.TrimEnd('/') + "/" + Path.GetFileName(filePath) };
    }

    // ─── S3-Compatible (AWS S3, Cloudflare R2, Backblaze B2, etc.) ──

    private static async Task<UploadResult> UploadS3(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.S3Endpoint) || string.IsNullOrWhiteSpace(s.S3Bucket) ||
            string.IsNullOrWhiteSpace(s.S3AccessKey) || string.IsNullOrWhiteSpace(s.S3SecretKey))
            return new UploadResult { Error = "S3 configuration incomplete (endpoint, bucket, access key, secret key required)" };

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

        string key = string.IsNullOrWhiteSpace(s.S3PathPrefix)
            ? $"yoink/{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}"
            : $"{s.S3PathPrefix.TrimEnd('/')}/yoink/{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";

        string region = string.IsNullOrWhiteSpace(s.S3Region) ? "auto" : s.S3Region;
        string endpoint = s.S3Endpoint.TrimEnd('/');
        string host = endpoint.Contains("://")
            ? new Uri(endpoint).Host
            : endpoint;

        // Build the URL
        string objectUrl = endpoint.Contains("://")
            ? $"{endpoint}/{s.S3Bucket}/{key}"
            : $"https://{endpoint}/{s.S3Bucket}/{key}";

        // AWS Signature v4
        string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");
        string amzDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string payloadHash;
        using (var hashStream = File.OpenRead(filePath))
            payloadHash = HashHex(hashStream);

        string canonicalUri = $"/{s.S3Bucket}/{key}";
        string canonicalQueryString = "";
        string canonicalHeaders =
            $"content-type:{contentType}\n" +
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";
        string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        string canonicalRequest =
            $"PUT\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        string credentialScope = $"{dateStamp}/{region}/s3/aws4_request";
        string stringToSign =
            $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{HashHex(Encoding.UTF8.GetBytes(canonicalRequest))}";

        byte[] signingKey = GetSignatureKey(s.S3SecretKey, dateStamp, region, "s3");
        string signature = HmacHex(signingKey, stringToSign);
        string authHeader =
            $"AWS4-HMAC-SHA256 Credential={s.S3AccessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUrl);
        request.Content = CreateFileStreamContent(filePath, contentType);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);

        var resp = await Http.SendAsync(request);
        if (resp.IsSuccessStatusCode)
        {
            // Build public URL
            string publicUrl = !string.IsNullOrWhiteSpace(s.S3PublicUrl)
                ? $"{s.S3PublicUrl.TrimEnd('/')}/{key}"
                : objectUrl;
            return new UploadResult { Success = true, Url = publicUrl };
        }

        var body = await resp.Content.ReadAsStringAsync();
        return new UploadResult { Error = $"S3 error ({resp.StatusCode}): {body[..Math.Min(body.Length, 200)]}" };
    }

    private static string HashHex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static string HashHex(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string HmacHex(byte[] key, string data) =>
        Convert.ToHexStringLower(HmacSha256(key, data));

    private static byte[] GetSignatureKey(string secretKey, string dateStamp, string region, string service)
    {
        byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        byte[] kRegion = HmacSha256(kDate, region);
        byte[] kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    // ─── Custom HTTP uploader ────────────────────────────────────────

    private static async Task<UploadResult> UploadCustom(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.CustomUploadUrl))
            return new UploadResult { Error = "Custom upload URL not configured" };

        using var content = new MultipartFormDataContent();
        string fieldName = string.IsNullOrWhiteSpace(s.CustomFileFormName) ? "file" : s.CustomFileFormName;
        content.Add(CreateFileStreamContent(filePath), fieldName, Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, s.CustomUploadUrl);
        if (!string.IsNullOrWhiteSpace(s.CustomHeaders))
        {
            foreach (var line in s.CustomHeaders.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                    request.Headers.TryAddWithoutValidation(parts[0], parts[1]);
            }
        }
        request.Content = content;

        var resp = await Http.SendAsync(request);
        var body = await resp.Content.ReadAsStringAsync();

        string? url = null;
        if (!string.IsNullOrWhiteSpace(s.CustomResponseUrlPath))
        {
            try
            {
                var node = TryParseJson(body);
                var pathParts = s.CustomResponseUrlPath.Split('.');
                JsonNode? current = node;
                foreach (var part in pathParts)
                    current = current?[part];
                url = current?.GetValue<string>();
            }
            catch { }
        }

        url ??= body.Trim();

        if (!string.IsNullOrWhiteSpace(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
            return new UploadResult { Success = true, Url = url };

        var match = Regex.Match(body, @"https?://\S+");
        if (match.Success)
            return new UploadResult { Success = true, Url = match.Value.TrimEnd('"', '\'', '}', ']') };

        return new UploadResult { Error = $"Upload returned: {body[..Math.Min(body.Length, 200)]}" };
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
}
