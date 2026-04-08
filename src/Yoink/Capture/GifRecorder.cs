using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AnimatedGif;

namespace Yoink.Capture;

/// <summary>
/// Captures screen frames at a target FPS and encodes them to GIF.
/// Pure engine — no UI. Create, Start, Stop/Discard.
/// </summary>
public sealed class GifRecorder : IDisposable
{
    private const int DefaultInitialCaptureDelayMs = 0;
    private readonly Rectangle _region;
    private readonly int _fps;
    private readonly int _maxDurationMs;
    private readonly bool _showCursor;
    private readonly string _tempDir;
    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<(Bitmap frame, int index)> _frameQueue = new(boundedCapacity: 60);

    private Thread? _captureThread;
    private Thread? _writerThread;
    private int _frameCount;
    private DateTime _startTime;
    private bool _disposed;
    private int _initialCaptureDelayMs = DefaultInitialCaptureDelayMs;

    public int FrameCount => _frameCount;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public bool IsRecording => _captureThread?.IsAlive == true;

    public GifRecorder(Rectangle region, int fps = 15, int maxDurationSeconds = 30, bool showCursor = false)
    {
        _region = region;
        _fps = Math.Clamp(fps, 5, 30);
        _maxDurationMs = maxDurationSeconds * 1000;
        _showCursor = showCursor;
        _tempDir = Path.Combine(Path.GetTempPath(), $"yoink_gif_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Start(int initialCaptureDelayMs = DefaultInitialCaptureDelayMs)
    {
        _initialCaptureDelayMs = Math.Max(0, initialCaptureDelayMs);
        _startTime = DateTime.UtcNow;

        // Producer: capture frames
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "GifCapture" };
        _captureThread.Start();

        // Consumer: write frames to disk
        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "GifWriter" };
        _writerThread.Start();
    }

