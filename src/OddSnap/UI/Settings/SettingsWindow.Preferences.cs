using System.IO;
using System.Windows;
using System.Windows.Controls;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "oddsnap-settings.json"
            };
            if (dlg.ShowDialog(this) != true) return;

            var json = SettingsService.ExportRedactedJson(_settingsService.Settings);
            File.WriteAllText(dlg.FileName, json);
            ToastWindow.Show("Settings exported", dlg.FileName);
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Export failed", ex.Message);
        }
    }

    private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog(this) != true) return;

            var json = File.ReadAllText(dlg.FileName);
            if (!SettingsService.TryDeserialize(json, out var imported))
            {
                ToastWindow.ShowError("Import failed", "Invalid settings file.");
                return;
            }

            _settingsService.Settings = imported;
            _settingsService.Save();
            HotkeyChanged?.Invoke();
            LoadSettings();
            ToastWindow.Show("Settings imported", "Settings have been applied.");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Import failed", ex.Message);
        }
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Reset Settings",
                "Reset all settings to defaults?\n\nThis cannot be undone.",
                "Reset",
                "Cancel"))
            return;
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Confirm Reset",
                "Are you sure? All hotkeys, upload configs, and preferences will be lost.",
                "Reset",
                "Cancel"))
            return;
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Final Confirmation",
                "Last chance - reset everything?",
                "Reset",
                "Cancel"))
            return;

        _settingsService.Settings = new AppSettings();
        _settingsService.Save();
        HotkeyChanged?.Invoke();
        LoadSettings();
        PopulateToolToggles();
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        UninstallRequested?.Invoke();
    }

    private void CrosshairGuidesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ShowCrosshairGuides = CrosshairGuidesCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void ShowCaptureMagnifierCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ShowCaptureMagnifier = ShowCaptureMagnifierCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void OverlayAllMonitorsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.OverlayCaptureAllMonitors = OverlayAllMonitorsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void ShowCursorCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool showCursor = ShowCursorCheck.IsChecked == true;
        _settingsService.Settings.ShowCursor = showCursor;
        if (RecordShowCursorCheck.IsChecked != showCursor)
            RecordShowCursorCheck.IsChecked = showCursor;
        _settingsService.Save();
    }

    private void AnnotationStrokeShadowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AnnotationStrokeShadow = AnnotationStrokeShadowCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void ToastPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ToastPosition = (ToastPosition)ToastPositionCombo.SelectedIndex;
        _settingsService.Save();
        ToastWindow.SetPosition(_settingsService.Settings.ToastPosition);
        PreviewWindow.SetPosition(_settingsService.Settings.ToastPosition);
    }

    private void ToastDurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ToastDurationCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs))
            {
                _settingsService.Settings.ToastDurationSeconds = secs;
                _settingsService.Save();
                ToastWindow.SetDuration(secs);
            }
        }
    }

    private void CaptureDockSideCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.CaptureDockSide = (CaptureDockSide)CaptureDockSideCombo.SelectedIndex;
        _settingsService.Save();
    }

    private void ToastFadeOutCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ToastFadeOutEnabled = ToastFadeOutCheck.IsChecked == true;
        ToastFadeDurationRow.Visibility = _settingsService.Settings.ToastFadeOutEnabled ? Visibility.Visible : Visibility.Collapsed;
        _settingsService.Save();
        ToastWindow.SetFadeOutBehavior(_settingsService.Settings.ToastFadeOutEnabled, _settingsService.Settings.ToastFadeOutSeconds);
    }

    private void ToastFadeDurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ToastFadeDurationCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs))
            {
                _settingsService.Settings.ToastFadeOutSeconds = secs;
                _settingsService.Save();
                ToastWindow.SetFadeOutBehavior(_settingsService.Settings.ToastFadeOutEnabled, secs);
            }
        }
    }

    private void AutoPinPreviewsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoPinPreviews = AutoPinPreviewsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void ShowImageSearchBarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settingsService.Settings.ShowImageSearchBar = ShowImageSearchBarCheck.IsChecked == true;
        _settingsService.Save();

        if (ShowImageSearchBarCheck.IsChecked != true)
        {
            if (!string.IsNullOrEmpty(ImageSearchBox.Text))
                ImageSearchBox.Clear();
            _imageSearchQuery = "";
        }

        if (HistoryTab.IsChecked == true)
            LoadCurrentHistoryTab();
    }

    private void ShowImageSearchDiagnosticsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settingsService.Settings.ShowImageSearchDiagnostics = ShowImageSearchDiagnosticsCheck.IsChecked == true;
        _settingsService.Save();

        if (HistoryTab.IsChecked == true)
            LoadCurrentHistoryTab();
    }

    private void AutoIndexImagesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settingsService.Settings.AutoIndexImages = AutoIndexImagesCheck.IsChecked == true;
        _settingsService.Save();

        if (_settingsService.Settings.AutoIndexImages)
            _imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);

        if (HistoryTab.IsChecked == true)
            LoadCurrentHistoryTab();
    }

    private void ResetImageIndexesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Reset Image Indexes",
                "Reset the image OCR/search index?\n\nThis rebuilds screenshot search data in the background.",
                "Reset",
                "Cancel"))
            return;

        _imageSearchIndexService.ReindexAll(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);
        if (HistoryTab.IsChecked == true)
            LoadCurrentHistoryTab();
        ToastWindow.Show("Image indexes reset", "Screenshot search will rebuild in the background.");
    }

    private void WindowDetectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (WindowDetectionCombo.SelectedIndex < 0) WindowDetectionCombo.SelectedIndex = 1;
        var mode = (WindowDetectionMode)WindowDetectionCombo.SelectedIndex;
        _settingsService.Settings.WindowDetection = mode;
        _settingsService.Settings.DetectWindows = mode != WindowDetectionMode.Off;
        _settingsService.Save();
    }

    private void CaptureDelayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.CaptureDelaySeconds = CaptureDelayCombo.SelectedIndex switch { 1 => 3, 2 => 5, 3 => 10, _ => 0 };
        _settingsService.Save();
    }
}
