using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private static readonly SemaphoreSlim RecognizeGate = new(1, 1);
    internal readonly record struct OcrLineLayout(string Text, double Left, double Top, double Right, double Bottom)
    {
        public double Width => Math.Max(0, Right - Left);
        public double Height => Math.Max(0, Bottom - Top);
    }

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
        await RecognizeGate.WaitAsync().ConfigureAwait(false);
        try
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
                if (result == null)
                    return "";

                var lines = result.Lines
                    .Select(CreateLineLayout)
                    .Where(layout => !string.IsNullOrWhiteSpace(layout.Text))
                    .ToList();

                return FormatRecognizedText(lines, result.Text);
            }).ConfigureAwait(false);
        }
        finally
        {
            RecognizeGate.Release();
        }
    }

    internal static string FormatRecognizedText(IReadOnlyList<OcrLineLayout> lines, string? fallbackText = null)
    {
        if (lines.Count == 0)
            return fallbackText?.Trim() ?? "";

        var ordered = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.Top)
            .ThenBy(line => line.Left)
            .ToList();

        if (ordered.Count == 0)
            return fallbackText?.Trim() ?? "";

        double medianHeight = Median(ordered.Select(line => line.Height).Where(value => value > 0));
        if (medianHeight <= 0)
            medianHeight = 16;

        double medianCharWidth = Median(ordered
            .Select(line =>
            {
                var length = line.Text.Trim().Length;
                return length == 0 || line.Width <= 0 ? 0 : line.Width / length;
            })
            .Where(value => value > 0));
        if (medianCharWidth <= 0)
            medianCharWidth = Math.Max(6, medianHeight * 0.45);

        double minLeft = ordered.Min(line => line.Left);
        double baselineWindow = Math.Max(medianCharWidth * 2, 8);
        var baselineCandidates = ordered
            .Select(line => line.Left)
            .Where(left => left - minLeft <= baselineWindow)
            .ToList();
        double baselineLeft = baselineCandidates.Count > 0 ? baselineCandidates.Average() : minLeft;

        var builder = new StringBuilder();
        OcrLineLayout? previous = null;

        foreach (var line in ordered)
        {
            var text = line.Text.Trim();
            if (text.Length == 0)
                continue;

            bool paragraphBreak = previous is OcrLineLayout prior && (line.Top - prior.Bottom) > Math.Max(medianHeight * 0.85, 8);
            int indentSpaces = ComputeIndentSpaces(line.Left - baselineLeft, medianCharWidth);
            int previousIndent = previous is OcrLineLayout previousLine
                ? ComputeIndentSpaces(previousLine.Left - baselineLeft, medianCharWidth)
                : 0;

            bool paragraphStart = previous == null || paragraphBreak || indentSpaces >= previousIndent + 2;

            if (builder.Length > 0)
                builder.Append(paragraphStart ? Environment.NewLine + Environment.NewLine : Environment.NewLine);

            if (paragraphStart && indentSpaces >= 2)
                builder.Append(' ', Math.Clamp(indentSpaces, 2, 8));

            builder.Append(text);
            previous = line;
        }

        return builder.ToString().Trim();
    }

    private static OcrLineLayout CreateLineLayout(OcrLine line)
    {
        if (line.Words == null || line.Words.Count == 0)
            return new OcrLineLayout(line.Text ?? "", 0, 0, 0, 0);

        double left = double.MaxValue;
        double top = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;

        foreach (var word in line.Words)
        {
            var rect = word.BoundingRect;
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        if (left == double.MaxValue || top == double.MaxValue || right == double.MinValue || bottom == double.MinValue)
            return new OcrLineLayout(line.Text ?? "", 0, 0, 0, 0);

        return new OcrLineLayout(line.Text ?? "", left, top, right, bottom);
    }

    private static int ComputeIndentSpaces(double indentPixels, double medianCharWidth)
    {
        if (indentPixels <= 0 || medianCharWidth <= 0)
            return 0;

        return (int)Math.Round(indentPixels / medianCharWidth, MidpointRounding.AwayFromZero);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return 0;

        int mid = ordered.Length / 2;
        if ((ordered.Length & 1) == 1)
            return ordered[mid];

        return (ordered[mid - 1] + ordered[mid]) / 2d;
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
