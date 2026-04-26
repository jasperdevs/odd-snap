using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using OddSnap.Models;

namespace OddSnap.Services;

public static class CaptureOutputService
{
    private static readonly ImageCodecInfo? PngEncoder =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/png");

    public static Bitmap PrepareBitmap(Bitmap source, int maxLongEdge)
    {
        if (maxLongEdge <= 0)
            return new Bitmap(source);

        int longest = Math.Max(source.Width, source.Height);
        if (longest <= maxLongEdge)
            return new Bitmap(source);

        double scale = maxLongEdge / (double)longest;
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return resized;
    }

    public static string GetExtension(CaptureImageFormat format) => format switch
    {
        CaptureImageFormat.Jpeg => "jpg",
        CaptureImageFormat.Bmp => "bmp",
        _ => "png"
    };

    public static void SaveBitmap(Bitmap bitmap, string filePath, CaptureImageFormat format, int jpegQuality)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        switch (format)
        {
            case CaptureImageFormat.Jpeg:
            {
                var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(jpegQuality, 1, 100));
                SaveWithAtomicWrite(bitmap, filePath, (bmp, path) => bmp.Save(path, encoder, parameters));
                break;
            }
            case CaptureImageFormat.Bmp:
                SaveWithAtomicWrite(bitmap, filePath, (bmp, path) => bmp.Save(path, ImageFormat.Bmp));
                break;
            default:
                SaveWithAtomicWrite(bitmap, filePath, SavePngCore);
                break;
        }
    }

    public static void SavePng(Bitmap bitmap, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        SaveWithAtomicWrite(bitmap, filePath, SavePngCore);
    }

    public static void WritePng(Bitmap bitmap, Stream stream) => SavePngCore(bitmap, stream);

    public static string SaveBitmapToTempPng(Bitmap bitmap, string fileNamePrefix)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{fileNamePrefix}_{Guid.NewGuid():N}.png");
        try
        {
            SavePngCore(bitmap, tempPath);
            return tempPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static void SaveWithAtomicWrite(Bitmap bitmap, string filePath, Action<Bitmap, string> saveAction)
    {
        var tmpPath = filePath + ".tmp";
        try
        {
            saveAction(bitmap, tmpPath);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception) when (File.Exists(tmpPath))
        {
            try { File.Delete(tmpPath); } catch { }
            saveAction(bitmap, filePath);
        }
    }

    private static void SavePngCore(Bitmap bitmap, string filePath)
    {
        if (PngEncoder is null)
            throw new InvalidOperationException("PNG encoder is not available.");

        using var parameters = CreatePngEncoderParameters();
        bitmap.Save(filePath, PngEncoder, parameters);
    }

    private static void SavePngCore(Bitmap bitmap, Stream stream)
    {
        if (PngEncoder is null)
            throw new InvalidOperationException("PNG encoder is not available.");

        using var parameters = CreatePngEncoderParameters();
        bitmap.Save(stream, PngEncoder, parameters);
    }

    private static EncoderParameters CreatePngEncoderParameters()
    {
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Compression, 6L);
        return parameters;
    }
}
