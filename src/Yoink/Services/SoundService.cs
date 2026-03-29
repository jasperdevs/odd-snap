using System.IO;
using System.Media;

namespace Yoink.Services;

public static class SoundService
{
    private static byte[]? _captureWav;
    private static byte[]? _colorWav;
    private static byte[]? _textWav;
    private static byte[]? _scanWav;
    private static byte[]? _recordStartWav;
    private static byte[]? _recordStopWav;
    private static byte[]? _errorWav;

    public static bool Muted { get; set; }

    public static void PlayCaptureSound()
    {
        if (Muted) return;
        _captureWav ??= GenerateCaptureWav();
        PlayAsync(_captureWav);
    }

    public static void PlayColorSound()
    {
        if (Muted) return;
        _colorWav ??= GenerateColorWav();
        PlayAsync(_colorWav);
    }

    public static void PlayTextSound()
    {
        if (Muted) return;
        _textWav ??= GenerateTextWav();
        PlayAsync(_textWav);
    }

    public static void PlayScanSound()
    {
        if (Muted) return;
        _scanWav ??= GenerateScanWav();
        PlayAsync(_scanWav);
    }

    public static void PlayRecordStartSound()
    {
        if (Muted) return;
        _recordStartWav ??= GenerateRecordStartWav();
        PlayAsync(_recordStartWav);
    }

    public static void PlayRecordStopSound()
    {
        if (Muted) return;
        _recordStopWav ??= GenerateRecordStopWav();
        PlayAsync(_recordStopWav);
    }

    public static void PlayErrorSound()
    {
        if (Muted) return;
        _errorWav ??= GenerateErrorWav();
        PlayAsync(_errorWav);
    }

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
            // Short noise burst (mechanical click)
            double noise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 200) * 0.25;
            // Resonant body tap
            double tone = Math.Sin(2 * Math.PI * 1200 * t) * Math.Exp(-t * 100) * 0.4;
            // Sub-thump for weight
            double sub = Math.Sin(2 * Math.PI * 300 * t) * Math.Exp(-t * 60) * 0.2;
            double sample = Math.Clamp(noise + tone + sub, -1.0, 1.0);
            bw.Write((short)(sample * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Color pick: gentle two-note pip (ascending, glassy)</summary>
    private static byte[] GenerateColorWav()
    {
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
            double freq = t < split ? 1319 : 1760;
            double env = t < split
                ? Math.Exp(-(t) * 20)
                : Math.Exp(-(t - split) * 25);
            // Pure sine + soft harmonic for glassy quality
            double sample = (Math.Sin(2 * Math.PI * freq * t) * 0.6
                           + Math.Sin(2 * Math.PI * freq * 2 * t) * 0.15) * env * 0.4;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Text/OCR copy: soft descending chime</summary>
    private static byte[] GenerateTextWav()
    {
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
            double env = Math.Exp(-t * 35);
            // Gentle descending sweep (B5 to G5)
            double freq = 988 - (396 * t / dur);
            double sample = (Math.Sin(2 * Math.PI * freq * t) * 0.55
                           + Math.Sin(2 * Math.PI * freq * 1.5 * t) * 0.2) * env * 0.4;
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>QR/Barcode scan: quick triple-beep like a barcode scanner</summary>
    private static byte[] GenerateScanWav()
    {
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
                    double freq = 1400 + b * 200; // 1400, 1600, 1800 Hz
                    double env = Math.Sin(Math.PI * local / beepLen); // smooth on/off
                    sample += Math.Sin(2 * Math.PI * freq * local) * env * 0.3;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Record start: quick ascending double-beep (low to high)</summary>
    private static byte[] GenerateRecordStartWav()
    {
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
            double[] freqs = [523, 659];
            for (int b = 0; b < 2; b++)
            {
                double start = b * beepLen * 1.3;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double env = Math.Sin(Math.PI * local / beepLen);
                    sample += Math.Sin(2 * Math.PI * freqs[b] * local) * env * 0.35;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Record stop: quick descending double-beep (high to low)</summary>
    private static byte[] GenerateRecordStopWav()
    {
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
            double[] freqs = [659, 523];
            for (int b = 0; b < 2; b++)
            {
                double start = b * beepLen * 1.3;
                double local = t - start;
                if (local >= 0 && local < beepLen)
                {
                    double env = Math.Sin(Math.PI * local / beepLen);
                    sample += Math.Sin(2 * Math.PI * freqs[b] * local) * env * 0.35;
                }
            }
            bw.Write((short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>Error: short descending alert tone</summary>
    private static byte[] GenerateErrorWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 170;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double env = Math.Exp(-t * 18);
            double freq = 780 - (260 * t / (durationMs / 1000.0));
            double sample = Math.Sin(2 * Math.PI * freq * t) * env * 0.45;
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
