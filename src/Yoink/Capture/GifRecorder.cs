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
    private readonly BlockingCollection<(byte[] png, int index)> _frameQueue = new(boundedCapacity: 60);

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
                using var bmp = ScreenCapture.CaptureRegion(_region);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                _frameQueue.TryAdd((ms.ToArray(), index), 100, ct);
                Interlocked.Increment(ref _frameCount);
                index++;
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
            foreach (var (png, index) in _frameQueue.GetConsumingEnumerable(_cts.Token))
            {
                string path = Path.Combine(_tempDir, $"frame_{index:D6}.png");
                File.WriteAllBytes(path, png);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Stops recording and encodes frames to GIF. Returns the output file path.</summary>
    public string StopAndEncode(string outputPath)
    {
        _cts.Cancel();
        _captureThread?.Join(3000);
        _writerThread?.Join(3000);

        int delayMs = 1000 / _fps;
        var frameFiles = Directory.GetFiles(_tempDir, "frame_*.png");
        Array.Sort(frameFiles, StringComparer.Ordinal);

        if (frameFiles.Length == 0)
            throw new InvalidOperationException("No frames captured.");

        using (var gif = AnimatedGif.AnimatedGif.Create(outputPath, delayMs))
        {
            foreach (var file in frameFiles)
            {
                using var img = Image.FromFile(file);
                gif.AddFrame(img, delay: -1, quality: GifQuality.Bit8);
            }
        }

        Cleanup();
        return outputPath;
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
        _frameQueue.Dispose();
        _cts.Dispose();
        Cleanup();
    }
}
