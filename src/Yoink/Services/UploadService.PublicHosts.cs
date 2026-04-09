using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Yoink.Services;

public static partial class UploadService
{
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

        if (!resp.IsSuccessStatusCode)
            return new UploadResult { Error = BuildHttpError("Catbox", resp, url), IsRateLimit = (int)resp.StatusCode == 429 };

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
        await Task.CompletedTask;
        return new UploadResult
        {
            Error = "The public transfer.sh service is unavailable. Choose Temp Hosts, Catbox, Litterbox, Uguu, or file.io."
        };
    }
}
