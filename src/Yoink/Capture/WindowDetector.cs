using System.Drawing;
using Yoink.Native;

namespace Yoink.Capture;

/// <summary>
/// Detects visible windows and finds which window is under a given screen point.
/// Used for the "Window" capture mode.
/// </summary>
public static class WindowDetector
{
    public struct WindowInfo
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public string Title;
    }

    /// <summary>
    /// Gets the bounding rectangle of the window under the given virtual screen point.
    /// Returns the rectangle in virtual screen coordinates, or Rectangle.Empty if none found.
    /// </summary>
    public static Rectangle GetWindowRectAtPoint(Point screenPoint, Rectangle virtualBounds)
    {
        var pt = new User32.POINT(screenPoint.X + virtualBounds.X, screenPoint.Y + virtualBounds.Y);
        IntPtr hwnd = User32.WindowFromPoint(pt);

        if (hwnd == IntPtr.Zero)
            return Rectangle.Empty;

        // Walk up to the root owner (top-level window)
        IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);
        if (root != IntPtr.Zero)
            hwnd = root;

        if (!User32.GetWindowRect(hwnd, out var rect))
            return Rectangle.Empty;

        // Convert from screen coords to bitmap-local coords
        return new Rectangle(
            rect.Left - virtualBounds.X,
            rect.Top - virtualBounds.Y,
            rect.Width,
            rect.Height);
    }

    /// <summary>
    /// Gets all visible top-level windows with their bounds.
    /// </summary>
    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        User32.EnumWindows((hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd))
                return true;

            int exStyle = User32.GetWindowLongA(hwnd, User32.GWL_EXSTYLE);
            if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0)
                return true;

            if (!User32.GetWindowRect(hwnd, out var rect))
                return true;

            if (rect.Width <= 0 || rect.Height <= 0)
                return true;

            var titleChars = new char[256];
            int len = User32.GetWindowTextW(hwnd, titleChars, 256);
            string title = len > 0 ? new string(titleChars, 0, len) : "";

            if (string.IsNullOrWhiteSpace(title))
                return true;

            windows.Add(new WindowInfo
            {
                Handle = hwnd,
                Bounds = rect.ToRectangle(),
                Title = title
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
