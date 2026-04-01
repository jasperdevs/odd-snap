using System.IO;
using System.Media;
using Yoink.Models;

namespace Yoink.Services;

public static class SoundService
{
    private static byte[]? _captureWav;
    private static byte[]? _colorWav;
    private static byte[]? _textWav;
    private static byte[]? _scanWav;
    private static byte[]? _recordStartWav;
    private static byte[]? _recordStopWav;
    private static byte[]? _uploadStartWav;
    private static byte[]? _uploadDoneWav;
    private static byte[]? _errorWav;

    public static bool Muted { get; set; }

    private static SoundPack _currentPack = SoundPack.Default;
    public static void SetPack(SoundPack pack) { _currentPack = pack; ClearCache(); }
    public static SoundPack CurrentPack => _currentPack;

    // Pack parameters: (pitch multiplier, decay multiplier, volume)
    private static (double pitch, double decay, double vol) PackParams => _currentPack switch
    {
        SoundPack.Soft => (0.8, 0.7, 0.3),   // lower pitched, slower decay, quieter
        SoundPack.Retro => (1.3, 1.5, 0.5),   // higher pitched, snappier, chiptune feel
        _ => (1.0, 1.0, 0.4),                  // default
    };

    private static void ClearCache()
    {
        _captureWav = _colorWav = _textWav = _scanWav = null;
        _recordStartWav = _recordStopWav = _uploadStartWav = _uploadDoneWav = _errorWav = null;
    }

    public static void PlayCaptureSound() { if (!Muted) PlayAsync(_captureWav ??= GenerateCaptureWav()); }
    public static void PlayColorSound() { if (!Muted) PlayAsync(_colorWav ??= GenerateColorWav()); }
    public static void PlayTextSound() { if (!Muted) PlayAsync(_textWav ??= GenerateTextWav()); }
    public static void PlayScanSound() { if (!Muted) PlayAsync(_scanWav ??= GenerateScanWav()); }
    public static void PlayRecordStartSound() { if (!Muted) PlayAsync(_recordStartWav ??= GenerateRecordStartWav()); }
    public static void PlayRecordStopSound() { if (!Muted) PlayAsync(_recordStopWav ??= GenerateRecordStopWav()); }
    public static void PlayUploadStartSound() { if (!Muted) PlayAsync(_uploadStartWav ??= GenerateUploadStartWav()); }
    public static void PlayUploadDoneSound() { if (!Muted) PlayAsync(_uploadDoneWav ??= GenerateUploadDoneWav()); }
    public static void PlayErrorSound() { if (!Muted) PlayAsync(_errorWav ??= GenerateErrorWav()); }

    private static void PlayAsync(byte[] wav)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var ms = new MemoryStream(wav);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch { }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    /// <summary>Capture: crisp camera-shutter click (short noise burst + resonant tap)</summary>
    private static byte[] GenerateCaptureWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 80;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        var rng = new Random(42);
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double noise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 200 * d) * 0.25;
            double tone = Math.Sin(2 * Math.PI * 1200 * p * t) * Math.Exp(-t * 100 * d) * 0.4;
            double sub = Math.Sin(2 * Math.PI * 300 * p * t) * Math.Exp(-t * 60 * d) * 0.2;
            double sample = Math.Clamp(noise + tone + sub, -1.0, 1.0) * v / 0.4;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Color pick: gentle two-note pip (ascending, glassy)</summary>
    private static byte[] GenerateColorWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 140;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double dur = durationMs / 1000.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Two notes: E6 then A6 with soft crossfade
            double split = dur * 0.4;
            double freq = t < split ? 1319 * p : 1760 * p;
            double env = t < split
                ? Math.Exp(-(t) * 20 * d)
                : Math.Exp(-(t - split) * 25 * d);
            // Pure sine + soft harmonic for glassy quality
            double sample = (Math.Sin(2 * Math.PI * freq * t) * 0.6
                           + Math.Sin(2 * Math.PI * freq * 2 * t) * 0.15) * env * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Text/OCR copy: soft descending chime</summary>
    private static byte[] GenerateTextWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 110;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double dur = durationMs / 1000.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Exp(-t * 35 * d);
            // Gentle descending sweep (B5 to G5)
            double freq = (988 - (396 * t / dur)) * p;
            double sample = (Math.Sin(2 * Math.PI * freq * t) * 0.55
                           + Math.Sin(2 * Math.PI * freq * 1.5 * t) * 0.2) * env * v;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>QR/Barcode scan: quick triple-beep like a barcode scanner</summary>
    private static byte[] GenerateScanWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 180;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double dur = durationMs / 1000.0;
        double beepLen = dur / 4.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Three short beeps at ascending pitches
            double sample = 0;
            for (int b = 0; b < 3; b++)
            {
                double start = b * beepLen * 1.2;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double freq = (1400 + b * 200) * p;
                    double env = Math.Sin(Math.PI * local / beepLen); // smooth on/off
                    sample += Math.Sin(2 * Math.PI * freq * local) * env * v * 0.75;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Record start: quick ascending double-beep (low to high)</summary>
    private static byte[] GenerateRecordStartWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 200;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double dur = durationMs / 1000.0;
        double beepLen = dur / 3.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = 0;
            // Two ascending beeps: C5 then E5
            double[] freqs = [523 * p, 659 * p];
            for (int b = 0; b < 2; b++)
            {
                double start = b * beepLen * 1.3;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double env = Math.Sin(Math.PI * local / beepLen);
                    sample += Math.Sin(2 * Math.PI * freqs[b] * local) * env * v * 0.875;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Record stop: quick descending double-beep (high to low)</summary>
    private static byte[] GenerateRecordStopWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 200;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double dur = durationMs / 1000.0;
        double beepLen = dur / 3.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = 0;
            // Two descending beeps: E5 then C5
            double[] freqs = [659 * p, 523 * p];
            for (int b = 0; b < 2; b++)
            {
                double start = b * beepLen * 1.3;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double env = Math.Sin(Math.PI * local / beepLen);
                    sample += Math.Sin(2 * Math.PI * freqs[b] * local) * env * v * 0.875;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    private static byte[] GenerateUploadStartWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 90;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Exp(-t * 26 * d);
            double sample = (Math.Sin(2 * Math.PI * 820 * p * t) * 0.42
                           + Math.Sin(2 * Math.PI * 1120 * p * t) * 0.18) * env * v / 0.4;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    private static byte[] GenerateUploadDoneWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 170;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double[] freqs = [740 * p, 988 * p];
        double beepLen = durationMs / 1000.0 / 2.5;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = 0;
            for (int b = 0; b < 2; b++)
            {
                double start = b * beepLen * 1.1;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double env = Math.Sin(Math.PI * local / beepLen);
                    sample += Math.Sin(2 * Math.PI * freqs[b] * local) * env * v * 0.85;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Error: short descending alert tone</summary>
    private static byte[] GenerateErrorWav()
    {
        var (p, d, v) = PackParams;
        const int sampleRate = 44100;
        const int durationMs = 170;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Exp(-t * 18 * d);
            double freq = (780 - (260 * t / (durationMs / 1000.0))) * p;
            double sample = Math.Sin(2 * Math.PI * freq * t) * env * v * 1.125;
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
}
