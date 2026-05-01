using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OddSnap.UI;

public static class UiScale
{
    public const double Default = 1.0;
    public const double Min = 0.8;
    public const double Max = 1.4;

    private static readonly ConditionalWeakTable<Window, WindowScaleState> WindowStates = new();

    public static double Current { get; private set; } = Default;

    public static event Action<double>? Changed;

    public static double Normalize(double scale)
        => Math.Clamp(double.IsFinite(scale) ? scale : Default, Min, Max);

    public static void Set(double scale)
    {
        var normalized = Normalize(scale);
        if (Math.Abs(Current - normalized) < 0.001)
            return;

        Current = normalized;
        Helpers.UiChrome.SetUiScale(normalized);
        Changed?.Invoke(normalized);
    }

    public static void ApplyToWindow(Window window, FrameworkElement root, bool scaleWindowBounds)
    {
        var scale = Normalize(Current);
        root.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
            ? Transform.Identity
            : new ScaleTransform(scale, scale);

        if (!scaleWindowBounds)
            return;

        var state = WindowStates.GetValue(window, static w => new WindowScaleState(
            w.Width,
            w.Height,
            w.MinWidth,
            w.MinHeight));

        window.MinWidth = Math.Max(320, state.MinWidth * scale);
        window.MinHeight = Math.Max(240, state.MinHeight * scale);
        if (!double.IsNaN(state.Width) && !double.IsInfinity(state.Width) && state.Width > 0)
            window.Width = state.Width * scale;
        if (!double.IsNaN(state.Height) && !double.IsInfinity(state.Height) && state.Height > 0)
            window.Height = state.Height * scale;

        ClampToCurrentMonitor(window);
    }

    private static void ClampToCurrentMonitor(Window window)
    {
        var area = GetWindowWorkArea(window);
        var maxWidth = Math.Max(window.MinWidth, area.Width - 24);
        var maxHeight = Math.Max(window.MinHeight, area.Height - 24);

        if (window.Width > maxWidth)
            window.Width = maxWidth;
        if (window.Height > maxHeight)
            window.Height = maxHeight;

        if (window.Left + window.Width > area.Right)
            window.Left = Math.Max(area.Left + 12, area.Right - window.Width - 12);
        if (window.Top + window.Height > area.Bottom)
            window.Top = Math.Max(area.Top + 12, area.Bottom - window.Height - 12);
    }

    private static Rect GetWindowWorkArea(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var screen = hwnd != IntPtr.Zero
                ? System.Windows.Forms.Screen.FromHandle(hwnd)
                : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            return PopupWindowHelper.PhysicalPixelsToDips(
                screen.WorkingArea,
                new System.Drawing.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        }
        catch
        {
            return PopupWindowHelper.GetCurrentWorkArea();
        }
    }

    private sealed record WindowScaleState(double Width, double Height, double MinWidth, double MinHeight);
}
