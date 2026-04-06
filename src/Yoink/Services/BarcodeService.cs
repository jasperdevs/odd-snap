using System.Drawing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace Yoink.Services;

public sealed record BarcodeDecodeResult(string Text, BarcodeFormat Format);

public static class BarcodeService
{
    private static readonly List<BarcodeFormat> Formats = new()
    {
        BarcodeFormat.QR_CODE,
        BarcodeFormat.AZTEC,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.CODE_93,
        BarcodeFormat.CODABAR,
        BarcodeFormat.ITF,
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E
    };

    public static string? Decode(Bitmap bitmap) => DecodeDetailed(bitmap)?.Text;

    public static BarcodeDecodeResult? DecodeDetailed(Bitmap bitmap)
    {
        static BarcodeDecodeResult? TryDecode(Bitmap bmp, bool harder, bool inverted)
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = harder,
                    TryInverted = inverted,
                    PossibleFormats = Formats
                }
            };
            var lum = new BitmapLuminanceSource(bmp);
            var result = reader.Decode(lum);
            return result is null || string.IsNullOrWhiteSpace(result.Text)
                ? null
                : new BarcodeDecodeResult(result.Text, result.BarcodeFormat);
        }

        var result = TryDecode(bitmap, true, true);
        if (result is not null) return result;

        // 1D barcodes often decode better when cropped to a horizontal middle band.
        int bandY = bitmap.Height / 3;
        int bandH = Math.Max(32, bitmap.Height / 3);
        var bandRect = Rectangle.Intersect(new Rectangle(0, bandY, bitmap.Width, bandH), new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (bandRect.Width > 20 && bandRect.Height > 20)
        {
            using var band = bitmap.Clone(bandRect, bitmap.PixelFormat);
            result = TryDecode(band, true, true);
            if (result is not null) return result;
        }

        // Vertical 1D barcodes may need a rotated band pass.
            using (var rotated = (Bitmap)bitmap.Clone())
        {
            rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
            result = TryDecode(rotated, true, true);
            if (result is not null) return result;

            int rBandY = rotated.Height / 3;
            int rBandH = Math.Max(32, rotated.Height / 3);
            var rBandRect = Rectangle.Intersect(new Rectangle(0, rBandY, rotated.Width, rBandH), new Rectangle(0, 0, rotated.Width, rotated.Height));
            if (rBandRect.Width > 20 && rBandRect.Height > 20)
            {
                using var rBand = rotated.Clone(rBandRect, rotated.PixelFormat);
                result = TryDecode(rBand, true, true);
                if (result is not null) return result;
            }
        }

        // Thresholded black/white pass helps faint linear barcodes.
        using (var threshold = ToThreshold(bitmap, 150))
        {
            result = TryDecode(threshold, true, true);
            if (result is not null) return result;
        }

        // Try a 2x upscaled pass for thin 1D barcodes.
        using var scaled = new Bitmap(bitmap.Width * 2, bitmap.Height * 2);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(bitmap, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }
        result = TryDecode(scaled, true, true);
        if (result is not null) return result;

        using var scaledThreshold = ToThreshold(scaled, 150);
        return TryDecode(scaledThreshold, true, true);
    }

    public static Bitmap RenderPreview(string text, BarcodeFormat format)
    {
        var options = format == BarcodeFormat.QR_CODE
            ? new EncodingOptions { Width = 200, Height = 200, Margin = 1, PureBarcode = true }
            : new EncodingOptions { Width = 360, Height = 120, Margin = 4, PureBarcode = true };

        var writer = new BarcodeWriter
        {
            Format = format,
            Options = options
        };

        return writer.Write(text);
    }

    private static unsafe Bitmap ToThreshold(Bitmap input, byte threshold)
    {
        int w = input.Width, h = input.Height;
        var output = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var srcData = input.LockBits(new Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var dstData = output.LockBits(new Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                byte* src = (byte*)srcData.Scan0 + y * srcData.Stride;
                byte* dst = (byte*)dstData.Scan0 + y * dstData.Stride;
                for (int x = 0; x < w; x++)
                {
                    int off = x * 4;
                    int l = (src[off + 2] * 299 + src[off + 1] * 587 + src[off] * 114) / 1000;
                    uint pixel = l >= threshold ? 0xFFFFFFFFu : 0xFF000000u;
                    *(uint*)(dst + off) = pixel;
                }
            }
        }
        finally
        {
            input.UnlockBits(srcData);
            output.UnlockBits(dstData);
        }
        return output;
    }
}
