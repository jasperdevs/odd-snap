using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Specialized;

namespace Yoink.Services;

public static class ClipboardService
{
    private static readonly ImageCodecInfo? PngEncoder =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/png");

    public static void CopyToClipboard(Bitmap bitmap, string? filePath = null)
    {
        // Use WinForms clipboard since we may be called from a WinForms context.
        // SetImage handles the format conversion automatically.
        // We also add PNG format for apps that support it (e.g. Discord, Slack).
        var dataObject = new System.Windows.Forms.DataObject();

        // Add as standard bitmap
        dataObject.SetData(System.Windows.Forms.DataFormats.Bitmap, bitmap);

        // Add as PNG stream for better compatibility
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
        dataObject.SetData("PNG", false, new MemoryStream(pngStream.ToArray()));

        // Also include the original file when available. Some web apps treat
        // pasted image files more reliably than raw bitmap clipboard formats.
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            dataObject.SetFileDropList(new StringCollection { filePath });
        }

        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
        }
        catch (Exception)
        {
            // Clipboard may be locked by another application - retry once
            Thread.Sleep(50);
            try { System.Windows.Forms.Clipboard.SetDataObject(dataObject, true); }
            catch { }
        }
    }

    public static void CopyTextToClipboard(string text)
    {
        var dataObject = new System.Windows.Forms.DataObject();
        dataObject.SetData(System.Windows.Forms.DataFormats.UnicodeText, false, text);
        dataObject.SetData(System.Windows.Forms.DataFormats.Text, false, text);

        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
        }
        catch (Exception)
        {
            Thread.Sleep(50);
            try { System.Windows.Forms.Clipboard.SetDataObject(dataObject, true); }
            catch { }
        }
    }
}
