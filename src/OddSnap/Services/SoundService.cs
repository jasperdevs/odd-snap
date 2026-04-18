using System.IO;
using System.Media;
using System.Collections.Concurrent;
using OddSnap.Models;

namespace OddSnap.Services;

public static class SoundService
{
    private const int MaxQueuedSounds = 8;
    private static byte[]? _captureWav;
    private static byte[]? _colorWav;
    private static byte[]? _textWav;
    private static byte[]? _scanWav;
    private static byte[]? _recordStartWav;
    private static byte[]? _recordStopWav;
    private static byte[]? _uploadStartWav;
    private static byte[]? _uploadDoneWav;
    private static byte[]? _errorWav;
    private static readonly object PlaybackGate = new();
    private static BlockingCollection<byte[]>? _playbackQueue;
    private static Thread? _playbackThread;
    private static int _suppressionDepth;

    public static bool Muted { get; set; }
    internal static bool IsPlaybackSuppressed => Muted || Volatile.Read(ref _suppressionDepth) > 0;

    private static SoundPack _currentPack = SoundPack.Default;
    public static void SetPack(SoundPack pack) { _currentPack = pack; ClearCache(); }
    public static SoundPack CurrentPack => _currentPack;

    // Pack parameters: (pitch multiplier, character, volume)
    // character: 0 = warm/round, 0.5 = balanced, 1 = bright/crisp
    private static (double pitch, double character, double vol) PackParams => _currentPack switch
    {
        SoundPack.Soft => (0.82, 0.15, 0.20),
        SoundPack.Retro => (1.15, 0.85, 0.35),
        _ => (1.0, 0.45, 0.28),
    };

    private static void ClearCache()
    {
        _captureWav = _colorWav = _textWav = _scanWav = null;
        _recordStartWav = _recordStopWav = _uploadStartWav = _uploadDoneWav = _errorWav = null;
    }

    public static void PlayCaptureSound() { if (!IsPlaybackSuppressed) PlayAsync(_captureWav ??= GenerateCaptureWav()); }
    public static void PlayColorSound() { if (!IsPlaybackSuppressed) PlayAsync(_colorWav ??= GenerateColorWav()); }
    public static void PlayTextSound() { if (!IsPlaybackSuppressed) PlayAsync(_textWav ??= GenerateTextWav()); }
    public static void PlayScanSound() { if (!IsPlaybackSuppressed) PlayAsync(_scanWav ??= GenerateScanWav()); }
    public static void PlayRecordStartSound() { if (!IsPlaybackSuppressed) PlayAsync(_recordStartWav ??= GenerateRecordStartWav()); }
    public static void PlayRecordStopSound() { if (!IsPlaybackSuppressed) PlayAsync(_recordStopWav ??= GenerateRecordStopWav()); }
    public static void PlayUploadStartSound() { if (!IsPlaybackSuppressed) PlayAsync(_uploadStartWav ??= GenerateUploadStartWav()); }
    public static void PlayUploadDoneSound() { if (!IsPlaybackSuppressed) PlayAsync(_uploadDoneWav ??= GenerateUploadDoneWav()); }
    public static void PlayErrorSound() { if (!IsPlaybackSuppressed) PlayAsync(_errorWav ??= GenerateErrorWav()); }

    public static IDisposable SuppressPlayback()
    {
        Interlocked.Increment(ref _suppressionDepth);
        return new PlaybackSuppressionHandle();
    }

    private static void PlayAsync(byte[] wav)
    {
        EnsurePlaybackWorker();
        _playbackQueue?.TryAdd(wav);
    }

