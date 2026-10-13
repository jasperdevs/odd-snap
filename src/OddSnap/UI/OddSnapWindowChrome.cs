using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using OddSnap.Native;

namespace OddSnap.UI;

public static class OddSnapWindowChrome
{
    private const double DefaultCornerRadius = 7;

    public static void Apply(Window window, double cornerRadius = DefaultCornerRadius)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(cornerRadius),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });

        ApplyRoundedCorners(window, cornerRadius);
    }

    public static void ApplyRoundedCorners(Window window, double radius)
    {
        OddSnapUiCaptureVisibility.Track(window);

        void ApplyCurrentRegion()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            Dwm.TrySetWindowCornerPreference(hwnd, Dwm.DWMWCP_ROUND);
            Dwm.TrySetImmersiveDarkMode(hwnd, Theme.IsDark);

            // On Windows 11 DWM rounds layered windows themselves, so clipping
            // a region on top of that would leave a crescent wherever the two
            // roundings disagree. Only Windows 10 needs the manual region.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            SetRoundedWindowRegion(window, hwnd, radius);
        }

        window.SourceInitialized += (_, _) => ApplyCurrentRegion();
        window.SizeChanged += (_, _) => ApplyCurrentRegion();

        // On Windows 10 the region must be recomputed when the UI scale
        // changes, because the content rounding scales with it.
        void OnUiScaleChanged(double _) => ApplyCurrentRegion();
        UiScale.Changed += OnUiScaleChanged;

        window.Closed += (_, _) =>
        {
            UiScale.Changed -= OnUiScaleChanged;
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

        // The root element is scaled by UiScale via a LayoutTransform, so the
        // visible corner radius is radius * uiScale in layout units.
        var effectiveRadius = radius * UiScale.Current;

        // GDI takes the ellipse *diameter*; WPF's CornerRadius is the radius.
        int diameterX = Math.Max(2, (int)Math.Round(effectiveRadius * 2 * transform.M11));
        int diameterY = Math.Max(2, (int)Math.Round(effectiveRadius * 2 * transform.M22));

        // Use the exact device bounds so the region lines up with the content.
        var region = Gdi32.CreateRoundRectRgn(0, 0, width, height, diameterX, diameterY);
        if (region == IntPtr.Zero)
            return;

        if (User32.SetWindowRgn(hwnd, region, true) == 0)
            Gdi32.DeleteObject(region);
    }
}
