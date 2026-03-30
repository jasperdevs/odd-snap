using System.Drawing;
using System.Runtime.InteropServices;
using Yoink.Native;
using Yoink.Models;

namespace Yoink.Capture;

/// <summary>Lightweight window/control info for auto-detection.</summary>
public sealed class DetectedWindow
{
    public IntPtr Handle { get; init; }
    public Rectangle Rectangle { get; set; }
    public bool IsTopLevel { get; init; }
}

/// <summary>
/// Enumerates visible windows (and optionally child controls) at overlay launch,
/// then provides fast point-lookup during mouse hover.
/// Matches the approach used by ShareX's WindowsRectangleList.
/// </summary>
public static class WindowDetector
{
    /// <summary>
    /// Pre-enumerate all visible windows and (optionally) their child controls.
    /// Run on a background thread before/during overlay display.
    /// </summary>
    /// <param name="ignoreHandle">The overlay's own HWND to exclude from results.</param>
    /// <param name="includeChildControls">Whether to enumerate child windows within each top-level window.</param>
    /// <param name="timeoutMs">Maximum time to spend enumerating (0 = no limit).</param>
    public static List<DetectedWindow> EnumerateWindows(
        IntPtr ignoreHandle, bool includeChildControls, int timeoutMs = 5000)
    {
        var raw = new List<DetectedWindow>();
        var parentsSeen = new HashSet<IntPtr>();
        CancellationTokenSource? cts = timeoutMs > 0 ? new CancellationTokenSource(timeoutMs) : null;

        try
        {
            User32.EnumWindows((hWnd, _) =>
            {
                if (cts is { IsCancellationRequested: true })
                    return false;

                CollectWindow(hWnd, null, ignoreHandle, includeChildControls,
                    parentsSeen, raw, cts);
                return true;
            }, IntPtr.Zero);
        }
        catch (OperationCanceledException) { }
        finally
        {
            cts?.Dispose();
        }

        // Deduplicate: remove child rects fully contained by an earlier (also-child) rect.
        // Top-level windows are always kept.
        var result = new List<DetectedWindow>(raw.Count);
        foreach (var win in raw)
        {
            if (win.IsTopLevel)
            {
                result.Add(win);
                continue;
            }

            bool hidden = false;
            foreach (var existing in result)
            {
                if (!existing.IsTopLevel && existing.Rectangle.Contains(win.Rectangle))
                {
                    hidden = true;
                    break;
                }
            }
            if (!hidden)
                result.Add(win);
        }

        return result;
    }

    private static void CollectWindow(
        IntPtr hWnd, Rectangle? clipRect,
        IntPtr ignoreHandle, bool includeChildren,
        HashSet<IntPtr> parentsSeen, List<DetectedWindow> results,
        CancellationTokenSource? cts)
    {
        bool isTopLevel = clipRect == null;

        if (cts is { IsCancellationRequested: true })
            return;

        if (hWnd == ignoreHandle)
            return;

        if (!User32.IsWindowVisible(hWnd))
            return;

        // Skip cloaked windows (hidden UWP / virtual desktop windows) -- top-level only
        if (isTopLevel && Dwm.IsWindowCloaked(hWnd))
            return;

        Rectangle rect;
        if (isTopLevel)
        {
            // Use DWM extended frame bounds for accurate visual rect on Win10/11
            rect = Dwm.GetExtendedFrameBounds(hWnd);
        }
        else
        {
            User32.GetWindowRect(hWnd, out var wr);
            rect = wr.ToRectangle();
            // Clip child rect to parent bounds
            if (clipRect.HasValue)
                rect = Rectangle.Intersect(rect, clipRect.Value);
        }

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Enumerate child controls before adding the parent (so children come first in the list)
        if (includeChildren && !parentsSeen.Contains(hWnd))
        {
            parentsSeen.Add(hWnd);
            User32.EnumChildWindows(hWnd, (childHwnd, _) =>
            {
                if (cts is { IsCancellationRequested: true })
                    return false;
                CollectWindow(childHwnd, rect, ignoreHandle, false,
                    parentsSeen, results, cts);
                return true;
            }, IntPtr.Zero);
        }

        // For top-level windows, also add the client rect as a separate entry (before the full rect)
        // so hovering over content area snaps to client, hovering over title bar snaps to full window.
        if (isTopLevel)
        {
            var clientRect = GetClientRectScreen(hWnd);
            if (clientRect.Width > 0 && clientRect.Height > 0 && clientRect != rect)
            {
                results.Add(new DetectedWindow
                {
                    Handle = hWnd,
                    Rectangle = clientRect,
                    IsTopLevel = false // treated as a sub-region of the window
                });
            }
        }

        results.Add(new DetectedWindow
        {
            Handle = hWnd,
            Rectangle = rect,
            IsTopLevel = isTopLevel
        });
    }

