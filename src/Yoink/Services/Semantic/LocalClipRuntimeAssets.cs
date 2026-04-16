using System.IO;
using System.Net.Http;

namespace Yoink.Services;

internal static class LocalClipRuntimeAssets
{
    private static readonly TimeSpan RuntimeProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly object SetupStateGate = new();
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string BundledRuntimeDir = Path.Combine(AppContext.BaseDirectory!, "Assets", "Clip");

    private static bool? _cachedRuntimeReady;
    private static string _cachedRuntimeStatus = "Unknown";
    private static DateTime _cachedRuntimeCheckedUtc;

    public static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)!, "Yoink", "clip");

    public static string VocabPath { get; } = Path.Combine(CacheDirectory, "vocab.json");
    public static string MergesPath { get; } = Path.Combine(CacheDirectory, "merges.txt");
    public static string TextModelPath { get; } = Path.Combine(CacheDirectory, "text_model_quantized.onnx");
    public static string VisionModelPath { get; } = Path.Combine(CacheDirectory, "vision_model_quantized.onnx");
    public static string RuntimeVersion { get; } = "xenova-clip-vit-base-patch32-quantized-v1";
    public static string IdleStatusText { get; } = "Preparing local semantic search";
    private static readonly string RuntimeVersionPath = Path.Combine(CacheDirectory, "runtime.version");
    private static readonly IReadOnlyList<(string Url, string TargetPath)> RuntimeAssets =
    [
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/vocab.json?download=1", VocabPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/merges.txt?download=1", MergesPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/text_model_quantized.onnx?download=1", TextModelPath),
        ("https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model_quantized.onnx?download=1", VisionModelPath)
    ];

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            return;

        AppDiagnostics.LogInfo("semantic.install", "Preparing local semantic runtime.");
        Directory.CreateDirectory(CacheDirectory);
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

    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "Not installed";

        var text = status.Trim().Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ');
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text.Length <= 140 ? text : text[..137] + "...";
    }

    public static void MarkUnavailable(string status)
    {
        UpdateRuntimeProbeCache(false, status);
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

    private static bool TryGetCachedRuntimeProbe(out bool isReady, out string status)
    {
        if (HasRuntimeFiles())
        {
            UpdateRuntimeProbeCache(true, "Installed");
            isReady = true;
            status = "Installed";
            return true;
        }

        lock (SetupStateGate)
        {
            if (_cachedRuntimeReady.HasValue && DateTime.UtcNow - _cachedRuntimeCheckedUtc <= RuntimeProbeCacheTtl)
            {
                isReady = _cachedRuntimeReady.Value;
                status = _cachedRuntimeStatus;
                return true;
            }
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
            _cachedRuntimeStatus = NormalizeStatus(status);
            _cachedRuntimeCheckedUtc = DateTime.UtcNow;
        }
    }
}
