using System.Drawing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace Yoink.Services;

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

    public static string? Decode(Bitmap bitmap)
    {
        static string? TryDecode(Bitmap bmp, bool harder, bool inverted)
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
            return reader.Decode(lum)?.Text;
        }

        var text = TryDecode(bitmap, true, true);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        // 1D barcodes often decode better when cropped to a horizontal middle band.
        int bandY = bitmap.Height / 3;
        int bandH = Math.Max(32, bitmap.Height / 3);
        var bandRect = Rectangle.Intersect(new Rectangle(0, bandY, bitmap.Width, bandH), new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (bandRect.Width > 20 && bandRect.Height > 20)
        {
            using var band = bitmap.Clone(bandRect, bitmap.PixelFormat);
            text = TryDecode(band, true, true);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        // Vertical 1D barcodes may need a rotated band pass.
        using (var rotated = (Bitmap)bitmap.Clone())
        {
            rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
            text = TryDecode(rotated, true, true);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            int rBandY = rotated.Height / 3;
            int rBandH = Math.Max(32, rotated.Height / 3);
            var rBandRect = Rectangle.Intersect(new Rectangle(0, rBandY, rotated.Width, rBandH), new Rectangle(0, 0, rotated.Width, rotated.Height));
            if (rBandRect.Width > 20 && rBandRect.Height > 20)
            {
                using var rBand = rotated.Clone(rBandRect, rotated.PixelFormat);
                text = TryDecode(rBand, true, true);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        // Thresholded black/white pass helps faint linear barcodes.
        using (var threshold = ToThreshold(bitmap, 150))
        {
            text = TryDecode(threshold, true, true);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        // Try a 2x upscaled pass for thin 1D barcodes.
        using var scaled = new Bitmap(bitmap.Width * 2, bitmap.Height * 2);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(bitmap, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }
        text = TryDecode(scaled, true, true);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        using var scaledThreshold = ToThreshold(scaled, 150);
        return TryDecode(scaledThreshold, true, true);
    }

    private static Bitmap ToThreshold(Bitmap input, byte threshold)
    {
        var output = new Bitmap(input.Width, input.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < input.Height; y++)
        {
            for (int x = 0; x < input.Width; x++)
            {
                var c = input.GetPixel(x, y);
                int l = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                output.SetPixel(x, y, l >= threshold ? Color.White : Color.Black);
            }
        }
        return output;
    }
}
