using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Yoink.Services;

public enum UpscaleProvider
{
    None,
    Local,
    DeepAiSuperResolution,
    DeepAiWaifu2x
}

public enum LocalUpscaleEngine
{
    SwinIrRealWorld,
    RealEsrganX4Plus
}

public enum UpscaleExecutionProvider
{
    Cpu,
    Gpu
}

public sealed class UpscaleSettings
{
    public UpscaleProvider Provider { get; set; } = UpscaleProvider.Local;
    public string DeepAiApiKey { get; set; } = "";
    public LocalUpscaleEngine LocalEngine { get; set; } = LocalUpscaleEngine.SwinIrRealWorld;
    public LocalUpscaleEngine LocalCpuEngine { get; set; } = LocalUpscaleEngine.SwinIrRealWorld;
    public LocalUpscaleEngine LocalGpuEngine { get; set; } = LocalUpscaleEngine.RealEsrganX4Plus;
    public UpscaleExecutionProvider LocalExecutionProvider { get; set; } = UpscaleExecutionProvider.Cpu;
    public int ScaleFactor { get; set; } = 4;
    public bool ShowPreviewWindow { get; set; } = true;

    public LocalUpscaleEngine GetActiveLocalEngine() => LocalExecutionProvider == UpscaleExecutionProvider.Gpu
        ? LocalGpuEngine
        : LocalCpuEngine;
}

public sealed class UpscaleResult
{
    public bool Success { get; init; }
    public Bitmap? Image { get; init; }
    public string Error { get; init; } = "";
    public string ProviderName { get; init; } = "";
}

public static class UpscaleService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(180),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
    };

    public static string GetName(UpscaleProvider provider) => provider switch
    {
        UpscaleProvider.Local => "Local",
        UpscaleProvider.DeepAiSuperResolution => "DeepAI Super Resolution",
        UpscaleProvider.DeepAiWaifu2x => "DeepAI Waifu2x",
        _ => ""
    };

    public static async Task<UpscaleResult> ProcessAsync(Bitmap input, UpscaleSettings settings)
    {
        return settings.Provider switch
        {
            UpscaleProvider.Local => await ProcessLocalAsync(input, settings),
            UpscaleProvider.DeepAiSuperResolution => await ProcessDeepAiAsync(input, settings, "https://api.deepai.org/api/torch-srgan", "DeepAI Super Resolution"),
            UpscaleProvider.DeepAiWaifu2x => await ProcessDeepAiAsync(input, settings, "https://api.deepai.org/api/waifu2x", "DeepAI Waifu2x"),
            _ => new UpscaleResult { Error = "No upscale provider configured" }
        };
    }

    private static async Task<UpscaleResult> ProcessDeepAiAsync(Bitmap input, UpscaleSettings settings, string endpoint, string providerName)
    {
        if (string.IsNullOrWhiteSpace(settings.DeepAiApiKey))
            return new UpscaleResult { Error = $"{providerName} API key not configured", ProviderName = providerName };

        var temp = CaptureOutputService.SaveBitmapToTempPng(input, "yoink_upscale");
        try
        {
            using var form = new MultipartFormDataContent();
            var imageContent = new StreamContent(new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan));
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(imageContent, "image", Path.GetFileName(temp));

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = form
            };
            request.Headers.TryAddWithoutValidation("api-key", settings.DeepAiApiKey);

            using var response = await Http.SendAsync(request);
            var payload = await response.Content.ReadAsByteArrayAsync();
            if (!response.IsSuccessStatusCode)
            {
                var body = System.Text.Encoding.UTF8.GetString(payload);
                return new UpscaleResult
                {
                    Error = string.IsNullOrWhiteSpace(body) ? $"{providerName} error: {response.StatusCode}" : body[..Math.Min(body.Length, 180)],
                    ProviderName = providerName
                };
            }

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("output_url", out var outputUrlElement))
            {
                return new UpscaleResult { Error = $"{providerName} did not return an output image URL", ProviderName = providerName };
            }

            var outputUrl = outputUrlElement.GetString();
            if (string.IsNullOrWhiteSpace(outputUrl))
                return new UpscaleResult { Error = $"{providerName} returned an empty output URL", ProviderName = providerName };

            using var outputResponse = await Http.GetAsync(outputUrl);
            var imageBytes = await outputResponse.Content.ReadAsByteArrayAsync();
            if (!outputResponse.IsSuccessStatusCode || imageBytes.Length == 0)
                return new UpscaleResult { Error = $"{providerName} failed to fetch the output image", ProviderName = providerName };

            using var ms = new MemoryStream(imageBytes);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return new UpscaleResult
            {
                Success = true,
                Image = new Bitmap(img),
                ProviderName = providerName
            };
        }
        catch (Exception ex)
        {
            return new UpscaleResult { Error = ex.Message, ProviderName = providerName };
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static async Task<UpscaleResult> ProcessLocalAsync(Bitmap input, UpscaleSettings settings)
    {
        var selectedEngine = settings.GetActiveLocalEngine();
        if (settings.LocalExecutionProvider == UpscaleExecutionProvider.Gpu)
        {
            var gpuAttempt = await TryProcessLocalAsync(input, selectedEngine, UpscaleExecutionProvider.Gpu, settings.ScaleFactor);
            if (gpuAttempt.Success)
                return gpuAttempt;

            var cpuFallbackEngine = settings.LocalCpuEngine;
            var cpuFallback = await TryProcessLocalAsync(input, cpuFallbackEngine, UpscaleExecutionProvider.Cpu, settings.ScaleFactor);
            if (cpuFallback.Success)
            {
                return new UpscaleResult
                {
                    Success = true,
                    Image = cpuFallback.Image,
                    ProviderName = $"{LocalUpscaleEngineService.GetEngineLabel(cpuFallbackEngine)} (CPU fallback)"
                };
            }

            return new UpscaleResult
            {
                Error = $"{gpuAttempt.Error} CPU fallback failed: {cpuFallback.Error}",
                ProviderName = LocalUpscaleEngineService.GetEngineLabel(selectedEngine)
            };
        }

        return await TryProcessLocalAsync(input, selectedEngine, UpscaleExecutionProvider.Cpu, settings.ScaleFactor);
    }

    private static async Task<UpscaleResult> TryProcessLocalAsync(Bitmap input, LocalUpscaleEngine engine, UpscaleExecutionProvider executionProvider, int scaleFactor)
    {
        try
        {
            using var processed = await Task.Run(() => LocalUpscaleEngineService.Process(input, engine, executionProvider, scaleFactor));
            return new UpscaleResult
            {
                Success = true,
                Image = new Bitmap(processed),
                ProviderName = LocalUpscaleEngineService.GetEngineLabel(engine)
            };
        }
        catch (Exception ex)
        {
            return new UpscaleResult
            {
                Error = ex.Message,
                ProviderName = LocalUpscaleEngineService.GetEngineLabel(engine)
            };
        }
    }

}
