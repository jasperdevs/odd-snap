using System.Drawing;
using Yoink.Models;

namespace Yoink.Helpers;

public static class ToolbarLayout
{
    public static Rectangle GetToolbarRect(
        Rectangle virtualBounds,
        Rectangle screenBounds,
        int toolbarWidth,
        int toolbarHeight,
        CaptureDockSide dockSide = CaptureDockSide.Top,
        int topMargin = UiChrome.ToolbarTopMargin,
        int horizontalPadding = 8)
    {
        if (toolbarWidth <= 0 || toolbarHeight <= 0)
            return Rectangle.Empty;

        int screenLeft = screenBounds.Left - virtualBounds.Left;
        int screenTop = screenBounds.Top - virtualBounds.Top;

        int centeredX = screenLeft + Math.Max(0, (screenBounds.Width - toolbarWidth) / 2);
        int centeredY = screenTop + Math.Max(0, (screenBounds.Height - toolbarHeight) / 2);
        int minX = screenLeft + horizontalPadding;
        int maxX = screenLeft + Math.Max(horizontalPadding, screenBounds.Width - toolbarWidth - horizontalPadding);
        int minY = screenTop + horizontalPadding;
        int maxY = screenTop + Math.Max(horizontalPadding, screenBounds.Height - toolbarHeight - horizontalPadding);

        int x;
        int y;

        switch (dockSide)
        {
            case CaptureDockSide.Bottom:
                x = Math.Clamp(centeredX, minX, maxX);
                y = Math.Clamp(screenTop + screenBounds.Height - toolbarHeight - (horizontalPadding + 10), minY, maxY);
                break;
            case CaptureDockSide.Left:
                x = minX;
                y = Math.Clamp(centeredY, minY, maxY);
                break;
            case CaptureDockSide.Right:
                x = Math.Clamp(screenLeft + screenBounds.Width - toolbarWidth - horizontalPadding, minX, maxX);
                y = Math.Clamp(centeredY, minY, maxY);
                break;
            default:
                x = Math.Clamp(centeredX, minX, maxX);
                y = Math.Clamp(screenTop + topMargin, minY, maxY);
                break;
        }

        return new Rectangle(x, y, toolbarWidth, toolbarHeight);
    }
}
