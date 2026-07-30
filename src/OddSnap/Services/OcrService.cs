using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace OddSnap.Services;

public enum OcrWorkload
{
    Fast = 0,
    Full = 1
}

public static class OcrService
{
    public const string EngineId = "winocr-v1";
    private const int FastWorkloadMaxLongEdge = 1600;
    private const int FullWorkloadMaxLongEdge = 6000;
    private const long FullWorkloadMaxPixels = 12_000_000;
    private const int EastAsianUpscaleMaxLongEdge = 1600;
    private static readonly SemaphoreSlim RecognizeGate = new(1, 1);
    private static readonly object EngineCacheGate = new();
    private static readonly Dictionary<string, OcrEngine> EngineCache = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<string>? AvailableLanguageCache;
    internal readonly record struct OcrLineLayout(string Text, double Left, double Top, double Right, double Bottom)
    {
        public double Width => Math.Max(0, Right - Left);
        public double Height => Math.Max(0, Bottom - Top);
    }

    public static void ClearEngines()
    {
        lock (EngineCacheGate)
        {
            EngineCache.Clear();
            AvailableLanguageCache = null;
        }
    }

    public static void TrimMemory()
    {
        lock (EngineCacheGate)
            EngineCache.Clear();
    }

    /// <summary>Returns BCP-47 language tags for all installed Windows OCR languages.</summary>
    public static IReadOnlyList<string> GetAvailableRecognizerLanguages(bool refresh = false)
    {
        lock (EngineCacheGate)
        {
            if (!refresh && AvailableLanguageCache is not null)
                return AvailableLanguageCache;
        }

        var languages = OcrEngine.AvailableRecognizerLanguages
            .Select(l => l.LanguageTag)
            .ToList();

        lock (EngineCacheGate)
            AvailableLanguageCache = languages;

        return languages;
    }