    private static Rectangle GetClientRectScreen(IntPtr hWnd)
    {
        if (!User32.GetClientRect(hWnd, out var cr))
            return Rectangle.Empty;

        var topLeft = new User32.POINT(cr.Left, cr.Top);
        User32.ClientToScreen(hWnd, ref topLeft);
        return new Rectangle(topLeft.X, topLeft.Y, cr.Width, cr.Height);
    }

    /// <summary>
    /// Find the most specific (smallest) detected rectangle that contains the given screen point.
    /// Returns the rectangle in bitmap-local coords, or Rectangle.Empty if nothing found.
    /// </summary>
    public static Rectangle FindWindowAt(Point screenPoint, List<DetectedWindow>? windows, Rectangle virtualBounds)
    {
        if (windows == null || windows.Count == 0)
            return Rectangle.Empty;

        // Convert bitmap-local point to screen coords
        int sx = screenPoint.X + virtualBounds.X;
        int sy = screenPoint.Y + virtualBounds.Y;
        var pt = new Point(sx, sy);

        // Walk list -- children appear before parents, so first hit is most specific
        foreach (var win in windows)
        {
            if (win.Rectangle.Contains(pt))
            {
                // Convert from screen coords to bitmap-local coords
                return new Rectangle(
                    win.Rectangle.X - virtualBounds.X,
                    win.Rectangle.Y - virtualBounds.Y,
                    win.Rectangle.Width,
                    win.Rectangle.Height);
            }
        }

        return Rectangle.Empty;
    }

    /// <summary>
    /// Legacy single-point lookup (fallback when pre-enumeration is disabled).
    /// Only detects top-level windows.
    /// </summary>
    public static Rectangle GetWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
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

    public static Rectangle GetDetectionRectAtPoint(Point screenPoint, Rectangle virtualBounds, WindowDetectionMode mode)
    {
        if (mode == WindowDetectionMode.Off)
            return Rectangle.Empty;

        return GetTopLevelWindowRectAtPoint(screenPoint, virtualBounds);
    }

    public static Rectangle GetTopLevelWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
    {
        var pt = new User32.POINT(screenPoint.X + virtualBounds.X, screenPoint.Y + virtualBounds.Y);

        foreach (var hwnd in EnumerateTopLevelWindows())
        {
            if (IsIgnoredWindowHandle(hwnd))
                continue;

            if (!User32.GetWindowRect(hwnd, out var rect))
                continue;

            if (pt.X < rect.Left || pt.X >= rect.Right || pt.Y < rect.Top || pt.Y >= rect.Bottom)
                continue;

            var hwndRect = GetWindowRectFromHandle(hwnd, virtualBounds);
            if (IsUsableRect(hwndRect, virtualBounds))
                return hwndRect;
        }

        return Rectangle.Empty;
    }

    private static Rectangle GetWindowRectFromHandle(IntPtr hwnd, Rectangle virtualBounds)
    {
        IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        if (root != IntPtr.Zero)
            hwnd = root;

        if (!User32.GetWindowRect(hwnd, out var rect))
            return Rectangle.Empty;

        return new Rectangle(
            rect.Left - virtualBounds.X,
            rect.Top - virtualBounds.Y,
            rect.Width,
            rect.Height);
    }

    private static IntPtr SkipOwnWindows(IntPtr hwnd)
    {
        while (hwnd != IntPtr.Zero)
        {
            if (!IsIgnoredWindowHandle(hwnd))
                return hwnd;
            hwnd = User32.GetWindow(hwnd, User32.GW_HWNDNEXT);
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<IntPtr> EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        User32.EnumWindows((hWnd, _) =>
        {
            if (hWnd != IntPtr.Zero && User32.IsWindowVisible(hWnd))
                windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IsUsableRect(Rectangle rect, Rectangle virtualBounds)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return false;

        var screenRect = new Rectangle(0, 0, virtualBounds.Width, virtualBounds.Height);
        rect.Intersect(screenRect);
        return rect.Width > 2 && rect.Height > 2;
    }

    private static bool IsIgnoredWindowHandle(nint hwnd)
    {
        if (hwnd == 0) return false;
        lock (IgnoredHandleLock)
            return IgnoredHandles.Contains(hwnd);
    }
}
