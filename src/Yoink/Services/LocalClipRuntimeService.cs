using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Yoink.Services;

public sealed class LocalClipRuntimeService : IDisposable
{
    private const string PythonLauncherArg = "-3";
    private const string ModelName = "ViT-B-32";
    private const string PretrainedName = "laion2b_s34b_b79k";
    private const string PipPackage = "open_clip_torch";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly string ScriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "local_clip_service.py");
    private static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "clip");

    public static string CacheDirectory => CacheDir;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly StringBuilder _stderrTail = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isAvailable;
    private string _statusText = "CLIP runtime idle";
    private int _requestId;
    private bool _disposed;
    private string _modelKey = $"{ModelName}/{PretrainedName}";

    public event Action<string>? StatusChanged;

    public bool IsAvailable { get { lock (_gate) return _isAvailable; } }
    public string StatusText { get { lock (_gate) return _statusText; } }
    public string ModelKey { get { lock (_gate) return _modelKey; } }

    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            return;

        if (!await IsPythonLauncherAvailableAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Python launcher 'py' was not found.");

        progress?.Report($"Installing {PipPackage}...");
        var install = await RunPythonAsync(new[] { PythonLauncherArg, "-m", "pip", "install", "--user", "--upgrade", PipPackage, "pillow" }, cancellationToken).ConfigureAwait(false);
        if (install.ExitCode != 0)
        {
            var message = !string.IsNullOrWhiteSpace(install.StdErr) ? install.StdErr.Trim() : install.StdOut.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? $"Couldn't install {PipPackage}." : message);
        }

        if (!await IsRuntimeReadyAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"{PipPackage} installed, but CLIP imports are still unavailable.");
    }

    public static async Task<bool> IsRuntimeReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsPythonLauncherAvailableAsync(cancellationToken).ConfigureAwait(false))
            return false;

        var result = await RunPythonAsync(new[] { PythonLauncherArg, "-c", "import open_clip, torch; print('ok')" }, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && result.StdOut.Contains("ok", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ClipEmbeddingResult> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ClipEmbeddingResult(null, "Text was empty.");

        var response = await SendRequestAsync("text", text, null, cancellationToken).ConfigureAwait(false);
        return response ?? new ClipEmbeddingResult(null, StatusText);
    }

    public async Task<ClipEmbeddingResult> EmbedImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return new ClipEmbeddingResult(null, "Image path was empty.");

        var response = await SendRequestAsync("image", null, imagePath, cancellationToken).ConfigureAwait(false);
        return response ?? new ClipEmbeddingResult(null, StatusText);
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            lock (_gate)
            {
                if (_process is { HasExited: false })
                {
                    try { _stdin?.WriteLine(JsonSerializer.Serialize(new ClipRequest("shutdown", 0, null, null), JsonOpts)); } catch { }
                    try { _stdin?.Flush(); } catch { }
                    try { _process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch
        {
        }
    }

    private async Task<ClipEmbeddingResult?> SendRequestAsync(string op, string? text = null, string? imagePath = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return new ClipEmbeddingResult(null, "CLIP runtime was disposed.");

        if (!await EnsureProcessAsync(cancellationToken).ConfigureAwait(false))
            return new ClipEmbeddingResult(null, StatusText);

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureProcessAsync(cancellationToken).ConfigureAwait(false))
                return new ClipEmbeddingResult(null, StatusText);

            var payload = JsonSerializer.Serialize(new ClipRequest(op, Interlocked.Increment(ref _requestId), text, imagePath), JsonOpts);
            StreamWriter? stdin;
            StreamReader? stdout;
            lock (_gate)
            {
                stdin = _stdin;
                stdout = _stdout;
            }

            if (stdin is null || stdout is null)
                return new ClipEmbeddingResult(null, "CLIP runtime is not connected.");

            await stdin.WriteLineAsync(payload).ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);

            var responseLine = await stdout.ReadLineAsync().ConfigureAwait(false);
            if (responseLine is null)
            {
                MarkUnavailable("CLIP helper exited unexpectedly.");
                return new ClipEmbeddingResult(null, StatusText);
            }

            var response = JsonSerializer.Deserialize<ClipResponse>(responseLine, JsonOpts);
            if (response is null)
                return new ClipEmbeddingResult(null, "CLIP helper returned invalid JSON.");

            if (!response.Ok)
                return new ClipEmbeddingResult(null, response.Error ?? "CLIP helper rejected the request.");

            return new ClipEmbeddingResult(response.Embedding ?? Array.Empty<float>(), null);
        }
        catch (Exception ex)
        {
            MarkUnavailable($"CLIP helper failed: {ex.Message}");
            return new ClipEmbeddingResult(null, StatusText);
        }
        finally
        {
            try { _requestGate.Release(); } catch { }
        }
    }

    private async Task<bool> EnsureProcessAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return false;

        lock (_gate)
        {
            if (_process is { HasExited: false } && _stdin is not null && _stdout is not null)
                return true;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return false;

            lock (_gate)
            {
                if (_process is { HasExited: false } && _stdin is not null && _stdout is not null)
                    return true;
            }

            if (!File.Exists(ScriptPath))
            {
                MarkUnavailable($"CLIP helper script was not found: {ScriptPath}");
                return false;
            }

            await EnsureInstalledAsync(null, cancellationToken).ConfigureAwait(false);
            SetStatus("Loading CLIP runtime...");

            var psi = new ProcessStartInfo
            {
                FileName = "py",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            psi.ArgumentList.Add(PythonLauncherArg);
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(ScriptPath);
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["TORCH_HOME"] = CacheDir;
            psi.EnvironmentVariables["HF_HOME"] = CacheDir;
            psi.EnvironmentVariables["XDG_CACHE_HOME"] = CacheDir;
            Directory.CreateDirectory(CacheDir);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                var message = ReadStderrTail();
                MarkUnavailable(string.IsNullOrWhiteSpace(message) ? "CLIP helper stopped." : $"CLIP helper stopped: {message}");
            };

            if (!process.Start())
            {
                MarkUnavailable("Could not start Python launcher.");
                return false;
            }

            lock (_gate)
            {
                _process = process;
                _stdin = process.StandardInput;
                _stdin.AutoFlush = true;
                _stdout = process.StandardOutput;
                _isAvailable = false;
            }

            _ = Task.Run(() => DrainStderrAsync(process));

            var readyLine = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            if (readyLine is null)
            {
                var stderr = ReadStderrTail();
                MarkUnavailable(string.IsNullOrWhiteSpace(stderr) ? "CLIP helper ended before initialization." : stderr);
                return false;
            }

            var ready = JsonSerializer.Deserialize<ClipBootstrapResponse>(readyLine, JsonOpts);
            if (ready is null || !ready.Ok)
            {
                MarkUnavailable(ready?.Error ?? "CLIP helper returned invalid startup data.");
                return false;
            }

            lock (_gate)
            {
                _isAvailable = true;
                _modelKey = string.IsNullOrWhiteSpace(ready.ModelKey) ? $"{ModelName}/{PretrainedName}" : ready.ModelKey;
                _statusText = $"CLIP ready ({ready.Device ?? "cpu"})";
                _stderrTail.Clear();
            }

            StatusChanged?.Invoke(StatusText);
            return true;
        }
        finally
        {
            try { _startGate.Release(); } catch { }
        }
    }

    private async Task DrainStderrAsync(Process process)
    {
        try
        {
            while (!_disposed && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

                lock (_gate)
                {
                    if (_stderrTail.Length > 0)
                        _stderrTail.AppendLine();
                    _stderrTail.Append(line);
                    if (_stderrTail.Length > 8192)
                        _stderrTail.Remove(0, _stderrTail.Length - 8192);
                }
            }
        }
        catch
        {
        }
    }

    private void MarkUnavailable(string status)
    {
        lock (_gate)
        {
            _isAvailable = false;
            _statusText = status;
            _stdout = null;
            _stdin = null;
            if (_process is { HasExited: false })
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }

            _process?.Dispose();
            _process = null;
        }

        StatusChanged?.Invoke(StatusText);
    }

    private void SetStatus(string status)
    {
        lock (_gate)
            _statusText = status;

        StatusChanged?.Invoke(status);
    }

    private string ReadStderrTail()
    {
        lock (_gate)
            return _stderrTail.ToString().Trim();
    }

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
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
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

    private sealed record ClipRequest(string Op, int Id, string? Text, string? Path);
    private sealed record ClipResponse(int Id, bool Ok, float[]? Embedding, string? Error);
    private sealed record ClipBootstrapResponse(bool Ok, string? Device, string? Model, string? Pretrained, string? ModelKey, string? Error);
    private sealed record PythonRunResult(int ExitCode, string StdOut, string StdErr);
}

public sealed record ClipEmbeddingResult(float[]? Embedding, string? Error)
{
    public bool IsSuccess => Embedding is { Length: > 0 } && string.IsNullOrWhiteSpace(Error);
}
