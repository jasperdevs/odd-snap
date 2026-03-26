using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Yoink.Services;

public static class ClipboardService
{
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
        bitmap.Save(pngStream, ImageFormat.Png);
        dataObject.SetData("PNG", false, new MemoryStream(pngStream.ToArray()));

        System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
    }
}