    private static void EnsurePlaybackWorker()
    {
        lock (PlaybackGate)
        {
            if (_playbackQueue is not null && _playbackThread?.IsAlive == true)
                return;

            _playbackQueue?.Dispose();
            _playbackQueue = new BlockingCollection<byte[]>(MaxQueuedSounds);
            _playbackThread = new Thread(() =>
            {
                foreach (var queuedWav in _playbackQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        using var ms = new MemoryStream(queuedWav);
                        using var player = new SoundPlayer(ms);
                        player.PlaySync();
                    }
                    catch
                    {
                    }
                }
            })
            {
                IsBackground = true,
                Name = "OddSnapSoundPlayback"
            };
            _playbackThread.Start();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static double SoftClip(double x)
        => Math.Tanh(x * 1.2) / Math.Tanh(1.2);

    private static double Env(double t, double attackMs, double decayRate)
    {
        double a = attackMs / 1000.0;
        double atk = t < a ? Math.Sin(Math.PI * 0.5 * t / a) : 1.0;
        return atk * Math.Exp(-t * decayRate);
    }

    // ── Capture: soft, warm tap ──────────────────────────────────────────
    private static byte[] GenerateCaptureWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 90;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        var rng = new Random(42);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;

            // Gentle noise transient — very quiet, just adds a tiny "tick"
            double noise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 400) * 0.06;

            // Warm fundamental with slow chirp settling
            double f0 = (720 + 20 * Math.Exp(-t * 80)) * p;
            double fund = Math.Sin(2 * Math.PI * f0 * t) * Env(t, 4, 32) * 0.30;

            // Soft harmonic
            double h2 = Math.Sin(2 * Math.PI * f0 * 2 * t) * Env(t, 4, 55) * (0.08 + c * 0.06);

            // Sub body
            double sub = Math.Sin(2 * Math.PI * 260 * p * t) * Env(t, 6, 25) * 0.12;

            double sample = SoftClip(noise + fund + h2 + sub) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Color pick: gentle bell dyad ─────────────────────────────────────
    private static byte[] GenerateColorWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 120;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;

            // Perfect fifth: gentle, pure
            double f1 = 1050 * p;
            double f2 = f1 * 1.5;

            double tone1 = Math.Sin(2 * Math.PI * f1 * t) * Env(t, 3, 28) * 0.30;
            double tone2 = Math.Sin(2 * Math.PI * f2 * t) * Env(t, 3, 35) * (0.16 + c * 0.04);
            // Faint bell overtone
            double bell = Math.Sin(2 * Math.PI * f1 * 3 * t) * Env(t, 2, 65) * 0.04;

            double sample = SoftClip(tone1 + tone2 + bell) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Text/OCR copy: quick soft chord ──────────────────────────────────
    private static byte[] GenerateTextWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 90;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;

            double f1 = 680 * p;
            double f2 = 880 * p;

            double tone1 = Math.Sin(2 * Math.PI * f1 * t) * Env(t, 3, 34) * 0.28;
            double tone2 = Math.Sin(2 * Math.PI * f2 * t) * Env(t, 3, 38) * (0.14 + c * 0.04);

