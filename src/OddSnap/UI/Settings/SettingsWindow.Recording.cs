using System.Windows;
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
        if (!IsLoaded) return;
        _settingsService.Settings.SaveHistory = SaveHistoryCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void HistoryRetentionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.HistoryRetention = (HistoryRetentionPeriod)HistoryRetentionCombo.SelectedIndex;
        _settingsService.Save();
        _historyService.PruneByRetention(_settingsService.Settings.HistoryRetention);
    }

    private void MuteSoundsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.MuteSounds = MuteSoundsCheck.IsChecked == true;
        _settingsService.Save();
        SoundService.Muted = _settingsService.Settings.MuteSounds;
    }

    private void DisableAnimationsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.DisableAnimations = DisableAnimationsCheck.IsChecked == true;
        _settingsService.Save();
        Motion.Disabled = _settingsService.Settings.DisableAnimations;
    }

    private void SoundPackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.SoundPack = (SoundPack)SoundPackCombo.SelectedIndex;
        _settingsService.Save();
        SoundService.SetPack(_settingsService.Settings.SoundPack);
        SoundService.PlayCaptureSound();
    }

    private void RecordingFormatCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.RecordingFormat = (RecordingFormat)RecordingFormatCombo.SelectedIndex;
        _settingsService.Save();
        UpdateRecordingFormatVisibility();
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
        if (!IsLoaded) return;
        _settingsService.Settings.RecordingQuality = (RecordingQuality)RecordingQualityCombo.SelectedIndex;
        _settingsService.Save();
    }

    private void RecordingFpsCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (RecordingFpsCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            if (int.TryParse(tag, out int fps))
            {
                _settingsService.Settings.RecordingFps = fps;
                _settingsService.Save();
            }
    }

    private void RecordShowCursorCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool showCursor = RecordShowCursorCheck.IsChecked == true;
        _settingsService.Settings.ShowCursor = showCursor;
        if (ShowCursorCheck.IsChecked != showCursor)
            ShowCursorCheck.IsChecked = showCursor;
        _settingsService.Save();
    }

    private void PopulateAudioDevices()
    {
        MicDeviceCombo.Items.Clear();
        var mics = AudioService.GetMicrophones();
        var preferredMicId = _settingsService.Settings.MicrophoneDeviceId
            ?? AudioService.GetDefaultMicrophoneId();
        foreach (var mic in mics)
        {
            var item = new ComboBoxItem { Content = mic.Name, Tag = mic.Id };
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
            var item = new ComboBoxItem { Content = dev.Name, Tag = dev.Id };
            DesktopAudioDeviceCombo.Items.Add(item);
            if (dev.Id == preferredDesktopAudioId)
                DesktopAudioDeviceCombo.SelectedItem = item;
        }
        if (DesktopAudioDeviceCombo.SelectedIndex < 0 && DesktopAudioDeviceCombo.Items.Count > 0)
            DesktopAudioDeviceCombo.SelectedIndex = 0;
    }

    private void RecordMicCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.RecordMicrophone = RecordMicCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void RecordDesktopAudioCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.RecordDesktopAudio = RecordDesktopAudioCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void MicDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (MicDeviceCombo.SelectedItem is ComboBoxItem item)
        {
            _settingsService.Settings.MicrophoneDeviceId = item.Tag as string;
            _settingsService.Save();
        }
    }

    private void DesktopAudioDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (DesktopAudioDeviceCombo.SelectedItem is ComboBoxItem item)
        {
            _settingsService.Settings.DesktopAudioDeviceId = item.Tag as string;
            _settingsService.Save();
        }
    }

    private void Hyperlink_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
