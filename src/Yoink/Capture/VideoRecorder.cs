using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Yoink.Capture;

/// <summary>
/// Captures screen frames and pipes them to FFmpeg for MP4/WebM encoding.
/// Same lifecycle as GifRecorder: Create, Start, Pause/Resume, Stop/Discard.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private const int DefaultInitialCaptureDelayMs = 0;
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

    private Thread? _captureThread;
    private Process? _ffmpeg;
    private Stream? _ffmpegStdin;
    private BufferedStream? _ffmpegBufferedStdin;
    private LimitedTextBuffer? _ffmpegStderr;
    private int _frameCount;
    private DateTime _startTime;
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

    public int FrameCount => _frameCount;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public bool IsRecording => _captureThread?.IsAlive == true;
    public bool IsPaused => _isPaused;

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
            Format.WebM => $"-c:v libvpx-vp9 -crf 30 -b:v 0 -pix_fmt yuv420p -vf scale={outW}:{outH}",
            Format.MKV => $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -vf scale={outW}:{outH}",
            _ => $"-c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -vf scale={outW}:{outH}",
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
                _desktopCapture = string.IsNullOrEmpty(_desktopDeviceId)
                    ? new WasapiLoopbackCapture()
                    : new WasapiLoopbackCapture(new MMDeviceEnumerator().GetDevice(_desktopDeviceId));
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
        int delayMs = 1000 / _fps;
        var ct = _cts.Token;
        byte[]? buffer = null;

        if (_initialCaptureDelayMs > 0)
        {
            try { Thread.Sleep(_initialCaptureDelayMs); }
            catch (ThreadInterruptedException) { return; }
        }

        while (!ct.IsCancellationRequested)
        {
            if ((DateTime.UtcNow - _startTime).TotalMilliseconds >= _maxDurationMs)
                break;

            // Pause support
            lock (_pauseLock)
            {
                while (_isPaused && !ct.IsCancellationRequested)
                    Monitor.Wait(_pauseLock, 100);
            }
            if (ct.IsCancellationRequested) break;

            var sw = Stopwatch.StartNew();
            try
            {
                using var bmp = ScreenCapture.CaptureRegionForRecording(_region, _showCursor);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int byteCount = data.Stride * data.Height;
                    if (buffer == null || buffer.Length != byteCount)
                        buffer = new byte[byteCount];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, byteCount);
                    _ffmpegBufferedStdin?.Write(buffer, 0, byteCount);
                }
                finally { bmp.UnlockBits(data); }

                Interlocked.Increment(ref _frameCount);
            }
            catch (OperationCanceledException) { break; }
            catch { /* skip frame */ }

            int sleep = delayMs - (int)sw.ElapsedMilliseconds;
            if (sleep > 0)
            {
                try { Thread.Sleep(sleep); }
                catch (ThreadInterruptedException) { break; }
            }
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
        MuxAudio(outputPath);

        return outputPath;
    }

    private void StopAudioCapture()
    {
        try { _micCapture?.StopRecording(); } catch { }
        try { _desktopCapture?.StopRecording(); } catch { }
        try { _micWriter?.Dispose(); _micWriter = null; } catch { }
        try { _desktopWriter?.Dispose(); _desktopWriter = null; } catch { }
        try { _micCapture?.Dispose(); _micCapture = null; } catch { }
        try { _desktopCapture?.Dispose(); _desktopCapture = null; } catch { }
    }

    private void MuxAudio(string videoPath)
    {
        // Determine which audio files exist
        var audioFiles = new List<string>();
        if (_desktopWavPath != null && HasMeaningfulAudio(_desktopWavPath))
            audioFiles.Add(_desktopWavPath);
        if (_micWavPath != null && HasMeaningfulAudio(_micWavPath))
            audioFiles.Add(_micWavPath);

        if (audioFiles.Count == 0) return;

        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath == null) return;

        string dir = Path.GetDirectoryName(videoPath)!;
        string ext = Path.GetExtension(videoPath);
        string tempOut = Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + "_muxed" + ext);

        try
        {
            string args;
            if (audioFiles.Count == 1)
            {
                // Single audio source — mux directly
                string audioCodec = ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "libopus" : "aac";
                args = $"-y -i \"{videoPath}\" -i \"{audioFiles[0]}\" -c:v copy -c:a {audioCodec} -shortest \"{tempOut}\"";
            }
            else
            {
                // Two audio sources — merge with amix filter then mux
                string audioCodec = ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "libopus" : "aac";
                args = $"-y -i \"{videoPath}\" -i \"{audioFiles[0]}\" -i \"{audioFiles[1]}\" " +
                       $"-filter_complex \"[1:a][2:a]amix=inputs=2:duration=shortest[a]\" " +
                       $"-c:v copy -c:a {audioCodec} -map 0:v -map \"[a]\" -shortest \"{tempOut}\"";
            }

            var proc = new Process
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
            foreach (var f in audioFiles)
                try { File.Delete(f); } catch { }
        }
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