    private void CaptureLoop()
    {
        int delayMs = 1000 / _fps;
        var ct = _cts.Token;
        int index = 0;

        if (_initialCaptureDelayMs > 0)
        {
            try { Thread.Sleep(_initialCaptureDelayMs); }
            catch (ThreadInterruptedException) { return; }
        }

        while (!ct.IsCancellationRequested)
        {
            // Auto-stop at max duration
            if ((DateTime.UtcNow - _startTime).TotalMilliseconds >= _maxDurationMs)
                break;

            var sw = Stopwatch.StartNew();
            try
            {
                var frame = ScreenCapture.CaptureRegionForRecording(_region, _showCursor);
                if (_frameQueue.TryAdd((frame, index), 100, ct))
                {
                    Interlocked.Increment(ref _frameCount);
                    index++;
                    frame = null!;
                }
                frame?.Dispose();
            }
            catch (OperationCanceledException) { break; }
            catch { /* skip frame on capture error */ }

            int sleep = delayMs - (int)sw.ElapsedMilliseconds;
            if (sleep > 0)
            {
                try { Thread.Sleep(sleep); }
                catch (ThreadInterruptedException) { break; }
            }
        }

        _frameQueue.CompleteAdding();
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var (frame, index) in _frameQueue.GetConsumingEnumerable())
            {
                string path = Path.Combine(_tempDir, $"frame_{index:D6}.bmp");
                using (frame)
                {
                    frame.Save(path, ImageFormat.Bmp);
                }
            }
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Stops recording and encodes frames to GIF. Uses FFmpeg if available (10-50x faster).</summary>
    public string StopAndEncode(string outputPath)
    {
        _cts.Cancel();
        // Ensure the consumer can drain and exit promptly.
        try { _frameQueue.CompleteAdding(); } catch { }

        // Wait for capture/writer threads to finish flushing frames to disk.
        if (_captureThread != null && !_captureThread.Join(10_000))
            throw new TimeoutException("GIF capture thread did not stop in time.");
        if (_writerThread != null && !_writerThread.Join(30_000))
            throw new TimeoutException("GIF writer thread did not flush frames in time.");

        try
        {
            var frameFiles = Directory.EnumerateFiles(_tempDir, "frame_*.bmp").ToArray();
            Array.Sort(frameFiles, StringComparer.Ordinal);

            if (frameFiles.Length == 0)
                throw new InvalidOperationException("No frames captured.");

            // Try FFmpeg first (much faster GIF encoding with palette optimization).
            // If FFmpeg fails for any reason, fall back to in-process encoding.
            Exception? ffmpegError = null;
            var ffmpeg = VideoRecorder.FindFfmpeg();
            if (ffmpeg != null)
            {
                try
                {
                    EncodeFfmpegGif(ffmpeg, outputPath);
                }
                catch (Exception ex)
                {
                    ffmpegError = ex;
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                }
            }

            if (!IsValidOutputFile(outputPath))
            {
                try
                {
                    EncodeAnimatedGif(outputPath, frameFiles);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        ffmpegError != null
                            ? $"GIF encoding failed. FFmpeg error: {ffmpegError.Message}"
                            : "GIF encoding failed.",
                        ex);
                }
            }

            if (!IsValidOutputFile(outputPath))
                throw new InvalidOperationException("GIF encoding failed — no output file produced.");

            return outputPath;
        }
        finally
        {
            Cleanup();
        }
    }

    private void EncodeFfmpegGif(string ffmpegPath, string outputPath)
    {
        // FFmpeg two-pass: generate palette then encode with it for best quality
        string paletteFile = Path.Combine(_tempDir, "palette.png");
        string inputPattern = Path.Combine(_tempDir, "frame_%06d.bmp");

        // Pass 1: generate palette
        RunFfmpegChecked(ffmpegPath,
            $"-y -framerate {_fps} -i \"{inputPattern}\" -vf \"palettegen=stats_mode=diff\" \"{paletteFile}\"",
            timeoutMs: 120_000);

        if (!File.Exists(paletteFile) || new FileInfo(paletteFile).Length == 0)
            throw new InvalidOperationException("FFmpeg palette generation failed — no palette file produced.");

        // Pass 2: encode using palette
        RunFfmpegChecked(ffmpegPath,
            $"-y -framerate {_fps} -i \"{inputPattern}\" -i \"{paletteFile}\" -lavfi \"paletteuse=dither=sierra2_4a\" \"{outputPath}\"",
            timeoutMs: 120_000);

        if (!IsValidOutputFile(outputPath))
            throw new InvalidOperationException("FFmpeg GIF encoding failed — no output file produced.");
    }

    private static void RunFfmpegChecked(string path, string args, int timeoutMs)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            }
        };

        var stderr = new LimitedTextBuffer(32_768);
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderr.AppendLine(e.Data);
        };

        proc.Start();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { proc.WaitForExit(2_000); } catch { }
            throw new TimeoutException("FFmpeg timed out while encoding GIF.");
        }

        try { proc.WaitForExit(500); } catch { } // allow async stderr flush

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed (exit code {proc.ExitCode}). {stderr}");
    }

    private void EncodeAnimatedGif(string outputPath, string[] frameFiles)
    {
        int delayMs = 1000 / _fps;
        using var gif = AnimatedGif.AnimatedGif.Create(outputPath, delayMs);
        foreach (var file in frameFiles)
        {
            using var img = Image.FromFile(file);
            gif.AddFrame(img, delay: -1, quality: GifQuality.Bit8);
        }
    }

    /// <summary>Gets the first frame as a Bitmap for preview. Caller must dispose.</summary>
    public Bitmap? GetFirstFrame()
    {
        try
        {
            var first = Path.Combine(_tempDir, "frame_000000.bmp");
            return File.Exists(first) ? new Bitmap(first) : null;
        }
        catch { return null; }
    }

    /// <summary>Cancels recording and discards all frames.</summary>
    public void Discard()
    {
        _cts.Cancel();
        try { _frameQueue.CompleteAdding(); } catch { }
        _captureThread?.Join(10_000);
        _writerThread?.Join(10_000);
        Cleanup();
    }

    private void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        while (_frameQueue.TryTake(out var pending))
            pending.frame.Dispose();
        _frameQueue.Dispose();
        _cts.Dispose();
        Cleanup();
    }

    private static bool IsValidOutputFile(string path)
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
