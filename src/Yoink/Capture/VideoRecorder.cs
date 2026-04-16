using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Yoink.Services;

namespace Yoink.Capture;

/// <summary>
/// Captures screen frames and pipes them to FFmpeg for MP4/WebM encoding.
/// Same lifecycle as GifRecorder: Create, Start, Pause/Resume, Stop/Discard.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private const int DefaultInitialCaptureDelayMs = 0;
    private const double DurationValidationToleranceSeconds = 0.35d;
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
    private string? _micWavPath;
    private string? _desktopWavPath;
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

    public VideoRecorder(Rectangle region, Format format = Format.MP4, int fps = 30,
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

            _cachedFfmpegPath = ResolveFfmpegPath();
            _ffmpegPathResolved = true;
            return _cachedFfmpegPath;
        }
    }

    private static string? ResolveFfmpegPath()
    {
        // Check common locations
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "ffmpeg.exe"),
        };
        foreach (var p in candidates)
            if (File.Exists(p)) return p;

        // Check PATH
        try
        {
            var psi = new ProcessStartInfo("where", "ffmpeg.exe")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(3000);
            if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0].Trim()))
                return output.Split('\n')[0].Trim();
        }
        catch { }

        return null;
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
        if (_maxHeight > 0 && outH > _maxHeight)
        {
            double scale = (double)_maxHeight / outH;
            outW = (int)(outW * scale);
            outH = _maxHeight;
        }
        // Ensure even dimensions (required by H.264/VP9)
        outW = outW / 2 * 2;
        outH = outH / 2 * 2;

        string codecArgs = _format switch
        {
            Format.WebM => $"-c:v libvpx-vp9 -deadline good -cpu-used 2 -row-mt 1 -crf 30 -b:v 0 -pix_fmt yuv420p -vf scale={outW}:{outH}",
            Format.MKV => $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -vf scale={outW}:{outH}",
            _ => $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -vf scale={outW}:{outH} -movflags +faststart",
        };

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
        {
            try
            {
                _desktopWavPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputPath) + "_desktop.wav");
                if (string.IsNullOrEmpty(_desktopDeviceId))
                {
                    _desktopCapture = new WasapiLoopbackCapture();
                }
                else
                {
                    using var enumerator = new MMDeviceEnumerator();
                    _desktopCapture = new WasapiLoopbackCapture(enumerator.GetDevice(_desktopDeviceId));
                }
                _desktopWriter = new WaveFileWriter(_desktopWavPath, _desktopCapture.WaveFormat);
                _desktopCapture.DataAvailable += (s, e) =>
                {
                    try { _desktopWriter?.Write(e.Buffer, 0, e.BytesRecorded); } catch { }
                };
                _desktopCapture.StartRecording();
            }
            catch { _desktopCapture = null; _desktopWriter = null; _desktopWavPath = null; }
        }

        if (_recordMic)
        {
            try
            {
                _micWavPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputPath) + "_mic.wav");
                int micDevice = ResolveMicDeviceNumber(_micDeviceId);
                _micCapture = new WaveInEvent
                {
                    DeviceNumber = micDevice,
                    WaveFormat = new WaveFormat(44100, 16, 1)
                };
                _micWriter = new WaveFileWriter(_micWavPath, _micCapture.WaveFormat);
                _micCapture.DataAvailable += (s, e) =>
                {
                    try { _micWriter?.Write(e.Buffer, 0, e.BytesRecorded); } catch { }
                };
                _micCapture.StartRecording();
            }
            catch { _micCapture = null; _micWriter = null; _micWavPath = null; }
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
        try { _micWriter?.Dispose(); _micWriter = null; } catch { }
        try { _desktopWriter?.Dispose(); _desktopWriter = null; } catch { }
        try { _micCapture?.Dispose(); _micCapture = null; } catch { }
        try { _desktopCapture?.Dispose(); _desktopCapture = null; } catch { }
    }

    private void CapturePreviewFrame(ScreenCapture.RecordingFrameCapturer frameCapturer)
    {
        if (_firstFramePreview is not null)
            return;

        lock (_previewFrameLock)
        {
            if (_firstFramePreview is null)
                _firstFramePreview = frameCapturer.CloneCurrentFrame();
        }
    }

    private bool MuxAudio(string videoPath)
    {
        var tempAudioFiles = new[] { _desktopWavPath, _micWavPath }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Determine which audio files exist
        var audioFiles = new List<string>();
        if (_desktopWavPath != null && HasMeaningfulAudio(_desktopWavPath))
            audioFiles.Add(_desktopWavPath);
        if (_micWavPath != null && HasMeaningfulAudio(_micWavPath))
            audioFiles.Add(_micWavPath);

        if (audioFiles.Count == 0) return false;

        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null) return false;

        string dir = Path.GetDirectoryName(videoPath)!;
        string ext = Path.GetExtension(videoPath);
        string tempOut = Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + "_muxed" + ext);
        string audioCodec = ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "libopus" : "aac";
        double targetDurationSeconds = GetCapturedVideoDurationSeconds();

        try
        {
            string args = BuildMuxArguments(videoPath, audioFiles, tempOut, audioCodec, targetDurationSeconds);

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };
            proc.Start();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);

            if (HasNonEmptyFile(tempOut))
            {
                File.Delete(videoPath);
                File.Move(tempOut, videoPath);
                return true;
            }
            else
            {
                // Mux failed — keep the original video without audio
                try { File.Delete(tempOut); } catch { }
            }
        }
        catch
        {
            // Mux failed — keep the original video without audio
            try { File.Delete(tempOut); } catch { }
        }
        finally
        {
            // Clean up temp WAV files
            foreach (var f in tempAudioFiles)
                try { File.Delete(f); } catch { }
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
        string videoCodec = GetRepairVideoCodecArguments(_format);
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
        if (_micWavPath != null) try { File.Delete(_micWavPath); } catch { }
        if (_desktopWavPath != null) try { File.Delete(_desktopWavPath); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        lock (_pauseLock) { _isPaused = false; Monitor.PulseAll(_pauseLock); }
        try { _delayedAudioStartThread?.Join(3_000); } catch { }
        StopAudioCapture();
        try { _ffmpegBufferedStdin?.Dispose(); } catch { }
        try { _ffmpegStdin?.Dispose(); } catch { }
        try { _ffmpeg?.Dispose(); } catch { }
        lock (_previewFrameLock)
        {
            _firstFramePreview?.Dispose();
            _firstFramePreview = null;
        }
        _cts.Dispose();
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
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };
            proc.Start();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60_000);

            if (proc.ExitCode != 0 || !HasNonEmptyFile(tempOut))
            {
                AppDiagnostics.LogWarning(
                    "recording.duration-repair",
                    $"Repair failed for {Path.GetFileName(outputPath)}. FFmpeg exit={proc.ExitCode}. {stderr}");
                try { File.Delete(tempOut); } catch { }
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
            try { File.Delete(tempOut); } catch { }
        }
    }

    private static bool TryGetMediaDurationSeconds(string ffmpegPath, string mediaPath, out double durationSeconds)
    {
        durationSeconds = 0d;
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-hide_banner -i \"{mediaPath}\" -f null -",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };

            proc.Start();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30_000);
            return TryParseMediaDuration(stderr, out durationSeconds);
        }
        catch
        {
            return false;
        }
    }

    private static string GetRepairVideoCodecArguments(Format format) => format switch
    {
        Format.WebM => "-c:v libvpx-vp9 -deadline good -cpu-used 2 -row-mt 1 -crf 30 -b:v 0 -pix_fmt yuv420p",
        Format.MKV => "-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p",
        _ => "-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -movflags +faststart",
    };

    private static string GetRepairAudioCodec(Format format)
        => format == Format.WebM ? "libopus" : "aac";

    private void LogRecordingStats(string outputPath)
    {
        AppDiagnostics.LogInfo(
            "recording.stats",
            $"{Path.GetFileName(outputPath)} duration={GetCapturedVideoDurationSeconds():F3}s encodedFrames={FrameCount} capturedFrames={CapturedFrameCount} duplicatedFrames={DuplicatedFrameCount} droppedFrames={DroppedFrameCount}");
    }

    private sealed class LimitedTextBuffer(int maxChars)
    {
        private readonly int _maxChars = Math.Max(256, maxChars);
        private readonly System.Text.StringBuilder _sb = new();

        public void AppendLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (_sb.Length > 0) _sb.AppendLine();
            _sb.Append(line);

            if (_sb.Length <= _maxChars) return;
            int remove = _sb.Length - _maxChars;
            _sb.Remove(0, remove);
        }

        public override string ToString() => _sb.ToString();
    }
}
