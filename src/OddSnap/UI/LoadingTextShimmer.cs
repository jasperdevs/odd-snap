using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace OddSnap.UI;

internal static class LoadingTextShimmer
{
    public static void Start(TextBlock textBlock, MediaColor baseColor, double durationSeconds = 1.0, double opacity = 1.0)
    {
        textBlock.Foreground = new SolidColorBrush(baseColor);
        textBlock.OpacityMask = null;
        textBlock.Opacity = opacity;
    }

    public static void Stop(TextBlock textBlock, MediaBrush fallbackBrush, double opacity = 1.0)
    {
        textBlock.Foreground = fallbackBrush;
        textBlock.OpacityMask = null;
        textBlock.Opacity = opacity;
    }
}
