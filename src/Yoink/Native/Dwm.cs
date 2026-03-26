using System.Runtime.InteropServices;

namespace Yoink.Native;

internal static partial class Dwm
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    public const int DWMSBT_MAINWINDOW = 2;  // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;  // Tabbed

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
