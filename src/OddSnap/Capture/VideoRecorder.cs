using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OddSnap.Helpers;
using OddSnap.Services;

namespace OddSnap.Capture;

/// <summary>
/// Captures screen frames and pipes them to FFmpeg for MP4/WebM encoding.
/// Same lifecycle as GifRecorder: Create, Start, Pause/Resume, Stop/Discard.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private const int DefaultInitialCaptureDelayMs = 0;
    private const double DurationValidationToleranceSeconds = 0.35d;
    private const int MaxPreviewLongEdge = 640;
    private const int H264Crf = 15;
    private const int Vp9Crf = 22;
    private const string H264ScreenRecordingArgs = "-c:v libx264 -preset faster -profile:v high -crf {0} -pix_fmt yuv420p -g {1} -threads 0";
    private const string Vp9ScreenRecordingArgs = "-c:v libvpx-vp9 -deadline realtime -cpu-used 4 -row-mt 1 -threads 0 -crf {0} -b:v 0 -pix_fmt yuv420p -g {1}";
    private const string HighQualityScaleFlags = "lanczos+accurate_rnd+full_chroma_int";
    public enum Format { MP4, WebM, MKV }
    private static readonly object FfmpegPathLock = new();
    private static string? _cachedFfmpegPath;
    private static bool _ffmpegPathResolved;

    private readonly Rectangle _region;
    private readonly int _fps;
    private readonly int _maxDurationMs;
    private readonly Format _format;
    private readonly int _maxHeight; // 0 = original
    private readonly bool _showCursor;
    private readonly bool _recordMic;
    private readonly string? _micDeviceId;
    private readonly bool _recordDesktop;
    private readonly string? _desktopDeviceId;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _previewFrameLock = new();

    private Thread? _captureThread;
    private Process? _ffmpeg;
    private Stream? _ffmpegStdin;
    private BufferedStream? _ffmpegBufferedStdin;
    private LimitedTextBuffer? _ffmpegStderr;
    private int _frameCount;
    private int _capturedFrameCount;
    private int _duplicatedFrameCount;
    private int _droppedFrameCount;
    private DateTime _startTime;
    private TimeSpan _recordedDuration = TimeSpan.Zero;
    private bool _isPaused;
    private bool _disposed;
    private readonly object _pauseLock = new();
    private int _initialCaptureDelayMs = DefaultInitialCaptureDelayMs;
    private Thread? _delayedAudioStartThread;

    // Audio capture
    private WaveInEvent? _micCapture;
    private WasapiLoopbackCapture? _desktopCapture;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _desktopWriter;
    private EventHandler<WaveInEventArgs>? _micDataAvailableHandler;
    private EventHandler<WaveInEventArgs>? _desktopDataAvailableHandler;
    private string? _micWavPath;
    private string? _desktopWavPath;
    private int _micWriteFailureLogged;
    private int _desktopWriteFailureLogged;
    private Bitmap? _firstFramePreview;

    public int FrameCount => _frameCount;
    public int CapturedFrameCount => _capturedFrameCount;
    public int DuplicatedFrameCount => _duplicatedFrameCount;
    public int DroppedFrameCount => _droppedFrameCount;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public bool IsRecording => _captureThread?.IsAlive == true;
    public bool IsPaused => _isPaused;

    public Bitmap? GetFirstFrame()
    {
        lock (_previewFrameLock)
            return _firstFramePreview is null ? null : new Bitmap(_firstFramePreview);
    }

    public VideoRecorder(Rectangle region, Format format = Format.MP4, int fps = 60,
                         int maxDurationSeconds = 300, int maxHeight = 0,
                         bool showCursor = false,
                         bool recordMic = false, string? micDeviceId = null,
                         bool recordDesktop = false, string? desktopDeviceId = null)
    {
        _region = region;
        _format = format;
        _fps = Math.Clamp(fps, 5, 60);
        _maxDurationMs = maxDurationSeconds * 1000;
        _maxHeight = maxHeight;
        _showCursor = showCursor;
        _recordMic = recordMic;
        _micDeviceId = micDeviceId;
        _recordDesktop = recordDesktop;
        _desktopDeviceId = desktopDeviceId;
    }

    public static string? FindFfmpeg()
    {
        lock (FfmpegPathLock)
        {
            if (_ffmpegPathResolved)
                return _cachedFfmpegPath;

            var resolved = ResolveFfmpegPath();
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                _cachedFfmpegPath = resolved;
                _ffmpegPathResolved = true;
            }

            return _cachedFfmpegPath;
        }
    }

    private static string? ResolveFfmpegPath()
    {
        foreach (var candidate in GetFfmpegCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Check PATH
        try
        {
            var result = RunProcessWithLimitedOutput("where", "ffmpeg.exe", timeoutMs: 3_000, captureStdOut: true);
            var output = result.StdOut.Trim();
            if (!result.TimedOut &&
                result.ExitCode == 0 &&
                !string.IsNullOrEmpty(output) &&
                File.Exists(output.Split('\n')[0].Trim()))
            {
                return output.Split('\n')[0].Trim();
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<string> GetFfmpegCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OddSnap", "ffmpeg.exe");

        foreach (var pathEntry in GetPathEntries())
            yield return Path.Combine(pathEntry, "ffmpeg.exe");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            foreach (var ffmpeg in FindFfmpegInDirectoryTree(wingetPackages))
                yield return ffmpeg;

            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            yield return Path.Combine(localAppData, "Programs", "FFmpeg", "bin", "ffmpeg.exe");
            yield return Path.Combine(localAppData, "scoop", "shims", "ffmpeg.exe");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe");

        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
            yield return Path.Combine(chocolateyInstall, "bin", "ffmpeg.exe");
    }

    private static IEnumerable<string> GetPathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(entry))
                yield return entry;
        }
    }

    private static IEnumerable<string> FindFfmpegInDirectoryTree(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(root, "ffmpeg.exe", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var match in matches)
            yield return match;
    }

    public void Start(string outputPath, int initialCaptureDelayMs = DefaultInitialCaptureDelayMs)
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
            throw new FileNotFoundException("FFmpeg not found. Place ffmpeg.exe in the app folder or install it to PATH.");

        _initialCaptureDelayMs = Math.Max(0, initialCaptureDelayMs);
        _startTime = DateTime.UtcNow;

        // Compute output dimensions
        int outW = _region.Width;
        int outH = _region.Height;
        bool scaledRecording = false;
        if (_maxHeight > 0 && outH > _maxHeight)
        {
            double scale = (double)_maxHeight / outH;
            outW = (int)(outW * scale);
            outH = _maxHeight;
            scaledRecording = true;
            // Ensure even dimensions for yuv420p encoders after intentional scaling.
            outW = Math.Max(2, outW / 2 * 2);
            outH = Math.Max(2, outH / 2 * 2);
        }

        string codecArgs = BuildVideoCodecArguments(_format, _fps, _region.Width, _region.Height, outW, outH, scaledRecording);

        var args = $"-y -f rawvideo -pix_fmt bgra -s {_region.Width}x{_region.Height} -r {_fps} -i pipe:0 {codecArgs} \"{outputPath}\"";

        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            }
        };
        _ffmpegStderr = new LimitedTextBuffer(32_768);
        _ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _ffmpegStderr?.AppendLine(e.Data);
        };
        _ffmpeg.Start();
        _ffmpeg.BeginErrorReadLine();
        _ffmpegStdin = _ffmpeg.StandardInput.BaseStream;
        _ffmpegBufferedStdin = new BufferedStream(_ffmpegStdin, 1 << 20);

        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "VideoCapture" };
        _captureThread.Start();

        StartAudioCaptureWithDelay(outputPath);
    }

    private void StartAudioCaptureWithDelay(string outputPath)
    {
        if (!_recordDesktop && !_recordMic)
            return;

        _delayedAudioStartThread = new Thread(() =>
        {
            if (_initialCaptureDelayMs > 0)
            {
                try { Thread.Sleep(_initialCaptureDelayMs); }
                catch (ThreadInterruptedException) { return; }
            }

            if (_cts.IsCancellationRequested)
                return;

            StartAudioCapture(outputPath);
        })
        {
            IsBackground = true,
            Name = "VideoAudioStart"
        };
        _delayedAudioStartThread.Start();
    }

    private void StartAudioCapture(string outputPath)
    {
        string dir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();

        if (_recordDesktop)
            StartDesktopAudioCapture(dir, outputPath);

        if (_recordMic)
            StartMicrophoneAudioCapture(dir, outputPath);
    }

    private void StartDesktopAudioCapture(string dir, string outputPath)
    {
        string wavPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputPath) + "_desktop.wav");
        WasapiLoopbackCapture? capture = null;
        WaveFileWriter? writer = null;
        EventHandler<WaveInEventArgs>? dataAvailableHandler = null;
        bool started = false;

        try
        {
            if (string.IsNullOrEmpty(_desktopDeviceId))
            {
                capture = new WasapiLoopbackCapture();
            }
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                capture = new WasapiLoopbackCapture(enumerator.GetDevice(_desktopDeviceId));
            }

            writer = new WaveFileWriter(wavPath, capture.WaveFormat);
            dataAvailableHandler = (_, e) =>
            {
                try
                {
                    writer?.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    LogAudioWriteFailureOnce(
                        ref _desktopWriteFailureLogged,
                        "Desktop audio could not be written. The recording may not contain desktop audio.",
                        ex);
                }
            };
            capture.DataAvailable += dataAvailableHandler;
            capture.StartRecording();

            _desktopWavPath = wavPath;
            _desktopCapture = capture;
            _desktopWriter = writer;
            _desktopDataAvailableHandler = dataAvailableHandler;
            started = true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "recording.audio-start",
                $"Desktop audio capture did not start: {ex.Message}",
                ex);
        }
        finally
        {
            if (!started)
            {
                if (capture is not null && dataAvailableHandler is not null)
                {
                    try { capture.DataAvailable -= dataAvailableHandler; } catch { }
                }

                try { writer?.Dispose(); } catch { }
                try { capture?.Dispose(); } catch { }
                TryDeleteRecordingTempFile(wavPath, "failed desktop audio startup");
            }
        }
    }

    private void StartMicrophoneAudioCapture(string dir, string outputPath)
    {
        string wavPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputPath) + "_mic.wav");
        WaveInEvent? capture = null;
        WaveFileWriter? writer = null;
        EventHandler<WaveInEventArgs>? dataAvailableHandler = null;
        bool started = false;

        try
        {
            int micDevice = ResolveMicDeviceNumber(_micDeviceId);
            capture = new WaveInEvent
            {
                DeviceNumber = micDevice,
                WaveFormat = new WaveFormat(44100, 16, 1)
            };
            writer = new WaveFileWriter(wavPath, capture.WaveFormat);
            dataAvailableHandler = (_, e) =>
            {
                try
                {
                    writer?.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    LogAudioWriteFailureOnce(
                        ref _micWriteFailureLogged,
                        "Microphone audio could not be written. The recording may not contain microphone audio.",
                        ex);
                }
            };
            capture.DataAvailable += dataAvailableHandler;
            capture.StartRecording();

            _micWavPath = wavPath;
            _micCapture = capture;
            _micWriter = writer;
            _micDataAvailableHandler = dataAvailableHandler;
            started = true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "recording.audio-start",
                $"Microphone audio capture did not start: {ex.Message}",
                ex);
        }
        finally
        {
            if (!started)
            {
                if (capture is not null && dataAvailableHandler is not null)
                {
                    try { capture.DataAvailable -= dataAvailableHandler; } catch { }
                }

                try { writer?.Dispose(); } catch { }
                try { capture?.Dispose(); } catch { }
                TryDeleteRecordingTempFile(wavPath, "failed microphone audio startup");
            }
        }
    }

    private static int ResolveMicDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return 0;
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (caps.ProductName.Contains(deviceId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private static void LogAudioWriteFailureOnce(ref int failureLogged, string message, Exception exception)
    {
        if (Interlocked.Exchange(ref failureLogged, 1) == 0)
            AppDiagnostics.LogWarning("recording.audio-write", message, exception);
    }

    public void Pause()
    {
        lock (_pauseLock) _isPaused = true;
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            _isPaused = false;
            Monitor.PulseAll(_pauseLock);
        }
    }

    private void CaptureLoop()
    {
        var ct = _cts.Token;
        using var frameCapturer = ScreenCapture.CreateRecordingFrameCapturer(_region, _showCursor);
        byte[]? captureBuffer = null;
        byte[]? lastFrameBuffer = null;
        int lastFrameByteCount = 0;
        double frameIntervalTicks = (double)Stopwatch.Frequency / _fps;

        if (_initialCaptureDelayMs > 0)
        {
            try { Thread.Sleep(_initialCaptureDelayMs); }
            catch (ThreadInterruptedException) { return; }
        }

        long activeStartTicks = Stopwatch.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            var activeElapsed = Stopwatch.GetElapsedTime(activeStartTicks);
            if (activeElapsed.TotalMilliseconds >= _maxDurationMs)
                break;

            // Pause support
            lock (_pauseLock)
            {
                while (_isPaused && !ct.IsCancellationRequested)
                    Monitor.Wait(_pauseLock, 100);
            }
            if (ct.IsCancellationRequested) break;

            WaitForNextFrameSlot(activeStartTicks, frameIntervalTicks, ct);
            if (ct.IsCancellationRequested)
                break;

            bool capturedFrame = false;
            try
            {
                captureBuffer = frameCapturer.CaptureToBuffer(captureBuffer);
                int byteCount = captureBuffer.Length;
                if (lastFrameBuffer == null || lastFrameBuffer.Length != byteCount)
                    lastFrameBuffer = new byte[byteCount];

                WriteFrame(captureBuffer, byteCount);
                Buffer.BlockCopy(captureBuffer, 0, lastFrameBuffer, 0, byteCount);
                lastFrameByteCount = byteCount;
                capturedFrame = true;

                CapturePreviewFrame(frameCapturer);
                Interlocked.Increment(ref _capturedFrameCount);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                Interlocked.Increment(ref _droppedFrameCount);
            }

            if (!capturedFrame && lastFrameBuffer == null)
                continue;

            int targetFrameCount = GetExpectedFrameCount(Stopwatch.GetElapsedTime(activeStartTicks), _fps);
            DuplicateLastFrameUntil(lastFrameBuffer, lastFrameByteCount, targetFrameCount);
        }

        _recordedDuration = Stopwatch.GetElapsedTime(activeStartTicks);
        if (lastFrameBuffer != null && lastFrameByteCount > 0)
        {
            int targetFrameCount = GetExpectedFrameCount(_recordedDuration, _fps);
            DuplicateLastFrameUntil(lastFrameBuffer, lastFrameByteCount, targetFrameCount);
        }
    }

    private void WaitForNextFrameSlot(long activeStartTicks, double frameIntervalTicks, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long nextDueTicks = activeStartTicks + (long)Math.Round(_frameCount * frameIntervalTicks);
            long nowTicks = Stopwatch.GetTimestamp();
            long remainingTicks = nextDueTicks - nowTicks;
            if (remainingTicks <= 0)
                break;

            int sleepMs = (int)Math.Min(20, remainingTicks * 1000 / Stopwatch.Frequency);
            if (sleepMs <= 1)
            {
                Thread.Yield();
                continue;
            }

            try { Thread.Sleep(sleepMs); }
            catch (ThreadInterruptedException) { break; }
        }
    }

    /// <summary>Stops recording and waits for FFmpeg to finish encoding.</summary>
    public string StopAndEncode(string outputPath)
    {
        _cts.Cancel();
        try { _delayedAudioStartThread?.Join(5_000); } catch { }
        // Unpause if paused so capture thread can exit
        lock (_pauseLock) { _isPaused = false; Monitor.PulseAll(_pauseLock); }
        _captureThread?.Join(10_000);

        // Stop audio capture
        StopAudioCapture();

        // Close stdin to signal EOF to FFmpeg
        try { _ffmpegBufferedStdin?.Flush(); } catch { }
        try { _ffmpegStdin?.Close(); } catch { }

        if (_ffmpeg == null)
            throw new InvalidOperationException("Video encoder not initialized.");

        if (!_ffmpeg.WaitForExit(30_000))
        {
            try { _ffmpeg.Kill(entireProcessTree: true); } catch { }
            try { _ffmpeg.WaitForExit(2_000); } catch { }
            throw new TimeoutException($"Video encoding timed out. {_ffmpegStderr}");
        }

        try { _ffmpeg.WaitForExit(500); } catch { } // allow async stderr flush

        if (_ffmpeg.ExitCode != 0)
            throw new InvalidOperationException($"Video encoding failed (exit code {_ffmpeg.ExitCode}). {_ffmpegStderr}");

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException($"Video encoding failed — no output file produced. {_ffmpegStderr}");

        // Mux audio if we captured any
        bool hasAudioTrack = MuxAudio(outputPath);
        ValidateAndRepairOutput(outputPath, hasAudioTrack);
        LogRecordingStats(outputPath);

        return outputPath;
    }

    private void StopAudioCapture()
    {
        StopCaptureAndWait(_micCapture);
        StopCaptureAndWait(_desktopCapture);
        DetachAudioDataHandlers();
        try { _micWriter?.Dispose(); _micWriter = null; } catch { }
        try { _desktopWriter?.Dispose(); _desktopWriter = null; } catch { }
        try { _micCapture?.Dispose(); _micCapture = null; } catch { }
        try { _desktopCapture?.Dispose(); _desktopCapture = null; } catch { }
    }

    private void DetachAudioDataHandlers()
    {
        if (_micCapture is not null && _micDataAvailableHandler is not null)
        {
            try { _micCapture.DataAvailable -= _micDataAvailableHandler; } catch { }
            _micDataAvailableHandler = null;
        }

        if (_desktopCapture is not null && _desktopDataAvailableHandler is not null)
        {
            try { _desktopCapture.DataAvailable -= _desktopDataAvailableHandler; } catch { }
            _desktopDataAvailableHandler = null;
        }
    }

    private void CapturePreviewFrame(ScreenCapture.RecordingFrameCapturer frameCapturer)
    {
        if (_firstFramePreview is not null)
            return;

        lock (_previewFrameLock)
        {
            if (_firstFramePreview is null)
            {
                using var frame = frameCapturer.CloneCurrentFrame();
                _firstFramePreview = CaptureOutputService.PrepareBitmap(frame, MaxPreviewLongEdge);
            }
        }
    }

    private bool MuxAudio(string videoPath)
    {
        var tempAudioFiles = new[] { _desktopWavPath, _micWavPath }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string? tempOut = null;

        try
        {
            // Determine which audio files exist
            var audioFiles = new List<string>();
            if (_desktopWavPath != null && HasMeaningfulAudio(_desktopWavPath))
                audioFiles.Add(_desktopWavPath);
            if (_micWavPath != null && HasMeaningfulAudio(_micWavPath))
                audioFiles.Add(_micWavPath);

            if (audioFiles.Count == 0)
                return false;

            var ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null)
            {
                AppDiagnostics.LogWarning(
                    "recording.mux-audio",
                    $"Audio mux skipped for {Path.GetFileName(videoPath)} because FFmpeg was not found.");
                return false;
            }

            string dir = Path.GetDirectoryName(videoPath)!;
            string ext = Path.GetExtension(videoPath);
            tempOut = Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + "_muxed" + ext);
            string audioCodec = ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "libopus" : "aac";
            double targetDurationSeconds = GetCapturedVideoDurationSeconds();

            string args = BuildMuxArguments(videoPath, audioFiles, tempOut, audioCodec, targetDurationSeconds);
            var result = RunProcessWithLimitedOutput(ffmpegPath, args, timeoutMs: 30_000);

            if (!result.TimedOut && result.ExitCode == 0 && HasNonEmptyFile(tempOut))
            {
                File.Delete(videoPath);
                File.Move(tempOut, videoPath);
                return true;
            }
            else
            {
                // Mux failed — keep the original video without audio
                AppDiagnostics.LogWarning(
                    "recording.mux-audio",
                    result.TimedOut
                        ? $"Audio mux timed out for {Path.GetFileName(videoPath)}."
                        : $"Audio mux failed for {Path.GetFileName(videoPath)}. FFmpeg exit={result.ExitCode}. {result.StdErr}");
                TryDeleteRecordingTempFile(tempOut, "failed mux output");
            }
        }
        catch
        {
            // Mux failed — keep the original video without audio
            TryDeleteRecordingTempFile(tempOut, "mux exception output");
        }
        finally
        {
            // Clean up temp WAV files
            foreach (var f in tempAudioFiles)
                TryDeleteRecordingTempFile(f, "audio capture");
        }

        return false;
    }

    private static void StopCaptureAndWait(IWaveIn? capture, int timeoutMs = 5_000)
    {
        if (capture == null)
            return;

        using var stopped = new ManualResetEventSlim(false);
        EventHandler<StoppedEventArgs>? handler = (_, _) => stopped.Set();
        capture.RecordingStopped += handler;
        try
        {
            try { capture.StopRecording(); }
            catch { stopped.Set(); }

            try { stopped.Wait(timeoutMs); } catch { }
        }
        finally
        {
            try { capture.RecordingStopped -= handler; } catch { }
        }
    }

    private double GetCapturedVideoDurationSeconds()
    {
        double elapsedDuration = _recordedDuration.TotalSeconds;
        if (elapsedDuration > 0)
            return elapsedDuration;

        double frameDuration = _fps > 0 ? FrameCount / (double)_fps : 0d;
        return Math.Max(0.1d, frameDuration);
    }

    internal static string BuildMuxArguments(
        string videoPath,
        IReadOnlyList<string> audioFiles,
        string tempOut,
        string audioCodec,
        double targetDurationSeconds)
    {
        string duration = targetDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        string muxerArgs = Path.GetExtension(tempOut).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            ? " -movflags +faststart"
            : "";

        if (audioFiles.Count == 1)
        {
            return $"-y -i \"{videoPath}\" -i \"{audioFiles[0]}\" " +
                   $"-filter_complex \"[1:a]apad,atrim=0:{duration}[a]\" " +
                   $"-c:v copy -c:a {audioCodec} -map 0:v -map \"[a]\"{muxerArgs} \"{tempOut}\"";
        }

        return $"-y -i \"{videoPath}\" -i \"{audioFiles[0]}\" -i \"{audioFiles[1]}\" " +
               $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest:dropout_transition=0,apad,atrim=0:{duration}[a]\" " +
               $"-c:v copy -c:a {audioCodec} -map 0:v -map \"[a]\"{muxerArgs} \"{tempOut}\"";
    }

    internal static int GetExpectedFrameCount(TimeSpan elapsed, int fps)
    {
        int clampedFps = Math.Clamp(fps, 1, 240);
        if (elapsed <= TimeSpan.Zero)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds * clampedFps));
    }

    internal static bool TryParseMediaDuration(string ffmpegOutput, out double durationSeconds)
    {
        durationSeconds = 0d;
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
            return false;

        const string marker = "Duration:";
        int markerIndex = ffmpegOutput.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        int valueStart = markerIndex + marker.Length;
        int valueEnd = ffmpegOutput.IndexOf(',', valueStart);
        string raw = valueEnd > valueStart
            ? ffmpegOutput[valueStart..valueEnd]
            : ffmpegOutput[valueStart..];

        if (!TimeSpan.TryParse(raw.Trim(), CultureInfo.InvariantCulture, out var duration))
            return false;

        durationSeconds = duration.TotalSeconds;
        return durationSeconds > 0;
    }

    internal string BuildRepairArguments(string videoPath, string tempOut, double actualDurationSeconds, bool hasAudioTrack)
    {
        string expectedDuration = GetCapturedVideoDurationSeconds().ToString("0.###", CultureInfo.InvariantCulture);
        string padDuration = Math.Max(0d, GetCapturedVideoDurationSeconds() - actualDurationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        string videoCodec = GetRepairVideoCodecArguments(_format, _fps);
        string audioCodec = GetRepairAudioCodec(_format);

        if (!hasAudioTrack)
        {
            return $"-y -i \"{videoPath}\" -vf \"tpad=stop_mode=clone:stop_duration={padDuration},trim=duration={expectedDuration}\" {videoCodec} \"{tempOut}\"";
        }

        return $"-y -i \"{videoPath}\" " +
               $"-filter_complex \"[0:v]tpad=stop_mode=clone:stop_duration={padDuration},trim=duration={expectedDuration}[v];[0:a]apad,atrim=0:{expectedDuration}[a]\" " +
               $"-map \"[v]\" -map \"[a]\" {videoCodec} -c:a {audioCodec} \"{tempOut}\"";
    }

    /// <summary>Cancels recording without saving.</summary>
    public void Discard()
    {
        _cts.Cancel();
        try { _delayedAudioStartThread?.Join(3_000); } catch { }
        lock (_pauseLock) { _isPaused = false; Monitor.PulseAll(_pauseLock); }
        _captureThread?.Join(3000);
        StopAudioCapture();
        try { _ffmpegStdin?.Close(); } catch { }
        try { _ffmpeg?.Kill(); } catch { }
        // Clean up temp WAV files
        TryDeleteRecordingTempFile(_micWavPath, "discarded mic audio");
        TryDeleteRecordingTempFile(_desktopWavPath, "discarded desktop audio");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        lock (_pauseLock) { _isPaused = false; Monitor.PulseAll(_pauseLock); }
        try { _delayedAudioStartThread?.Join(3_000); } catch { }
        JoinThreadIfNotCurrent(_captureThread, 3_000);
        StopAudioCapture();
        TryDeleteRecordingTempFile(_micWavPath, "disposed mic audio");
        TryDeleteRecordingTempFile(_desktopWavPath, "disposed desktop audio");
        try { _ffmpegBufferedStdin?.Dispose(); } catch { }
        try { _ffmpegStdin?.Dispose(); } catch { }
        try
        {
            if (_ffmpeg is { HasExited: false })
                _ffmpeg.Kill(entireProcessTree: true);
        }
        catch { }
        try { _ffmpeg?.Dispose(); } catch { }
        lock (_previewFrameLock)
        {
            _firstFramePreview?.Dispose();
            _firstFramePreview = null;
        }
        _cts.Dispose();
    }

    private static void JoinThreadIfNotCurrent(Thread? thread, int timeoutMs)
    {
        if (thread is null || !thread.IsAlive || ReferenceEquals(thread, Thread.CurrentThread))
            return;

        try { thread.Join(timeoutMs); } catch { }
    }

    private static bool HasMeaningfulAudio(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 44; }
        catch { return false; }
    }

    private static bool HasNonEmptyFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    private void WriteFrame(byte[] frame, int byteCount)
    {
        _ffmpegBufferedStdin?.Write(frame, 0, byteCount);
        Interlocked.Increment(ref _frameCount);
    }

    private void DuplicateLastFrameUntil(byte[]? lastFrameBuffer, int byteCount, int targetFrameCount)
    {
        if (lastFrameBuffer == null || byteCount <= 0)
            return;

        while (_frameCount < targetFrameCount)
        {
            WriteFrame(lastFrameBuffer, byteCount);
            Interlocked.Increment(ref _duplicatedFrameCount);
        }
    }

    private void ValidateAndRepairOutput(string outputPath, bool hasAudioTrack)
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null)
            return;

        double expectedDuration = GetCapturedVideoDurationSeconds();
        if (expectedDuration <= 0.1d)
            return;

        if (!TryGetMediaDurationSeconds(ffmpegPath, outputPath, out double actualDuration))
            return;

        if (Math.Abs(actualDuration - expectedDuration) <= DurationValidationToleranceSeconds)
            return;

        AppDiagnostics.LogWarning(
            "recording.duration-mismatch",
            $"Expected about {expectedDuration:F3}s but encoded {actualDuration:F3}s for {Path.GetFileName(outputPath)}. Attempting repair.");

        string tempOut = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            Path.GetFileNameWithoutExtension(outputPath) + "_repaired" + Path.GetExtension(outputPath));

        try
        {
            string args = BuildRepairArguments(outputPath, tempOut, actualDuration, hasAudioTrack);
            var result = RunProcessWithLimitedOutput(ffmpegPath, args, timeoutMs: 60_000);

            if (result.TimedOut || result.ExitCode != 0 || !HasNonEmptyFile(tempOut))
            {
                AppDiagnostics.LogWarning(
                    "recording.duration-repair",
                    result.TimedOut
                        ? $"Repair timed out for {Path.GetFileName(outputPath)}."
                        : $"Repair failed for {Path.GetFileName(outputPath)}. FFmpeg exit={result.ExitCode}. {result.StdErr}");
                TryDeleteRecordingTempFile(tempOut, "failed repair output");
                return;
            }

            File.Delete(outputPath);
            File.Move(tempOut, outputPath);

            if (TryGetMediaDurationSeconds(ffmpegPath, outputPath, out double repairedDuration))
            {
                AppDiagnostics.LogInfo(
                    "recording.duration-repair",
                    $"Repaired {Path.GetFileName(outputPath)} from {actualDuration:F3}s to {repairedDuration:F3}s.");
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("recording.duration-repair", ex);
            TryDeleteRecordingTempFile(tempOut, "repair exception output");
        }
    }

    private static void TryDeleteRecordingTempFile(string? path, string context)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning(
                "recording.temp-cleanup",
                $"Failed to delete {context} temporary file {Path.GetFileName(path)}: {ex.Message}",
                ex);
        }
    }

    private static bool TryGetMediaDurationSeconds(string ffmpegPath, string mediaPath, out double durationSeconds)
    {
        durationSeconds = 0d;
        try
        {
            var result = RunProcessWithLimitedOutput(ffmpegPath, $"-hide_banner -i \"{mediaPath}\"", timeoutMs: 30_000);
            return !result.TimedOut && TryParseMediaDuration(result.StdErr, out durationSeconds);
        }
        catch
        {
            return false;
        }
    }

    private static string GetRepairVideoCodecArguments(Format format, int fps) => format switch
    {
        Format.WebM => BuildVp9ScreenRecordingArguments(fps),
        Format.MKV => BuildH264ScreenRecordingArguments(fps),
        _ => $"{BuildH264ScreenRecordingArguments(fps)} -movflags +faststart",
    };

    internal static string BuildVideoCodecArguments(Format format, int fps, int inputWidth, int inputHeight, int outputWidth, int outputHeight, bool scaleToOutput)
    {
        string filterArgs = BuildVideoFilterArguments(inputWidth, inputHeight, outputWidth, outputHeight, scaleToOutput);

        return format switch
        {
            Format.WebM => $"{BuildVp9ScreenRecordingArguments(fps)}{filterArgs}",
            Format.MKV => $"{BuildH264ScreenRecordingArguments(fps)}{filterArgs}",
            _ => $"{BuildH264ScreenRecordingArguments(fps)}{filterArgs} -movflags +faststart",
        };
    }

    private static string BuildVideoFilterArguments(int inputWidth, int inputHeight, int outputWidth, int outputHeight, bool scaleToOutput)
    {
        var filters = new List<string>();
        int filteredWidth = inputWidth;
        int filteredHeight = inputHeight;

        if (scaleToOutput && outputWidth > 0 && outputHeight > 0)
        {
            filters.Add($"scale={outputWidth}:{outputHeight}:flags={HighQualityScaleFlags}");
            filteredWidth = outputWidth;
            filteredHeight = outputHeight;
        }

        if (filteredWidth % 2 != 0 || filteredHeight % 2 != 0)
            filters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");

        return filters.Count == 0
            ? string.Empty
            : $" -vf \"{string.Join(",", filters)}\"";
    }

    private static string BuildH264ScreenRecordingArguments(int fps)
        => string.Format(
            CultureInfo.InvariantCulture,
            H264ScreenRecordingArgs,
            H264Crf,
            GetKeyframeInterval(fps));

    private static string BuildVp9ScreenRecordingArguments(int fps)
        => string.Format(
            CultureInfo.InvariantCulture,
            Vp9ScreenRecordingArgs,
            Vp9Crf,
            GetKeyframeInterval(fps));

    private static int GetKeyframeInterval(int fps)
        => Math.Clamp(fps, 1, 240) * 2;

    private static string GetRepairAudioCodec(Format format)
        => format == Format.WebM ? "libopus" : "aac";

    private void LogRecordingStats(string outputPath)
    {
        AppDiagnostics.LogInfo(
            "recording.stats",
            $"{Path.GetFileName(outputPath)} duration={GetCapturedVideoDurationSeconds():F3}s encodedFrames={FrameCount} capturedFrames={CapturedFrameCount} duplicatedFrames={DuplicatedFrameCount} droppedFrames={DroppedFrameCount}");
    }

    private static ProcessCaptureResult RunProcessWithLimitedOutput(string fileName, string arguments, int timeoutMs, bool captureStdOut = false)
    {
        using var errorMode = WindowsErrorModeScope.SuppressSystemDialogs();
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = captureStdOut,
                RedirectStandardError = true,
            }
        };

        var stdout = captureStdOut ? new LimitedTextBuffer(32_768) : null;
        var stderr = new LimitedTextBuffer(32_768);
        if (captureStdOut)
        {
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    stdout?.AppendLine(e.Data);
            };
        }

        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderr.AppendLine(e.Data);
        };

        proc.Start();
        if (captureStdOut)
            proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { proc.WaitForExit(2_000); } catch { }
            return new ProcessCaptureResult(-1, stdout?.ToString() ?? "", stderr.ToString(), TimedOut: true);
        }

        try { proc.WaitForExit(500); } catch { }
        return new ProcessCaptureResult(proc.ExitCode, stdout?.ToString() ?? "", stderr.ToString(), TimedOut: false);
    }

    private sealed record ProcessCaptureResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

}
