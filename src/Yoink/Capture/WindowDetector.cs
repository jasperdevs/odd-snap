using System.Drawing;
using Yoink.Models;
using Yoink.Native;

namespace Yoink.Capture;

/// <summary>
/// Provides point lookup for window-only snapping.
/// Resolves the top-most snappable top-level window at the pointer.
/// </summary>
public static class WindowDetector
{
    private static readonly HashSet<IntPtr> IgnoredHandles = new();
    private static readonly object IgnoredHandleLock = new();
    private static readonly string[] IgnoredWindowClasses =
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "tooltips_class32",
        "#32768"
    };

    public static Rectangle GetDetectionRectAtPoint(
        Point screenPoint,
        Rectangle virtualBounds,
        WindowDetectionMode mode)
    {
        if (mode == WindowDetectionMode.Off)
            return Rectangle.Empty;

        return GetTopLevelWindowRectAtPoint(screenPoint, virtualBounds);
    }

    public static Rectangle GetWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
        => GetDetectionRectAtPoint(screenPoint, virtualBounds, WindowDetectionMode.WindowOnly);

    /// <summary>
    /// <summary>Legacy no-op kept so overlay startup callers do not need special casing.</summary>
    public static void SnapshotWindows(Rectangle virtualBounds) { }

    /// <summary>Legacy no-op kept so overlay shutdown callers do not need special casing.</summary>
    public static void ClearSnapshot() { }

    public static void RegisterIgnoredWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (IgnoredHandleLock)
            IgnoredHandles.Add(hwnd);
    }

    public static void UnregisterIgnoredWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (IgnoredHandleLock)
            IgnoredHandles.Remove(hwnd);
    }

    private static Rectangle GetTopLevelWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
    {
        var pt = ToScreenPoint(screenPoint, virtualBounds);
        Rectangle detected = Rectangle.Empty;
        var seen = new HashSet<IntPtr>();

        User32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero || !seen.Add(hwnd))
                return true;

            if (!TryGetWindowRect(hwnd, pt, virtualBounds, out var rect))
                return true;

            detected = rect;
            return false;
        }, IntPtr.Zero);

        if (!detected.IsEmpty)
            return detected;

        return GetTopLevelWindowRectFallbackFromPoint(pt, virtualBounds);
    }

    /// <summary>Secondary live fallback when z-order enumeration doesn't resolve a hit.</summary>
    private static Rectangle GetTopLevelWindowRectFallbackFromPoint(User32.POINT pt, Rectangle virtualBounds)
    {
        IntPtr rawHwnd = User32.WindowFromPoint(pt);

        Span<IntPtr> visited = stackalloc IntPtr[32];
        int visitedCount = 0;
        IntPtr hwnd = rawHwnd;

        for (int depth = 0; depth < 32 && hwnd != IntPtr.Zero; depth++)
        {
            IntPtr candidate = NormalizeTopLevelWindowForHitTest(hwnd);
            if (candidate == IntPtr.Zero || visited[..visitedCount].Contains(candidate))
                break;
            visited[visitedCount++] = candidate;

            if (TryGetWindowRect(candidate, pt, virtualBounds, out var rect))
                return rect;

            hwnd = User32.GetWindow(candidate, User32.GW_HWNDNEXT);
        }

        return Rectangle.Empty;
    }

    private static bool TryGetWindowRect(IntPtr hwnd, User32.POINT? point, Rectangle virtualBounds, out Rectangle rect)
    {
        rect = Rectangle.Empty;

        if (hwnd == IntPtr.Zero || IsIgnoredWindowHandle(hwnd) || !User32.IsWindowVisible(hwnd) || Dwm.IsWindowCloaked(hwnd))
            return false;
        if (!IsSnappableWindow(hwnd))
            return false;

        var screenRect = GetSnappableBounds(hwnd);
        if (screenRect.Width <= 2 || screenRect.Height <= 2)
            return false;
        if (point.HasValue && !screenRect.Contains(point.Value.X, point.Value.Y))
            return false;

        rect = new Rectangle(
            screenRect.Left - virtualBounds.X,
            screenRect.Top - virtualBounds.Y,
            screenRect.Width,
            screenRect.Height);
        return rect.Width > 2 && rect.Height > 2;
    }

    private static IntPtr NormalizeTopLevelWindowForHitTest(IntPtr hwnd)
    {
        IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        return root != IntPtr.Zero ? root : hwnd;
    }

    internal static bool IsSnappableWindowCandidate(int style, int exStyle, string className, string windowTitle)
    {
        if ((style & User32.WS_CHILD) != 0)
            return false;

        if ((style & User32.WS_DISABLED) != 0)
            return false;

        if ((exStyle & User32.WS_EX_TRANSPARENT) != 0)
            return false;

        if ((exStyle & User32.WS_EX_NOACTIVATE) != 0 && (exStyle & User32.WS_EX_APPWINDOW) == 0)
            return false;

        if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0 && (exStyle & User32.WS_EX_APPWINDOW) == 0)
            return false;

        if (IgnoredWindowClasses.Any(ignored => string.Equals(ignored, className, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (string.IsNullOrWhiteSpace(windowTitle) && (exStyle & User32.WS_EX_APPWINDOW) == 0)
            return false;

        return true;
    }

    private static bool IsSnappableWindow(IntPtr hwnd)
    {
        int style = User32.GetWindowLongA(hwnd, User32.GWL_STYLE);
        int exStyle = User32.GetWindowLongA(hwnd, User32.GWL_EXSTYLE);
        string className = GetClassName(hwnd);
        string title = GetWindowTitle(hwnd);
        return IsSnappableWindowCandidate(style, exStyle, className, title);
    }

    internal static Rectangle ChoosePreferredBounds(Rectangle dwmRect, Rectangle rawRect)
    {
        if (rawRect.Width <= 2 || rawRect.Height <= 2)
            return dwmRect;

        if (dwmRect.Width <= 2 || dwmRect.Height <= 2)
            return rawRect;

        if (!rawRect.Contains(dwmRect))
            return dwmRect;

        int leftInset = dwmRect.Left - rawRect.Left;
        int topInset = dwmRect.Top - rawRect.Top;
        int rightInset = rawRect.Right - dwmRect.Right;
        int bottomInset = rawRect.Bottom - dwmRect.Bottom;
        int largestInset = Math.Max(Math.Max(leftInset, topInset), Math.Max(rightInset, bottomInset));

        return largestInset >= 12 ? rawRect : dwmRect;
    }

    private static Rectangle GetSnappableBounds(IntPtr hwnd)
    {
        var dwmRect = Dwm.GetExtendedFrameBounds(hwnd);
        if (!User32.GetWindowRect(hwnd, out var rawRect))
            return dwmRect;

        return ChoosePreferredBounds(dwmRect, rawRect.ToRectangle());
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int length = User32.GetWindowTextLengthW(hwnd);
        if (length <= 0)
            return string.Empty;

        var buffer = new char[length + 1];
        int copied = User32.GetWindowTextW(hwnd, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        int copied = User32.GetClassNameW(hwnd, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    private static User32.POINT ToScreenPoint(Point overlayPoint, Rectangle virtualBounds)
        => new(overlayPoint.X + virtualBounds.X, overlayPoint.Y + virtualBounds.Y);

    private static bool IsIgnoredWindowHandle(nint hwnd)
    {
        if (hwnd == 0) return false;
        lock (IgnoredHandleLock)
            return IgnoredHandles.Contains(hwnd);
    }
}
