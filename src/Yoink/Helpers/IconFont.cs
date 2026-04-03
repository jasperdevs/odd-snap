using System.Drawing;
using System.Drawing.Text;
using System.Reflection;

namespace Yoink.Helpers;

/// <summary>
/// Shared helper to load the embedded Lucide icon font for use in WinForms contexts
/// (tray menu, toolbar, overlays). WPF contexts should use the XAML FontFamily reference instead.
/// </summary>
public static class IconFont
{
    private static PrivateFontCollection? _collection;
    private static FontFamily? _family;
    private static byte[]? _fontData;

    /// <summary>Gets the Lucide icon FontFamily, loading from embedded resource on first call.</summary>
    public static FontFamily Family => _family ??= LoadFamily();

    /// <summary>Creates a new Font instance at the given size (in points).</summary>
    public static Font Create(float sizePoints) =>
        new(Family, sizePoints, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Render a lucide glyph to a WPF BitmapSource. Uses PrivateFontCollection so ALL codepoints work.</summary>
    public static System.Windows.Media.Imaging.BitmapSource RenderGlyphWpf(char glyph, System.Drawing.Color color, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = Create(size * 0.55f);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), sf);
        }
        return BitmapPerf.ToBitmapSource(bmp);
    }

    private static FontFamily LoadFamily()
    {
        _collection = new PrivateFontCollection();

        // Try embedded resource first
        const string resourceName = "Yoink.lucide.ttf";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            _fontData = new byte[checked((int)stream.Length)];
            int offset = 0;
            while (offset < _fontData.Length)
            {
                int read = stream.Read(_fontData, offset, _fontData.Length - offset);
                if (read <= 0) break;
                offset += read;
            }

            if (offset == _fontData.Length)
            {
                unsafe
                {
                    fixed (byte* ptr = _fontData)
                        _collection.AddMemoryFont((nint)ptr, _fontData.Length);
                }
            }
        }

        if (_collection.Families.Length > 0)
        {
            _fontData = null; // free byte array, font is loaded
            return _collection.Families[0];
        }

        // Fallback: file in output directory
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "lucide.ttf");
        if (System.IO.File.Exists(path))
        {
            _collection.AddFontFile(path);
            if (_collection.Families.Length > 0)
                return _collection.Families[0];
        }

        // Last resort
        return new FontFamily("Segoe UI");
    }
}
