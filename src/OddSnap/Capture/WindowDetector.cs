using System.Drawing;
using OddSnap.Models;
using OddSnap.Native;

namespace OddSnap.Capture;

/// <summary>
/// Provides point lookup for window-only snapping.
/// Resolves the top-most snappable top-level window at the pointer.
/// </summary>
public static class WindowDetector
{
    private sealed record WindowSnapshot(Rectangle VirtualBounds, SnapshotWindow[] Windows);

    private readonly record struct SnapshotWindow(Rectangle Rect, WindowHitResult Hit);

    private enum WindowHitResult
    {
        PassThrough,
        Snappable,
        Blocked
    }

    private static readonly HashSet<IntPtr> IgnoredHandles = new();
    private static readonly object IgnoredHandleLock = new();
    private static readonly object SnapshotLock = new();
    private static WindowSnapshot? _snapshot;
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

    public static bool TryGetSnapshotDetectionRectAtPoint(
        Point overlayPoint,
        Rectangle virtualBounds,
        WindowDetectionMode mode,
        out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (mode == WindowDetectionMode.Off)
            return true;

        WindowSnapshot? snapshot;
        lock (SnapshotLock)
            snapshot = _snapshot;

        if (snapshot is null || snapshot.VirtualBounds != virtualBounds)
            return false;

        foreach (var window in snapshot.Windows)
        {
            if (!window.Rect.Contains(overlayPoint))
                continue;

            if (window.Hit == WindowHitResult.Snappable)
                rect = window.Rect;
            return true;
        }

        return true;
    }

    /// <summary>Builds a short-lived z-order snapshot for non-blocking overlay hover detection.</summary>
    public static void SnapshotWindows(Rectangle virtualBounds)
    {
        var windows = new List<SnapshotWindow>();
        var seen = new HashSet<IntPtr>();

        User32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero || !seen.Add(hwnd))
                return true;

            var hit = TryGetWindowRect(hwnd, null, virtualBounds, out var rect);
            if (hit != WindowHitResult.PassThrough && rect.Width > 0 && rect.Height > 0)
                windows.Add(new SnapshotWindow(rect, hit));

            return true;
        }, IntPtr.Zero);

        lock (SnapshotLock)
            _snapshot = new WindowSnapshot(virtualBounds, windows.ToArray());
    }

    /// <summary>Clears the hover-detection snapshot owned by the active overlay.</summary>
    public static void ClearSnapshot()
    {
        lock (SnapshotLock)
            _snapshot = null;
    }

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
        bool blockedByRealWindow = false;
        var seen = new HashSet<IntPtr>();

        User32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero || !seen.Add(hwnd))
                return true;

            var hit = TryGetWindowRect(hwnd, pt, virtualBounds, out var rect);
            if (hit == WindowHitResult.PassThrough)
                return true;

            if (hit == WindowHitResult.Snappable)
                detected = rect;
            else
                blockedByRealWindow = true;
            return false;
        }, IntPtr.Zero);

        return detected.IsEmpty && !blockedByRealWindow
            ? GetTopLevelWindowRectFallbackFromPoint(pt, virtualBounds)
            : detected;
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

            var hit = TryGetWindowRect(candidate, pt, virtualBounds, out var rect);
            if (hit == WindowHitResult.Snappable)
                return rect;
            if (hit == WindowHitResult.Blocked)
                break;

            hwnd = User32.GetWindow(candidate, User32.GW_HWNDNEXT);
        }

        return Rectangle.Empty;
    }

    private static WindowHitResult TryGetWindowRect(IntPtr hwnd, User32.POINT? point, Rectangle virtualBounds, out Rectangle rect)
    {
        rect = Rectangle.Empty;

        if (hwnd == IntPtr.Zero || IsIgnoredWindowHandle(hwnd) || !User32.IsWindowVisible(hwnd) || Dwm.IsWindowCloaked(hwnd))
            return WindowHitResult.PassThrough;

        var screenRect = GetSnappableBounds(hwnd);
        if (screenRect.Width <= 2 || screenRect.Height <= 2)
            return WindowHitResult.PassThrough;
        if (point.HasValue && !screenRect.Contains(point.Value.X, point.Value.Y))
            return WindowHitResult.PassThrough;

        int style = User32.GetWindowLongA(hwnd, User32.GWL_STYLE);
        int exStyle = User32.GetWindowLongA(hwnd, User32.GWL_EXSTYLE);
        string className = GetClassName(hwnd);
        string title = GetWindowTitle(hwnd);
        rect = new Rectangle(
            screenRect.Left - virtualBounds.X,
            screenRect.Top - virtualBounds.Y,
            screenRect.Width,
            screenRect.Height);

        if (!IsSnappableWindowCandidate(style, exStyle, className, title))
            return IsPassThroughWindowCandidate(exStyle, className)
                ? WindowHitResult.PassThrough
                : WindowHitResult.Blocked;

        return rect.Width > 2 && rect.Height > 2
            ? WindowHitResult.Snappable
            : WindowHitResult.PassThrough;
    }

    private static IntPtr NormalizeTopLevelWindowForHitTest(IntPtr hwnd)
    {
        IntPtr rootOwner = User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);
        if (rootOwner != IntPtr.Zero)
            return rootOwner;

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

    internal static bool IsPassThroughWindowCandidate(int exStyle, string className)
    {
        if ((exStyle & User32.WS_EX_TRANSPARENT) != 0)
            return true;

        return IgnoredWindowClasses.Any(ignored => string.Equals(ignored, className, StringComparison.OrdinalIgnoreCase));
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
