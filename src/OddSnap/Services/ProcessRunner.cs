using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OddSnap.Services;

internal sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

internal static class ProcessRunner
{
    private const int MaxCapturedOutputChars = 256 * 1024;

    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        string? standardInput = null,
        Action<ProcessStartInfo>? configure = null,
        string? startFailureMessage = null,
        Action<string>? onStartFailure = null)
    {
        var psi = CreateStartInfo(fileName, redirectStandardInput: standardInput is not null);

        configure?.Invoke(psi);
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var errorMode = WindowsErrorModeScope.SuppressSystemDialogs();
        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                var message = startFailureMessage ?? $"Could not start process '{fileName}'.";
                onStartFailure?.Invoke(message);
                return new ProcessRunResult(-1, "", message);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        {
            var message = startFailureMessage ?? $"Could not start process '{fileName}'.";
            onStartFailure?.Invoke(message);
            return new ProcessRunResult(-1, "", $"{message} {ex.Message}".Trim());
        }

        var stdoutTask = ReadLimitedOutputAsync(process.StandardOutput, "stdout", cancellationToken);
        var stderrTask = ReadLimitedOutputAsync(process.StandardError, "stderr", cancellationToken);
        var stdinTask = standardInput is null
            ? Task.CompletedTask
            : WriteStandardInputAsync(process, standardInput, cancellationToken);
        try
        {
            await stdinTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            await ObserveProcessIoTasksAfterCancellationAsync(stdinTask, stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    internal static ProcessStartInfo CreateStartInfo(string fileName, bool redirectStandardInput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            CreateNoWindow = true
        };

        if (redirectStandardInput)
            psi.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return psi;
    }

    private static async Task<string> ReadLimitedOutputAsync(StreamReader reader, string streamName, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(capacity: Math.Min(MaxCapturedOutputChars, 4096));
        var buffer = new char[4096];
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            if (builder.Length < MaxCapturedOutputChars)
            {
                var remaining = MaxCapturedOutputChars - builder.Length;
                if (read <= remaining)
                {
                    builder.Append(buffer, 0, read);
                }
                else
                {
                    builder.Append(buffer, 0, remaining);
                    truncated = true;
                }
            }
            else
            {
                truncated = true;
            }
        }

        if (truncated)
            builder.AppendLine().Append("[OddSnap truncated ").Append(streamName).Append(" after ").Append(MaxCapturedOutputChars).Append(" characters]");

        return builder.ToString();
    }

    private static async Task WriteStandardInputAsync(Process process, string standardInput, CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { }
        }
    }

    private static async Task ObserveProcessIoTasksAfterCancellationAsync(params Task[] tasks)
    {
        var combined = Task.WhenAll(tasks);
        try
        {
            await combined.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            if (!combined.IsCompleted)
            {
                _ = combined.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
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
