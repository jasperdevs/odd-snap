using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Yoink.Services;

public static class RembgRuntimeService
{
    private const string PythonLauncherArg = "-3";

    private sealed record PythonRunResult(int ExitCode, string StdOut, string StdErr);

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "rembg");

    private static readonly string ModelCacheDir = Path.Combine(RootDir, "models");

    public static string RootDirectory => RootDir;
    public static string ModelCacheDirectory => ModelCacheDir;

    public static string GetSetupButtonText(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "Install rembg + CUDA"
        : "Install rembg";

    public static string GetRuntimeSummary(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "GPU uses rembg's CUDA backend when installed. If CUDA is not available, use CPU."
        : "CPU uses the local rembg package and downloads models automatically.";

    public static string GetSetupTargetName(StickerExecutionProvider provider) => provider == StickerExecutionProvider.Gpu
        ? "rembg + CUDA"
        : "rembg";

    public static bool IsModelCached(LocalStickerEngine engine) => File.Exists(GetModelPath(engine));

    public static bool RemoveCachedModel(LocalStickerEngine engine)
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

    public static async Task EnsureInstalledAsync(StickerExecutionProvider provider, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(provider, cancellationToken).ConfigureAwait(false))
            return;

        var package = provider == StickerExecutionProvider.Gpu ? "rembg[gpu]" : "rembg[cpu]";
        progress?.Report($"Installing {package}...");

        var install = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-m", "pip", "install", "--user", "--upgrade", package
        }, cancellationToken).ConfigureAwait(false);

        if (install.ExitCode != 0)
        {
            var message = !string.IsNullOrWhiteSpace(install.StdErr)
                ? install.StdErr.Trim()
                : install.StdOut.Trim();

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? $"Couldn't install {package}."
                : message);
        }
    }

    public static async Task<bool> IsRuntimeReadyAsync(StickerExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        if (!await IsPythonLauncherAvailableAsync(cancellationToken).ConfigureAwait(false))
            return false;

        var checkCommand = provider == StickerExecutionProvider.Gpu
            ? "import rembg, onnxruntime as ort; print('CUDAExecutionProvider' in ort.get_available_providers())"
            : "import rembg; print('ok')";

        var result = await RunPythonAsync(new[]
        {
            PythonLauncherArg, "-c", checkCommand
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            return false;

        if (provider == StickerExecutionProvider.Gpu)
            return result.StdOut.Contains("True", StringComparison.OrdinalIgnoreCase);

        return true;
    }

    public static async Task<Bitmap> RemoveBackgroundAsync(Bitmap input, LocalStickerEngine engine, StickerExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(provider, null, cancellationToken).ConfigureAwait(false);

        var tempInput = SaveTempPng(input);
        var tempOutput = tempInput + ".out.png";

        try
        {
            var result = await RunPythonAsync(new[]
            {
                PythonLauncherArg,
                "-c",
                BuildRembgScript(),
                tempInput,
                tempOutput,
                GetModelId(engine)
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var message = !string.IsNullOrWhiteSpace(result.StdErr)
                    ? result.StdErr.Trim()
                    : result.StdOut.Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? "rembg failed to process the image."
                    : message);
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

    public static async Task WarmupModelAsync(LocalStickerEngine engine, StickerExecutionProvider provider, CancellationToken cancellationToken = default)
    {
        using var blank = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(blank);
        g.Clear(Color.Transparent);
        using var output = await RemoveBackgroundAsync(blank, engine, provider, cancellationToken).ConfigureAwait(false);
    }

    public static string GetModelPath(LocalStickerEngine engine) => Path.Combine(ModelCacheDir, GetModelFileName(engine));

    public static string GetModelFileName(LocalStickerEngine engine) => engine switch
    {
        LocalStickerEngine.BriaRmbg => "bria-rmbg-2.0.onnx",
        LocalStickerEngine.U2Netp => "u2netp.onnx",
        LocalStickerEngine.U2Net => "u2net.onnx",
        LocalStickerEngine.BiRefNetLite => "BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx",
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

        psi.EnvironmentVariables["U2NET_HOME"] = ModelCacheDir;
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

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
        var temp = Path.Combine(Path.GetTempPath(), $"yoink_rembg_{Guid.NewGuid():N}.png");
        input.Save(temp, ImageFormat.Png);
        return temp;
    }

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
}