    public static async Task<string> RecognizeAsync(
        Bitmap bitmap,
        string? languageTag = null,
        OcrWorkload workload = OcrWorkload.Full,
        CancellationToken cancellationToken = default)
    {
        var started = PerformanceTrace.Timestamp();
        await RecognizeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var engine = CreateEngine(languageTag);
                if (engine == null)
                    return "";

                var recognizerLanguage = engine.RecognizerLanguage;
                using var workloadBitmap = CreateWorkloadBitmap(
                    bitmap,
                    workload,
                    recognizerLanguage.LanguageTag);
                var inputBitmap = workloadBitmap ?? bitmap;

                // Convert GDI Bitmap to SoftwareBitmap via in-memory PNG
                using var ms = new MemoryStream();
                CaptureOutputService.WritePng(inputBitmap, ms);
                ms.Position = 0;
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = ms.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                cancellationToken.ThrowIfCancellationRequested();
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                cancellationToken.ThrowIfCancellationRequested();

                var result = await engine.RecognizeAsync(softwareBitmap);
                cancellationToken.ThrowIfCancellationRequested();
                if (result == null)
                    return "";

                var isRightToLeft = recognizerLanguage.LayoutDirection == LanguageLayoutDirection.Rtl;
                var lines = result.Lines
                    .Select(line => CreateLineLayout(line, isRightToLeft))
                    .Where(layout => !string.IsNullOrWhiteSpace(layout.Text))
                    .ToList();

                var formatted = FormatRecognizedText(lines, result.Text, isRightToLeft);
                return NormalizeLanguageSpacing(formatted, recognizerLanguage.LanguageTag);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RecognizeGate.Release();
            PerformanceTrace.LogIfSlow(
                "perf.ocr.recognize",
                started,
                TimeSpan.FromMilliseconds(250),
                $"{bitmap.Width}x{bitmap.Height} workload={workload}");
        }
    }

    internal static string FormatRecognizedText(
        IReadOnlyList<OcrLineLayout> lines,
        string? fallbackText = null,
        bool rightToLeft = false)
    {
        if (lines.Count == 0)
            return fallbackText?.Trim() ?? "";

        var ordered = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.Top)
            .ThenBy(line => rightToLeft ? -line.Right : line.Left)
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

        double baselineEdge = rightToLeft
            ? ordered.Max(line => line.Right)
            : ordered.Min(line => line.Left);
        double baselineWindow = Math.Max(medianCharWidth * 2, 8);
        var baselineCandidates = ordered
            .Select(line => rightToLeft ? line.Right : line.Left)
            .Where(edge => Math.Abs(edge - baselineEdge) <= baselineWindow)
            .ToList();
        double baseline = baselineCandidates.Count > 0 ? baselineCandidates.Average() : baselineEdge;

        var builder = new StringBuilder();
        OcrLineLayout? previous = null;

        foreach (var line in ordered)
        {
            var text = line.Text.Trim();
            if (text.Length == 0)
                continue;

            bool paragraphBreak = previous is OcrLineLayout prior && (line.Top - prior.Bottom) > Math.Max(medianHeight * 0.85, 8);
            int indentSpaces = ComputeIndentSpaces(
                rightToLeft ? baseline - line.Right : line.Left - baseline,
                medianCharWidth);
            int previousIndent = previous is OcrLineLayout previousLine
                ? ComputeIndentSpaces(
                    rightToLeft ? baseline - previousLine.Right : previousLine.Left - baseline,
                    medianCharWidth)
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

    private static OcrLineLayout CreateLineLayout(OcrLine line, bool rightToLeft)
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

        var text = rightToLeft
            ? OrderRightToLeftWords(line.Words.Select(word => new OcrWordLayout(word.Text ?? "", word.BoundingRect.X)).ToList())
            : line.Text ?? "";
        return new OcrLineLayout(text, left, top, right, bottom);
    }

    internal readonly record struct OcrWordLayout(string Text, double Left);

    internal static string OrderRightToLeftWords(IReadOnlyList<OcrWordLayout> words)
    {
        var ordered = words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.Left)
            .ToList();

        for (var index = 0; index < ordered.Count;)
        {
            if (GetStrongDirection(ordered[index].Text) == StrongTextDirection.RightToLeft)
            {
                index++;
                continue;
            }

            var runStart = index;
            var containsLeftToRight = false;
            while (index < ordered.Count && GetStrongDirection(ordered[index].Text) != StrongTextDirection.RightToLeft)
            {
                containsLeftToRight |= GetStrongDirection(ordered[index].Text) == StrongTextDirection.LeftToRight;
                index++;
            }

            if (containsLeftToRight)
                ordered.Reverse(runStart, index - runStart);
        }

        return string.Join(' ', ordered.Select(word => word.Text.Trim()));
    }

    internal static string NormalizeLanguageSpacing(string text, string? languageTag)
    {
        if (string.IsNullOrEmpty(text) || !IsJapaneseOrChinese(languageTag))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] is not (' ' or '\t'))
            {
                builder.Append(text[index]);
                index++;
                continue;
            }

            var whitespaceStart = index;
            while (index < text.Length && text[index] is ' ' or '\t')
                index++;

            var previous = whitespaceStart > 0 ? text[whitespaceStart - 1] : '\0';
            var next = index < text.Length ? text[index] : '\0';
            if (!IsCjkScriptCharacter(previous) || !IsCjkScriptCharacter(next))
                builder.Append(text, whitespaceStart, index - whitespaceStart);
        }

        return builder.ToString();
    }

    private enum StrongTextDirection
    {
        Neutral,
        LeftToRight,
        RightToLeft
    }

    private static StrongTextDirection GetStrongDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsRightToLeftCodePoint(rune.Value))
                return StrongTextDirection.RightToLeft;
            if (Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.UppercaseLetter
                or System.Globalization.UnicodeCategory.LowercaseLetter
                or System.Globalization.UnicodeCategory.TitlecaseLetter
                or System.Globalization.UnicodeCategory.ModifierLetter
                or System.Globalization.UnicodeCategory.OtherLetter)
            {
                return StrongTextDirection.LeftToRight;
            }
        }

        return StrongTextDirection.Neutral;
    }

    private static bool IsRightToLeftCodePoint(int value)
        => value is >= 0x0590 and <= 0x08FF
            or >= 0xFB1D and <= 0xFDFF
            or >= 0xFE70 and <= 0xFEFF
            or >= 0x1EE00 and <= 0x1EEFF;

    private static bool IsJapaneseOrChinese(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
            return false;

        var neutral = languageTag.Split('-', 2)[0];
        return neutral.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || neutral.Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEastAsianLanguage(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
            return false;

        var neutral = languageTag.Split('-', 2)[0];
        return neutral.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || neutral.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || neutral.Equals("ko", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCjkScriptCharacter(char value)
        => value is >= '\u3000' and <= '\u30FF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFF01' and <= '\uFF9D';

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
        var cacheKey = GetEngineCacheKey(languageTag);
        lock (EngineCacheGate)
        {
            if (EngineCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var engine = CreateEngineUncached(languageTag);
        if (engine is not null)
        {
            lock (EngineCacheGate)
                EngineCache[cacheKey] = engine;
        }

        return engine;
    }

    private static Bitmap? CreateWorkloadBitmap(Bitmap bitmap, OcrWorkload workload, string? languageTag)
    {
        var longEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (workload == OcrWorkload.Fast)
        {
            return longEdge > FastWorkloadMaxLongEdge
                ? CaptureOutputService.PrepareBitmap(bitmap, FastWorkloadMaxLongEdge)
                : null;
        }

        var pixelCount = (long)bitmap.Width * bitmap.Height;
        if (ShouldUpscaleEastAsian(bitmap.Width, bitmap.Height, workload, languageTag))
            return ResizeBitmap(bitmap, scale: 2);

        if (pixelCount <= FullWorkloadMaxPixels && longEdge <= FullWorkloadMaxLongEdge)
            return null;

        var pixelCappedLongEdge = CalculatePixelCappedLongEdge(bitmap.Width, bitmap.Height, FullWorkloadMaxPixels);
        var targetLongEdge = Math.Min(FullWorkloadMaxLongEdge, pixelCappedLongEdge);
        return targetLongEdge < longEdge
            ? CaptureOutputService.PrepareBitmap(bitmap, targetLongEdge)
            : null;
    }

    internal static bool ShouldUpscaleEastAsian(
        int width,
        int height,
        OcrWorkload workload,
        string? languageTag)
    {
        if (workload != OcrWorkload.Full || width <= 0 || height <= 0 || !IsEastAsianLanguage(languageTag))
            return false;

        var longEdge = Math.Max(width, height);
        var upscaledPixels = (long)width * height * 4;
        return longEdge <= EastAsianUpscaleMaxLongEdge && upscaledPixels <= FullWorkloadMaxPixels;
    }

    private static Bitmap ResizeBitmap(Bitmap source, int scale)
    {
        var resized = new Bitmap(
            checked(source.Width * scale),
            checked(source.Height * scale),
            PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, resized.Width, resized.Height));
        return resized;
    }

    private static int CalculatePixelCappedLongEdge(int width, int height, long maxPixels)
    {
        var pixelCount = (long)Math.Max(1, width) * Math.Max(1, height);
        var longEdge = Math.Max(width, height);
        if (pixelCount <= maxPixels)
            return longEdge;

        var scale = Math.Sqrt(maxPixels / (double)pixelCount);
        return Math.Max(1, (int)Math.Floor(longEdge * scale));
    }

    private static string GetEngineCacheKey(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag) && languageTag != "auto")
            return languageTag.Trim().ToLowerInvariant();

        try
        {
            return "auto:" + LocalizationService.ResolveContentLanguageCode();
        }
        catch
        {
            return "auto";
        }
    }

    private static OcrEngine? CreateEngineUncached(string? languageTag)
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

        // Auto: prefer the active app/system UI language when installed, then user profile languages.
        try
        {
            var uiLanguage = LocalizationService.ResolveContentLanguageCode();
            if (!string.IsNullOrWhiteSpace(uiLanguage))
            {
                var lang = new Windows.Globalization.Language(uiLanguage);
                var engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return engine;
            }
        }
        catch { }

        var userEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (userEngine != null) return userEngine;

        // Last resort: first available language
        var available = OcrEngine.AvailableRecognizerLanguages;
        if (available.Count > 0)
            return OcrEngine.TryCreateFromLanguage(available[0]);

        return null;
    }
}
