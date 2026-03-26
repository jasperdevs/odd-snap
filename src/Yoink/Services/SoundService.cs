using System.IO;
using System.Media;

namespace Yoink.Services;

public static class SoundService
{
    private static byte[]? _captureWav;
    private static byte[]? _colorWav;
    private static byte[]? _textWav;

    public static bool Muted { get; set; }

    public static void PlayCaptureSound()
    {
        if (Muted) return;
        _captureWav ??= GenerateClickWav();
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

    /// <summary>Plays a WAV on a background thread so it doesn't get killed with the caller.</summary>
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

    /// <summary>Short 30ms soft click/pop for screenshot capture.</summary>
    private static byte[] GenerateClickWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 50;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-t * 80);
            double sample = Math.Sin(2 * Math.PI * 800 * t) * envelope * 0.5;
            bw.Write((short)(sample * short.MaxValue));
        }

        return ms.ToArray();
    }

    /// <summary>Bright ascending two-tone "bling" for color pick.</summary>
    private static byte[] GenerateColorWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 120;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double totalDuration = durationMs / 1000.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-t * 25);
            // Two quick ascending tones
            double freq = t < (totalDuration * 0.4) ? 1100 : 1500;
            double sample = Math.Sin(2 * Math.PI * freq * t) * envelope * 0.5;
            bw.Write((short)(sample * short.MaxValue));
        }

        return ms.ToArray();
    }

    /// <summary>Soft descending sweep for text/OCR capture.</summary>
    private static byte[] GenerateTextWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 100;
        int numSamples = sampleRate * durationMs / 1000;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, numSamples, sampleRate);

        double totalDuration = durationMs / 1000.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-t * 30);
            // Descending sweep from 900Hz to 500Hz
            double freq = 900 - (400 * t / totalDuration);
            double sample = (Math.Sin(2 * Math.PI * freq * t) * 0.7
                           + Math.Sin(2 * Math.PI * freq * 1.5 * t) * 0.3)
                           * envelope * 0.5;
            bw.Write((short)(sample * short.MaxValue));
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
