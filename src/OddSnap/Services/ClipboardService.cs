using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Specialized;

namespace OddSnap.Services;

public static class ClipboardService
{
    private static readonly ImageCodecInfo? PngEncoder =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/png");

    public static void CopyToClipboard(Bitmap bitmap, string? filePath = null)
    {
        var dataObject = new System.Windows.Forms.DataObject();

        dataObject.SetData(System.Windows.Forms.DataFormats.Bitmap, bitmap);

        using var pngStream = new MemoryStream();
        if (PngEncoder is not null)
        {
            using var enc = new EncoderParameters(1);
            enc.Param[0] = new EncoderParameter(Encoder.Compression, 6L);
            bitmap.Save(pngStream, PngEncoder, enc);
        }
        else
        {
            bitmap.Save(pngStream, ImageFormat.Png);
        }
        if (pngStream.TryGetBuffer(out var pngBuffer))
            dataObject.SetData("PNG", false, new MemoryStream(pngBuffer.Array!, pngBuffer.Offset, pngBuffer.Count, writable: false, publiclyVisible: true));
        else
            dataObject.SetData("PNG", false, new MemoryStream(pngStream.ToArray(), writable: false));

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            dataObject.SetFileDropList(new StringCollection { filePath });
        }

        SetClipboardWithRetry(dataObject);
    }

    public static void CopyTextToClipboard(string text)
    {
        var dataObject = new System.Windows.Forms.DataObject();
        dataObject.SetData(System.Windows.Forms.DataFormats.UnicodeText, false, text);
        dataObject.SetData(System.Windows.Forms.DataFormats.Text, false, text);

        SetClipboardWithRetry(dataObject);
    }

    private static void SetClipboardWithRetry(System.Windows.Forms.DataObject dataObject, int maxRetries = 3)
    {
        Exception? lastError = null;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch (Exception) when (i < maxRetries - 1)
            {
                lastError = null;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            AppDiagnostics.LogWarning("clipboard.set", "Failed to write to clipboard after retries.", lastError);
    }
}
