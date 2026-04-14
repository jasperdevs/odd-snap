using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Yoink.Services;

public sealed class LocalClipRuntimeService : IDisposable
{
    private const int TargetImageSize = 224;
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    public static string CacheDirectory => LocalClipRuntimeAssets.CacheDirectory;
    public static string SetupHelpText => "Semantic search is prepared automatically the first time image search needs it.";
    public static string IdleStatusText => LocalClipRuntimeAssets.IdleStatusText;

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
    public string ModelKey => LocalClipRuntimeAssets.RuntimeVersion;

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => await LocalClipRuntimeAssets.EnsureInstalledAsync(progress, cancellationToken).ConfigureAwait(false);

    public static Task<bool> IsRuntimeReadyAsync(CancellationToken cancellationToken = default)
        => LocalClipRuntimeAssets.IsRuntimeReadyAsync(cancellationToken);

    public static Task<string> GetRuntimeStatusAsync(CancellationToken cancellationToken = default)
        => LocalClipRuntimeAssets.GetRuntimeStatusAsync(cancellationToken);

    public static bool TryGetCachedStatus(out bool isReady, out string status)
        => LocalClipRuntimeAssets.TryGetCachedStatus(out isReady, out status);

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

    public async Task<ClipEmbeddingResult> EmbedImageAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        if (bitmap is null)
            return new ClipEmbeddingResult(null, "Image was null.");

        if (!await EnsureSessionsAsync(cancellationToken).ConfigureAwait(false))
            return new ClipEmbeddingResult(null, StatusText);

        try
        {
            var pixels = PrepareImageTensor(bitmap);
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
        if (_disposed)
            return;

        _disposed = true;
        ReleaseSessions(keepAvailability: false);
        _startGate.Dispose();
    }

    public void ReleaseSessions()
    {
        if (_disposed)
            return;

        ReleaseSessions(keepAvailability: true);
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

            var tokenizer = ClipOnnxTokenizer.Load(LocalClipRuntimeAssets.VocabPath, LocalClipRuntimeAssets.MergesPath);
            var textSession = new InferenceSession(LocalClipRuntimeAssets.TextModelPath, options);
            var visionSession = new InferenceSession(LocalClipRuntimeAssets.VisionModelPath, options);

            lock (_gate)
            {
                if (_disposed)
                {
                    textSession.Dispose();
                    visionSession.Dispose();
                    return false;
                }

                _tokenizer = tokenizer;
                _textSession = textSession;
                _visionSession = visionSession;
                _isAvailable = true;
                _statusText = "Ready";
            }

            NotifyStatusChanged(StatusText);
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
            _statusText = LocalClipRuntimeAssets.NormalizeStatus(status);
            _textSession?.Dispose();
            _textSession = null;
            _visionSession?.Dispose();
            _visionSession = null;
            _tokenizer = null;
        }

        AppDiagnostics.LogWarning("semantic.runtime", _statusText);
        LocalClipRuntimeAssets.MarkUnavailable(_statusText);
        NotifyStatusChanged(StatusText);
    }

    private void ReleaseSessions(bool keepAvailability)
    {
        lock (_gate)
        {
            _textSession?.Dispose();
            _textSession = null;
            _visionSession?.Dispose();
            _visionSession = null;
            _tokenizer = null;
            if (!keepAvailability)
                _isAvailable = false;
        }
    }

    private void SetStatus(string status)
    {
        lock (_gate)
            _statusText = LocalClipRuntimeAssets.NormalizeStatus(status);

        NotifyStatusChanged(StatusText);
    }

    private void NotifyStatusChanged(string status)
    {
        var handlers = StatusChanged;
        if (handlers is null)
            return;

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(status);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("semantic.status", ex);
            }
        }
    }

    private static float[] PrepareImageTensor(string imagePath)
    {
        using var source = new Bitmap(imagePath);
        return PrepareImageTensor(source);
    }

    private static float[] PrepareImageTensor(Bitmap source)
    {
        using var prepared = new Bitmap(TargetImageSize, TargetImageSize, PixelFormat.Format32bppArgb);
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
        var data = prepared.LockBits(new Rectangle(0, 0, TargetImageSize, TargetImageSize), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;
                for (int y = 0; y < TargetImageSize; y++)
                {
                    var row = scan0 + (y * data.Stride);
                    for (int x = 0; x < TargetImageSize; x++)
                    {
                        var pixel = row + (x * 4);
                        var b = pixel[0] / 255f;
                        var g = pixel[1] / 255f;
                        var r = pixel[2] / 255f;
                        var index = y * TargetImageSize + x;
                        tensor[index] = (r - Mean[0]) / Std[0];
                        tensor[TargetImageSize * TargetImageSize + index] = (g - Mean[1]) / Std[1];
                        tensor[2 * TargetImageSize * TargetImageSize + index] = (b - Mean[2]) / Std[2];
                    }
                }
            }
        }
        finally
        {
            prepared.UnlockBits(data);
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

}

public sealed record ClipEmbeddingResult(float[]? Embedding, string? Error)
{
    public bool IsSuccess => Embedding is { Length: > 0 } && string.IsNullOrWhiteSpace(Error);
}
