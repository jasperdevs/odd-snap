using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Yoink.Services;

public static class ClipboardService
{
    private static readonly ImageCodecInfo? PngEncoder =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/png");

    public static void CopyToClipboard(Bitmap bitmap)
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

        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard may be locked by another application - retry once
            Thread.Sleep(50);
            try { System.Windows.Forms.Clipboard.SetDataObject(dataObject, true); }
            catch { }
        }
    }
}
