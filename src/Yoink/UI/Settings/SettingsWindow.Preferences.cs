using System.IO;
using System.Windows;
using System.Windows.Controls;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private bool _semanticRuntimeInstalled;
    private bool _semanticRuntimeInstallInProgress;

    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "yoink-settings.json"
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
        if (MessageBox.Show("Reset all settings to defaults?\n\nThis cannot be undone.",
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (MessageBox.Show("Are you sure? All hotkeys, upload configs, and preferences will be lost.",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (MessageBox.Show("Last chance — reset everything?",
                "Final Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        _settingsService.Settings.ShowCursor = ShowCursorCheck.IsChecked == true;
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

    private void SemanticRuntimeInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_semanticRuntimeInstallInProgress)
            return;

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                SemanticRuntimeJobKey,
                "Semantic search",
                "Preparing semantic search...",
                "Semantic search ready",
                "Local semantic search finished preparing.",
                "Semantic search setup failed")
            {
                SuccessStatus = "Ready"
            },
            (progress, cancellationToken) => LocalClipRuntimeService.EnsureInstalledAsync(progress, cancellationToken));

        if (!started)
            ToastWindow.Show("Semantic search", "Semantic search is already preparing in the background.");

        _ = RefreshSemanticRuntimeStatusAsync();
    }

    private Task RefreshSemanticRuntimeStatusAsync()
    {
        try
        {
            if (BackgroundRuntimeJobService.TryGetSnapshot(SemanticRuntimeJobKey, out var job) && job.IsRunning)
            {
                _semanticRuntimeInstallInProgress = true;
                _semanticRuntimeInstalled = false;
                SemanticRuntimeStatusText.Text = job.Status;
                SemanticRuntimeInstallBtn.Content = "Repair";
                SemanticRuntimeInstallBtn.IsEnabled = false;
                SemanticRuntimeProgressBar.Visibility = Visibility.Visible;
                SemanticRuntimeSection.Visibility = Visibility.Visible;
                SetLoadingTextShimmer(SemanticRuntimeStatusText, true, 0.7, 0.45);
                LoadImageSearchSources();
                return Task.CompletedTask;
            }

            _semanticRuntimeInstallInProgress = false;
            string status;
            if (LocalClipRuntimeService.TryGetCachedStatus(out var installed, out var cachedStatus))
            {
                _semanticRuntimeInstalled = installed;
                status = cachedStatus;
            }
            else if (BackgroundRuntimeJobService.TryGetSnapshot(SemanticRuntimeJobKey, out var failedJob) && failedJob is { LastSucceeded: false })
            {
                _semanticRuntimeInstalled = false;
                status = $"Failed: {failedJob.LastError}";
            }
            else
            {
                _semanticRuntimeInstalled = false;
                status = LocalClipRuntimeService.IdleStatusText;
            }

            var hasActionableIssue = !_semanticRuntimeInstalled &&
                                     !string.Equals(status, LocalClipRuntimeService.IdleStatusText, StringComparison.OrdinalIgnoreCase);

            SemanticRuntimeStatusText.Text = _semanticRuntimeInstalled ? "Ready" : status;
            SemanticRuntimeInstallBtn.Content = "Repair";
            SemanticRuntimeInstallBtn.IsEnabled = true;
            SemanticRuntimeProgressBar.Visibility = Visibility.Collapsed;
            SemanticRuntimeSection.Visibility = hasActionableIssue ? Visibility.Visible : Visibility.Collapsed;
            SetLoadingTextShimmer(SemanticRuntimeStatusText, false, 0.7, 0.45);
            LoadImageSearchSources();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.semantic-status", ex);
            _semanticRuntimeInstallInProgress = false;
            _semanticRuntimeInstalled = false;
            SemanticRuntimeStatusText.Text = ex.Message;
            SemanticRuntimeInstallBtn.Content = "Repair";
            SemanticRuntimeInstallBtn.IsEnabled = true;
            SemanticRuntimeProgressBar.Visibility = Visibility.Collapsed;
            SemanticRuntimeSection.Visibility = Visibility.Visible;
            SetLoadingTextShimmer(SemanticRuntimeStatusText, false, 0.7, 0.45);
            LoadImageSearchSources();
        }

        return Task.CompletedTask;
    }

    private void ResetImageIndexesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset the image OCR/search index?\n\nThis rebuilds screenshot search data in the background.",
                "Reset Image Indexes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
