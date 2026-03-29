using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.WIC;
using Vortice.DXGI;
using PixelFormat = Vortice.DCommon.PixelFormat;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace Yoink.Capture;

/// <summary>
/// Renders real color emoji using Direct2D with D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT.
/// This is the ONLY way to get color emoji on Windows - GDI, GDI+, and WPF all render monochrome.
/// </summary>
public sealed class EmojiRenderer : IDisposable
{
    private readonly IWICImagingFactory2 _wicFactory;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly Dictionary<(string emoji, int sizeKey), Bitmap> _cache = new();
    private const int MaxCacheSize = 512;

    public EmojiRenderer()
    {
        _wicFactory = new IWICImagingFactory2();
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
    }

    public Bitmap GetEmoji(string emoji, float size)
    {
        int sizeKey = (int)MathF.Round(size * 10);
        if (_cache.TryGetValue((emoji, sizeKey), out var cached))
            return cached;

        if (_cache.Count >= MaxCacheSize)
        {
            foreach (var bmp in _cache.Values)
                bmp.Dispose();
            _cache.Clear();
        }

        var rendered = Render(emoji, size);
        _cache[(emoji, sizeKey)] = rendered;
        return rendered;
    }

    private Bitmap Render(string emoji, float size)
    {
        uint w = (uint)(size * 1.4f) + 4;
        uint h = w;

        using var wicBitmap = _wicFactory.CreateBitmap(w, h,
            Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);

        var rtProps = new RenderTargetProperties
        {
            Type = RenderTargetType.Default,
            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96,
        };
        using var rt = _d2dFactory.CreateWicBitmapRenderTarget(wicBitmap, rtProps);
        using var textFormat = _dwFactory.CreateTextFormat("Segoe UI Emoji", size);
        using var brush = rt.CreateSolidColorBrush(new Vortice.Mathematics.Color4(1, 1, 1, 1));

        rt.BeginDraw();
        rt.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0)); // transparent background

        var rect = new Vortice.Mathematics.Rect(0, 0, (float)w, (float)h);
        rt.DrawText(emoji, textFormat, rect, brush, DrawTextOptions.EnableColorFont);

        rt.EndDraw();

        // Copy WIC bitmap to GDI+ Bitmap
        var pixels = new byte[w * h * 4];
        wicBitmap.CopyPixels(w * 4, pixels);
        var result = new Bitmap((int)w, (int)h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var bits = result.LockBits(new Rectangle(0, 0, (int)w, (int)h),
            ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
        result.UnlockBits(bits);
        return result;
    }

    public void Dispose()
    {
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
        _wicFactory.Dispose();
    }
}
