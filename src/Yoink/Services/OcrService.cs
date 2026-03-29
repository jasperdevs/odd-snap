using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Yoink.Services;

public static class OcrService
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        // Scale up small images for better OCR accuracy
        var bmpToUse = bitmap;
        bool scaled = false;
        if (bitmap.Width < 300 || bitmap.Height < 100)
        {
            int scale = Math.Max(2, 600 / Math.Max(bitmap.Width, 1));
            bmpToUse = new Bitmap(bitmap, bitmap.Width * scale, bitmap.Height * scale);
            scaled = true;
        }

        try
        {
            return await DoOcr(bmpToUse);
        }
        finally
        {
            if (scaled) bmpToUse.Dispose();
        }
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

            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

            if (engine is null) return "";

            var result = await engine.RecognizeAsync(softwareBmp);
            return result.Text ?? "";
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
}
