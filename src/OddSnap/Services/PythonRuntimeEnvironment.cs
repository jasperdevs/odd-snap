using System.IO;

namespace OddSnap.Services;

internal static class PythonRuntimeEnvironment
{
    private const string DefaultPythonLauncherArg = "-3";

    public static Task<ProcessRunResult> RunLauncherAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ProcessRunner.RunAsync("py", arguments, cancellationToken);

    public static async Task<string?> ResolveCompatibleOnnxRuntimeLauncherAsync(CancellationToken cancellationToken)
    {
        var list = await ListAvailableLaunchersAsync(cancellationToken).ConfigureAwait(false);
        if (list.Count > 0)
            return PythonLauncherSelector.SelectOnnxRuntimeLauncherArgument(list);

        var versionProbe = await RunLauncherAsync([DefaultPythonLauncherArg, "--version"], cancellationToken).ConfigureAwait(false);
        return versionProbe.ExitCode == 0 && PythonLauncherSelector.IsSupportedOnnxRuntimeVersion(versionProbe.StdOut)
            ? DefaultPythonLauncherArg
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

    public static void TryDeleteDirectory(string path)
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
