using System.Drawing;

namespace Yoink.Services;

public sealed record LocalUpscaleEngineDownloadProgress(long BytesReceived, long? TotalBytes, string StatusMessage)
{
    public double Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : 0d;
}

public sealed record LocalUpscaleModelInstallResult(bool Success, string Message, string? ModelPath = null, string? ReferenceUrl = null);

public static class LocalUpscaleEngineService
{
    private sealed record ModelDef(string Label, string Description, string ProjectUrl, string DownloadUrl, string FileName, int ScaleFactor);

    private static readonly IReadOnlyDictionary<LocalUpscaleEngine, ModelDef> Models = new Dictionary<LocalUpscaleEngine, ModelDef>
    {
        [LocalUpscaleEngine.SwinIrRealWorld] = new(
            "SwinIR x4 (CPU default)",
            "Cleaner, more faithful x4 upscale. Good default for screenshots and UI on CPU.",
            "https://github.com/JingyunLiang/SwinIR",
            "https://huggingface.co/rocca/swin-ir-onnx/resolve/main/003_realSR_BSRGAN_DFO_s64w8_SwinIR-M_x4_GAN.onnx?download=1",
            "swinir-realworld-x4.onnx",
            4),
        [LocalUpscaleEngine.RealEsrganX4Plus] = new(
            "Real-ESRGAN x4plus (GPU default)",
            "Higher-detail x4 upscale for natural images. Best quality option for stronger machines.",
            "https://github.com/xinntao/Real-ESRGAN",
            "https://huggingface.co/bukuroo/RealESRGAN-ONNX/resolve/main/real-esrgan-x4plus-128.onnx?download=1",
            "real-esrgan-x4plus.onnx",
            4)
    };

    public static string GetEngineLabel(LocalUpscaleEngine engine) => Models[engine].Label;
    public static string GetEngineDescription(LocalUpscaleEngine engine) => Models[engine].Description;
    public static string GetProjectUrl(LocalUpscaleEngine engine) => Models[engine].ProjectUrl;
    public static int GetScaleFactor(LocalUpscaleEngine engine) => Models[engine].ScaleFactor;
    public static int GetMinScaleFactor(LocalUpscaleEngine engine) => 2;

    public static string GetQualityHint(LocalUpscaleEngine engine) => engine switch
    {
        LocalUpscaleEngine.SwinIrRealWorld => "Balanced / faithful UI upscale",
        LocalUpscaleEngine.RealEsrganX4Plus => "Best quality / heavier",
        _ => "Unknown"
    };

    public static bool IsModelDownloaded(LocalUpscaleEngine engine) => UpscaleRuntimeService.IsModelCached(engine);
    public static string GetModelPath(LocalUpscaleEngine engine) => UpscaleRuntimeService.GetModelPath(engine);
    public static bool RemoveDownloadedModel(LocalUpscaleEngine engine) => UpscaleRuntimeService.RemoveCachedModel(engine);

    public static async Task<LocalUpscaleModelInstallResult> DownloadModelAsync(LocalUpscaleEngine engine, IProgress<LocalUpscaleEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var def = Models[engine];
            progress?.Report(new LocalUpscaleEngineDownloadProgress(0, null, $"Preparing {def.Label}..."));
            await UpscaleRuntimeService.EnsureInstalledAsync(UpscaleExecutionProvider.Cpu, null, cancellationToken).ConfigureAwait(false);
            await UpscaleRuntimeService.EnsureModelDownloadedAsync(engine, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new LocalUpscaleEngineDownloadProgress(100, 100, "Model is ready."));
            return new LocalUpscaleModelInstallResult(true, $"Prepared {def.Label}.", GetModelPath(engine), def.ProjectUrl);
        }
        catch (Exception ex)
        {
            return new LocalUpscaleModelInstallResult(false, ex.Message, null, GetProjectUrl(engine));
        }
    }

    public static Bitmap Process(Bitmap input, LocalUpscaleEngine engine, UpscaleExecutionProvider provider, int scaleFactor)
    {
        var maxScale = GetScaleFactor(engine);
        var requestedScale = scaleFactor <= 0 ? maxScale : Math.Clamp(scaleFactor, GetMinScaleFactor(engine), maxScale);
        using var native = UpscaleRuntimeService.UpscaleAsync(input, engine, provider, maxScale).GetAwaiter().GetResult();
        if (requestedScale == maxScale)
            return new Bitmap(native);

        return ResizeBitmap(native, input.Width * requestedScale, input.Height * requestedScale);
    }

    private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return resized;
    }
}
