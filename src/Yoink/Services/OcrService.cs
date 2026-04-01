using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Yoink.Helpers;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace Yoink.Services;

public static class OcrService
{
    private static readonly object LanguagesLock = new();
    private static IReadOnlyList<Language>? _cachedLanguages;

    public static IReadOnlyList<Language> GetAvailableRecognizerLanguages(bool refresh = false)
    {
        if (!refresh && _cachedLanguages != null)
            return _cachedLanguages;

        lock (LanguagesLock)
        {
            if (!refresh && _cachedLanguages != null)
                return _cachedLanguages;

            // Keep ordering stable for UI/determinism.
            _cachedLanguages = OcrEngine.AvailableRecognizerLanguages
                .OrderBy(l => l.LanguageTag, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return _cachedLanguages;
        }
    }

    public static async Task<string> RecognizeAsync(Bitmap bitmap, string? languageTag = null)
    {
        using var scaled = ScaleForOcr(bitmap);
        using var prepared = PrepareForOcr(scaled);

        string best = await DoOcr(scaled, languageTag);
        string enhanced = await DoOcr(prepared, languageTag);
        return Score(enhanced) > Score(best) ? enhanced : best;
    }

    private static async Task<string> DoOcr(Bitmap bitmap, string? languageTag)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"yoink_ocr_{Guid.NewGuid():N}.png");

        try
        {
            bitmap.Save(tmpPath, ImageFormat.Png);

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tmpPath);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBmp = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            // Deterministic engine selection:
            // - explicit language tag: use that engine only (fallback only if it can't be created)
            // - auto: use Windows profile languages (fallback only if unavailable)
            OcrEngine? engine = null;

            if (!string.IsNullOrWhiteSpace(languageTag) &&
                !languageTag.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                engine = TryCreateEngineFromTag(languageTag);
            }

            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine is null)
            {
                var languages = GetAvailableRecognizerLanguages();
                if (languages.Count > 0)
                {
                    try { engine = OcrEngine.TryCreateFromLanguage(languages[0]); }
                    catch { engine = null; }
                }
            }

            if (engine is null)
                return "";

            var result = await engine.RecognizeAsync(softwareBmp);
            return result.Text?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { }
        }
    }

    private static OcrEngine? TryCreateEngineFromTag(string languageTag)
    {
        try
        {
            // Use cache first, but refresh once in case the user installed a language pack while Yoink is running.
            var lang = FindLanguage(languageTag, refresh: false) ?? FindLanguage(languageTag, refresh: true);
            if (lang is null)
                return null;
            return OcrEngine.TryCreateFromLanguage(lang);
        }
        catch
        {
            return null;
        }
    }

    private static Language? FindLanguage(string languageTag, bool refresh)
    {
        var languages = GetAvailableRecognizerLanguages(refresh);
        return languages.FirstOrDefault(l => l.LanguageTag.Equals(languageTag, StringComparison.OrdinalIgnoreCase));
    }

    private static Bitmap ScaleForOcr(Bitmap source)
    {
        int scale = 1;
        if (source.Width < 500 || source.Height < 160)
            scale = Math.Max(2, 1000 / Math.Max(source.Width, 1));

        if (scale <= 1)
            return new Bitmap(source);

        int width = Math.Max(1, source.Width * scale);
        int height = Math.Max(1, source.Height * scale);
        var scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(scaled);
        g.Clear(System.Drawing.Color.White);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height));
        return scaled;
    }

    private static Bitmap PrepareForOcr(Bitmap source)
    {
        var prepared = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(prepared);
        g.Clear(System.Drawing.Color.White);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        BitmapPerf.BoostGrayscaleInPlace(prepared);

        return prepared;
    }

    private static int Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int nonWhitespace = text.Count(c => !char.IsWhiteSpace(c));
        int cjk = text.Count(c => c >= 0x2E80 && c <= 0x9FFF);
        return nonWhitespace + cjk * 4;
    }
}
