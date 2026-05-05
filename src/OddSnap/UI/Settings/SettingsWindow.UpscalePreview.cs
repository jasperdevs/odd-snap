using System.Windows;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void UpscaleShowPreviewWindowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;

        var previous = ActiveUpscaleSettings.ShowPreviewWindow;

        try
        {
            ActiveUpscaleSettings.ShowPreviewWindow = UpscaleShowPreviewWindowCheck.IsChecked == true;
            UpdateUpscaleDefaultScaleUi(ActiveUpscaleSettings.GetActiveLocalEngine());
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.upscale-preview-window", ex);
            ActiveUpscaleSettings.ShowPreviewWindow = previous;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.upscale-preview-window-rollback", rollbackEx);
            }

            _suppressUpscaleSettingChange = true;
            try
            {
                UpscaleShowPreviewWindowCheck.IsChecked = previous;
                UpdateUpscaleDefaultScaleUi(ActiveUpscaleSettings.GetActiveLocalEngine());
            }
            finally
            {
                _suppressUpscaleSettingChange = false;
            }

            ShowUpscaleSettingSaveFailed("Preview window", ex);
        }
    }
}
