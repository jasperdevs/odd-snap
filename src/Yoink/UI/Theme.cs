using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Yoink.UI;

// Centralized theme colors.
public static class Theme
{
    public static bool IsDark { get; private set; } = true;

    // Backgrounds
    public static Color BgPrimary => IsDark ? C(28, 28, 28) : C(243, 243, 243);
    public static Color BgSecondary => IsDark ? C(36, 36, 36) : C(249, 249, 249);
    public static Color BgElevated => IsDark ? C(44, 44, 44) : C(255, 255, 255);
    public static Color BgHover => IsDark ? C(55, 55, 55) : C(235, 235, 235);
    public static Color BgCard => IsDark ? C(40, 40, 40) : C(251, 251, 251);
    public static Color BgOverlay => IsDark ? CA(0, 0, 0, 140) : CA(0, 0, 0, 100);

    // Text
    public static Color TextPrimary => IsDark ? C(240, 240, 240) : C(20, 20, 20);
    public static Color TextSecondary => IsDark ? C(160, 160, 160) : C(100, 100, 100);
    public static Color TextMuted => IsDark ? C(100, 100, 100) : C(150, 150, 150);

    // Borders
    public static Color Border => IsDark ? C(60, 60, 60) : C(210, 210, 210);
    public static Color BorderSubtle => IsDark ? C(50, 50, 50) : C(225, 225, 225);

    // Shared stroke: the one white outline used on preview, toast, buttons, cards
    public static Color Stroke => IsDark ? CA(255, 255, 255, 0xCC) : CA(0, 0, 0, 0x40);
    public const double StrokeThickness = 1.5;
    public static SolidColorBrush StrokeBrush() => Brush(Stroke);

    // Accent (monochrome - white tint in dark, dark tint in light)
    public static Color Accent => IsDark ? C(255, 255, 255) : C(0, 0, 0);
    public static Color AccentSubtle => IsDark ? CA(255, 255, 255, 15) : CA(0, 0, 0, 8);
    public static Color AccentHover => IsDark ? CA(255, 255, 255, 25) : CA(0, 0, 0, 12);

    // Selection
    public static Color SelectionBg => IsDark ? CA(255, 255, 255, 20) : CA(0, 0, 0, 10);

    // Window chrome
    public static Color TitleBar => IsDark ? C(22, 22, 22) : C(235, 235, 235);
    public static Color WindowBorder => IsDark ? CA(255, 255, 255, 48) : CA(0, 0, 0, 30);
    public static Color CardBg => IsDark ? CA(255, 255, 255, 10) : CA(0, 0, 0, 6);
    public static Color TabActiveBg => IsDark ? CA(255, 255, 255, 21) : CA(0, 0, 0, 10);
    public static Color TabHoverBg => IsDark ? CA(255, 255, 255, 12) : CA(0, 0, 0, 6);
    public static Color PreviewStroke => IsDark ? CA(0, 0, 0, 64) : CA(0, 0, 0, 25);

    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static void Refresh()
    {
        IsDark = DetectDarkMode();
    }

    private static bool DetectDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch { return true; }
    }

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color CA(byte r, byte g, byte b, byte a) => Color.FromArgb(a, r, g, b);
}
