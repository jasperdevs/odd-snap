using System.Drawing;
using System.Drawing.Text;

namespace Yoink.Helpers;

/// <summary>
/// Shared visual tokens for the capture chrome, pickers, and other floating surfaces.
/// Keeps spacing, typography, and icon sizing aligned across the app.
/// </summary>
public static class UiChrome
{
    public const int SurfacePadding = 10;
    public const int SurfaceGap = 8;
    public const int SurfaceRadius = 12;

    public const int ToolbarHeight = 46;
    public const int ToolbarButtonSize = 34;
    public const int ToolbarButtonSpacing = 4;
    public const int ToolbarTopMargin = 16;
    public const int ToolbarGroupGap = 12;
    public const int ToolbarInnerPadding = 10;
    public const int ToolbarFlyoutPadding = 9;

    public const int PopupMargin = 20;
    public const int PopupGap = 8;
    public const int PopupRadius = 12;

    public const float IconGlyphSize = 15f;
    public const float ChromeBodySize = 9.5f;
    public const float ChromeBodyBoldSize = 10f;
    public const float ChromeSmallSize = 8.25f;
    public const float ChromeTitleSize = 11f;
    public const float ChromeHintSize = 13f;
    public const string DefaultFontFamily = "Segoe UI";

    public static Font ChromeFont(float size = ChromeBodySize, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font(PreferredFamilyName, size, style);
        }
        catch
        {
            return new Font(FallbackFamilyName, size, style);
        }
    }

    public static bool IsDark => Yoink.UI.Theme.IsDark;
    public static string PreferredFamilyName => "Segoe UI Variable Text";
    public static string FallbackFamilyName => "Segoe UI";

    public static FontFamily FontFamily =>
        TryCreateFontFamily(PreferredFamilyName) ?? TryCreateFontFamily(FallbackFamilyName) ?? SystemFonts.DefaultFont.FontFamily;

    public static System.Drawing.Color SurfaceWindowBackground => IsDark ? System.Drawing.Color.FromArgb(28, 28, 28) : System.Drawing.Color.FromArgb(245, 245, 245);
    public static System.Drawing.Color SurfaceBackground => IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.FromArgb(252, 252, 252);
    public static System.Drawing.Color SurfaceElevated => IsDark ? System.Drawing.Color.FromArgb(24, 24, 24) : System.Drawing.Color.FromArgb(255, 255, 255);
    public static System.Drawing.Color SurfaceBorder => IsDark ? System.Drawing.Color.FromArgb(18, 255, 255, 255) : System.Drawing.Color.FromArgb(30, 0, 0, 0);
    public static System.Drawing.Color SurfaceBorderStrong => IsDark ? System.Drawing.Color.FromArgb(30, 255, 255, 255) : System.Drawing.Color.FromArgb(42, 0, 0, 0);
    public static System.Drawing.Color SurfaceBorderSubtle => IsDark ? System.Drawing.Color.FromArgb(24, 255, 255, 255) : System.Drawing.Color.FromArgb(18, 0, 0, 0);
    public static System.Drawing.Color SurfaceTextPrimary => IsDark ? System.Drawing.Color.FromArgb(255, 255, 255, 255) : System.Drawing.Color.FromArgb(24, 24, 24);
    public static System.Drawing.Color SurfaceTextSecondary => IsDark ? System.Drawing.Color.FromArgb(190, 255, 255, 255) : System.Drawing.Color.FromArgb(120, 0, 0, 0);
    public static System.Drawing.Color SurfaceTextMuted => IsDark ? System.Drawing.Color.FromArgb(120, 255, 255, 255) : System.Drawing.Color.FromArgb(90, 0, 0, 0);
    public static System.Drawing.Color SurfaceHover => IsDark ? System.Drawing.Color.FromArgb(18, 255, 255, 255) : System.Drawing.Color.FromArgb(16, 0, 0, 0);
    public static System.Drawing.Color SurfacePill => IsDark ? System.Drawing.Color.FromArgb(245, 28, 28, 28) : System.Drawing.Color.FromArgb(245, 255, 255, 255);
    public static System.Drawing.Color SurfaceTooltip => IsDark ? System.Drawing.Color.FromArgb(240, 24, 24, 24) : System.Drawing.Color.FromArgb(245, 255, 255, 255);
    public static System.Drawing.Color SurfaceShadow => System.Drawing.Color.FromArgb(IsDark ? 60 : 34, 0, 0, 0);
    public static System.Drawing.Color SurfaceDimOverlay => System.Drawing.Color.FromArgb(IsDark ? 35 : 18, 0, 0, 0);
    public static System.Drawing.Color SurfaceSelectionOverlay => System.Drawing.Color.FromArgb(IsDark ? 100 : 72, 0, 0, 0);

    private static FontFamily? TryCreateFontFamily(string name)
    {
        try { return new FontFamily(name); }
        catch { return null; }
    }
}
