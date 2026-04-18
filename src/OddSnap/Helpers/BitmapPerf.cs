using System.Drawing;
using System.Drawing.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OddSnap.Helpers;

internal static class BitmapPerf
{
    public static Bitmap Clone32bppArgb(Bitmap source)
    {
        if (source.PixelFormat == DrawingPixelFormat.Format32bppArgb)
            return new Bitmap(source);

        var clone = new Bitmap(source.Width, source.Height, DrawingPixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return clone;
    }

    public static BitmapSource ToBitmapSource(Bitmap source)
    {
        Bitmap? ownedClone = null;
        var bitmap = source;
        if (source.PixelFormat != DrawingPixelFormat.Format32bppArgb)
        {
            ownedClone = Clone32bppArgb(source);
            bitmap = ownedClone;
        }

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        try
        {
            var src = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);
            src.Freeze();
            return src;
        }
        finally
        {
            bitmap.UnlockBits(data);
            ownedClone?.Dispose();
        }
    }

    public static unsafe void BoostGrayscaleInPlace(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, DrawingPixelFormat.Format32bppArgb);
        try
        {
            byte* basePtr = (byte*)data.Scan0;
            int stride = data.Stride;

            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = basePtr + (y * stride);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte* px = row + (x * 4);
                    byte b = px[0];
                    byte g = px[1];
                    byte r = px[2];

                    int lum = (int)(r * 0.299 + g * 0.587 + b * 0.114);
                    int boosted = Math.Clamp((lum - 128) * 2 + 128, 0, 255);
                    byte v = (byte)boosted;

                    px[0] = v;
                    px[1] = v;
                    px[2] = v;
                    px[3] = 255;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static unsafe Bitmap CleanupTransparentPixels(Bitmap source, byte alphaThreshold)
    {
        var cleaned = Clone32bppArgb(source);
        var rect = new Rectangle(0, 0, cleaned.Width, cleaned.Height);
        var data = cleaned.LockBits(rect, ImageLockMode.ReadWrite, DrawingPixelFormat.Format32bppArgb);

        try
        {
            byte* basePtr = (byte*)data.Scan0;
            int stride = data.Stride;

            for (int y = 0; y < cleaned.Height; y++)
            {
                byte* row = basePtr + (y * stride);
                for (int x = 0; x < cleaned.Width; x++)
                {
                    byte* px = row + (x * 4);
                    if (px[3] <= alphaThreshold)
                        px[0] = px[1] = px[2] = px[3] = 0;
                }
            }
        }
        finally
        {
            cleaned.UnlockBits(data);
        }

        return cleaned;
    }

    public static unsafe Bitmap TrimTransparentBounds(Bitmap source, byte alphaThreshold)
    {
        var normalized = Clone32bppArgb(source);
        var rect = new Rectangle(0, 0, normalized.Width, normalized.Height);
        var data = normalized.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
        Rectangle? crop = null;
        bool isEmpty = false;

        try
        {
            byte* basePtr = (byte*)data.Scan0;
            int stride = data.Stride;
            int minX = normalized.Width;
            int minY = normalized.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < normalized.Height; y++)
            {
                byte* row = basePtr + (y * stride);
                for (int x = 0; x < normalized.Width; x++)
                {
                    if (row[(x * 4) + 3] <= alphaThreshold)
                        continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                isEmpty = true;
            }
            else
            {
                crop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }

        if (isEmpty || crop is null)
            return normalized;

        return normalized.Clone(crop.Value, DrawingPixelFormat.Format32bppArgb);
    }
}
