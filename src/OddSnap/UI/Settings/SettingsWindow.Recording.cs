using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OddSnap.Models;
using OddSnap.Helpers;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void UpdateCaptureFormatControls()
    {
        var isJpeg = (CaptureImageFormat)CaptureFormatCombo.SelectedIndex == CaptureImageFormat.Jpeg;
        JpegQualityPanel.Visibility = isJpeg ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveHistoryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryPreferenceChange) return;

        var previous = _settingsService.Settings.SaveHistory;
        var selected = SaveHistoryCheck.IsChecked == true;
        UpdateHistoryPreference(
            "settings.save-history",
            "Save capture history",
            previous,
            selected,
            value => _settingsService.Settings.SaveHistory = value,
            value => SaveHistoryCheck.IsChecked = value);
    }

    private void HistoryRetentionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryPreferenceChange) return;

        var previous = _settingsService.Settings.HistoryRetention;
        var selected = (HistoryRetentionPeriod)Math.Clamp(HistoryRetentionCombo.SelectedIndex, 0, 4);
        UpdateHistoryPreference(
            "settings.history-retention",
            "History retention",
            previous,
            selected,
            value => _settingsService.Settings.HistoryRetention = value,
            value =>
            {
                HistoryRetentionCombo.SelectedIndex = (int)value;
                _historyService.RetentionPeriod = value;
            },
            value => _historyService.PruneByRetention(value));
    }

    private void UpdateHistoryPreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi,
        Action<T>? applySuccess = null)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            applySuccess?.Invoke(current);
            SetHistoryPreferenceStatus(string.Empty);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError(diagnosticKey, ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"{diagnosticKey}-rollback", rollbackEx);
            }

            _suppressHistoryPreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressHistoryPreferenceChange = false;
            }

            SetHistoryPreferenceStatus($"{label} change was not saved. Previous setting restored.");
            ToastWindow.ShowError(
                $"{label} failed",
                $"The previous history setting was restored. Check Settings -> Recording and try again.\n{ex.Message}");
        }
    }

    private void SetHistoryPreferenceStatus(string message)
    {
        HistoryPreferenceStatusText.Text = message;
        HistoryPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void MuteSoundsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressGeneralPreferenceChange) return;

        var previous = _settingsService.Settings.MuteSounds;
        var selected = MuteSoundsCheck.IsChecked == true;
        UpdateGeneralPreference(
            "settings.mute-sounds",
            "Mute sounds",
            previous,
            selected,
            value => _settingsService.Settings.MuteSounds = value,
            value => MuteSoundsCheck.IsChecked = value,
            value => SoundService.Muted = value);
    }

    private void DisableAnimationsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressGeneralPreferenceChange) return;

        var previous = _settingsService.Settings.DisableAnimations;
        var selected = DisableAnimationsCheck.IsChecked == true;
        UpdateGeneralPreference(
            "settings.disable-animations",
            "Disable animations",
            previous,
            selected,
            value => _settingsService.Settings.DisableAnimations = value,
            value => DisableAnimationsCheck.IsChecked = value,
            value => Motion.Disabled = value);
    }

    private void SoundPackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressGeneralPreferenceChange) return;

        var previous = _settingsService.Settings.SoundPack;
        var selected = (SoundPack)Math.Clamp(SoundPackCombo.SelectedIndex, 0, 2);
        UpdateGeneralPreference(
            "settings.sound-pack",
            "Sound pack",
            previous,
            selected,
            value => _settingsService.Settings.SoundPack = value,
            value => SoundPackCombo.SelectedIndex = (int)value,
            value =>
            {
                SoundService.SetPack(value);
                SoundService.PlayCaptureSound();
            });
    }

    private void RecordingFormatCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;

        var previous = _settingsService.Settings.RecordingFormat;
        var selected = (RecordingFormat)Math.Clamp(RecordingFormatCombo.SelectedIndex, 0, 3);
        UpdateRecordingPreference(
            "settings.recording-format",
            "Recording format",
            previous,
            selected,
            value => _settingsService.Settings.RecordingFormat = value,
            value =>
            {
                RecordingFormatCombo.SelectedIndex = (int)value;
                SelectRecordingFps(value == RecordingFormat.GIF
                    ? _settingsService.Settings.GifFps
                    : _settingsService.Settings.RecordingFps);
                UpdateRecordingFormatVisibility();
            },
            value =>
            {
                SelectRecordingFps(value == RecordingFormat.GIF
                    ? _settingsService.Settings.GifFps
                    : _settingsService.Settings.RecordingFps);
                UpdateRecordingFormatVisibility();
            });
    }

    private void UpdateRecordingFormatVisibility()
    {
        bool isGif = RecordingFormatCombo.SelectedIndex == 0;
        var videoOnlyVisibility = isGif ? Visibility.Collapsed : Visibility.Visible;
        AudioSettingsLabel.Visibility = videoOnlyVisibility;
        VideoOnlySettings.Visibility = videoOnlyVisibility;
    }

    private void RecordingQualityCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;

        var previous = _settingsService.Settings.RecordingQuality;
        var selected = (RecordingQuality)Math.Clamp(RecordingQualityCombo.SelectedIndex, 0, 3);
        UpdateRecordingPreference(
            "settings.recording-quality",
            "Recording quality",
            previous,
            selected,
            value => _settingsService.Settings.RecordingQuality = value,
            value => RecordingQualityCombo.SelectedIndex = (int)value);
    }

    private void RecordingFpsCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;
        if (RecordingFpsCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            return;

        if (!int.TryParse(tag, out int fps))
            return;

        var isGif = _settingsService.Settings.RecordingFormat == RecordingFormat.GIF;
        var previous = isGif ? _settingsService.Settings.GifFps : _settingsService.Settings.RecordingFps;
        UpdateRecordingPreference(
            "settings.recording-fps",
            "Recording FPS",
            previous,
            fps,
            value =>
            {
                if (isGif)
                    _settingsService.Settings.GifFps = value;
                else
                    _settingsService.Settings.RecordingFps = value;
            },
            SelectRecordingFps);
    }

    private void SelectRecordingFps(int fps)
    {
        RecordingFpsCombo.SelectedIndex = fps switch
        {
            15 => 0,
            24 => 1,
            30 => 2,
            60 => 3,
            _ => 2
        };
    }

    private void RecordShowCursorCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.ShowCursor;
        var selected = RecordShowCursorCheck.IsChecked == true;
        UpdateCaptureSavePreference(
            "settings.record-show-cursor",
            "Recording cursor",
            previous,
            selected,
            value => _settingsService.Settings.ShowCursor = value,
            value =>
            {
                RecordShowCursorCheck.IsChecked = value;
                ShowCursorCheck.IsChecked = value;
            },
            () =>
            {
                if (ShowCursorCheck.IsChecked != selected)
                    ShowCursorCheck.IsChecked = selected;
            });
    }

    private void PopulateAudioDevices()
    {
        MicDeviceCombo.Items.Clear();
        var mics = AudioService.GetMicrophones();
        var preferredMicId = _settingsService.Settings.MicrophoneDeviceId
            ?? AudioService.GetDefaultMicrophoneId();
        foreach (var mic in mics)
        {
            var item = CreateAudioDeviceItem(
                mic.Name,
                mic.Id,
                $"Microphone device {mic.Name}",
                $"Use {mic.Name} for microphone recording.");
            MicDeviceCombo.Items.Add(item);
            if (mic.Id == preferredMicId)
                MicDeviceCombo.SelectedItem = item;
        }
        if (MicDeviceCombo.SelectedIndex < 0 && MicDeviceCombo.Items.Count > 0)
            MicDeviceCombo.SelectedIndex = 0;

        DesktopAudioDeviceCombo.Items.Clear();
        var outputs = AudioService.GetDesktopAudioDevices();
        var preferredDesktopAudioId = _settingsService.Settings.DesktopAudioDeviceId
            ?? AudioService.GetDefaultDesktopAudioId();
        foreach (var dev in outputs)
        {
            var item = CreateAudioDeviceItem(
                dev.Name,
                dev.Id,
                $"Desktop audio device {dev.Name}",
                $"Use {dev.Name} for desktop audio recording.");
            DesktopAudioDeviceCombo.Items.Add(item);
            if (dev.Id == preferredDesktopAudioId)
                DesktopAudioDeviceCombo.SelectedItem = item;
        }
        if (DesktopAudioDeviceCombo.SelectedIndex < 0 && DesktopAudioDeviceCombo.Items.Count > 0)
            DesktopAudioDeviceCombo.SelectedIndex = 0;
    }

    private static ComboBoxItem CreateAudioDeviceItem(string name, string id, string automationName, string helpText)
    {
        var item = new ComboBoxItem { Content = name, Tag = id, ToolTip = helpText };
        AutomationProperties.SetName(item, automationName);
        AutomationProperties.SetHelpText(item, helpText);
        return item;
    }

    private void RecordMicCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;

        var previous = _settingsService.Settings.RecordMicrophone;
        var selected = RecordMicCheck.IsChecked == true;
        UpdateRecordingPreference(
            "settings.record-microphone",
            "Microphone recording",
            previous,
            selected,
            value => _settingsService.Settings.RecordMicrophone = value,
            value => RecordMicCheck.IsChecked = value);
    }

    private void RecordDesktopAudioCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;

        var previous = _settingsService.Settings.RecordDesktopAudio;
        var selected = RecordDesktopAudioCheck.IsChecked == true;
        UpdateRecordingPreference(
            "settings.record-desktop-audio",
            "Desktop audio recording",
            previous,
            selected,
            value => _settingsService.Settings.RecordDesktopAudio = value,
            value => RecordDesktopAudioCheck.IsChecked = value);
    }

    private void MicDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;
        if (MicDeviceCombo.SelectedItem is not ComboBoxItem item)
            return;

        var previous = _settingsService.Settings.MicrophoneDeviceId;
        var selected = item.Tag as string;
        UpdateRecordingPreference(
            "settings.microphone-device",
            "Microphone device",
            previous,
            selected,
            value => _settingsService.Settings.MicrophoneDeviceId = value,
            SelectMicDeviceById);
    }

    private void DesktopAudioDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressRecordingPreferenceChange) return;
        if (DesktopAudioDeviceCombo.SelectedItem is not ComboBoxItem item)
            return;

        var previous = _settingsService.Settings.DesktopAudioDeviceId;
        var selected = item.Tag as string;
        UpdateRecordingPreference(
            "settings.desktop-audio-device",
            "Desktop audio device",
            previous,
            selected,
            value => _settingsService.Settings.DesktopAudioDeviceId = value,
            SelectDesktopAudioDeviceById);
    }

    private void UpdateRecordingPreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi,
        Action<T>? applySuccessUi = null)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            SetRecordingPreferenceStatus(string.Empty);
            if (applySuccessUi != null)
            {
                _suppressRecordingPreferenceChange = true;
                try
                {
                    applySuccessUi(current);
                }
                finally
                {
                    _suppressRecordingPreferenceChange = false;
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError(diagnosticKey, ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"{diagnosticKey}-rollback", rollbackEx);
            }

            _suppressRecordingPreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressRecordingPreferenceChange = false;
            }

            SetRecordingPreferenceStatus($"{label} change was not saved. Previous setting restored.");
            ToastWindow.ShowError(
                $"{label} failed",
                $"The previous recording setting was restored. Check Settings -> Recording and try again.\n{ex.Message}");
        }
    }

    private void SetRecordingPreferenceStatus(string message)
    {
        RecordingPreferenceStatusText.Text = message;
        RecordingPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SelectMicDeviceById(string? deviceId)
    {
        SelectComboItemByTag(MicDeviceCombo, deviceId);
    }

    private void SelectDesktopAudioDeviceById(string? deviceId)
    {
        SelectComboItemByTag(DesktopAudioDeviceCombo, deviceId);
    }

    private static void SelectComboItemByTag(System.Windows.Controls.ComboBox comboBox, string? tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private void Hyperlink_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenSupportUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void KoFiSupport_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSupportUrl("https://ko-fi.com/T6T71X9ZAM");
        e.Handled = true;
    }

    private void KoFiSupport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        OpenSupportUrlFromKeyboard("https://ko-fi.com/T6T71X9ZAM", e);
    }

    private void PayPalSupport_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSupportUrl("https://www.paypal.com/paypalme/9KGFX");
        e.Handled = true;
    }

    private void PayPalSupport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        OpenSupportUrlFromKeyboard("https://www.paypal.com/paypalme/9KGFX", e);
    }

    private static void OpenSupportUrlFromKeyboard(string url, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space))
            return;

        OpenSupportUrl(url);
        e.Handled = true;
    }

    private static bool OpenSupportUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open failed", "No support link is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open failed", "The support link is not a valid web link.");
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            if (process is null)
            {
                ToastWindow.ShowError("Open failed", "Windows did not open the support link. Copy the link from Settings -> About and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open failed",
                $"OddSnap could not open the support link. Copy the link from Settings -> About and open it manually.\n{ex.Message}");
            return false;
        }
    }
}
