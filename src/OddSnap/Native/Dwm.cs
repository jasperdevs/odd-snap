using System.Runtime.InteropServices;

namespace OddSnap.Native;

internal static partial class Dwm
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    public const int DWMSBT_NONE = 1;        // No system backdrop
    public const int DWMSBT_MAINWINDOW = 2;  // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;  // Tabbed

    /// <summary>Disable system backdrop (Mica/Acrylic) on a window.</summary>
    public static void DisableBackdrop(IntPtr hwnd)
    {
        if (!SupportsSystemBackdropType())
            return;

        int val = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref val, sizeof(int));
    }

    public static void TrySetWindowCornerPreference(IntPtr hwnd, int preference)
    {
        if (!SupportsWindowCornerPreference())
            return;

        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    public static void TrySetImmersiveDarkMode(IntPtr hwnd, bool enabled)
    {
        if (!SupportsImmersiveDarkMode())
            return;

        int value = enabled ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute, out User32.RECT pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>Get the visual (DWM extended) frame bounds, falling back to GetWindowRect.</summary>
    public static System.Drawing.Rectangle GetExtendedFrameBounds(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
            out User32.RECT rect, System.Runtime.InteropServices.Marshal.SizeOf<User32.RECT>());
        if (hr == 0)
            return rect.ToRectangle();

        if (User32.GetWindowRect(hwnd, out var wr))
            return wr.ToRectangle();

        return System.Drawing.Rectangle.Empty;
    }

    /// <summary>Check if a window is cloaked (hidden UWP/virtual-desktop window).</summary>
    public static bool IsWindowCloaked(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED,
            out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static bool SupportsImmersiveDarkMode() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
    private static bool SupportsWindowCornerPreference() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    private static bool SupportsSystemBackdropType() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
}
