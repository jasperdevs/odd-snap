using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;

namespace Yoink.Services;

public static class UpscaleRuntimeService
{
    private const string PythonLauncherArg = "-3";
    private const int RuntimeLayoutVersion = 2;
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "Yoink/1.0" } }
    };

    private sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);
    private sealed record ProbeState(bool? Ready, string Status, DateTime CheckedUtc);

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "upscale");

    private static readonly string ModelCacheDir = Path.Combine(RootDir, "models");
    private static readonly string RuntimeDir = Path.Combine(RootDir, "runtime");
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

        var launcherArg = await ResolveCompatiblePythonLauncherAsync(cancellationToken).ConfigureAwait(false);
        if (launcherArg is null)
        {
            var message = await BuildPythonCompatibilityMessageAsync(cancellationToken).ConfigureAwait(false);
            UpdateProbeCache(provider, false, message);
            throw new InvalidOperationException(message);
        }

        progress?.Report("Creating isolated upscale runtime...");
        await EnsureEnvironmentAsync(provider, launcherArg, progress, cancellationToken).ConfigureAwait(false);

        if (!await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The upscale runtime did not become ready after installation.");
    }

    public static async Task<bool> IsRuntimeReadyAsync(UpscaleExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        if (TryGetCachedStatus(provider, out var cachedReady, out _))
            return cachedReady;

        var pythonPath = GetRuntimePythonPath(provider);
        if (!File.Exists(pythonPath) || !IsRuntimeMarkerCurrent(provider))
        {
            UpdateProbeCache(provider, false, "Not installed");
            return false;
        }

        var checkCommand = provider == UpscaleExecutionProvider.Gpu
            ? "import onnxruntime as ort; print('CUDAExecutionProvider' in ort.get_available_providers())"
            : "import onnxruntime, numpy, PIL; print('ok')";

        var result = await RunRuntimePythonAsync(provider, new[] { "-c", checkCommand }, cancellationToken).ConfigureAwait(false);
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
            var result = await RunRuntimePythonAsync(provider, new[]
            {
                "-c",
                BuildUpscaleScript(),
                tempInput,
                tempOutput,
                GetModelPath(engine),
                provider == UpscaleExecutionProvider.Gpu ? "gpu" : "cpu",
                scaleFactor.ToString()
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(GetProcessErrorMessage(result, "Upscale processing failed."));

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

    internal static string GetRuntimeEnvironmentDirectory(UpscaleExecutionProvider provider)
        => Path.Combine(RuntimeDir, provider == UpscaleExecutionProvider.Gpu ? "gpu" : "cpu");

    internal static string GetRuntimePythonPath(UpscaleExecutionProvider provider)
        => Path.Combine(GetRuntimeEnvironmentDirectory(provider), "Scripts", "python.exe");

    internal static string GetRuntimeMarkerPath(UpscaleExecutionProvider provider)
        => Path.Combine(GetRuntimeEnvironmentDirectory(provider), ".yoink-runtime-version");

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

    private static async Task EnsureEnvironmentAsync(UpscaleExecutionProvider provider, string launcherArg, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RuntimeDir);

        var envDir = GetRuntimeEnvironmentDirectory(provider);
        var pythonPath = GetRuntimePythonPath(provider);
        var runtimeVersion = File.Exists(pythonPath)
            ? await GetRuntimePythonVersionAsync(provider, cancellationToken).ConfigureAwait(false)
            : null;
        var recreate = !File.Exists(pythonPath) || !IsRuntimeMarkerCurrent(provider) || !PythonLauncherSelector.IsSupportedOnnxRuntimeVersion(runtimeVersion);

        if (recreate)
        {
            TryDeleteDirectory(envDir);
            progress?.Report("Creating isolated Python environment...");
            var create = await RunLauncherAsync(new[] { launcherArg, "-m", "venv", envDir }, cancellationToken).ConfigureAwait(false);
            if (create.ExitCode != 0)
                throw new InvalidOperationException(GetProcessErrorMessage(create, "Couldn't create the isolated Python environment."));
        }

        progress?.Report("Installing upscale runtime packages...");
        var toolsInstall = await RunRuntimePythonAsync(provider, new[]
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip", "setuptools", "wheel"
        }, cancellationToken).ConfigureAwait(false);
        if (toolsInstall.ExitCode != 0)
            throw new InvalidOperationException(GetProcessErrorMessage(toolsInstall, "Couldn't prepare pip inside the isolated runtime."));

        var runtimeInstall = await InstallRuntimePackagesAsync(provider, progress, cancellationToken).ConfigureAwait(false);
        if (runtimeInstall.ExitCode != 0)
            throw new InvalidOperationException(GetProcessErrorMessage(runtimeInstall, "Couldn't install the upscale runtime."));

        File.WriteAllText(GetRuntimeMarkerPath(provider), RuntimeLayoutVersion.ToString());
        ClearProbeCache(provider);
    }

    private static async Task<ProcessRunResult> InstallRuntimePackagesAsync(UpscaleExecutionProvider provider, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (provider == UpscaleExecutionProvider.Gpu)
        {
            progress?.Report("Installing runtime packages with CUDA support...");
            var gpuInstall = await RunRuntimePythonAsync(provider, BuildInstallArguments(useGpuPackage: true), cancellationToken).ConfigureAwait(false);
            if (gpuInstall.ExitCode == 0)
                return gpuInstall;

            var gpuMessage = GetProcessErrorMessage(gpuInstall, "CUDA runtime package was unavailable.");
            AppDiagnostics.LogWarning("upscale.runtime.install.gpu-optional", gpuMessage);
            progress?.Report("CUDA package unavailable. Falling back to CPU runtime...");
        }

        return await RunRuntimePythonAsync(provider, BuildInstallArguments(useGpuPackage: false), cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> BuildInstallArguments(bool useGpuPackage)
    {
        yield return "-m";
        yield return "pip";
        yield return "install";
        yield return "--disable-pip-version-check";
        yield return "--upgrade";
        yield return "--prefer-binary";
        yield return useGpuPackage ? "onnxruntime-gpu" : "onnxruntime";
        yield return "numpy";
        yield return "pillow";
    }

    private static bool IsRuntimeMarkerCurrent(UpscaleExecutionProvider provider)
    {
        try
        {
            var markerPath = GetRuntimeMarkerPath(provider);
            return File.Exists(markerPath) &&
                   int.TryParse(File.ReadAllText(markerPath).Trim(), out var version) &&
                   version == RuntimeLayoutVersion;
        }
        catch
        {
            return false;
        }
    }

    private static Task<ProcessRunResult> RunLauncherAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
        => RunProcessAsync("py", arguments, cancellationToken);

    private static Task<ProcessRunResult> RunRuntimePythonAsync(UpscaleExecutionProvider provider, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => RunProcessAsync(GetRuntimePythonPath(provider), arguments, cancellationToken);

    private static async Task<ProcessRunResult> RunProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
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
            return new ProcessRunResult(-1, "", $"Could not start process '{fileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminateProcess(process);
            throw;
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            process.WaitForExit(5000);
        }
        catch
        {
        }
    }

    private static string GetProcessErrorMessage(ProcessRunResult result, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            return result.StdErr.Trim();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            return result.StdOut.Trim();
        return fallbackMessage;
    }

    private static async Task<string?> ResolveCompatiblePythonLauncherAsync(CancellationToken cancellationToken)
    {
        var list = await TryListPythonLaunchersAsync(cancellationToken).ConfigureAwait(false);
        if (list.Count > 0)
            return PythonLauncherSelector.SelectOnnxRuntimeLauncherArgument(list);

        var versionProbe = await RunLauncherAsync(new[] { PythonLauncherArg, "--version" }, cancellationToken).ConfigureAwait(false);
        return versionProbe.ExitCode == 0 && PythonLauncherSelector.IsSupportedOnnxRuntimeVersion(versionProbe.StdOut)
            ? PythonLauncherArg
            : null;
    }

    private static async Task<string> BuildPythonCompatibilityMessageAsync(CancellationToken cancellationToken)
    {
        var list = await TryListPythonLaunchersAsync(cancellationToken).ConfigureAwait(false);
        return PythonLauncherSelector.BuildOnnxRuntimeMissingVersionMessage(list);
    }

    private static async Task<IReadOnlyList<PythonLauncherSelector.LauncherEntry>> TryListPythonLaunchersAsync(CancellationToken cancellationToken)
    {
        var result = await RunLauncherAsync(new[] { "--list-paths" }, cancellationToken).ConfigureAwait(false);
        var entries = PythonLauncherSelector.ParseLauncherListOutput($"{result.StdOut}{Environment.NewLine}{result.StdErr}");
        if (entries.Count > 0)
            return entries;

        result = await RunLauncherAsync(new[] { "-0p" }, cancellationToken).ConfigureAwait(false);
        return PythonLauncherSelector.ParseLauncherListOutput($"{result.StdOut}{Environment.NewLine}{result.StdErr}");
    }

    private static async Task<string?> GetRuntimePythonVersionAsync(UpscaleExecutionProvider provider, CancellationToken cancellationToken)
    {
        var result = await RunRuntimePythonAsync(provider, ["--version"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
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
input_meta = session.get_inputs()[0]
output_meta = session.get_outputs()[0]

native_scale = scale
input_shape = input_meta.shape
output_shape = output_meta.shape
if (
    len(input_shape) >= 4 and len(output_shape) >= 4 and
    isinstance(input_shape[2], int) and isinstance(input_shape[3], int) and
    isinstance(output_shape[2], int) and isinstance(output_shape[3], int) and
    input_shape[2] > 0 and input_shape[3] > 0
):
    native_scale = max(output_shape[2] // input_shape[2], output_shape[3] // input_shape[3], 1)

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
_, _, padded_h, padded_w = arr.shape
output = np.zeros((1, 3, padded_h * native_scale, padded_w * native_scale), dtype=np.float32)
weight = np.zeros_like(output)

expected_h = input_shape[2] if len(input_shape) >= 4 and isinstance(input_shape[2], int) and input_shape[2] > 0 else None
expected_w = input_shape[3] if len(input_shape) >= 4 and isinstance(input_shape[3], int) and input_shape[3] > 0 else None

for y in range(0, padded_h, tile):
    for x in range(0, padded_w, tile):
        input_tile = arr[:, :, y:min(y + tile, padded_h), x:min(x + tile, padded_w)]
        tile_h = input_tile.shape[2]
        tile_w = input_tile.shape[3]

        if expected_h is not None and expected_w is not None and (tile_h != expected_h or tile_w != expected_w):
            pad_h = max(0, expected_h - tile_h)
            pad_w = max(0, expected_w - tile_w)
            if pad_h or pad_w:
                input_tile = np.pad(input_tile, ((0, 0), (0, 0), (0, pad_h), (0, pad_w)), mode='reflect')

        output_tile = session.run([output_name], {input_name: input_tile})[0]
        crop_h = tile_h * native_scale
        crop_w = tile_w * native_scale
        output_tile = output_tile[:, :, :crop_h, :crop_w]
        out_y = y * native_scale
        out_x = x * native_scale
        out_h = output_tile.shape[2]
        out_w = output_tile.shape[3]
        output[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += output_tile
        weight[:, :, out_y:out_y + out_h, out_x:out_x + out_w] += 1.0

output = output / np.maximum(weight, 1e-8)
output = output[:, :, :h * native_scale, :w * native_scale]
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

    private static void ClearProbeCache(UpscaleExecutionProvider provider)
    {
        lock (ProbeGate)
            ProbeCache.Remove(provider);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
