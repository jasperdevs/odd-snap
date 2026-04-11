using System.Windows.Controls;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void SetLoadingTextShimmer(TextBlock textBlock, bool active, double activeOpacity = 1.0, double inactiveOpacity = 0.35)
    {
        if (active)
            LoadingTextShimmer.Start(textBlock, System.Windows.Media.Colors.White, opacity: activeOpacity);
        else
            LoadingTextShimmer.Stop(textBlock, Theme.Brush(Theme.TextPrimary), inactiveOpacity);
    }
}
