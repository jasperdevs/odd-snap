using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Yoink.Services;

public static class OcrService
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        // Convert System.Drawing.Bitmap to PNG bytes
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        // Write to WinRT InMemoryRandomAccessStream
        var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
        }
        ras.Seek(0);

        // Decode to SoftwareBitmap
        var decoder = await BitmapDecoder.CreateAsync(ras);
        var softwareBmp = await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        // Run OCR
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            // Fallback to English
            engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        }

        if (engine is null)
            return "";

        var result = await engine.RecognizeAsync(softwareBmp);
        return result.Text ?? "";
    }
}
