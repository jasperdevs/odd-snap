using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.UI;

internal static class PopupWindowHelper
{
    public static Rect GetCurrentWorkArea()
    {
        try
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            return new Rect(area.Left, area.Top, area.Width, area.Height);
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
        double offScreenDistance = 10)
    {
        return position switch
        {
            ToastPosition.Left =>
                (workArea.Left + edge, workArea.Bottom - actualHeight - edge, workArea.Left - actualWidth - offScreenDistance, workArea.Bottom - actualHeight - edge, true),
            ToastPosition.TopLeft =>
                (workArea.Left + edge, workArea.Top + edge, workArea.Left + edge, workArea.Top - actualHeight - offScreenDistance, false),
            ToastPosition.TopRight =>
                (workArea.Right - actualWidth - edge, workArea.Top + edge, workArea.Right - actualWidth - edge, workArea.Top - actualHeight - offScreenDistance, false),
            _ =>
                (workArea.Right - actualWidth - edge, workArea.Bottom - actualHeight - edge, workArea.Right + offScreenDistance, workArea.Bottom - actualHeight - edge, true),
        };
    }

    public static (double exitLeft, double exitTop, bool animateLeft) GetDismissPlacement(
        ToastPosition position,
        double actualWidth,
        double actualHeight,
        Rect workArea,
        double edge = 8,
        double exitDistance = 20)
    {
        return position switch
        {
            ToastPosition.Left =>
                (workArea.Left - actualWidth - exitDistance, workArea.Bottom - actualHeight - edge, true),
            ToastPosition.TopLeft =>
                (workArea.Left + edge, workArea.Top - actualHeight - exitDistance, false),
            ToastPosition.TopRight =>
                (workArea.Right - actualWidth - edge, workArea.Top - actualHeight - exitDistance, false),
            _ =>
                (workArea.Right + exitDistance, workArea.Bottom - actualHeight - edge, true),
        };
    }
}