            double sample = SoftClip(tone1 + tone2) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── QR/Barcode scan: gentle ascending triple pip ─────────────────────
    private static byte[] GenerateScanWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 170;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        double dur = durationMs / 1000.0;
        double pipLen = dur / 4.5;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double sample = 0;
            for (int b = 0; b < 3; b++)
            {
                double start = b * pipLen * 1.25;
                double local = t - start;
                if (local >= 0 && local < pipLen)
                {
                    double freq = (820 + b * 110) * p;
                    double env = Math.Sin(Math.PI * local / pipLen);
                    double tone = Math.Sin(2 * Math.PI * freq * local) * 0.30;
                    double h = Math.Sin(2 * Math.PI * freq * 2 * local) * (0.04 + c * 0.03);
                    sample += (tone + h) * env;
                }
            }
            sample = SoftClip(sample) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Record start: gentle ascending two-note ──────────────────────────
    private static byte[] GenerateRecordStartWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 160;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        double dur = durationMs / 1000.0;
        double pipLen = dur / 3.0;
        double[] freqs = [480 * p, 600 * p]; // major third up
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double sample = 0;
            for (int b = 0; b < 2; b++)
            {
                double start = b * pipLen * 1.2;
                double local = t - start;
                if (local >= 0 && local < pipLen)
                {
                    double env = Math.Sin(Math.PI * local / pipLen);
                    double tone = Math.Sin(2 * Math.PI * freqs[b] * local) * 0.28;
                    double h = Math.Sin(2 * Math.PI * freqs[b] * 2 * local) * (0.06 + c * 0.03);
                    sample += (tone + h) * env;
                }
            }
            sample = SoftClip(sample) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Record stop: gentle descending two-note ──────────────────────────
    private static byte[] GenerateRecordStopWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 160;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        double dur = durationMs / 1000.0;
        double pipLen = dur / 3.0;
        double[] freqs = [600 * p, 480 * p]; // major third down
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double sample = 0;
            for (int b = 0; b < 2; b++)
            {
                double start = b * pipLen * 1.2;
                double local = t - start;
                if (local >= 0 && local < pipLen)
                {
                    double env = Math.Sin(Math.PI * local / pipLen);
                    double tone = Math.Sin(2 * Math.PI * freqs[b] * local) * 0.28;
                    double h = Math.Sin(2 * Math.PI * freqs[b] * 2 * local) * (0.06 + c * 0.03);
                    sample += (tone + h) * env;
                }
            }
            sample = SoftClip(sample) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Upload start: soft anticipatory tone ─────────────────────────────
    private static byte[] GenerateUploadStartWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 80;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = (680 + 100 * Math.Exp(-t * 60)) * p; // gentle upward chirp
            double tone = Math.Sin(2 * Math.PI * f * t) * Env(t, 3, 28) * 0.28;
            double h = Math.Sin(2 * Math.PI * f * 1.5 * t) * Env(t, 3, 40) * (0.08 + c * 0.04);

            double sample = SoftClip(tone + h) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Upload done: resolved two-note chord ─────────────────────────────
    private static byte[] GenerateUploadDoneWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 140;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        double dur = durationMs / 1000.0;
        double pipLen = dur / 2.5;
        double[] freqs = [680 * p, 900 * p]; // ascending
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double sample = 0;
            for (int b = 0; b < 2; b++)
            {
                double start = b * pipLen * 1.1;
                double local = t - start;
                if (local >= 0 && local < pipLen)
                {
                    double env = Math.Sin(Math.PI * local / pipLen);
                    double tone = Math.Sin(2 * Math.PI * freqs[b] * local) * 0.26;
                    double h = Math.Sin(2 * Math.PI * freqs[b] * 2 * local) * (0.05 + c * 0.03);
                    sample += (tone + h) * env;
                }
            }
            sample = SoftClip(sample) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    // ── Error: gentle descending minor interval ──────────────────────────
    private static byte[] GenerateErrorWav()
    {
        var (p, c, v) = PackParams;
        const int sr = 44100;
        const int durationMs = 180;
        int n = sr * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, n, sr);

        double dur = durationMs / 1000.0;
        double pipLen = dur / 2.6;
        double[] freqs = [560 * p, 420 * p]; // descending minor third
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double sample = 0;
            for (int b = 0; b < 2; b++)
            {
                double start = b * pipLen * 1.15;
                double local = t - start;
                if (local >= 0 && local < pipLen)
                {
                    double env = Math.Sin(Math.PI * local / pipLen) * Math.Exp(-local * 10);
                    double tone = Math.Sin(2 * Math.PI * freqs[b] * local) * 0.30;
                    double h = Math.Sin(2 * Math.PI * freqs[b] * 1.5 * local) * Env(local, 3, 30) * 0.06;
                    sample += (tone + h) * env;
                }
            }
            sample = SoftClip(sample) * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    private static void WriteWavHeader(BinaryWriter bw, int numSamples, int sampleRate)
    {
        int dataSize = numSamples * 2;
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);
    }

    private sealed class PlaybackSuppressionHandle : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Interlocked.Decrement(ref _suppressionDepth);
        }
    }
}
