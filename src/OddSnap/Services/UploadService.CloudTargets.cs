using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentFTP;
using Renci.SshNet;

namespace OddSnap.Services;

public static partial class UploadService
{
    private static async Task<UploadResult> UploadDropbox(string filePath, UploadSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DropboxAccessToken))
            return new UploadResult { Error = "Dropbox access token not configured" };

        string remotePath = $"/{(string.IsNullOrWhiteSpace(s.DropboxPathPrefix) ? "OddSnap" : s.DropboxPathPrefix.Trim('/'))}/{Path.GetFileName(filePath)}";

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

        string folder = string.IsNullOrWhiteSpace(s.OneDriveFolder) ? "OddSnap" : s.OneDriveFolder.Trim('/');
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

        var rawUrl = s.FtpUrl.Trim();
        if (!rawUrl.Contains("://", StringComparison.Ordinal))
            rawUrl = "ftp://" + rawUrl;

        if (!Uri.TryCreate(rawUrl.EndsWith('/') ? rawUrl : rawUrl + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("ftp" or "ftps"))
        {
            return new UploadResult { Error = "FTP URL must be a valid ftp:// or ftps:// address." };
        }

        string fileName = Path.GetFileName(filePath);
        string remotePath = baseUri.AbsolutePath.TrimEnd('/') + "/" + fileName;
        string url = new Uri(baseUri, Uri.EscapeDataString(fileName)).ToString();

        var config = new FtpConfig
        {
            EncryptionMode = FtpEncryptionMode.Explicit,
            DataConnectionType = FtpDataConnectionType.AutoPassive
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
            if (!TryNormalizeSftpFingerprint(s.SftpHostKeyFingerprint, out var expectedFingerprint))
                return new UploadResult { Error = "SFTP host key fingerprint not configured or invalid (expected 64 hex chars)." };

            using var client = new SftpClient(s.SftpHost, s.SftpPort <= 0 ? 22 : s.SftpPort, s.SftpUsername, s.SftpPassword ?? string.Empty);
            client.HostKeyReceived += (_, e) =>
            {
                var actual = Convert.ToHexString(SHA256.HashData(e.HostKey));
                e.CanTrust = string.Equals(actual, expectedFingerprint, StringComparison.OrdinalIgnoreCase);
            };
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

    private static bool TryNormalizeSftpFingerprint(string? fingerprint, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        normalized = new string(fingerprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 64;
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
            ? $"oddsnap/{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}"
            : $"{s.S3PathPrefix.TrimEnd('/')}/oddsnap/{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";

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
