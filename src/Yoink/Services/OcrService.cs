using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace Yoink.Services;

public static class OcrService
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        using var scaled = ScaleForOcr(bitmap);
        using var prepared = PrepareForOcr(scaled);

        string best = await DoOcr(scaled);
        string enhanced = await DoOcr(prepared);
        return Score(enhanced) > Score(best) ? enhanced : best;
    }

    private static async Task<string> DoOcr(Bitmap bitmap)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"yoink_ocr_{Guid.NewGuid():N}.png");

        try
        {
            bitmap.Save(tmpPath, ImageFormat.Png);

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tmpPath);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBmp = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var engines = new List<OcrEngine>();
            var profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (profileEngine != null)
                engines.Add(profileEngine);

            var languages = OcrEngine.AvailableRecognizerLanguages;
            foreach (var language in Windows.System.UserProfile.GlobalizationPreferences.Languages
                         .Select(code => new Language(code))
                         .Where(lang => languages.Any(av => av.LanguageTag.Equals(lang.LanguageTag, StringComparison.OrdinalIgnoreCase))))
            {
                var engine = OcrEngine.TryCreateFromLanguage(language);
                if (engine != null && engines.All(e => e.RecognizerLanguage.LanguageTag != engine.RecognizerLanguage.LanguageTag))
                    engines.Add(engine);
            }

            foreach (var language in languages)
            {
                var engine = OcrEngine.TryCreateFromLanguage(language);
                if (engine != null && engines.All(e => e.RecognizerLanguage.LanguageTag != engine.RecognizerLanguage.LanguageTag))
                    engines.Add(engine);
            }

            string bestText = "";
            int bestScore = -1;

            foreach (var engine in engines)
            {
                var result = await engine.RecognizeAsync(softwareBmp);
                var text = result.Text?.Trim() ?? "";
                int score = result.Lines.Count * 100 + text.Length;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestText = text;
                }

                if (score > 0 && engine == profileEngine)
                    return text;
            }

            return bestText;
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

        for (int y = 0; y < prepared.Height; y++)
        for (int x = 0; x < prepared.Width; x++)
        {
            var c = prepared.GetPixel(x, y);
            int lum = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
            int boosted = Math.Clamp((lum - 128) * 2 + 128, 0, 255);
            prepared.SetPixel(x, y, System.Drawing.Color.FromArgb(255, boosted, boosted, boosted));
        }

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
