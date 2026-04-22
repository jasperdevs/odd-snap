using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace OddSnap.UI;

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
        TitleLogo.Source = ThemedLogo.Wordmark(92, 17);
        Icon = ThemedLogo.Square(32);
        Foreground = Theme.Brush(Theme.TextPrimary);

        ApplyThemeToVisualTree(OuterBorder);
        UpdateSectionIcons();
        RefreshToastButtonEditor();
    }

    private void UpdateSectionIcons()
    {
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(160, 255, 255, 255)
            : System.Drawing.Color.FromArgb(170, 0, 0, 0);

        _ = iconColor;
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
        TryLoadSettingsSection("settings.load-ocr-languages", LoadOcrLanguageOptions);

        DefaultCaptureModeCombo.SelectedIndex = s.DefaultCaptureMode == OddSnap.Models.CaptureMode.Freeform ? 1 : 0;
        var afterCapture = Enum.IsDefined(typeof(AfterCaptureAction), s.AfterCapture)
            ? s.AfterCapture
            : AfterCaptureAction.PreviewAndCopy;
        AfterCaptureCombo.SelectedIndex = (int)afterCapture;
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
        ImageSearchExactMatchCheck.IsChecked = s.ImageSearchExactMatch;
        ShowImageSearchBarCheck.IsChecked = s.ShowImageSearchBar;
        ShowImageSearchDiagnosticsCheck.IsChecked = s.ShowImageSearchDiagnostics;
        AutoIndexImagesCheck.IsChecked = s.AutoIndexImages;
        MuteSoundsCheck.IsChecked = s.MuteSounds;
        DisableAnimationsCheck.IsChecked = s.DisableAnimations;
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        ShowCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        OverlayAllMonitorsCheck.IsChecked = s.OverlayCaptureAllMonitors;
        ShowToolNumberBadgesCheck.IsChecked = s.ShowToolNumberBadges;
        AskFileNameCheck.IsChecked = s.AskForFileNameOnSave;
        MonthlyFoldersCheck.IsChecked = s.SaveInMonthlyFolders;
        LoadFileNameTemplate(s.FileNameTemplate);
        ToastPositionCombo.SelectedIndex = (int)s.ToastPosition;
        CaptureDockSideCombo.SelectedIndex = (int)s.CaptureDockSide;
        WindowDetectionCombo.SelectedIndex = (int)s.WindowDetection;
        ShowCursorCheck.IsChecked = s.ShowCursor;
        AnnotationStrokeShadowCheck.IsChecked = s.AnnotationStrokeShadow;
        CaptureDelayCombo.SelectedIndex = s.CaptureDelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        AutoPinPreviewsCheck.IsChecked = s.AutoPinPreviews;
        SoundPackCombo.SelectedIndex = (int)s.SoundPack;
        RecordingFormatCombo.SelectedIndex = (int)s.RecordingFormat;
        RecordingQualityCombo.SelectedIndex = (int)s.RecordingQuality;
        RecordingFpsCombo.SelectedIndex = s.RecordingFps switch { 15 => 0, 24 => 1, 30 => 2, 60 => 3, _ => 2 };
        RecordShowCursorCheck.IsChecked = s.ShowCursor;
        RecordMicCheck.IsChecked = s.RecordMicrophone;
        RecordDesktopAudioCheck.IsChecked = s.RecordDesktopAudio;
        TryLoadSettingsSection("settings.populate-audio-devices", PopulateAudioDevices);

        double dur = s.ToastDurationSeconds;
        int durIdx = dur switch { 1.5 => 0, 2.0 => 1, 2.5 => 2, 3.0 => 3, 4.0 => 4, 5.0 => 5, _ => 2 };
        ToastDurationCombo.SelectedIndex = durIdx;
        ToastFadeOutCheck.IsChecked = s.ToastFadeOutEnabled;
        double fadeDur = s.ToastFadeOutSeconds;
        int fadeDurIdx = fadeDur switch { 1.0 => 0, 2.0 => 1, 3.0 => 2, 5.0 => 3, _ => 2 };
        ToastFadeDurationCombo.SelectedIndex = fadeDurIdx;
        ToastFadeDurationRow.Visibility = s.ToastFadeOutEnabled ? Visibility.Visible : Visibility.Collapsed;
        LoadToastButtonEditor();

        SelectUploadDestByTag((int)s.ImageUploadDestination);
        AutoUploadScreenshotsCheck.IsChecked = s.AutoUploadScreenshots;
        AutoUploadGifsCheck.IsChecked = s.AutoUploadGifs;
        AutoUploadVideosCheck.IsChecked = s.AutoUploadVideos;
        TryLoadSettingsSection("settings.load-upload-settings", () => LoadUploadSettingsIntoUi(s.ImageUploadSettings));
        TryLoadSettingsSection("settings.load-sticker-settings", () => LoadStickerSettingsIntoUi(s.StickerUploadSettings));
        TryLoadSettingsSection("settings.load-upscale-settings", () => LoadUpscaleSettingsIntoUi(s.UpscaleUploadSettings));
        UpdateUploadSettingsVisibility();
        UpdateUploadTabVisibility();
        VersionText.Text = $"OddSnap {UpdateService.GetCurrentVersionLabel()}";
        TryLoadSettingsSection("settings.populate-tool-toggles", PopulateToolToggles);
        TryLoadSettingsSection("settings.update-capture-format-controls", UpdateCaptureFormatControls);
        TryLoadSettingsSection("settings.update-recording-format-visibility", UpdateRecordingFormatVisibility);

        if (HistoryTab.IsChecked == true)
        {
            TryLoadSettingsSection("settings.schedule-history-tab-load", () => ScheduleHistoryTabLoad());
        }
    }

    private static void TryLoadSettingsSection(string logKey, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError(logKey, ex);
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
        ApplyMainTabSelection();
    }

    private void ApplyMainTabSelection()
    {
        SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ToastPanel.Visibility = ToastTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = HotkeysTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = CaptureTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = RecordingTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OcrPanel.Visibility = OcrTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UploadsPanel.Visibility = UploadsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = AboutTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            CancelImageSearchWork();

        if (HistoryTab.IsChecked == true)
            ScheduleHistoryTabLoad(preserveTransientState: true);
        if (UploadsTab.IsChecked == true)
            UpdateUploadTabVisibility();
        if (OcrTab.IsChecked == true)
            LoadOcrTab();
        UpdateHistoryMonitorState();
    }

    private void HistoryCategoryCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateImageSearchUi();
        ScheduleHistoryTabLoad(preserveTransientState: true);
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

    private void LoadCurrentHistoryTab(bool preserveTransientState = false)
    {
        var loadSw = System.Diagnostics.Stopwatch.StartNew();
        var selectedCategory = HistoryCategoryCombo.SelectedIndex;
        if (!preserveTransientState)
        {
            _selectMode = false;
            SelectBtn.Content = "Select";
            DeleteSelectedBtn.Visibility = Visibility.Collapsed;
            _ocrSearchSurface = null;
            _colorSearchSurface = null;
            _ocrSearchQuery = "";
            _colorSearchQuery = "";
            _imageSearchQuery = "";
            if (ImageSearchBox != null) ImageSearchBox.Text = "";
        }

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
            case 0:
                ImagesPanel.Visibility = Visibility.Visible;
                if (CanReuseLoadedImageHistory())
                    ApplyImageSearchFilter();
                else
                    _ = LoadHistoryAsync();
                break;
            case 1: TextPanel.Visibility = Visibility.Visible; LoadOcrHistory(); break;
            case 2: GifsPanel.Visibility = Visibility.Visible; LoadMediaHistory(); break;
            case 3: ColorsPanel.Visibility = Visibility.Visible; LoadColorHistory(); break;
            case 4: StickersPanel.Visibility = Visibility.Visible; LoadStickerHistory(); break;
        }

        UpdateHistoryMonitorState();
        loadSw.Stop();
        AppDiagnostics.LogInfo(
            "history.tab-load",
            $"category={selectedCategory} preserve={preserveTransientState} elapsedMs={loadSw.ElapsedMilliseconds}");
    }
}
