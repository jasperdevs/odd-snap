using System.Drawing;
using System.IO;

namespace OddSnap.Services;

public static class RembgRuntimeService
{
    private const int RuntimeLayoutVersion = 4;
    private const string PipPackage = "pip==26.1";
    private const string SetuptoolsPackage = "setuptools==82.0.1";
    private const string WheelPackage = "wheel==0.47.0";
    private const string RembgPackage = "rembg==2.0.75";
    private const string NumpyPackage = "numpy==2.4.4";
    private const string PillowPackage = "pillow==12.2.0";
    private const string OnnxRuntimePackage = "onnxruntime==1.25.1";
    private const string OnnxRuntimeGpuPackage = "onnxruntime-gpu==1.25.1";
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(10);

    private sealed record ProbeState(bool? Ready, string Status, DateTime CheckedUtc);

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap", "rembg");

    private static readonly string ModelCacheDir = Path.Combine(RootDir, "models");
    private static readonly string RuntimeDir = Path.Combine(RootDir, "runtime");
    private static readonly string LegacyModelCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".u2net");
    private static readonly object ProbeGate = new();
    private static readonly Dictionary<StickerExecutionProvider, ProbeState> ProbeCache = new();

    public static string RootDirectory => RootDir;
    public static string ModelCacheDirectory => ModelCacheDir;

    public static string GetSetupButtonText(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "Install rembg (GPU optional)"
        : "Install rembg";

    public static string GetRuntimeSummary(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "GPU uses rembg's CUDA backend when available and falls back to CPU otherwise."
        : "CPU uses the local rembg package and downloads models automatically.";

    public static string GetSetupTargetName(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "rembg runtime"
        : "rembg";

    public static bool IsModelCached(LocalStickerEngine engine) => ResolveExistingModelPath(engine) is not null;

    public static bool HasAnyCachedModels()
    {
        try { return Enum.GetValues<LocalStickerEngine>().Any(IsModelCached); }
        catch { return false; }
    }

    public static bool RemoveCachedModel(LocalStickerEngine engine)
    {
        try
        {
            foreach (var path in GetCandidateModelPaths(engine))
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

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
            foreach (var engine in Enum.GetValues<LocalStickerEngine>())
                RemoveCachedModel(engine);

            TryDeleteDirectoryIfEmpty(ModelCacheDir);
            TryDeleteDirectoryIfEmpty(LegacyModelCacheDir);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoveRuntime(StickerExecutionProvider provider)
    {
        try
        {
            PythonRuntimeEnvironment.TryDeleteDirectory(GetRuntimeEnvironmentDirectory(provider));
            ClearProbeCache(provider);
            TryDeleteDirectoryIfEmpty(RuntimeDir);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task EnsureInstalledAsync(StickerExecutionProvider provider, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false))
            return;

        var launcherArg = await PythonRuntimeEnvironment.ResolveCompatibleOnnxRuntimeLauncherAsync(cancellationToken).ConfigureAwait(false);
        if (launcherArg is null)
        {
            var message = await PythonRuntimeEnvironment.BuildMissingOnnxRuntimeMessageAsync(cancellationToken).ConfigureAwait(false);
            UpdateProbeCache(provider, false, message);
            throw new InvalidOperationException(message);
        }

        progress?.Report("Creating isolated rembg runtime...");
        AppDiagnostics.LogInfo("stickers.runtime.install", $"Installing isolated rembg runtime for {provider}.");

        await EnsureEnvironmentAsync(provider, launcherArg, progress, cancellationToken).ConfigureAwait(false);

        if (!await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The rembg runtime did not become ready after installation.");
    }

    public static async Task<bool> IsRuntimeReadyAsync(StickerExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        if (TryGetCachedStatus(provider, out var cachedReady, out _))
            return cachedReady;

        var pythonPath = GetRuntimePythonPath(provider);
        if (!File.Exists(pythonPath) || !IsRuntimeMarkerCurrent(provider))
        {
            UpdateProbeCache(provider, false, "Not installed");
            return false;
        }

        var checkCommand = provider == StickerExecutionProvider.Gpu
            ? "import rembg, onnxruntime as ort; print('CUDAExecutionProvider' in ort.get_available_providers())"
            : "import rembg, onnxruntime, numpy, PIL; print('ok')";

        var result = await RunRuntimePythonAsync(provider, new[] { "-c", checkCommand }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            UpdateProbeCache(provider, false, "Not installed");
            return false;
        }

        if (provider == StickerExecutionProvider.Gpu)
        {
            var cudaAvailable = result.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);
            UpdateProbeCache(provider, true, cudaAvailable ? "Installed (CUDA available)" : "Installed (CPU fallback)");
            return true;
        }

        UpdateProbeCache(provider, true, "Installed");
        return true;
    }

    public static bool TryGetCachedStatus(StickerExecutionProvider provider, out bool isReady, out string status)
    {
        var pythonPath = GetRuntimePythonPath(provider);
        if (File.Exists(pythonPath) && IsRuntimeMarkerCurrent(provider))
        {
            lock (ProbeGate)
            {
                if (ProbeCache.TryGetValue(provider, out var installedState) &&
                    installedState.Ready == true &&
                    DateTime.UtcNow - installedState.CheckedUtc <= ProbeCacheTtl)
                {
                    isReady = true;
                    status = installedState.Status;
                    return true;
                }
            }

            UpdateProbeCache(provider, true, "Installed");
            isReady = true;
            status = "Installed";
            return true;
        }

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

    public static async Task EnsureModelReadyAsync(LocalStickerEngine engine, StickerExecutionProvider provider, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(provider, progress, cancellationToken).ConfigureAwait(false);

        var modelPath = GetModelPath(engine);
        if (File.Exists(modelPath))
            return;

        progress?.Report($"Preparing {LocalStickerEngineService.GetEngineLabel(engine)}...");
        var result = await RunRuntimePythonAsync(provider, new[]
        {
            "-c",
            BuildModelPrepareScript(),
            GetModelId(engine)
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(ProcessRunner.GetFailureMessage(result, "Couldn't prepare the sticker model."));

        if (!File.Exists(modelPath))
            throw new InvalidOperationException("The sticker model did not finish downloading.");
    }

    public static async Task<Bitmap> RemoveBackgroundAsync(Bitmap input, LocalStickerEngine engine, StickerExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var tempInput = CaptureOutputService.SaveBitmapToTempPng(input, "oddsnap_rembg");
        var tempOutput = tempInput + ".out.png";

        try
        {
            var result = await RunRuntimePythonAsync(provider, new[]
            {
                "-c",
                BuildRembgScript(),
                tempInput,
                tempOutput,
                GetModelId(engine)
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var message = ProcessRunner.GetFailureMessage(result, "rembg failed to process the image.");
                AppDiagnostics.LogWarning("stickers.runtime.remove-background", message);
                throw new InvalidOperationException(message);
            }

            if (!File.Exists(tempOutput))
                throw new InvalidOperationException("rembg did not produce an output image.");

            using var img = Image.FromFile(tempOutput);
            return new Bitmap(img);
        }
        finally
        {
            try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
    }

    public static string GetModelPath(LocalStickerEngine engine)
        => ResolveExistingModelPath(engine) ?? GetPreferredModelPath(engine);

    private static string GetPreferredModelPath(LocalStickerEngine engine)
        => Path.Combine(ModelCacheDir, GetModelFileName(engine));

    public static string GetModelFileName(LocalStickerEngine engine) => engine switch
    {
        LocalStickerEngine.BriaRmbg => "bria-rmbg.onnx",
        LocalStickerEngine.U2Netp => "u2netp.onnx",
        LocalStickerEngine.U2Net => "u2net.onnx",
        LocalStickerEngine.BiRefNetLite => "birefnet-general-lite.onnx",
        LocalStickerEngine.IsNetGeneralUse => "isnet-general-use.onnx",
        _ => "u2netp.onnx"
    };

    public static string GetModelId(LocalStickerEngine engine) => engine switch
    {
        LocalStickerEngine.BriaRmbg => "bria-rmbg",
        LocalStickerEngine.U2Netp => "u2netp",
        LocalStickerEngine.U2Net => "u2net",
        LocalStickerEngine.BiRefNetLite => "birefnet-general-lite",
        LocalStickerEngine.IsNetGeneralUse => "isnet-general-use",
        _ => "u2netp"
    };

    internal static string GetRuntimeEnvironmentDirectory(StickerExecutionProvider provider)
        => Path.Combine(RuntimeDir, provider == StickerExecutionProvider.Gpu ? "gpu" : "cpu");

    internal static string GetRuntimePythonPath(StickerExecutionProvider provider)
        => Path.Combine(GetRuntimeEnvironmentDirectory(provider), "Scripts", "python.exe");

    internal static string GetRuntimeMarkerPath(StickerExecutionProvider provider)
        => Path.Combine(GetRuntimeEnvironmentDirectory(provider), ".oddsnap-runtime-version");

    private static async Task EnsureEnvironmentAsync(StickerExecutionProvider provider, string launcherArg, IProgress<string>? progress, CancellationToken cancellationToken)
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
            PythonRuntimeEnvironment.TryDeleteDirectory(envDir);
            progress?.Report("Creating isolated Python environment...");
            var create = await PythonRuntimeEnvironment.RunLauncherAsync(new[] { launcherArg, "-m", "venv", envDir }, cancellationToken).ConfigureAwait(false);
            if (create.ExitCode != 0)
                throw new InvalidOperationException(ProcessRunner.GetFailureMessage(create, "Couldn't create the isolated Python environment."));
        }

        progress?.Report("Installing rembg packages...");
        var toolsInstall = await RunRuntimePythonAsync(provider, new[]
        {
            "-m", "pip", "install", "--disable-pip-version-check", PipPackage, SetuptoolsPackage, WheelPackage
        }, cancellationToken).ConfigureAwait(false);
        if (toolsInstall.ExitCode != 0)
            throw new InvalidOperationException(ProcessRunner.GetFailureMessage(toolsInstall, "Couldn't prepare pip inside the isolated runtime."));

        var runtimeInstall = await InstallRuntimePackagesAsync(provider, progress, cancellationToken).ConfigureAwait(false);
        if (runtimeInstall.ExitCode != 0)
            throw new InvalidOperationException(ProcessRunner.GetFailureMessage(runtimeInstall, "Couldn't install the rembg runtime."));

        File.WriteAllText(GetRuntimeMarkerPath(provider), RuntimeLayoutVersion.ToString());
        ClearProbeCache(provider);
    }

    private static async Task<ProcessRunResult> InstallRuntimePackagesAsync(StickerExecutionProvider provider, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (provider == StickerExecutionProvider.Gpu)
        {
            progress?.Report("Installing rembg packages with CUDA support...");
            var gpuInstall = await RunRuntimePythonAsync(provider, BuildInstallArguments(useGpuPackage: true), cancellationToken).ConfigureAwait(false);
            if (gpuInstall.ExitCode == 0)
                return gpuInstall;

            var gpuMessage = ProcessRunner.GetFailureMessage(gpuInstall, "CUDA runtime package was unavailable.");
            AppDiagnostics.LogWarning("stickers.runtime.install.gpu-optional", gpuMessage);
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
        yield return "--prefer-binary";
        yield return RembgPackage;
        yield return NumpyPackage;
        yield return PillowPackage;
        yield return useGpuPackage ? OnnxRuntimeGpuPackage : OnnxRuntimePackage;
    }

    private static bool IsRuntimeMarkerCurrent(StickerExecutionProvider provider)
    {
        return PythonRuntimeEnvironment.IsRuntimeMarkerCurrent(GetRuntimeMarkerPath(provider), RuntimeLayoutVersion);
    }

    private static Task<ProcessRunResult> RunRuntimePythonAsync(StickerExecutionProvider provider, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => RunProcessAsync(GetRuntimePythonPath(provider), arguments, cancellationToken);

    private static Task<ProcessRunResult> RunProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ProcessRunner.RunAsync(
            fileName,
            arguments,
            cancellationToken,
            configure: psi =>
            {
                psi.EnvironmentVariables["U2NET_HOME"] = ModelCacheDir;
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            },
            onStartFailure: message => AppDiagnostics.LogWarning("stickers.runtime.process-start", message));

    private static async Task<string?> GetRuntimePythonVersionAsync(StickerExecutionProvider provider, CancellationToken cancellationToken)
        => await PythonRuntimeEnvironment.GetPythonVersionAsync(GetRuntimePythonPath(provider), cancellationToken).ConfigureAwait(false);

    private static string? ResolveExistingModelPath(LocalStickerEngine engine)
    {
        foreach (var path in GetCandidateModelPaths(engine))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateModelPaths(LocalStickerEngine engine)
    {
        var fileName = GetModelFileName(engine);
        yield return Path.Combine(ModelCacheDir, fileName);
        yield return Path.Combine(LegacyModelCacheDir, fileName);
    }

    private static string BuildModelPrepareScript() => """
import sys
from rembg import new_session

model_name = sys.argv[1]
new_session(model_name)
print("ok")
""";

    private static string BuildRembgScript() => """
import sys
from rembg import remove, new_session

input_path = sys.argv[1]
output_path = sys.argv[2]
model_name = sys.argv[3]

with open(input_path, "rb") as f:
    input_data = f.read()

session = new_session(model_name)
output_data = remove(input_data, session=session)

with open(output_path, "wb") as f:
    f.write(output_data)
""";

    private static void UpdateProbeCache(StickerExecutionProvider provider, bool ready, string status)
    {
        lock (ProbeGate)
            ProbeCache[provider] = new ProbeState(ready, status, DateTime.UtcNow);
    }

    private static void ClearProbeCache(StickerExecutionProvider provider)
    {
        lock (ProbeGate)
            ProbeCache.Remove(provider);
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
        }
    }
}
