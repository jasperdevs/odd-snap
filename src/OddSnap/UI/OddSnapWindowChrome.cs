using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using OddSnap.Native;

namespace OddSnap.UI;

public static class OddSnapWindowChrome
{
    private const double DefaultCornerRadius = 12;

    /// <summary>
    /// Whether OddSnap windows use rounded corners. Set from <c>AppSettings.UseRoundedCorners</c>
    /// during startup and whenever the setting changes; windows opened afterwards pick it up, and
    /// <see cref="Changed"/> lets open windows re-square themselves.
    /// </summary>
    public static bool RoundedCornersEnabled { get; private set; } = true;

    /// <summary>Raised after <see cref="RoundedCornersEnabled"/> flips.</summary>
    public static event Action? Changed;

    /// <summary>Resource key XAML window roots bind their <c>CornerRadius</c> to.</summary>
    public const string WindowCornerRadiusKey = "OddSnapWindowCornerRadius";

    /// <summary>Resource key for the smaller radius used by cards and inner surfaces.</summary>
    public const string CardCornerRadiusKey = "OddSnapCardCornerRadius";

    /// <summary>Resource key for the title bar, which only rounds its top two corners.</summary>
    public const string TitleBarCornerRadiusKey = "OddSnapTitleBarCornerRadius";

    /// <summary>Resource key for the title bar close button, which only rounds its top-right corner.</summary>
    public const string TitleCloseCornerRadiusKey = "OddSnapTitleCloseCornerRadius";

    /// <summary>Resource key for progress bars, which round only their bottom corners.</summary>
    public const string ProgressCornerRadiusKey = "OddSnapProgressCornerRadius";

    public static void SetRoundedCornersEnabled(bool enabled)
    {
        bool changed = RoundedCornersEnabled != enabled;
        RoundedCornersEnabled = enabled;
        PublishCornerResources();
        if (!changed)
            return;

        RefreshOpenWindows();
        Changed?.Invoke();
    }

    /// <summary>Pushes the corner radii into app resources so XAML picks them up via DynamicResource.</summary>
    public static void PublishCornerResources()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        app.Resources[WindowCornerRadiusKey] = WindowRadius();
        app.Resources[CardCornerRadiusKey] = RadiusFor(8);
        app.Resources[TitleBarCornerRadiusKey] = RoundedCornersEnabled
            ? new CornerRadius(11, 11, 0, 0)
            : new CornerRadius(0);
        app.Resources[TitleCloseCornerRadiusKey] = RoundedCornersEnabled
            ? new CornerRadius(0, 11, 0, 0)
            : new CornerRadius(0);
        app.Resources[ProgressCornerRadiusKey] = RoundedCornersEnabled
            ? new CornerRadius(0, 0, 14, 14)
            : new CornerRadius(0);
    }

    /// <summary>Radius for a history card, so the grid squares off with the rest of the chrome.</summary>
    public static double CardRadius() => RoundedCornersEnabled ? 8 : 0;

    /// <summary>Corner radius to use for a window root border, honouring the user's preference.</summary>
    public static CornerRadius RadiusFor(double preferred)
        => new(RoundedCornersEnabled ? preferred : 0);

    public static CornerRadius WindowRadius() => RadiusFor(DefaultCornerRadius);

    public static void Apply(Window window)
    {
        void ApplyWindowChrome()
        {
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = WindowRadius(),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(8),
                UseAeroCaptionButtons = false
            });
        }

        ApplyWindowChrome();
        Changed += ApplyWindowChrome;
        window.Closed += (_, _) => Changed -= ApplyWindowChrome;

        ApplyRoundedCorners(window, DefaultCornerRadius);
    }

    public static void ApplyRoundedCorners(Window window, double radius)
    {
        void ApplyCurrentRegion()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            Dwm.TrySetWindowCornerPreference(
                hwnd,
                RoundedCornersEnabled ? Dwm.DWMWCP_ROUND : Dwm.DWMWCP_DONOTROUND);
            Dwm.TrySetImmersiveDarkMode(hwnd, Theme.IsDark);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            if (!RoundedCornersEnabled)
            {
                User32.SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            SetRoundedWindowRegion(window, hwnd, radius);
        }

        window.SourceInitialized += (_, _) => ApplyCurrentRegion();
        window.SizeChanged += (_, _) => ApplyCurrentRegion();
        Changed += ApplyCurrentRegion;
        window.Closed += (_, _) =>
        {
            Changed -= ApplyCurrentRegion;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                User32.SetWindowRgn(hwnd, IntPtr.Zero, true);
        };
    }

    private static void RefreshOpenWindows()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        foreach (Window window in app.Windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                continue;

            Dwm.TrySetWindowCornerPreference(
                hwnd,
                RoundedCornersEnabled ? Dwm.DWMWCP_ROUND : Dwm.DWMWCP_DONOTROUND);
        }
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
