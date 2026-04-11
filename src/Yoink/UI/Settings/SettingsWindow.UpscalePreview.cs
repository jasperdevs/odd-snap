using System.Windows;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void UpscaleShowPreviewWindowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUpscaleSettings.ShowPreviewWindow = UpscaleShowPreviewWindowCheck.IsChecked == true;
        UpdateUpscaleDefaultScaleUi(ActiveUpscaleSettings.GetActiveLocalEngine());
        _settingsService.Save();
    }
}
