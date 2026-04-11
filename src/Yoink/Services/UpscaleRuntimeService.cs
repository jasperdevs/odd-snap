using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;

namespace Yoink.Services;

public static class UpscaleRuntimeService
{
    private const string PythonLauncherArg = "-3";
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
    };

    private sealed record PythonRunResult(int ExitCode, string StdOut, string StdErr);
    private sealed record ProbeState(bool? Ready, string Status, DateTime CheckedUtc);

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "upscale");

    private static readonly string ModelCacheDir = Path.Combine(RootDir, "models");
    private static readonly object ProbeGate = new();
    private static readonly Dictionary<UpscaleExecutionProvider, ProbeState> ProbeCache = new();

    public static string RootDirectory => RootDir;
    public static string ModelCacheDirectory => ModelCacheDir;

    public static string GetSetupButtonText(UpscaleExecutionProvider provider) => provider == UpscaleExecutionProvider.Gpu
        ? "Install onnxruntime (GPU optional)"
        : "Install onnxruntime";

    public static string GetRuntimeSummary(UpscaleExecutionProvider provider) => provider == UpscaleExecutionProvider.Gpu
        ? "GPU uses Python ONNX Runtime with CUDA when available and falls back to CPU otherwise."
        : "CPU uses Python ONNX Runtime and downloaded ONNX models.";

    public static string GetSetupTargetName(UpscaleExecutionProvider provider) => provider == UpscaleExecutionProvider.Gpu
        ? "onnxruntime runtime"
        : "onnxruntime";

    public static bool IsModelCached(LocalUpscaleEngine engine) => File.Exists(GetModelPath(engine));

    public static bool HasAnyCachedModels()
    {
        try { return Directory.Exists(ModelCacheDir) && Directory.EnumerateFiles(ModelCacheDir, "*.onnx").Any(); }
        catch { return false; }
    }

    public static bool RemoveCachedModel(LocalUpscaleEngine engine)
    {
        var modelPath = GetModelPath(engine);
        try
        {
            if (File.Exists(modelPath))
                File.Delete(modelPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoveAllCachedModels()
    {
        try
        {
            if (Directory.Exists(ModelCacheDir))
                Directory.Delete(ModelCacheDir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task EnsureInstalledAsync(UpscaleExecutionProvider provider, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false))
            return;

        progress?.Report("Installing Python runtime packages...");
        var install = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-m", "pip", "install", "--user", "--upgrade", "onnxruntime", "numpy", "pillow"
        }, cancellationToken).ConfigureAwait(false);

        if (install.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(install.StdErr) ? install.StdOut.Trim() : install.StdErr.Trim());

        if (provider == UpscaleExecutionProvider.Gpu)
        {
            progress?.Report("Trying to enable CUDA acceleration...");
            var gpuInstall = await RunPythonAsync(new[]
            {
                PythonLauncherArg, "-m", "pip", "install", "--user", "--upgrade", "onnxruntime-gpu"
            }, cancellationToken).ConfigureAwait(false);

            if (gpuInstall.ExitCode != 0)
            {
                var gpuMessage = string.IsNullOrWhiteSpace(gpuInstall.StdErr) ? gpuInstall.StdOut.Trim() : gpuInstall.StdErr.Trim();
                AppDiagnostics.LogWarning("upscale.runtime.install.gpu-optional", string.IsNullOrWhiteSpace(gpuMessage)
                    ? "CUDA acceleration package was unavailable; using CPU fallback."
                    : gpuMessage);
            }
        }

        await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> IsRuntimeReadyAsync(UpscaleExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        if (TryGetCachedStatus(provider, out var cachedReady, out _))
            return cachedReady;

        if (!await IsPythonLauncherAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            UpdateProbeCache(provider, false, "Python not found");
            return false;
        }

        var checkCommand = provider == UpscaleExecutionProvider.Gpu
            ? "import onnxruntime as ort; print('CUDAExecutionProvider' in ort.get_available_providers())"
            : "import onnxruntime, numpy, PIL; print('ok')";

        var result = await RunPythonAsync(new[] { PythonLauncherArg, "-c", checkCommand }, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            UpdateProbeCache(provider, false, "Not installed");
            return false;
        }

        if (provider == UpscaleExecutionProvider.Gpu)
        {
            var cudaAvailable = result.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);
            UpdateProbeCache(provider, true, cudaAvailable ? "Installed (CUDA available)" : "Installed (CPU fallback)");
            return true;
        }

        UpdateProbeCache(provider, true, "Installed");
        return true;
    }

    public static bool TryGetCachedStatus(UpscaleExecutionProvider provider, out bool isReady, out string status)
    {
        lock (ProbeGate)
        {
            if (ProbeCache.TryGetValue(provider, out var state) &&
                state.Ready.HasValue &&
                DateTime.UtcNow - state.CheckedUtc <= ProbeCacheTtl)
            {
                isReady = state.Ready.Value;
                status = state.Status;
                return true;
            }
        }

        isReady = false;
        status = "Checking runtime...";
        return false;
    }

    public static async Task EnsureModelDownloadedAsync(LocalUpscaleEngine engine, IProgress<LocalUpscaleEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var modelPath = GetModelPath(engine);
        if (File.Exists(modelPath))
            return;

        Directory.CreateDirectory(ModelCacheDir);
        var tempPath = modelPath + ".download";
        var url = GetModelDownloadUrl(engine);
        progress?.Report(new LocalUpscaleEngineDownloadProgress(0, null, $"Downloading {LocalUpscaleEngineService.GetEngineLabel(engine)}..."));

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                long read = 0;

                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    read += count;
                    progress?.Report(new LocalUpscaleEngineDownloadProgress(read, total, $"Downloading {LocalUpscaleEngineService.GetEngineLabel(engine)}..."));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, modelPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    public static async Task<Bitmap> UpscaleAsync(Bitmap input, LocalUpscaleEngine engine, UpscaleExecutionProvider provider, int scaleFactor, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(provider, null, cancellationToken).ConfigureAwait(false);
        await EnsureModelDownloadedAsync(engine, null, cancellationToken).ConfigureAwait(false);

        var tempInput = SaveTempPng(input);
        var tempOutput = tempInput + ".out.png";

        try
        {
            var result = await RunPythonAsync(new[]
            {
                PythonLauncherArg,
                "-c",
                BuildUpscaleScript(),
                tempInput,
                tempOutput,
                GetModelPath(engine),
                provider == UpscaleExecutionProvider.Gpu ? "gpu" : "cpu",
                scaleFactor.ToString()
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim());

            if (!File.Exists(tempOutput))
                throw new InvalidOperationException("Upscale did not produce an output image.");

            using var img = Image.FromFile(tempOutput);
            return new Bitmap(img);
        }
        finally
        {
            try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
    }

    public static string GetModelPath(LocalUpscaleEngine engine) => Path.Combine(ModelCacheDir, GetModelFileName(engine));

    private static string GetModelDownloadUrl(LocalUpscaleEngine engine) => engine switch
    {
        LocalUpscaleEngine.SwinIrRealWorld => "https://huggingface.co/rocca/swin-ir-onnx/resolve/main/003_realSR_BSRGAN_DFO_s64w8_SwinIR-M_x4_GAN.onnx?download=1",
        LocalUpscaleEngine.RealEsrganX4Plus => "https://huggingface.co/bukuroo/RealESRGAN-ONNX/resolve/main/real-esrgan-x4plus-128.onnx?download=1",
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

    private static string GetModelFileName(LocalUpscaleEngine engine) => engine switch
    {
        LocalUpscaleEngine.SwinIrRealWorld => "swinir-realworld-x4.onnx",
        LocalUpscaleEngine.RealEsrganX4Plus => "real-esrgan-x4plus.onnx",
        _ => "upscale.onnx"
    };

    private static async Task<bool> IsPythonLauncherAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await RunPythonAsync(new[] { PythonLauncherArg, "--version" }, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static async Task<PythonRunResult> RunPythonAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "py",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var errorMode = WindowsErrorModeScope.SuppressSystemDialogs();
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return new PythonRunResult(-1, "", "Could not start Python launcher.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new PythonRunResult(process.ExitCode, stdout, stderr);
    }

    private static string SaveTempPng(Bitmap input)
    {
        Directory.CreateDirectory(Path.GetTempPath());
        var temp = Path.Combine(Path.GetTempPath(), $"yoink_upscale_{Guid.NewGuid():N}.png");
        input.Save(temp, ImageFormat.Png);
        return temp;
    }

    private static string BuildUpscaleScript() => """
import sys
import numpy as np
from PIL import Image
import onnxruntime as ort

input_path = sys.argv[1]
output_path = sys.argv[2]
model_path = sys.argv[3]
device = sys.argv[4]
scale = int(sys.argv[5])

providers = ['CPUExecutionProvider']
if device == 'gpu':
    available = ort.get_available_providers()
    if 'CUDAExecutionProvider' in available:
        providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']

session = ort.InferenceSession(model_path, providers=providers)
input_name = session.get_inputs()[0].name
output_name = session.get_outputs()[0].name

img = Image.open(input_path).convert('RGB')
arr = np.asarray(img).astype(np.float32) / 255.0
arr = np.transpose(arr, (2, 0, 1))[None, :, :, :]

window = 8
h = arr.shape[2]
w = arr.shape[3]
pad_h = (window - h % window) % window
pad_w = (window - w % window) % window
if pad_h or pad_w:
    arr = np.pad(arr, ((0, 0), (0, 0), (0, pad_h), (0, pad_w)), mode='reflect')

tile = 256
tile_overlap = 24
_, _, padded_h, padded_w = arr.shape
output = np.zeros((1, 3, padded_h * scale, padded_w * scale), dtype=np.float32)
weight = np.zeros_like(output)

for y in range(0, padded_h, tile):
    for x in range(0, padded_w, tile):
        input_tile = arr[:, :, y:min(y + tile, padded_h), x:min(x + tile, padded_w)]
        output_tile = session.run([output_name], {input_name: input_tile})[0]
        out_y = y * scale
        out_x = x * scale
        out_h = output_tile.shape[2]
        out_w = output_tile.shape[3]
        output[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += output_tile
        weight[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += 1.0

output = output / np.maximum(weight, 1e-8)
output = output[:, :, :h * scale, :w * scale]
output = np.clip(output[0], 0.0, 1.0)
output = np.transpose(output, (1, 2, 0))
output = (output * 255.0).round().astype(np.uint8)
Image.fromarray(output).save(output_path)
""";

    private static void UpdateProbeCache(UpscaleExecutionProvider provider, bool ready, string status)
    {
        lock (ProbeGate)
            ProbeCache[provider] = new ProbeState(ready, status, DateTime.UtcNow);
    }
}
