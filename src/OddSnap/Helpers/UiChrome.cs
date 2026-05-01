using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace OddSnap.Helpers;

/// <summary>
/// Shared visual tokens for the capture chrome, pickers, and other floating surfaces.
/// Keeps spacing, typography, and icon sizing aligned across the app.
/// </summary>
public static class UiChrome
{
    public const int SurfacePadding = 10;
    public const int SurfaceGap = 8;
    public const int SurfaceRadius = 8;

    public const int ToolbarHeight = 48;
    public const int ToolbarButtonSize = 36;
    public const int ToolbarButtonSpacing = 3;
    public const int ToolbarTopMargin = 14;
    public const int ToolbarGroupGap = 10;
    public const int ToolbarInnerPadding = 8;
    public const int ToolbarFlyoutPadding = 7;
    public const float ToolbarCornerRadius = 8f;

    public const int PopupMargin = 20;
    public const int PopupGap = 8;
    public const int PopupRadius = 8;

    public const float IconGlyphSize = 15f;
    public const float ChromeBodySize = 9.5f;
    public const float ChromeBodyBoldSize = 10f;
    public const float ChromeSmallSize = 8.25f;
    public const float ChromeTitleSize = 11f;
    public const float ChromeHintSize = 13f;
    public const string DefaultFontFamily = "Segoe UI";

    public static double UiScale { get; private set; } = 1.0;

    public static void SetUiScale(double scale)
        => UiScale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, 0.8, 1.4);

    public static int ScaleInt(int value)
        => Math.Max(1, (int)Math.Round(value * UiScale, MidpointRounding.AwayFromZero));

    public static float ScaleFloat(float value)
        => (float)(value * UiScale);

    public static int ScaledSurfacePadding => ScaleInt(SurfacePadding);
    public static int ScaledSurfaceGap => ScaleInt(SurfaceGap);
    public static int ScaledSurfaceRadius => ScaleInt(SurfaceRadius);
    public static int ScaledToolbarHeight => ScaleInt(ToolbarHeight);
    public static int ScaledToolbarButtonSize => ScaleInt(ToolbarButtonSize);
    public static int ScaledToolbarButtonSpacing => ScaleInt(ToolbarButtonSpacing);
    public static int ScaledToolbarTopMargin => ScaleInt(ToolbarTopMargin);
    public static int ScaledToolbarGroupGap => ScaleInt(ToolbarGroupGap);
    public static int ScaledToolbarInnerPadding => ScaleInt(ToolbarInnerPadding);
    public static int ScaledToolbarFlyoutPadding => ScaleInt(ToolbarFlyoutPadding);
    public static float ScaledToolbarCornerRadius => ScaleFloat(ToolbarCornerRadius);
    public static int ScaledPopupMargin => ScaleInt(PopupMargin);
    public static int ScaledPopupGap => ScaleInt(PopupGap);
    public static int ScaledPopupRadius => ScaleInt(PopupRadius);

    public static Font ChromeFont(float size = ChromeBodySize, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font(PreferredFamilyName, ScaleFloat(size), style);
        }
        catch
        {
            return new Font(FallbackFamilyName, ScaleFloat(size), style);
        }
    }

    public static bool IsDark => OddSnap.UI.Theme.IsDark;
    public static string PreferredFamilyName => "Segoe UI Variable Text";
    public static string FallbackFamilyName => "Segoe UI";

    public static FontFamily FontFamily =>
        TryCreateFontFamily(PreferredFamilyName) ?? TryCreateFontFamily(FallbackFamilyName) ?? SystemFonts.DefaultFont.FontFamily;

    public static System.Drawing.Color SurfaceWindowBackground => IsDark ? System.Drawing.Color.FromArgb(28, 28, 28) : System.Drawing.Color.FromArgb(245, 245, 245);
    public static System.Drawing.Color SurfaceBackground => IsDark ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.FromArgb(252, 252, 252);
    public static System.Drawing.Color SurfaceElevated => IsDark ? System.Drawing.Color.FromArgb(255, 44, 44, 44) : System.Drawing.Color.FromArgb(255, 255, 255);
    public static System.Drawing.Color SurfaceBorder => IsDark ? System.Drawing.Color.FromArgb(24, 255, 255, 255) : System.Drawing.Color.FromArgb(24, 0, 0, 0);
    public static System.Drawing.Color SurfaceBorderStrong => IsDark ? System.Drawing.Color.FromArgb(34, 255, 255, 255) : System.Drawing.Color.FromArgb(36, 0, 0, 0);
    public static System.Drawing.Color SurfaceBorderSubtle => IsDark ? System.Drawing.Color.FromArgb(16, 255, 255, 255) : System.Drawing.Color.FromArgb(14, 0, 0, 0);
    public static System.Drawing.Color SurfaceTextPrimary => IsDark ? System.Drawing.Color.FromArgb(255, 255, 255, 255) : System.Drawing.Color.FromArgb(24, 24, 24);
    public static System.Drawing.Color SurfaceTextSecondary => IsDark ? System.Drawing.Color.FromArgb(190, 255, 255, 255) : System.Drawing.Color.FromArgb(120, 0, 0, 0);
    public static System.Drawing.Color SurfaceTextMuted => IsDark ? System.Drawing.Color.FromArgb(120, 255, 255, 255) : System.Drawing.Color.FromArgb(90, 0, 0, 0);
    public static System.Drawing.Color SurfaceHover => IsDark ? System.Drawing.Color.FromArgb(22, 255, 255, 255) : System.Drawing.Color.FromArgb(14, 0, 0, 0);
    public static System.Drawing.Color SurfacePill => IsDark ? System.Drawing.Color.FromArgb(255, 44, 44, 44) : System.Drawing.Color.FromArgb(255, 255, 255, 255);
    public static System.Drawing.Color SurfaceTooltip => IsDark ? System.Drawing.Color.FromArgb(255, 48, 48, 48) : System.Drawing.Color.FromArgb(255, 255, 255, 255);
    public static System.Drawing.Color SurfaceShadow => System.Drawing.Color.FromArgb(IsDark ? 60 : 34, 0, 0, 0);
    public static System.Drawing.Color SurfaceDimOverlay => System.Drawing.Color.FromArgb(IsDark ? 35 : 18, 0, 0, 0);
    public static System.Drawing.Color SurfaceSelectionOverlay => System.Drawing.Color.FromArgb(IsDark ? 100 : 72, 0, 0, 0);

    // ─── Monitor refresh rate ────────────────────────────────────────

    /// <summary>Milliseconds per frame for the primary monitor's refresh rate.</summary>
    public static int FrameIntervalMs { get; private set; } = 16;

    /// <summary>The detected refresh rate in Hz (e.g. 60, 144, 240).</summary>
    public static int RefreshRateHz { get; private set; } = 60;

    /// <summary>
    /// Queries the primary monitor's refresh rate via EnumDisplaySettings.
    /// Call once at app startup. Safe to call multiple times (just updates cached values).
    /// </summary>
    public static void DetectRefreshRate()
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);
            if (EnumDisplaySettings(null, -1 /* ENUM_CURRENT_SETTINGS */, ref dm) && dm.dmDisplayFrequency > 0)
            {
                RefreshRateHz = dm.dmDisplayFrequency;
                FrameIntervalMs = Math.Max(1, (int)Math.Round(1000.0 / RefreshRateHz));
            }
        }
        catch
        {
            // Fall back to 60 Hz defaults
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion;
        public short dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight;
        public int dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private static FontFamily? TryCreateFontFamily(string name)
    {
        try { return new FontFamily(name); }
        catch { return null; }
    }
}
