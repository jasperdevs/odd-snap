using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;
using OddSnap.Models;
using OddSnap.Native;

namespace OddSnap.UI;

internal static class PopupWindowHelper
{
    public static Rect GetCurrentWorkArea()
    {
        try
        {
            var cursor = Cursor.Position;
            return PhysicalPixelsToDips(Screen.FromPoint(cursor).WorkingArea, cursor);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    public static void ApplyNoActivateChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int exStyle = Native.User32.GetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE);
        exStyle |= 0x80; // WS_EX_TOOLWINDOW
        exStyle |= 0x08000000; // WS_EX_NOACTIVATE
        Native.User32.SetWindowLongA(hwnd, Native.User32.GWL_EXSTYLE, exStyle);
        Native.Dwm.DisableBackdrop(hwnd);
    }

    public static (double targetLeft, double targetTop, double startLeft, double startTop, bool animateLeft) GetPlacement(
        ToastPosition position,
        double actualWidth,
        double actualHeight,
        Rect workArea,
        double edge = 8,
        double stackOffset = 0,
        double offScreenDistance = 10)
    {
        var safeStackOffset = Math.Max(0, stackOffset);
        var stackedEdge = edge + safeStackOffset;
        return position switch
        {
            ToastPosition.Left =>
                (workArea.Left + edge, workArea.Bottom - actualHeight - stackedEdge, workArea.Left - actualWidth - offScreenDistance, workArea.Bottom - actualHeight - stackedEdge, true),
            ToastPosition.TopLeft =>
                (workArea.Left + edge, workArea.Top + stackedEdge, workArea.Left + edge, workArea.Top - actualHeight - offScreenDistance, false),
            ToastPosition.TopRight =>
                (workArea.Right - actualWidth - edge, workArea.Top + stackedEdge, workArea.Right - actualWidth - edge, workArea.Top - actualHeight - offScreenDistance, false),
            _ =>
                (workArea.Right - actualWidth - edge, workArea.Bottom - actualHeight - stackedEdge, workArea.Right + offScreenDistance, workArea.Bottom - actualHeight - stackedEdge, true),
        };
    }

    public static (double exitLeft, double exitTop, bool animateLeft) GetDismissPlacement(
        ToastPosition position,
        double actualWidth,
        double actualHeight,
        Rect workArea,
        double edge = 8,
        double stackOffset = 0,
        double exitDistance = 20)
    {
        var safeStackOffset = Math.Max(0, stackOffset);
        var stackedEdge = edge + safeStackOffset;
        return position switch
        {
            ToastPosition.Left =>
                (workArea.Left - actualWidth - exitDistance, workArea.Bottom - actualHeight - stackedEdge, true),
            ToastPosition.TopLeft =>
                (workArea.Left + edge, workArea.Top - actualHeight - exitDistance - safeStackOffset, false),
            ToastPosition.TopRight =>
                (workArea.Right - actualWidth - edge, workArea.Top - actualHeight - exitDistance - safeStackOffset, false),
            _ =>
                (workArea.Right + exitDistance, workArea.Bottom - actualHeight - stackedEdge, true),
        };
    }

    internal static Rect PhysicalPixelsToDips(System.Drawing.Rectangle physicalRect, System.Drawing.Point monitorPoint)
    {
        var (scaleX, scaleY) = GetScaleForPoint(monitorPoint);
        return new Rect(
            physicalRect.Left / scaleX,
            physicalRect.Top / scaleY,
            physicalRect.Width / scaleX,
            physicalRect.Height / scaleY);
    }

    private static (double X, double Y) GetScaleForPoint(System.Drawing.Point point)
    {
        try
        {
            var monitor = User32.MonitorFromPoint(
                new User32.POINT(point.X, point.Y),
                User32.MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero
                && Shcore.GetDpiForMonitor(monitor, Shcore.MonitorDpiType.EffectiveDpi, out uint dpiX, out uint dpiY) == 0
                && dpiX > 0
                && dpiY > 0)
            {
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch
        {
            // Fall back below.
        }

        using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return (Math.Max(1, graphics.DpiX / 96.0), Math.Max(1, graphics.DpiY / 96.0));
    }
}
