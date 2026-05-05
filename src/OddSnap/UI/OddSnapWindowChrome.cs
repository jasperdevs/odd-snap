using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using OddSnap.Native;

namespace OddSnap.UI;

public static class OddSnapWindowChrome
{
    private const double DefaultCornerRadius = 12;

    public static void Apply(Window window)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(DefaultCornerRadius),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });

        ApplyRoundedCorners(window, DefaultCornerRadius);
    }

    public static void ApplyRoundedCorners(Window window, double radius)
    {
        void ApplyCurrentRegion()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            Dwm.TrySetWindowCornerPreference(hwnd, Dwm.DWMWCP_ROUND);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            SetRoundedWindowRegion(window, hwnd, radius);
        }

        window.SourceInitialized += (_, _) => ApplyCurrentRegion();
        window.SizeChanged += (_, _) => ApplyCurrentRegion();
        window.Closed += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                User32.SetWindowRgn(hwnd, IntPtr.Zero, true);
        };
    }

    private static void SetRoundedWindowRegion(Window window, IntPtr hwnd, double radius)
    {
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
            return;

        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * transform.M22));
        int diameterX = Math.Max(1, (int)Math.Round(radius * 2 * transform.M11));
        int diameterY = Math.Max(1, (int)Math.Round(radius * 2 * transform.M22));

        var region = Gdi32.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameterX, diameterY);
        if (region == IntPtr.Zero)
            return;

        if (User32.SetWindowRgn(hwnd, region, true) == 0)
            Gdi32.DeleteObject(region);
    }
}
