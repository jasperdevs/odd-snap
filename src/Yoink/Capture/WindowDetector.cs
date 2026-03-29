using System.Drawing;
using Yoink.Native;

namespace Yoink.Capture;

public static class WindowDetector
{
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
}
