using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void ApplyThemeColors()
    {
        Theme.Refresh();
        Theme.ApplyTo(Application.Current.Resources);
        Resources["ThemeTextPrimaryBrush"] = Theme.Brush(Theme.TextPrimary);
        Resources["ThemeTextSecondaryBrush"] = Theme.Brush(Theme.TextSecondary);
        Resources["ThemeMutedBrush"] = Theme.Brush(Theme.TextMuted);
        Resources["ThemeCardBrush"] = Theme.Brush(Theme.BgCard);
        Resources["ThemeTabActiveBrush"] = Theme.Brush(Theme.TabActiveBg);
        Resources["ThemeTabHoverBrush"] = Theme.Brush(Theme.TabHoverBg);
        Resources["ThemeInputBackgroundBrush"] = Theme.Brush(Theme.BgSecondary);
        Resources["ThemeInputBorderBrush"] = Theme.Brush(Theme.BorderSubtle);
        Resources["ThemeWindowBorderBrush"] = Theme.Brush(Theme.WindowBorder);
        Resources["ThemeAccentBrush"] = Theme.Brush(Theme.Accent);
        Resources["ThemeSeparatorBrush"] = Theme.Brush(Theme.Separator);
        OuterBorder.Background = Theme.Brush(Theme.BgPrimary);
        OuterBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        TitleBarBorder.Background = Theme.Brush(Theme.TitleBar);
        TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
        Foreground = Theme.Brush(Theme.TextPrimary);
        ApplyThemeToVisualTree(OuterBorder);
        UpdateSectionIcons();
    }

    private void UpdateSectionIcons()
    {
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(160, 255, 255, 255)
            : System.Drawing.Color.FromArgb(170, 0, 0, 0);

        StickerUploadsPanel.IconSource = ToolIcons.RenderStickerWpf(iconColor, 16);
    }

    private void ApplyThemeToVisualTree(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            switch (child)
            {
                case System.Windows.Controls.TextBox textBox:
                    textBox.Background = Theme.Brush(Theme.BgSecondary);
                    textBox.Foreground = Theme.Brush(Theme.TextPrimary);
                    textBox.BorderBrush = Theme.Brush(Theme.BorderSubtle);
                    textBox.CaretBrush = Theme.Brush(Theme.TextPrimary);
                    break;
                case System.Windows.Controls.ComboBox comboBox:
                    comboBox.Background = Theme.Brush(Theme.BgSecondary);
                    comboBox.Foreground = Theme.Brush(Theme.TextPrimary);
                    comboBox.BorderBrush = Theme.Brush(Theme.BorderSubtle);
                    break;
                case Button button when button.Style == null:
                    button.Background = Theme.Brush(Theme.AccentSubtle);
                    button.Foreground = Theme.Brush(Theme.TextPrimary);
                    button.BorderBrush = Theme.Brush(Theme.BorderSubtle);
                    break;
                case CheckBox checkBox:
                    checkBox.Foreground = Theme.Brush(Theme.TextPrimary);
                    break;
            }

            ApplyThemeToVisualTree(child);
        }
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        try { LoadOcrLanguageOptions(s.OcrLanguageTag); } catch { }

        DefaultCaptureModeCombo.SelectedIndex = s.DefaultCaptureMode == Yoink.Models.CaptureMode.Freeform ? 1 : 0;
        AfterCaptureCombo.SelectedIndex = (int)s.AfterCapture;
        SaveToFileCheck.IsChecked = s.SaveToFile;
        CaptureFormatCombo.SelectedIndex = (int)s.CaptureImageFormat;
        JpegQualityCombo.SelectedIndex = s.JpegQuality switch
        {
            >= 95 => 0,
            >= 90 => 1,
            >= 85 => 2,
            >= 75 => 3,
            _ => 4
        };
        CaptureSizeCombo.SelectedIndex = s.CaptureMaxLongEdge switch
        {
            2160 => 1,
            1440 => 2,
            1080 => 3,
            720 => 4,
            480 => 5,
            _ => 0
        };
        SaveDirBox.Text = s.SaveDirectory;
        SaveDirPanel.Visibility = s.SaveToFile ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
        AutoUpdateCheck.IsChecked = s.AutoCheckForUpdates;
        SaveHistoryCheck.IsChecked = s.SaveHistory;
        HistoryRetentionCombo.SelectedIndex = (int)s.HistoryRetention;
        ImageSearchFileNameCheck.IsChecked = (s.ImageSearchSources & ImageSearchSourceOptions.FileName) != 0;
        ImageSearchOcrCheck.IsChecked = (s.ImageSearchSources & ImageSearchSourceOptions.OcrText) != 0;
        ImageSearchSemanticCheck.IsChecked = (s.ImageSearchSources & ImageSearchSourceOptions.Semantic) != 0;
        ImageSearchExactMatchCheck.IsChecked = s.ImageSearchExactMatch;
        ShowImageSearchBarCheck.IsChecked = s.ShowImageSearchBar;
        ShowImageSearchDiagnosticsCheck.IsChecked = s.ShowImageSearchDiagnostics;
        AutoIndexImagesCheck.IsChecked = s.AutoIndexImages;
        MuteSoundsCheck.IsChecked = s.MuteSounds;
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        ShowCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        ShowToolNumberBadgesCheck.IsChecked = s.ShowToolNumberBadges;
        AskFileNameCheck.IsChecked = s.AskForFileNameOnSave;
        ToastPositionCombo.SelectedIndex = (int)s.ToastPosition;
        WindowDetectionCombo.SelectedIndex = (int)s.WindowDetection;
        ShowCursorCheck.IsChecked = s.ShowCursor;
        CaptureDelayCombo.SelectedIndex = s.CaptureDelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        AutoPinPreviewsCheck.IsChecked = s.AutoPinPreviews;
        SoundPackCombo.SelectedIndex = (int)s.SoundPack;
        RecordingFormatCombo.SelectedIndex = (int)s.RecordingFormat;
        RecordingQualityCombo.SelectedIndex = (int)s.RecordingQuality;
        RecordingFpsCombo.SelectedIndex = s.RecordingFps switch { 15 => 0, 24 => 1, 30 => 2, 60 => 3, _ => 2 };
        RecordMicCheck.IsChecked = s.RecordMicrophone;
        RecordDesktopAudioCheck.IsChecked = s.RecordDesktopAudio;
        try { PopulateAudioDevices(); } catch { }

        double dur = s.ToastDurationSeconds;
        int durIdx = dur switch { 1.5 => 0, 2.0 => 1, 2.5 => 2, 3.0 => 3, 4.0 => 4, 5.0 => 5, _ => 2 };
        ToastDurationCombo.SelectedIndex = durIdx;

        UploadDestCombo.SelectedIndex = (int)s.ImageUploadDestination;
        AutoUploadScreenshotsCheck.IsChecked = s.AutoUploadScreenshots;
        AutoUploadGifsCheck.IsChecked = s.AutoUploadGifs;
        AutoUploadVideosCheck.IsChecked = s.AutoUploadVideos;
        try { LoadUploadSettingsIntoUi(s.ImageUploadSettings); } catch { }
        try { LoadStickerSettingsIntoUi(s.StickerUploadSettings); } catch { }
        UpdateUploadSettingsVisibility();
        UpdateUploadTabVisibility();
        VersionText.Text = $"Yoink {UpdateService.GetCurrentVersionLabel()}";

        try { PopulateToolToggles(); } catch { }
        try { UpdateCaptureFormatControls(); } catch { }
        try { UpdateRecordingFormatVisibility(); } catch { }

        if (HistoryTab.IsChecked == true)
        {
            try { LoadCurrentHistoryTab(); } catch { }
        }
    }

    internal static readonly (string id, string label, char icon)[] ExtraTools =
        ToolListBuilder.ExtraTools;

    private void PopulateToolToggles() =>
        ToolListBuilder.Build(ToolTogglePanel, _settingsService, this, () => HotkeyChanged?.Invoke());

    private void ShowToolNumberBadgesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ShowToolNumberBadges = ShowToolNumberBadgesCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void TabChanged(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = HotkeysTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = CaptureTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = RecordingTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UploadsPanel.Visibility = UploadsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = AboutTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            CancelImageSearchWork();

        if (HistoryTab.IsChecked == true) LoadCurrentHistoryTab();
        if (UploadsTab.IsChecked == true) UpdateUploadTabVisibility();
        UpdateHistoryMonitorState();
    }

    private void HistoryCategoryCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateImageSearchUi();
        LoadCurrentHistoryTab();
    }

    private void HistoryCategoryCombo_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox comboBox)
            return;

        if (comboBox.IsDropDownOpen)
            return;

        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void UploadSubTabChanged(object sender, RoutedEventArgs e)
    {
        UpdateUploadTabVisibility();
    }

    private void LoadCurrentHistoryTab()
    {
        ImagesPanel.Visibility = Visibility.Collapsed;
        GifsPanel.Visibility = Visibility.Collapsed;
        TextPanel.Visibility = Visibility.Collapsed;
        ColorsPanel.Visibility = Visibility.Collapsed;
        StickersPanel.Visibility = Visibility.Collapsed;
        UpdateImageSearchUi();

        if (HistoryCategoryCombo.SelectedIndex != 0)
            CancelImageSearchWork();

        switch (HistoryCategoryCombo.SelectedIndex)
        {
            case 0: ImagesPanel.Visibility = Visibility.Visible; LoadHistory(); break;
            case 1: TextPanel.Visibility = Visibility.Visible; LoadOcrHistory(); break;
            case 2: GifsPanel.Visibility = Visibility.Visible; LoadMediaHistory(); break;
            case 3: ColorsPanel.Visibility = Visibility.Visible; LoadColorHistory(); break;
            case 4: StickersPanel.Visibility = Visibility.Visible; LoadStickerHistory(); break;
        }

        UpdateHistoryMonitorState();
    }
}
