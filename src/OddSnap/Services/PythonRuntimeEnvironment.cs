using System.IO;

namespace OddSnap.Services;

internal static class PythonRuntimeEnvironment
{
    internal const string DefaultLauncherArgument = "-3";
    internal const string PipPackage = "pip==26.1";
    internal const string SetuptoolsPackage = "setuptools==82.0.1";
    internal const string WheelPackage = "wheel==0.47.0";
    internal const string NumpyPackage = "numpy==2.4.4";
    internal const string PillowPackage = "pillow==12.2.0";
    internal const string OnnxRuntimePackage = "onnxruntime==1.25.1";
    internal const string OnnxRuntimeGpuPackage = "onnxruntime-gpu==1.25.1";

    public static Task<ProcessRunResult> RunLauncherAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ProcessRunner.RunAsync("py", arguments, cancellationToken);

    public static Task<ProcessRunResult> RunUtf8LauncherAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        string diagnosticCategory,
        string? standardInput = null)
        => ProcessRunner.RunAsync(
            "py",
            arguments,
            cancellationToken,
            standardInput,
            configure: psi =>
            {
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
            },
            startFailureMessage: "Could not start Python launcher.",
            onStartFailure: message => AppDiagnostics.LogWarning(diagnosticCategory, message));

    public static async Task<string?> ResolveCompatibleOnnxRuntimeLauncherAsync(CancellationToken cancellationToken)
    {
        var list = await ListAvailableLaunchersAsync(cancellationToken).ConfigureAwait(false);
        if (list.Count > 0)
            return PythonLauncherSelector.SelectOnnxRuntimeLauncherArgument(list);

        var versionProbe = await RunLauncherAsync([DefaultLauncherArgument, "--version"], cancellationToken).ConfigureAwait(false);
        return versionProbe.ExitCode == 0 && PythonLauncherSelector.IsSupportedOnnxRuntimeVersion(versionProbe.StdOut)
            ? DefaultLauncherArgument
            : null;
    }

    public static async Task<string> BuildMissingOnnxRuntimeMessageAsync(CancellationToken cancellationToken)
    {
        var list = await ListAvailableLaunchersAsync(cancellationToken).ConfigureAwait(false);
        return PythonLauncherSelector.BuildOnnxRuntimeMissingVersionMessage(list);
    }

    public static async Task<IReadOnlyList<PythonLauncherSelector.LauncherEntry>> ListAvailableLaunchersAsync(CancellationToken cancellationToken)
    {
        var result = await RunLauncherAsync(["--list-paths"], cancellationToken).ConfigureAwait(false);
        var entries = PythonLauncherSelector.ParseLauncherListOutput($"{result.StdOut}{Environment.NewLine}{result.StdErr}");
        if (entries.Count > 0)
            return entries;

        result = await RunLauncherAsync(["-0p"], cancellationToken).ConfigureAwait(false);
        return PythonLauncherSelector.ParseLauncherListOutput($"{result.StdOut}{Environment.NewLine}{result.StdErr}");
    }

    public static async Task<string?> GetPythonVersionAsync(string pythonPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(pythonPath, ["--version"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    public static bool IsRuntimeMarkerCurrent(string markerPath, int expectedVersion)
    {
        try
        {
            return File.Exists(markerPath) &&
                   int.TryParse(File.ReadAllText(markerPath).Trim(), out var version) &&
                   version == expectedVersion;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDeleteDirectory(string path, string diagnosticCategory, string context)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                diagnosticCategory,
                $"Failed to delete {context} directory {Path.GetFileName(path)}: {ex.Message}",
                ex);
            return false;
        }
    }
}
