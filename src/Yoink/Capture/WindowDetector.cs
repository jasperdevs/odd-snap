using System.Drawing;
using Yoink.Models;
using Yoink.Native;

namespace Yoink.Capture;

/// <summary>
/// Provides point lookup for window-only snapping.
/// </summary>
public static class WindowDetector
{
    private static readonly HashSet<IntPtr> IgnoredHandles = new();
    private static readonly object IgnoredHandleLock = new();
    private const int MaxZOrderProbeDepth = 32;

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
        => GetTopLevelWindowRectAtPoint(screenPoint, virtualBounds);

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

    public static Rectangle GetTopLevelWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
    {
        var pt = new User32.POINT(screenPoint.X + virtualBounds.X, screenPoint.Y + virtualBounds.Y);
        IntPtr hwnd = User32.WindowFromPoint(pt);
        var visited = new HashSet<IntPtr>();

        for (int depth = 0; depth < MaxZOrderProbeDepth && hwnd != IntPtr.Zero; depth++)
        {
            IntPtr candidate = NormalizeTopLevelWindow(hwnd);
            if (candidate == IntPtr.Zero || !visited.Add(candidate))
                break;

            if (TryGetWindowRect(candidate, pt, virtualBounds, out var rect))
                return rect;

            hwnd = User32.GetWindow(candidate, User32.GW_HWNDNEXT);
        }

        return Rectangle.Empty;
    }

    private static bool TryGetWindowRect(IntPtr hwnd, User32.POINT point, Rectangle virtualBounds, out Rectangle rect)
    {
        rect = Rectangle.Empty;

        if (hwnd == IntPtr.Zero || IsIgnoredWindowHandle(hwnd) || !User32.IsWindowVisible(hwnd) || Dwm.IsWindowCloaked(hwnd))
            return false;

        var screenRect = Dwm.GetExtendedFrameBounds(hwnd);
        if (!IsUsableRect(screenRect))
            return false;
        if (!screenRect.Contains(point.X, point.Y))
            return false;

        rect = new Rectangle(
            screenRect.Left - virtualBounds.X,
            screenRect.Top - virtualBounds.Y,
            screenRect.Width,
            screenRect.Height);
        return IsUsableRect(rect);
    }

    private static IntPtr NormalizeTopLevelWindow(IntPtr hwnd)
    {
        IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        return root != IntPtr.Zero ? root : hwnd;
    }

    private static bool IsUsableRect(Rectangle rect)
        => rect.Width > 2 && rect.Height > 2;

    private static bool IsIgnoredWindowHandle(nint hwnd)
    {
        if (hwnd == 0) return false;
        lock (IgnoredHandleLock)
            return IgnoredHandles.Contains(hwnd);
    }
}
