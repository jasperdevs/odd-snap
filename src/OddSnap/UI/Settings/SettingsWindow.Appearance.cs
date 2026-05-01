using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

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
        var settingsBg = Theme.IsDark ? MediaColor.FromRgb(31, 31, 31) : MediaColor.FromRgb(243, 243, 243);
        var settingsCard = Theme.IsDark ? MediaColor.FromRgb(43, 43, 43) : MediaColor.FromRgb(255, 255, 255);
        var settingsInput = Theme.IsDark ? MediaColor.FromRgb(36, 36, 36) : MediaColor.FromRgb(249, 249, 249);
        Resources["ThemeCardBrush"] = Theme.Brush(settingsCard);
        Resources["ThemeTabActiveBrush"] = Theme.Brush(Theme.IsDark ? MediaColor.FromArgb(26, 255, 255, 255) : MediaColor.FromArgb(18, 0, 0, 0));
        Resources["ThemeTabHoverBrush"] = Theme.Brush(Theme.IsDark ? MediaColor.FromArgb(16, 255, 255, 255) : MediaColor.FromArgb(12, 0, 0, 0));
        Resources["ThemeInputBackgroundBrush"] = Theme.Brush(settingsInput);
        Resources["ThemeInputBorderBrush"] = Theme.Brush(Theme.IsDark ? MediaColor.FromArgb(28, 255, 255, 255) : MediaColor.FromArgb(22, 0, 0, 0));
        Resources["ThemeWindowBorderBrush"] = Theme.Brush(Theme.IsDark ? MediaColor.FromArgb(30, 255, 255, 255) : MediaColor.FromArgb(22, 0, 0, 0));
        Resources["ThemeAccentBrush"] = Theme.Brush(Theme.Accent);
        Resources["ThemeSeparatorBrush"] = Theme.Brush(Theme.IsDark ? MediaColor.FromArgb(20, 255, 255, 255) : MediaColor.FromArgb(18, 0, 0, 0));
        OuterBorder.Background = Theme.Brush(settingsBg);
        OuterBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        Icon = ThemedLogo.Square(32);
        Foreground = Theme.Brush(Theme.TextPrimary);
        UiScale.ApplyToWindow(this, OuterBorder, scaleWindowBounds: true);

        ApplyThemeToVisualTree(OuterBorder);
        UpdateSectionIcons();
        RefreshToastButtonLayoutDesigner();
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
                    textBox.Background = (MediaBrush)Resources["ThemeInputBackgroundBrush"];
                    textBox.Foreground = (MediaBrush)Resources["ThemeTextPrimaryBrush"];
                    textBox.BorderBrush = (MediaBrush)Resources["ThemeInputBorderBrush"];
                    textBox.CaretBrush = (MediaBrush)Resources["ThemeTextPrimaryBrush"];
                    break;
                case System.Windows.Controls.ComboBox comboBox:
                    comboBox.Background = (MediaBrush)Resources["ThemeInputBackgroundBrush"];
                    comboBox.Foreground = (MediaBrush)Resources["ThemeTextPrimaryBrush"];
                    comboBox.BorderBrush = (MediaBrush)Resources["ThemeInputBorderBrush"];
                    break;
                case Button button when button.Style == null:
                    button.Background = Theme.Brush(Theme.AccentSubtle);
                    button.Foreground = (MediaBrush)Resources["ThemeTextPrimaryBrush"];
                    button.BorderBrush = (MediaBrush)Resources["ThemeInputBorderBrush"];
                    break;
                case CheckBox checkBox:
                    checkBox.Foreground = (MediaBrush)Resources["ThemeTextPrimaryBrush"];
                    break;
            }

            ApplyThemeToVisualTree(child);
        }
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        if (string.Equals(s.InterfaceLanguage, LocalizationService.DefaultLanguageCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(LocalizationService.ResolveLanguageCode(LocalizationService.AutoLanguageCode), LocalizationService.DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            s.InterfaceLanguage = LocalizationService.AutoLanguageCode;
            _settingsService.Save();
        }

        TryLoadSettingsSection("settings.load-ocr-languages", LoadOcrLanguageOptions);

        PopulateInterfaceLanguageOptions();
        SelectInterfaceLanguage(s.InterfaceLanguage);
        DefaultCaptureModeCombo.SelectedIndex = s.DefaultCaptureMode switch
        {
            CaptureMode.Center => 1,
            CaptureMode.Freeform => 2,
            _ => 0
        };
        CenterAspectRatioCombo.SelectedIndex = Enum.IsDefined(typeof(CenterSelectionAspectRatio), s.CenterSelectionAspectRatio)
            ? (int)s.CenterSelectionAspectRatio
            : 0;
        var afterCapture = Enum.IsDefined(typeof(AfterCaptureAction), s.AfterCapture)
            ? s.AfterCapture
            : AfterCaptureAction.PreviewAndCopy;
        AfterCaptureCombo.SelectedIndex = afterCapture switch
        {
            AfterCaptureAction.CopyToClipboard => 0,
            AfterCaptureAction.PreviewOnly => 2,
            _ => 1
        };
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
        SelectUiScale(s.UiScale);
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        ShowCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        OverlayAllMonitorsCheck.IsChecked = s.OverlayCaptureAllMonitors;
        ShowToolNumberBadgesCheck.IsChecked = s.ShowToolNumberBadges;
        AskFileNameCheck.IsChecked = s.AskForFileNameOnSave;
        MonthlyFoldersCheck.IsChecked = s.SaveInMonthlyFolders;
        LoadFileNameTemplate(s.FileNameTemplate);
        ToastPositionCombo.SelectedIndex = (int)s.ToastPosition;
        CaptureDockSideCombo.SelectedIndex = (int)s.CaptureDockSide;
        ScrollingCaptureModeCombo.SelectedIndex = Enum.IsDefined(typeof(ScrollingCaptureMode), s.ScrollingCaptureMode)
            ? (int)s.ScrollingCaptureMode
            : 0;
        WindowDetectionCombo.SelectedIndex = (int)s.WindowDetection;
        ShowCursorCheck.IsChecked = s.ShowCursor;
        AnnotationStrokeShadowCheck.IsChecked = s.AnnotationStrokeShadow;
        CaptureDelayCombo.SelectedIndex = s.CaptureDelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        AutoPinPreviewsCheck.IsChecked = s.AutoPinPreviews;
        SoundPackCombo.SelectedIndex = (int)s.SoundPack;
        RecordingFormatCombo.SelectedIndex = (int)s.RecordingFormat;
        RecordingQualityCombo.SelectedIndex = (int)s.RecordingQuality;
        SelectRecordingFps(s.RecordingFormat == RecordingFormat.GIF ? s.GifFps : s.RecordingFps);
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
        var fadeDurationVisibility = s.ToastFadeOutEnabled ? Visibility.Visible : Visibility.Collapsed;
        ToastFadeDurationSeparator.Visibility = fadeDurationVisibility;
        ToastFadeDurationRow.Visibility = fadeDurationVisibility;
        LoadToastButtonLayoutDesigner();

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

        ApplyLocalization();
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

    private void PopulateInterfaceLanguageOptions()
    {
        InterfaceLanguageCombo.Items.Clear();
        InterfaceLanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = "Auto (system language)",
            Tag = LocalizationService.AutoLanguageCode,
            ToolTip = "Uses Windows language when OddSnap has translations for it.",
        });

        foreach (var language in LocalizationService.Languages)
        {
            bool available = LocalizationService.HasInterfaceTranslations(language.Code);
            var label = string.Equals(language.EnglishName, language.NativeName, StringComparison.OrdinalIgnoreCase)
                ? language.EnglishName
                : $"{language.EnglishName} - {language.NativeName}";
            InterfaceLanguageCombo.Items.Add(new ComboBoxItem
            {
                Content = available ? label : $"{label} (not translated yet)",
                Tag = language.Code,
                IsEnabled = available,
                ToolTip = available
                    ? null
                    : "This language is recognized, but OddSnap does not have app translations for it yet.",
            });
        }
    }

    private void ShowToolNumberBadgesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ShowToolNumberBadges = ShowToolNumberBadgesCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void SelectInterfaceLanguage(string languageCode)
    {
        var normalized = LocalizationService.NormalizeLanguageSetting(languageCode);
        foreach (var item in InterfaceLanguageCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                InterfaceLanguageCombo.SelectedItem = item;
                return;
            }
        }

        InterfaceLanguageCombo.SelectedIndex = 0;
    }

    private void InterfaceLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var selected = InterfaceLanguageCombo.SelectedItem as ComboBoxItem;
        var languageCode = selected?.Tag?.ToString() ?? LocalizationService.AutoLanguageCode;
        if (!string.Equals(languageCode, LocalizationService.AutoLanguageCode, StringComparison.OrdinalIgnoreCase) &&
            !LocalizationService.HasInterfaceTranslations(languageCode))
        {
            ToastWindow.Show("Language not available", "OddSnap does not have translations for that language yet.");
            SelectInterfaceLanguage(_settingsService.Settings.InterfaceLanguage);
            return;
        }

        _settingsService.Settings.InterfaceLanguage = LocalizationService.NormalizeLanguageSetting(languageCode);
        _settingsService.Save();
        ApplyLocalization();
        LocalizationChanged?.Invoke();
    }

    private void ApplyLocalization()
    {
        LocalizationService.ApplyCurrentCulture(_settingsService.Settings.InterfaceLanguage);
        LocalizationService.ApplyTo(this, _settingsService.Settings.InterfaceLanguage);
    }

    private void SelectUiScale(double scale)
    {
        var normalized = UiScale.Normalize(scale);
        foreach (var item in UiScaleCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag &&
                double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var itemScale) &&
                Math.Abs(itemScale - normalized) < 0.001)
            {
                UiScaleCombo.SelectedItem = item;
                return;
            }
        }

        UiScaleCombo.SelectedIndex = 2;
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
        PageTitleText.Text = GetSelectedSettingsPageTitle();

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

    private string GetSelectedSettingsPageTitle()
    {
        if (ToastTab.IsChecked == true) return "Toast";
        if (HotkeysTab.IsChecked == true) return "Tools";
        if (CaptureTab.IsChecked == true) return "Capture";
        if (RecordingTab.IsChecked == true) return "Recording";
        if (OcrTab.IsChecked == true) return "OCR";
        if (HistoryTab.IsChecked == true) return "History";
        if (UploadsTab.IsChecked == true) return "Uploads";
        if (AboutTab.IsChecked == true) return "About";
        return "General";
    }

    private void HistoryCategoryCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateImageSearchUi();
        UpdateHistoryUploadFilterUi();
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
        UpdateHistoryUploadFilterUi();

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
