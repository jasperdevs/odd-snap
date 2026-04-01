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
    private readonly Rectangle _region;
    private readonly int _fps;
    private readonly int _maxDurationMs;
    private readonly string _tempDir;
    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<(Bitmap frame, int index)> _frameQueue = new(boundedCapacity: 60);

    private Thread? _captureThread;
    private Thread? _writerThread;
    private int _frameCount;
    private DateTime _startTime;
    private bool _disposed;

    public int FrameCount => _frameCount;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public bool IsRecording => _captureThread?.IsAlive == true;

    public GifRecorder(Rectangle region, int fps = 15, int maxDurationSeconds = 30)
    {
        _region = region;
        _fps = Math.Clamp(fps, 5, 30);
        _maxDurationMs = maxDurationSeconds * 1000;
        _tempDir = Path.Combine(Path.GetTempPath(), $"yoink_gif_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Start()
    {
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

        while (!ct.IsCancellationRequested)
        {
            // Auto-stop at max duration
            if ((DateTime.UtcNow - _startTime).TotalMilliseconds >= _maxDurationMs)
                break;

            var sw = Stopwatch.StartNew();
            try
            {
                var frame = ScreenCapture.CaptureRegionForRecording(_region);
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
        _captureThread?.Join(3000);
        _writerThread?.Join(3000);

        var frameFiles = Directory.GetFiles(_tempDir, "frame_*.bmp");
        Array.Sort(frameFiles, StringComparer.Ordinal);

        if (frameFiles.Length == 0)
            throw new InvalidOperationException("No frames captured.");

        // Try FFmpeg first (much faster GIF encoding with palette optimization)
        var ffmpeg = VideoRecorder.FindFfmpeg();
        if (ffmpeg != null)
        {
            EncodeFfmpegGif(ffmpeg, outputPath, frameFiles);
        }
        else
        {
            EncodeAnimatedGif(outputPath, frameFiles);
        }

        Cleanup();
        return outputPath;
    }

    private void EncodeFfmpegGif(string ffmpegPath, string outputPath, string[] frameFiles)
    {
        // FFmpeg two-pass: generate palette then encode with it for best quality
        string paletteFile = Path.Combine(_tempDir, "palette.png");
        string inputPattern = Path.Combine(_tempDir, "frame_%06d.bmp");

        // Pass 1: generate palette
        RunFfmpeg(ffmpegPath, $"-y -framerate {_fps} -i \"{inputPattern}\" -vf \"palettegen=stats_mode=diff\" \"{paletteFile}\"");

        // Pass 2: encode using palette
        RunFfmpeg(ffmpegPath, $"-y -framerate {_fps} -i \"{inputPattern}\" -i \"{paletteFile}\" -lavfi \"paletteuse=dither=sierra2_4a\" \"{outputPath}\"");
    }

    private static void RunFfmpeg(string path, string args)
    {
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        });
        proc?.WaitForExit(120000);
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
            var first = Directory.GetFiles(_tempDir, "frame_000000.bmp").FirstOrDefault();
            return first != null ? new Bitmap(first) : null;
        }
        catch { return null; }
    }

    /// <summary>Cancels recording and discards all frames.</summary>
    public void Discard()
    {
        _cts.Cancel();
        _captureThread?.Join(2000);
        _writerThread?.Join(2000);
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
}
