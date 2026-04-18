using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace OddSnap.UI;

// Centralized theme colors.
public static class Theme
{
    public static bool IsDark { get; private set; } = true;

    // Backgrounds — Windows 11 Settings-inspired
    public static Color BgPrimary => IsDark ? C(31, 31, 31) : C(243, 243, 243);
    public static Color BgSecondary => IsDark ? C(30, 30, 30) : C(249, 249, 249);
    public static Color BgElevated => IsDark ? C(45, 45, 45) : C(255, 255, 255);
    public static Color BgHover => IsDark ? C(55, 55, 55) : C(229, 229, 229);
    public static Color BgCard => IsDark ? C(45, 45, 45) : C(255, 255, 255);
    public static Color BgOverlay => IsDark ? CA(0, 0, 0, 140) : CA(0, 0, 0, 100);

    // Text
    public static Color TextPrimary => IsDark ? C(245, 245, 245) : C(26, 26, 26);
    public static Color TextSecondary => IsDark ? C(162, 162, 162) : C(96, 96, 96);
    public static Color TextMuted => IsDark ? C(110, 110, 110) : C(128, 128, 128);

    // Borders
    public static Color Border => IsDark ? CA(255, 255, 255, 40) : CA(0, 0, 0, 22);
    public static Color BorderSubtle => IsDark ? CA(255, 255, 255, 24) : CA(0, 0, 0, 14);

    // Shared stroke: the one white outline used on preview, toast, buttons, cards
    public static Color Stroke => IsDark ? CA(255, 255, 255, 0xCC) : CA(0, 0, 0, 0x40);
    public const double StrokeThickness = 1.5;
    public static SolidColorBrush StrokeBrush() => Brush(Stroke);

    // Accent (monochrome - white tint in dark, dark tint in light)
    public static Color Accent => IsDark ? C(255, 255, 255) : C(0, 0, 0);
    public static Color AccentSubtle => IsDark ? CA(255, 255, 255, 15) : CA(0, 0, 0, 18);
    public static Color AccentHover => IsDark ? CA(255, 255, 255, 25) : CA(0, 0, 0, 28);
    public static Color DangerHover => IsDark ? CA(196, 43, 28, 210) : CA(196, 43, 28, 225);

    // Selection
    public static Color SelectionBg => IsDark ? CA(255, 255, 255, 20) : CA(0, 0, 0, 10);

    // Window chrome
    public static Color TitleBar => IsDark ? C(26, 26, 26) : C(240, 240, 240);
    public static Color WindowBorder => IsDark ? CA(255, 255, 255, 18) : CA(0, 0, 0, 20);
    public static Color CardBg => IsDark ? C(45, 45, 45) : C(255, 255, 255);
    public static Color TabActiveBg => IsDark ? CA(255, 255, 255, 21) : CA(0, 0, 0, 16);
    public static Color TabHoverBg => IsDark ? CA(255, 255, 255, 12) : CA(0, 0, 0, 10);
    public static Color PreviewStroke => IsDark ? CA(0, 0, 0, 64) : CA(0, 0, 0, 25);

    // Section icon tints
    public static Color SectionIconBg => IsDark ? CA(255, 255, 255, 14) : CA(0, 0, 0, 8);
    public static Color SectionIconFg => IsDark ? CA(255, 255, 255, 200) : CA(0, 0, 0, 170);

    // Separator
    public static Color Separator => IsDark ? CA(255, 255, 255, 16) : CA(0, 0, 0, 10);

    // Toast background (needs to be opaque enough to read)
    public static Color ToastBg => IsDark ? C(48, 48, 48) : C(252, 252, 252);
    public static Color ToastBorder => IsDark ? CA(255, 255, 255, 30) : CA(0, 0, 0, 18);

    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static void ApplyTo(System.Windows.ResourceDictionary resources)
    {
        resources["ChromeButtonBackground"] = Brush(AccentSubtle);
        resources["ChromeButtonForeground"] = Brush(TextPrimary);
        resources["ChromeButtonBorderBrush"] = Brush(BorderSubtle);
        resources["ChromeButtonHoverBrush"] = Brush(AccentHover);
        resources["ChromeButtonPressedBrush"] = Brush(SelectionBg);
        resources["ThemeTextPrimaryBrush"] = Brush(TextPrimary);
        resources["ThemeTextSecondaryBrush"] = Brush(TextSecondary);
        resources["ThemeInputBackgroundBrush"] = Brush(BgSecondary);
        resources["ThemeInputBorderBrush"] = Brush(BorderSubtle);
        resources["ThemeTabHoverBrush"] = Brush(TabHoverBg);
        resources["ThemeTabActiveBrush"] = Brush(TabActiveBg);
    }

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
