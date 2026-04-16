using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Yoink.Services;

public sealed record LocalStickerEngineDownloadProgress(long BytesReceived, long? TotalBytes, string StatusMessage)
{
    public double Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : 0d;
}

public sealed record LocalStickerModelInstallResult(bool Success, string Message, string? ModelPath = null, string? ReferenceUrl = null);

public static class LocalStickerEngineService
{
    private sealed record ModelDef(string Label, string Description, string SourceUrl, string FileName, string ModelId);

    private static readonly IReadOnlyDictionary<LocalStickerEngine, ModelDef> Models = new Dictionary<LocalStickerEngine, ModelDef>
    {
        [LocalStickerEngine.BriaRmbg] = new(
            "BRIA RMBG (Recommended, Best quality)",
            "Recommended default. Best quality overall, with a bit more processing cost.",
            "https://huggingface.co/briaai/RMBG-2.0",
            "bria-rmbg-2.0.onnx",
            "bria-rmbg"),
        [LocalStickerEngine.U2Netp] = new(
            "U2Netp (Fastest, Lowest quality)",
            "Fastest option. Lightest model, but the roughest edges.",
            "https://github.com/xuebinqin/U-2-Net",
            "u2netp.onnx",
            "u2netp"),
        [LocalStickerEngine.U2Net] = new(
            "U2Net (Older, Middle quality)",
            "Older general-use model. Middle-of-the-road quality and speed.",
            "https://github.com/xuebinqin/U-2-Net",
            "u2net.onnx",
            "u2net"),
        [LocalStickerEngine.BiRefNetLite] = new(
            "BiRefNet Lite (High quality)",
            "High-quality model. Heavier than BRIA RMBG, often strong on detailed subjects.",
            "https://github.com/ZhengPeng7/BiRefNet",
            "BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx",
            "birefnet-general-lite"),
        [LocalStickerEngine.IsNetGeneralUse] = new(
            "ISNet General Use (Balanced)",
            "Balanced quality and speed. Solid middle option for mixed content.",
            "https://github.com/xuebinqin/DIS",
            "isnet-general-use.onnx",
            "isnet-general-use")
    };

    public static string GetEngineLabel(LocalStickerEngine engine) => Models[engine].Label;
    public static string GetEngineDescription(LocalStickerEngine engine) => Models[engine].Description;
    public static string GetProjectUrl(LocalStickerEngine engine) => Models[engine].SourceUrl;

    public static string GetQualityHint(LocalStickerEngine engine) => engine switch
    {
        LocalStickerEngine.BriaRmbg => "Recommended / best quality",
        LocalStickerEngine.BiRefNetLite => "High quality",
        LocalStickerEngine.IsNetGeneralUse => "Balanced",
        LocalStickerEngine.U2Net => "Older / middle quality",
        LocalStickerEngine.U2Netp => "Fastest / lowest quality",
        _ => "Unknown"
    };

    public static bool IsModelDownloaded(LocalStickerEngine engine) => RembgRuntimeService.IsModelCached(engine);

    public static string GetModelPath(LocalStickerEngine engine) => RembgRuntimeService.GetModelPath(engine);

    public static bool RemoveDownloadedModel(LocalStickerEngine engine) => RembgRuntimeService.RemoveCachedModel(engine);

    public static async Task<LocalStickerModelInstallResult> DownloadModelAsync(LocalStickerEngine engine, StickerExecutionProvider provider, IProgress<LocalStickerEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report(new LocalStickerEngineDownloadProgress(0, null, $"Preparing {GetEngineLabel(engine)}..."));
            var runtimeProgress = new Progress<string>(message =>
                progress?.Report(new LocalStickerEngineDownloadProgress(0, null, message)));
            await RembgRuntimeService.EnsureModelReadyAsync(engine, provider, runtimeProgress, cancellationToken).ConfigureAwait(false);
            var modelPath = GetModelPath(engine);
            progress?.Report(new LocalStickerEngineDownloadProgress(100, 100, "Model is ready."));
            return new LocalStickerModelInstallResult(true, $"Prepared {GetEngineLabel(engine)}.", modelPath, GetProjectUrl(engine));
        }
        catch (Exception ex)
        {
            return new LocalStickerModelInstallResult(false, ex.Message, null, GetProjectUrl(engine));
        }
    }

    public static Bitmap Process(Bitmap input, LocalStickerEngine engine, StickerExecutionProvider executionProvider)
    {
        return RembgRuntimeService.RemoveBackgroundAsync(input, engine, executionProvider).GetAwaiter().GetResult();
    }

    public static Bitmap ApplyPresentationEffects(Bitmap source, bool addStroke, bool addShadow)
    {
        if (!addStroke && !addShadow)
            return new Bitmap(source);

        int padding = (addShadow ? 18 : 0) + (addStroke ? 4 : 0);
        var canvas = new Bitmap(source.Width + padding * 2, source.Height + padding * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var whiteMask = CreateAlphaTintBitmap(source, Color.White);
        using var blackMask = CreateAlphaTintBitmap(source, Color.Black);

        if (addShadow)
        {
            DrawMask(g, blackMask, padding + 7, padding + 8, 0.12f);
            DrawMask(g, blackMask, padding + 5, padding + 6, 0.09f);
            DrawMask(g, blackMask, padding + 3, padding + 4, 0.06f);
        }

        if (addStroke)
        {
            foreach (var (dx, dy) in GetStrokeOffsets(3))
                DrawMask(g, whiteMask, padding + dx, padding + dy, 0.95f);
        }

        g.DrawImage(source, padding, padding, source.Width, source.Height);
        return canvas;
    }

    private static Bitmap CreateAlphaTintBitmap(Bitmap source, Color tint)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);

        using var attributes = new ImageAttributes();
        var colorMatrix = new ColorMatrix(new[]
        {
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, 0f, 1f }
        });
        attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static void DrawMask(Graphics g, Bitmap mask, int x, int y, float opacity)
    {
        var cm = new ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = opacity
        };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(mask, new Rectangle(x, y, mask.Width, mask.Height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attributes);
    }

    private static IEnumerable<(int dx, int dy)> GetStrokeOffsets(int radius)
    {
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x == 0 && y == 0) continue;
                if (x * x + y * y <= radius * radius)
                    yield return (x, y);
            }
        }
    }
}
