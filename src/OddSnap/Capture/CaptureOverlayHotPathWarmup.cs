using System.Drawing;
using System.Drawing.Imaging;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.Capture;

internal static class CaptureOverlayHotPathWarmup
{
    private static int _warmed;

    public static void Warm()
    {
        if (Interlocked.Exchange(ref _warmed, 1) != 0)
            return;

        try
        {
            using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using var overlay = new RegionOverlayForm(
                bitmap,
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                CaptureMode.Rectangle,
                WindowDetectionMode.Off,
                CenterSelectionAspectRatio.Free)
            {
                ShowCrosshairGuides = true,
                ShowCaptureMagnifier = true,
                DetectWindows = false
            };

            _ = overlay.Handle;
            overlay.PrepareFirstMoveChrome();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("startup.capture-overlay-hot-path-warmup", ex);
        }
    }
}
