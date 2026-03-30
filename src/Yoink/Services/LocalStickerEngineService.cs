using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Yoink.Services;

public sealed record LocalStickerEngineDownloadProgress(long BytesReceived, long? TotalBytes, string StatusMessage)
{
    public double Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : 0d;
}

public sealed record LocalStickerModelInstallResult(bool Success, string Message, string? ModelPath = null, string? ReferenceUrl = null);

public static class LocalStickerEngineService
{
    private sealed record ModelDef(string Id, string Label, string Description, string Url, string ReferenceUrl, int Resolution, string FileName, string InputName = "input");

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Dictionary<string, InferenceSession> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock SessionLock = new();

    private static readonly IReadOnlyDictionary<LocalStickerEngine, ModelDef> Models = new Dictionary<LocalStickerEngine, ModelDef>
    {
        [LocalStickerEngine.BriaRmbg] = new(
            "bria_rmbg_quantized",
            "BRIA RMBG",
            "Higher-quality local model. Recommended for most screenshots.",
            "https://huggingface.co/briaai/RMBG-1.4/resolve/28f8f4114c1385f1478e1102922dce7038164c43/onnx/model_quantized.onnx?download=true",
            "https://huggingface.co/briaai/RMBG-1.4",
            1024,
            "bria-rmbg-quantized.onnx"),
        [LocalStickerEngine.U2Netp] = new(
            "u2netp",
            "U2Netp",
            "Smaller lightweight local model. Faster, but a little rougher around edges.",
            "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
            "https://github.com/xuebinqin/U-2-Net",
            320,
            "u2netp.onnx")
    };

    public static string GetEngineLabel(LocalStickerEngine engine) => Models[engine].Label;
    public static string GetEngineDescription(LocalStickerEngine engine) => Models[engine].Description;
    public static string GetProjectUrl(LocalStickerEngine engine) => Models[engine].ReferenceUrl;

    public static bool IsModelDownloaded(LocalStickerEngine engine) => File.Exists(GetModelPath(engine));

    public static string GetModelPath(LocalStickerEngine engine)
    {
        Directory.CreateDirectory(GetModelDirectory());
        return Path.Combine(GetModelDirectory(), Models[engine].FileName);
    }

