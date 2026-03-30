using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Yoink.Services;

public enum StickerProvider
{
    None,
    RemoveBg,
    Photoroom,
    LocalCpu
}

public enum LocalStickerEngine
{
    BriaRmbg,
    U2Netp
}

public sealed class StickerSettings
{
    public StickerProvider Provider { get; set; } = StickerProvider.None;
    public string RemoveBgApiKey { get; set; } = "";
    public string PhotoroomApiKey { get; set; } = "";
    public LocalStickerEngine LocalEngine { get; set; } = LocalStickerEngine.U2Netp;
    public bool AddShadow { get; set; }
    public bool AddStroke { get; set; }
}

public sealed class StickerResult
{
    public bool Success { get; init; }
    public Bitmap? Image { get; init; }
    public string Error { get; init; } = "";
    public string ProviderName { get; init; } = "";
}

public static class StickerService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
    };

    public static string GetName(StickerProvider provider) => provider switch
    {
        StickerProvider.RemoveBg => "Remove.bg",
        StickerProvider.Photoroom => "Photoroom",
        StickerProvider.LocalCpu => "Local CPU",
        _ => ""
    };

    public static async Task<StickerResult> ProcessAsync(Bitmap input, StickerSettings settings)
    {
        return settings.Provider switch
        {
            StickerProvider.RemoveBg => await ProcessRemoveBgAsync(input, settings),
            StickerProvider.Photoroom => await ProcessPhotoroomAsync(input, settings),
            StickerProvider.LocalCpu => await ProcessLocalAsync(input, settings),
            _ => new StickerResult { Error = "No sticker provider configured" }
        };
    }

    private static async Task<StickerResult> ProcessRemoveBgAsync(Bitmap input, StickerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RemoveBgApiKey))
            return new StickerResult { Error = "remove.bg API key not configured" };

        var temp = SaveTempPng(input);
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("auto"), "size");
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(temp)), "image_file", Path.GetFileName(temp));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.remove.bg/v1.0/removebg")
            {
                Content = form
            };
            request.Headers.TryAddWithoutValidation("X-Api-Key", settings.RemoveBgApiKey);

            return await SendImageRequestAsync(request, "Remove.bg");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static async Task<StickerResult> ProcessPhotoroomAsync(Bitmap input, StickerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PhotoroomApiKey))
            return new StickerResult { Error = "Photoroom API key not configured" };

        var temp = SaveTempPng(input);
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(temp)), "image_file", Path.GetFileName(temp));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://sdk.photoroom.com/v1/segment")
            {
                Content = form
            };
            request.Headers.TryAddWithoutValidation("x-api-key", settings.PhotoroomApiKey);

            return await SendImageRequestAsync(request, "Photoroom");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static async Task<StickerResult> ProcessLocalAsync(Bitmap input, StickerSettings settings)
    {
        try
        {
            using var processed = await Task.Run(() => LocalStickerEngineService.Process(input, settings.LocalEngine));
            using var finished = LocalStickerEngineService.ApplyPresentationEffects(processed, settings.AddStroke, settings.AddShadow);
            return new StickerResult
            {
                Success = true,
                Image = new Bitmap(finished),
                ProviderName = LocalStickerEngineService.GetEngineLabel(settings.LocalEngine)
            };
        }
        catch (Exception ex)
        {
            return new StickerResult { Error = ex.Message, ProviderName = LocalStickerEngineService.GetEngineLabel(settings.LocalEngine) };
        }
    }

    private static async Task<StickerResult> SendImageRequestAsync(HttpRequestMessage request, string providerName)
    {
        try
        {
            using var resp = await Http.SendAsync(request);
            var bytes = await resp.Content.ReadAsByteArrayAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var body = System.Text.Encoding.UTF8.GetString(bytes);
                if ((int)resp.StatusCode == 429)
                    return new StickerResult { Error = $"{providerName} rate limit reached", ProviderName = providerName };

                if (!string.IsNullOrWhiteSpace(body))
                    return new StickerResult { Error = body.Length > 180 ? body[..180] : body, ProviderName = providerName };

                return new StickerResult { Error = $"{providerName} error: {resp.StatusCode}", ProviderName = providerName };
            }

            if (bytes.Length == 0)
                return new StickerResult { Error = $"{providerName} returned an empty image", ProviderName = providerName };

            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return new StickerResult
            {
                Success = true,
                Image = new Bitmap(img),
                ProviderName = providerName
            };
        }
        catch (Exception ex)
        {
            return new StickerResult { Error = ex.Message, ProviderName = providerName };
        }
    }

    private static string SaveTempPng(Bitmap input)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"yoink_sticker_{Guid.NewGuid():N}.png");
        input.Save(temp, ImageFormat.Png);
        return temp;
    }
}
