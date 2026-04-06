using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Yoink.Services;

public sealed class LocalClipRuntimeService : IDisposable
{
    private const int TargetImageSize = 224;
    private static readonly TimeSpan RuntimeProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly object SetupStateGate = new();
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "clip");
    private static readonly string VocabPath = Path.Combine(CacheDir, "vocab.json");
    private static readonly string MergesPath = Path.Combine(CacheDir, "merges.txt");
    private static readonly string TextModelPath = Path.Combine(CacheDir, "text_model_quantized.onnx");
    private static readonly string VisionModelPath = Path.Combine(CacheDir, "vision_model_quantized.onnx");
    private static readonly string RuntimeVersionPath = Path.Combine(CacheDir, "runtime.version");
    private static readonly string BundledRuntimeDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Clip");
    private static readonly string RuntimeVersion = "xenova-clip-vit-base-patch32-quantized-v1";
    private static readonly IReadOnlyList<(string Url, string TargetPath)> RuntimeAssets =
    [
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/vocab.json?download=1", VocabPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/merges.txt?download=1", MergesPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/text_model_quantized.onnx?download=1", TextModelPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model_quantized.onnx?download=1", VisionModelPath)
    ];
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    private static bool? _cachedRuntimeReady;
    private static string _cachedRuntimeStatus = "Unknown";
    private static DateTime _cachedRuntimeCheckedUtc;

    public static string CacheDirectory => CacheDir;
    public static string SetupHelpText => "Semantic search is prepared automatically during install and app startup.";
    public static string IdleStatusText => "Preparing local semantic search";

    private readonly object _gate = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private InferenceSession? _textSession;
    private InferenceSession? _visionSession;
    private ClipOnnxTokenizer? _tokenizer;
    private bool _isAvailable;
    private string _statusText = IdleStatusText;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public bool IsAvailable { get { lock (_gate) return _isAvailable; } }
    public string StatusText { get { lock (_gate) return _statusText; } }
    public string ModelKey => RuntimeVersion;

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            return;

        AppDiagnostics.LogInfo("semantic.install", "Preparing local semantic runtime.");
        Directory.CreateDirectory(CacheDir);
        if (TryCopyBundledRuntimeAssets(progress))
        {
            await File.WriteAllTextAsync(RuntimeVersionPath, RuntimeVersion, cancellationToken).ConfigureAwait(false);
            UpdateRuntimeProbeCache(true, "Installed");
            return;
        }

        foreach (var (url, targetPath) in RuntimeAssets)
        {
            progress?.Report($"Downloading {Path.GetFileName(targetPath)}...");
            await DownloadFileAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(RuntimeVersionPath, RuntimeVersion, cancellationToken).ConfigureAwait(false);
        UpdateRuntimeProbeCache(true, "Installed");
    }

    public static Task<bool> IsRuntimeReadyAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedRuntimeProbe(out var cachedReady, out _))
            return Task.FromResult(cachedReady);

        var ready = HasRuntimeFiles();
        UpdateRuntimeProbeCache(ready, ready ? "Installed" : IdleStatusText);
        return Task.FromResult(ready);
    }

    public static Task<string> GetRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedRuntimeProbe(out _, out var cachedStatus))
            return Task.FromResult(cachedStatus);

        return Task.FromResult(IdleStatusText);
    }

    public static bool TryGetCachedStatus(out bool isReady, out string status)
        => TryGetCachedRuntimeProbe(out isReady, out status);

    public async Task<ClipEmbeddingResult> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ClipEmbeddingResult(null, "Text was empty.");

        if (!await EnsureSessionsAsync(cancellationToken).ConfigureAwait(false))
            return new ClipEmbeddingResult(null, StatusText);

        try
        {
            var tokenizer = _tokenizer!;
            var (inputIds, attentionMask) = tokenizer.Encode(text);
            var inputNames = _textSession!.InputMetadata.Keys.ToList();
            var inputs = new List<NamedOnnxValue>(2)
            {
                NamedOnnxValue.CreateFromTensor(inputNames[0], new DenseTensor<long>(inputIds, [1, inputIds.Length]))
            };
            if (inputNames.Count > 1)
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputNames[1], new DenseTensor<long>(attentionMask, [1, attentionMask.Length])));

            using var results = _textSession.Run(inputs);
            var vector = ExtractEmbedding(results);
            return vector is null
                ? new ClipEmbeddingResult(null, "Text embedding failed.")
                : new ClipEmbeddingResult(vector, null);
        }
        catch (Exception ex)
        {
            MarkUnavailable($"Text embedding failed: {ex.Message}");
            return new ClipEmbeddingResult(null, StatusText);
        }
    }

    public async Task<ClipEmbeddingResult> EmbedImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return new ClipEmbeddingResult(null, "Image path was empty.");

        if (!await EnsureSessionsAsync(cancellationToken).ConfigureAwait(false))
            return new ClipEmbeddingResult(null, StatusText);

        try
        {
            var pixels = PrepareImageTensor(imagePath);
            var inputName = _visionSession!.InputMetadata.Keys.First();
            using var results = _visionSession.Run([
                NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<float>(pixels, [1, 3, TargetImageSize, TargetImageSize]))
            ]);
            var vector = ExtractEmbedding(results);
            return vector is null
                ? new ClipEmbeddingResult(null, "Image embedding failed.")
                : new ClipEmbeddingResult(vector, null);
        }
        catch (Exception ex)
        {
            MarkUnavailable($"Image embedding failed: {ex.Message}");
            return new ClipEmbeddingResult(null, StatusText);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            _textSession?.Dispose();
            _textSession = null;
            _visionSession?.Dispose();
            _visionSession = null;
            _tokenizer = null;
            _isAvailable = false;
        }
    }

    private async Task<bool> EnsureSessionsAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return false;

        lock (_gate)
        {
            if (_textSession is not null && _visionSession is not null && _tokenizer is not null)
                return true;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return false;

            lock (_gate)
            {
                if (_textSession is not null && _visionSession is not null && _tokenizer is not null)
                    return true;
            }

            if (!await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    SetStatus("Downloading local semantic search...");
                    await EnsureInstalledAsync(new Progress<string>(SetStatus), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MarkUnavailable(ex.Message);
                    return false;
                }
            }

            SetStatus("Loading local semantic search...");
            var options = new SessionOptions
            {
                EnableCpuMemArena = true,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var tokenizer = ClipOnnxTokenizer.Load(VocabPath, MergesPath);
            var textSession = new InferenceSession(TextModelPath, options);
            var visionSession = new InferenceSession(VisionModelPath, options);

            lock (_gate)
            {
                _tokenizer = tokenizer;
                _textSession = textSession;
                _visionSession = visionSession;
                _isAvailable = true;
                _statusText = "Ready";
            }

            StatusChanged?.Invoke(StatusText);
            return true;
        }
        finally
        {
            try { _startGate.Release(); } catch { }
        }
    }

    private void MarkUnavailable(string status)
    {
        lock (_gate)
        {
            _isAvailable = false;
            _statusText = NormalizeRuntimeStatus(status);
            _textSession?.Dispose();
            _textSession = null;
            _visionSession?.Dispose();
            _visionSession = null;
            _tokenizer = null;
        }

        AppDiagnostics.LogWarning("semantic.runtime", _statusText);
        UpdateRuntimeProbeCache(false, _statusText);
        StatusChanged?.Invoke(StatusText);
    }

    private void SetStatus(string status)
    {
        lock (_gate)
            _statusText = NormalizeRuntimeStatus(status);

        StatusChanged?.Invoke(StatusText);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Yoink/semantic-runtime");
        return client;
    }

    private static bool TryCopyBundledRuntimeAssets(IProgress<string>? progress)
    {
        if (!Directory.Exists(BundledRuntimeDir))
            return false;

        var bundledVersionPath = Path.Combine(BundledRuntimeDir, "runtime.version");
        if (!File.Exists(bundledVersionPath))
            return false;

        var bundledVersion = SafeReadAllText(bundledVersionPath);
        if (!string.Equals(bundledVersion, RuntimeVersion, StringComparison.Ordinal))
            return false;

        foreach (var targetPath in new[] { VocabPath, MergesPath, TextModelPath, VisionModelPath })
        {
            var fileName = Path.GetFileName(targetPath);
            var bundledPath = Path.Combine(BundledRuntimeDir, fileName);
            if (!File.Exists(bundledPath))
                return false;

            progress?.Report($"Preparing {fileName}...");
            File.Copy(bundledPath, targetPath, overwrite: true);
        }

        return true;
    }

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        output.Close();

        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static float[] PrepareImageTensor(string imagePath)
    {
        using var source = new Bitmap(imagePath);
        using var prepared = new Bitmap(TargetImageSize, TargetImageSize);
        using (var g = Graphics.FromImage(prepared))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.Clear(System.Drawing.Color.Black);

            var crop = CenterCrop(source.Width, source.Height);
            g.DrawImage(source, new Rectangle(0, 0, TargetImageSize, TargetImageSize), crop, GraphicsUnit.Pixel);
        }

        var tensor = new float[3 * TargetImageSize * TargetImageSize];
        for (int y = 0; y < TargetImageSize; y++)
        {
            for (int x = 0; x < TargetImageSize; x++)
            {
                var pixel = prepared.GetPixel(x, y);
                var index = y * TargetImageSize + x;
                tensor[index] = ((pixel.R / 255f) - Mean[0]) / Std[0];
                tensor[TargetImageSize * TargetImageSize + index] = ((pixel.G / 255f) - Mean[1]) / Std[1];
                tensor[2 * TargetImageSize * TargetImageSize + index] = ((pixel.B / 255f) - Mean[2]) / Std[2];
            }
        }

        return tensor;
    }

    private static Rectangle CenterCrop(int width, int height)
    {
        var size = Math.Min(width, height);
        var x = (width - size) / 2;
        var y = (height - size) / 2;
        return new Rectangle(x, y, size, size);
    }

    private static float[]? ExtractEmbedding(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        foreach (var result in results)
        {
            if (result.Value is not Tensor<float> tensor)
                continue;

            var values = tensor.ToArray();
            if (values.Length == 0)
                continue;

            NormalizeInPlace(values);
            return values;
        }

        return null;
    }

    private static void NormalizeInPlace(float[] values)
    {
        double sum = 0;
        foreach (var value in values)
            sum += value * value;

        var norm = Math.Sqrt(sum);
        if (norm <= 0)
            return;

        for (int i = 0; i < values.Length; i++)
            values[i] = (float)(values[i] / norm);
    }

    private static bool TryGetCachedRuntimeProbe(out bool isReady, out string status)
    {
        lock (SetupStateGate)
        {
            if (_cachedRuntimeReady.HasValue && DateTime.UtcNow - _cachedRuntimeCheckedUtc <= RuntimeProbeCacheTtl)
            {
                isReady = _cachedRuntimeReady.Value;
                status = _cachedRuntimeStatus;
                return true;
            }
        }

        if (HasRuntimeFiles())
        {
            UpdateRuntimeProbeCache(true, "Installed");
            isReady = true;
            status = "Installed";
            return true;
        }

        isReady = false;
        status = "";
        return false;
    }

    private static bool HasRuntimeFiles()
    {
        try
        {
            return File.Exists(VocabPath) &&
                   File.Exists(MergesPath) &&
                   File.Exists(TextModelPath) &&
                   File.Exists(VisionModelPath) &&
                   File.Exists(RuntimeVersionPath) &&
                   string.Equals(File.ReadAllText(RuntimeVersionPath).Trim(), RuntimeVersion, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("semantic.runtime-check", ex.Message, ex);
            return false;
        }
    }

    private static void UpdateRuntimeProbeCache(bool isReady, string status)
    {
        lock (SetupStateGate)
        {
            _cachedRuntimeReady = isReady;
            _cachedRuntimeStatus = NormalizeRuntimeStatus(status);
            _cachedRuntimeCheckedUtc = DateTime.UtcNow;
        }
    }

    private static string NormalizeRuntimeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "Not installed";

        var text = status.Trim().Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ');
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length <= 140 ? text : text[..137] + "...";
    }
}

public sealed record ClipEmbeddingResult(float[]? Embedding, string? Error)
{
    public bool IsSuccess => Embedding is { Length: > 0 } && string.IsNullOrWhiteSpace(Error);
}
