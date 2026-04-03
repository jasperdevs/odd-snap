using System.Drawing;

namespace Yoink.Helpers;

public static class ToolbarLayout
{
    public static Rectangle GetToolbarRect(
        Rectangle virtualBounds,
        Rectangle screenBounds,
        int toolbarWidth,
        int toolbarHeight,
        int topMargin = UiChrome.ToolbarTopMargin,
        int horizontalPadding = 8)
    {
        if (toolbarWidth <= 0 || toolbarHeight <= 0)
            return Rectangle.Empty;

        int screenLeft = screenBounds.Left - virtualBounds.Left;
        int screenTop = screenBounds.Top - virtualBounds.Top;

        int x = screenLeft + Math.Max(0, (screenBounds.Width - toolbarWidth) / 2);
        if (screenBounds.Width > toolbarWidth + horizontalPadding * 2)
        {
            int minX = screenLeft + horizontalPadding;
            int maxX = screenLeft + screenBounds.Width - toolbarWidth - horizontalPadding;
            x = Math.Clamp(x, minX, maxX);
        }
        else
        {
            x = screenLeft + horizontalPadding;
        }

        int y = screenTop + topMargin;
        if (screenBounds.Height > toolbarHeight + topMargin + horizontalPadding)
        {
            int minY = screenTop + topMargin;
            int maxY = screenTop + screenBounds.Height - toolbarHeight - horizontalPadding;
            y = Math.Clamp(y, minY, maxY);
        }

        return new Rectangle(x, y, toolbarWidth, toolbarHeight);
    }
}