    public static bool RemoveDownloadedModel(LocalStickerEngine engine)
    {
        var modelPath = GetModelPath(engine);
        try
        {
            lock (SessionLock)
            {
                if (Sessions.Remove(modelPath, out var session))
                    session.Dispose();
            }

            if (File.Exists(modelPath))
                File.Delete(modelPath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<LocalStickerModelInstallResult> DownloadModelAsync(LocalStickerEngine engine, IProgress<LocalStickerEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var model = Models[engine];
        var modelPath = GetModelPath(engine);
        var tempPath = modelPath + ".download";

        try
        {
            progress?.Report(new LocalStickerEngineDownloadProgress(0, null, $"Downloading {model.Label} model..."));

            using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var output = File.Create(tempPath))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                await CopyWithProgressAsync(input, output, response.Content.Headers.ContentLength, progress, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(modelPath))
                File.Delete(modelPath);
            File.Move(tempPath, modelPath);

            progress?.Report(new LocalStickerEngineDownloadProgress(new FileInfo(modelPath).Length, new FileInfo(modelPath).Length, "Download complete."));
            return new LocalStickerModelInstallResult(true, $"Downloaded {model.Label}.", modelPath, model.ReferenceUrl);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return new LocalStickerModelInstallResult(false, ex is HttpRequestException
                ? $"Couldn't download {model.Label}. Check your connection or open the model page and verify the source."
                : ex.Message, null, model.ReferenceUrl);
        }
    }

    public static Bitmap Process(Bitmap input, LocalStickerEngine engine)
    {
        var modelPath = GetModelPath(engine);
        if (!File.Exists(modelPath))
            throw new InvalidOperationException($"{GetEngineLabel(engine)} is not downloaded yet.");

        var model = Models[engine];
        using var resized = ResizeBitmap(input, model.Resolution, model.Resolution);
        var session = GetOrCreateSession(modelPath);
        var inputName = session.InputMetadata.Keys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(inputName))
            throw new InvalidOperationException($"{GetEngineLabel(engine)} model is missing an input definition.");

        var tensor = CreateInputTensor(resized);

        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
        var firstResult = results.FirstOrDefault();
        if (firstResult is null)
            throw new InvalidOperationException($"{GetEngineLabel(engine)} returned no output.");

        var output = firstResult.AsTensor<float>();
        if (output is null)
            throw new InvalidOperationException($"{GetEngineLabel(engine)} returned an invalid output tensor.");

        return ComposeTransparentBitmap(input, output, model.Resolution, model.Resolution);
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

    private static string GetModelDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "sticker-models");

    private static InferenceSession GetOrCreateSession(string modelPath)
    {
        lock (SessionLock)
        {
            if (Sessions.TryGetValue(modelPath, out var existing))
                return existing;

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            var session = new InferenceSession(modelPath, options);
            Sessions[modelPath] = session;
            return session;
        }
    }

    private static DenseTensor<float> CreateInputTensor(Bitmap bitmap)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, bitmap.Height, bitmap.Width });
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                tensor[0, 0, y, x] = (pixel.R - 127.5f) / 127.5f;
                tensor[0, 1, y, x] = (pixel.G - 127.5f) / 127.5f;
                tensor[0, 2, y, x] = (pixel.B - 127.5f) / 127.5f;
            }
        }
        return tensor;
    }

    private static Bitmap ComposeTransparentBitmap(Bitmap original, Tensor<float> maskTensor, int maskWidth, int maskHeight)
    {
        using var alphaMask = new Bitmap(maskWidth, maskHeight);
        for (int y = 0; y < maskHeight; y++)
        {
            for (int x = 0; x < maskWidth; x++)
            {
                float raw = maskTensor.Rank switch
                {
                    4 => maskTensor[0, 0, y, x],
                    3 => maskTensor[0, y, x],
                    _ => maskTensor[y, x]
                };
                int alpha = (int)Math.Clamp(raw * 255f, 0f, 255f);
                alphaMask.SetPixel(x, y, Color.FromArgb(alpha, alpha, alpha, alpha));
            }
        }

        using var resizedMask = ResizeBitmap(alphaMask, original.Width, original.Height);
        var output = new Bitmap(original.Width, original.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                var src = original.GetPixel(x, y);
                int alpha = resizedMask.GetPixel(x, y).A;
                output.SetPixel(x, y, Color.FromArgb(alpha, src.R, src.G, src.B));
            }
        }
        return output;
    }

    private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
    {
        var output = new Bitmap(width, height);
        using var g = Graphics.FromImage(output);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, 0, 0, width, height);
        return output;
    }

    private static Bitmap CreateAlphaTintBitmap(Bitmap source, Color tint)
    {
        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var src = source.GetPixel(x, y);
                if (src.A == 0) continue;
                output.SetPixel(x, y, Color.FromArgb(src.A, tint.R, tint.G, tint.B));
            }
        }
        return output;
    }

    private static void DrawMask(Graphics g, Bitmap mask, int x, int y, float opacity)
    {
        using var attrs = new ImageAttributes();
        var matrix = new ColorMatrix
        {
            Matrix33 = opacity
        };
        attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(mask, new Rectangle(x, y, mask.Width, mask.Height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attrs);
    }

    private static IEnumerable<(int dx, int dy)> GetStrokeOffsets(int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (dx * dx + dy * dy <= radius * radius)
                    yield return (dx, dy);
            }
        }
    }

    private static async Task CopyWithProgressAsync(Stream input, Stream output, long? totalBytes, IProgress<LocalStickerEngineDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 128];
        long received = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new LocalStickerEngineDownloadProgress(received, totalBytes, totalBytes is > 0
                ? $"Downloading... {received / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB"
                : $"Downloading... {received / 1024 / 1024} MB"));
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Yoink/{UpdateService.GetCurrentVersion()}");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
