using System.Diagnostics;

namespace OddSnap.Services;

internal sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

internal static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Action<ProcessStartInfo>? configure = null,
        string? startFailureMessage = null,
        Action<string>? onStartFailure = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        configure?.Invoke(psi);
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var errorMode = WindowsErrorModeScope.SuppressSystemDialogs();
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            var message = startFailureMessage ?? $"Could not start process '{fileName}'.";
            onStartFailure?.Invoke(message);
            return new ProcessRunResult(-1, "", message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    public static string GetFailureMessage(ProcessRunResult result, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            return result.StdErr.Trim();
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            return result.StdOut.Trim();
        return fallbackMessage;
    }

    public static void Terminate(Process process)
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
}
