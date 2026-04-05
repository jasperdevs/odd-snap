using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Yoink.Services;

public enum OcrWorkload
{
    Fast = 0,
    Full = 1
}

public static class OcrService
{
    public const string EngineId = "winocr-v1";

    /// <summary>Windows OCR is always ready — no downloads needed.</summary>
    public static bool IsReady() => true;

    /// <summary>Dispose is a no-op for Windows OCR.</summary>
    public static void ClearEngines() { }

    /// <summary>Returns BCP-47 language tags for all installed Windows OCR languages.</summary>
    public static IReadOnlyList<string> GetAvailableRecognizerLanguages(bool refresh = false)
    {
        return OcrEngine.AvailableRecognizerLanguages
            .Select(l => l.LanguageTag)
            .ToList();
    }

    public static async Task<string> RecognizeAsync(Bitmap bitmap, string? languageTag = null, OcrWorkload workload = OcrWorkload.Full)
    {
        return await Task.Run(async () =>
        {
            var engine = CreateEngine(languageTag);
            if (engine == null)
                return "";

            // Convert GDI Bitmap to SoftwareBitmap via in-memory PNG
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            var stream = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(softwareBitmap);
            return result?.Text?.Trim() ?? "";
        }).ConfigureAwait(false);
    }

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        // If specific language requested, try it
        if (!string.IsNullOrWhiteSpace(languageTag) && languageTag != "auto")
        {
            try
            {
                var lang = new Windows.Globalization.Language(languageTag);
                var engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return engine;
            }
            catch { }
        }

        // Auto: use user profile languages, fall back to any available
        var userEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (userEngine != null) return userEngine;

        // Last resort: first available language
        var available = OcrEngine.AvailableRecognizerLanguages;
        if (available.Count > 0)
            return OcrEngine.TryCreateFromLanguage(available[0]);

        return null;
    }
}
