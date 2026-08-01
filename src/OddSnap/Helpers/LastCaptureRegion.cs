using System.Drawing;
using OddSnap.Models;

namespace OddSnap.Helpers;

internal static class LastCaptureRegion
{
    public static void Store(AppSettings settings, Rectangle region)
    {
        settings.LastCaptureRegionX = region.X;
        settings.LastCaptureRegionY = region.Y;
        settings.LastCaptureRegionWidth = region.Width;
        settings.LastCaptureRegionHeight = region.Height;
    }

    public static Rectangle Resolve(AppSettings settings, Rectangle virtualScreenBounds)
    {
        if (settings.LastCaptureRegionWidth <= 1 || settings.LastCaptureRegionHeight <= 1)
            return Rectangle.Empty;

        long left = Math.Max((long)settings.LastCaptureRegionX, virtualScreenBounds.Left);
        long top = Math.Max((long)settings.LastCaptureRegionY, virtualScreenBounds.Top);
        long right = Math.Min(
            (long)settings.LastCaptureRegionX + settings.LastCaptureRegionWidth,
            virtualScreenBounds.Right);
        long bottom = Math.Min(
            (long)settings.LastCaptureRegionY + settings.LastCaptureRegionHeight,
            virtualScreenBounds.Bottom);

        if (right - left <= 1 || bottom - top <= 1)
            return Rectangle.Empty;

        return new Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }
}
