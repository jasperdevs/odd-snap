using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void ApplyThemeColors()
    {
        Theme.Refresh();
        Theme.ApplyTo(Application.Current.Resources);
        // All values come from the Theme token set — no inline hex here, so the
        // settings palette cannot drift from the rest of the app.
        Resources["ThemeTextPrimaryBrush"] = Theme.Brush(Theme.TextPrimary);
        Resources["ThemeTextSecondaryBrush"] = Theme.Brush(Theme.TextSecondary);
        Resources["ThemeMutedBrush"] = Theme.Brush(Theme.TextMuted);
        Resources["ThemeCardBrush"] = Theme.Brush(Theme.SettingsCardBg);
        Resources["ThemeTabActiveBrush"] = Theme.Brush(Theme.SettingsTabActive);
        Resources["ThemeTabHoverBrush"] = Theme.Brush(Theme.SettingsTabHover);
        Resources["ThemeInputBackgroundBrush"] = Theme.Brush(Theme.SettingsInputBg);
        Resources["ThemeInputBorderBrush"] = Theme.Brush(Theme.SettingsInputBorder);
        Resources["ThemeWindowBorderBrush"] = Theme.Brush(Theme.SettingsWindowBorder);
        Resources["ThemeAccentBrush"] = Theme.Brush(Theme.Accent);
        Resources["ThemeSeparatorBrush"] = Theme.Brush(Theme.SettingsSeparator);
        OuterBorder.Background = Theme.Brush(Theme.BgPrimary);
        OuterBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        Icon = ThemedLogo.Square(32);
        SidebarLogo.Source = ThemedLogo.Square(30);
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
        SetSaveDirectoryPath(s.SaveDirectory);
        SaveDirPanel.Visibility = s.SaveToFile ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
        CreateStartMenuShortcutCheck.IsChecked = s.CreateStartMenuShortcut;
        TrayLeftClickActionCombo.SelectedIndex = Enum.IsDefined(typeof(TrayIconAction), s.TrayLeftClickAction)
            ? (int)s.TrayLeftClickAction
            : 0;
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
        OcrAutoCopyCheck.IsChecked = s.OcrAutoCopyToClipboard;
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        ShowCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        OverlayAllMonitorsCheck.IsChecked = s.OverlayCaptureAllMonitors;
        HdrCaptureCompatibleModeCheck.IsChecked = s.HdrCaptureCompatibleMode;
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
        ShowOddSnapUiInScreenshotsCheck.IsChecked = s.ShowOddSnapUiInScreenshots;
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
        LoadToolbarLayoutDesigner();

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
        var autoLanguageItem = new ComboBoxItem
        {
            // English source text; ApplyTo localizes it and the tooltip.
            Content = "Auto (system language)",
            Tag = LocalizationService.AutoLanguageCode,
            ToolTip = "Uses Windows language when OddSnap has translations for it.",
        };
        AutomationProperties.SetName(autoLanguageItem, "Auto interface language");
        AutomationProperties.SetHelpText(autoLanguageItem, "Use the Windows language when OddSnap has app translations for it.");
        InterfaceLanguageCombo.Items.Add(autoLanguageItem);

        foreach (var language in LocalizationService.Languages)
        {
            bool available = LocalizationService.HasInterfaceTranslations(language.Code);
            // Language names are data, not prose: "English" and "繁體中文" must
            // stay in their own script whatever the UI language is. Wrapping
            // them in a TextBlock keeps LocalizationService.ApplyTo from
            // treating the name as a translatable string, which would turn
            // "English" into its Chinese translation.
            var label = string.Equals(language.EnglishName, language.NativeName, StringComparison.OrdinalIgnoreCase)
                ? language.EnglishName
                : $"{language.EnglishName} - {language.NativeName}";

            var item = new ComboBoxItem
            {
                // Language names are data and must never be translated. The
                // tooltip is prose built from a template; the whole list is
                // rebuilt by ApplyLocalization on a language change, so the
                // finished sentence only ever needs the current language.
                Content = available ? label : $"{label} (not translated yet)",
                Tag = language.Code,
                IsEnabled = available,
                ToolTip = available
                    ? string.Format(LocalizationService.Translate("Use {0} for the OddSnap interface."), label)
                    : LocalizationService.Translate("This language is recognized, but OddSnap does not have app translations for it yet."),
            };
            // Keep ApplyTo away from the language name itself.
            LocalizationService.SetSkipContentLocalization(item, true);
            AutomationProperties.SetName(item, string.Format(LocalizationService.Translate("{0} interface language"), label));
            AutomationProperties.SetHelpText(item, available
                ? string.Format(LocalizationService.Translate("Use {0} for OddSnap menus, settings, and prompts."), label)
                : string.Format(LocalizationService.Translate("{0} is recognized, but OddSnap does not have app translations for it yet."), label));
            InterfaceLanguageCombo.Items.Add(item);
        }
    }

    private void ShowToolNumberBadgesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressGeneralPreferenceChange) return;

        var previous = _settingsService.Settings.ShowToolNumberBadges;
        var selected = ShowToolNumberBadgesCheck.IsChecked == true;
        UpdateGeneralPreference(
            "settings.tool-number-badges",
            "Tool number badges",
            previous,
            selected,
            value => _settingsService.Settings.ShowToolNumberBadges = value,
            value => ShowToolNumberBadgesCheck.IsChecked = value);
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
        if (!IsLoaded || _suppressGeneralPreferenceChange) return;
        var selected = InterfaceLanguageCombo.SelectedItem as ComboBoxItem;
        var languageCode = selected?.Tag?.ToString() ?? LocalizationService.AutoLanguageCode;
        if (!string.Equals(languageCode, LocalizationService.AutoLanguageCode, StringComparison.OrdinalIgnoreCase) &&
            !LocalizationService.HasInterfaceTranslations(languageCode))
        {
            ToastWindow.Show("Language not available", "OddSnap does not have translations for that language yet.");
            SelectInterfaceLanguage(_settingsService.Settings.InterfaceLanguage);
            return;
        }

        var previous = _settingsService.Settings.InterfaceLanguage;
        var normalized = LocalizationService.NormalizeLanguageSetting(languageCode);
        UpdateGeneralPreference(
            "settings.interface-language",
            "Interface language",
            previous,
            normalized,
            value => _settingsService.Settings.InterfaceLanguage = value,
            SelectInterfaceLanguage,
            _ =>
            {
                ApplyLocalization();
                LocalizationChanged?.Invoke();
            });
    }

    private void ApplyLocalization()
    {
        LocalizationService.ApplyCurrentCulture(_settingsService.Settings.InterfaceLanguage);

        // The language list is composed from a template, and ApplyTo cannot
        // re-run string.Format, so it has to be built again for the tooltips to
        // follow a language change. Rebuilding raises SelectionChanged, which
        // itself calls back into this method, so suppress that while we work.
        var wasSuppressed = _suppressGeneralPreferenceChange;
        _suppressGeneralPreferenceChange = true;
        try
        {
            PopulateInterfaceLanguageOptions();
            SelectInterfaceLanguage(_settingsService.Settings.InterfaceLanguage);
        }
        finally
        {
            _suppressGeneralPreferenceChange = wasSuppressed;
        }

        LocalizationService.ApplyTo(this, _settingsService.Settings.InterfaceLanguage);
        InvalidateSearchIndex();

        // Combo item tooltips are formatted from templates ("Open {0} after a
        // capture"), so ApplyTo cannot re-translate the formatted result. Rebuild
        // them from the remembered English source instead.
        RefreshComboItemLocalization();

        // History cards store pre-formatted strings (relative times such as
        // "29m ago", file sizes, counts). Those cannot be re-translated by
        // ApplyTo because the formatted value is not a lookup key, so the
        // caches have to be dropped and the cards rebuilt in the new language.
        ResetHistoryCachesForLanguageChange();

        // Several panels build their content in code and the page title is a
        // runtime assignment, so re-run the tab selection to refresh anything
        // ApplyTo could not reach on its own.
        ApplyMainTabSelection();
    }

    /// <summary>
    /// Drops every cached history view model so relative timestamps and other
    /// pre-formatted labels are recomputed with the newly selected language.
    /// </summary>
    private void ResetHistoryCachesForLanguageChange()
    {
        InvalidateHistoryCategoryCaches();
        ResetMaterializedImageHistory();
    }

    /// <summary>
    /// Rebuilds every combo item tooltip and accessibility string from its
    /// remembered English source. Called on a language change, because those
    /// strings are produced by <c>string.Format</c> and therefore cannot be
    /// matched against the translation table.
    /// </summary>
    private void RefreshComboItemLocalization()
    {
        foreach (var item in EnumerateComboItemsWithSource(this))
        {
            if (SettingsComboSource.Get(item) is not { } source)
                continue;

            if (source.Payload is Services.UploadDestination destination)
            {
                ApplyUploadDestinationItemMetadata(item, destination, source.Text, source.ForLensUpload);
                continue;
            }

            ApplySettingsComboItemMetadata(item);
        }
    }

    /// <summary>
    /// Walks the visual tree looking for <see cref="T:System.Windows.Controls.ComboBox"/> controls and
    /// yields the items that carry a <see cref="SettingsComboItemSource"/>.
    /// Combo items live inside the combo box's own popup, so they are not
    /// reachable by a plain tree walk of the items themselves.
    /// </summary>
    private static IEnumerable<ComboBoxItem> EnumerateComboItemsWithSource(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current is ComboBox combo)
            {
                foreach (var raw in combo.Items)
                {
                    if (raw is ComboBoxItem item && SettingsComboSource.Get(item) is not null)
                        yield return item;
                }
            }

            int count;
            try { count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); }
            catch { count = 0; }

            for (var i = 0; i < count; i++)
            {
                try { stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i)); }
                catch { }
            }
        }
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

        SelectComboByTag(UiScaleCombo, "1.0");
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
        ToolbarPanel.Visibility = ToolbarTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = CaptureTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = RecordingTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OcrPanel.Visibility = OcrTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UploadsPanel.Visibility = UploadsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = AboutTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        // The page title is a plain TextBlock.Text assignment, so the value
        // watcher cannot follow it. Translate here and again on language change
        // (see RefreshPageTitle).
        PageTitleText.Text = LocalizationService.Translate(GetSelectedSettingsPageTitle());

        if (HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            CancelImageSearchWork();

        if (HistoryTab.IsChecked == true)
            ScheduleHistoryTabLoad(preserveTransientState: true);
        if (UploadsTab.IsChecked == true)
            UpdateUploadTabVisibility();
        if (OcrTab.IsChecked == true)
            LoadOcrTab();
        UpdateHistoryMonitorState();

        // The panel that just became visible may have been built while hidden,
        // and the page title was assigned in English above. Localize both now.
        if (_settingsService is not null && IsLoaded)
            LocalizationService.ApplyTo(this, _settingsService.Settings.InterfaceLanguage);
    }

    private string GetSelectedSettingsPageTitle()
    {
        if (ToastTab.IsChecked == true) return "Toast";
        if (HotkeysTab.IsChecked == true) return "Tools";
        if (ToolbarTab.IsChecked == true) return "Toolbar";
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
            UpdateSelectModeControls();
            _ocrSearchQuery = "";
            _colorSearchQuery = "";
            _codeSearchQuery = "";
            _imageSearchQuery = "";
            if (ImageSearchBox != null) ImageSearchBox.Text = "";
        }

        ImagesPanel.Visibility = Visibility.Collapsed;
        GifsPanel.Visibility = Visibility.Collapsed;
        TextPanel.Visibility = Visibility.Collapsed;
        ColorsPanel.Visibility = Visibility.Collapsed;
        StickersPanel.Visibility = Visibility.Collapsed;
        CodesPanel.Visibility = Visibility.Collapsed;
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
            case 5: CodesPanel.Visibility = Visibility.Visible; LoadCodeHistory(); break;
        }

        UpdateHistoryMonitorState();
        UpdateHistoryActionButtons();
        loadSw.Stop();
        AppDiagnostics.LogInfo(
            "history.tab-load",
            $"category={selectedCategory} preserve={preserveTransientState} elapsedMs={loadSw.ElapsedMilliseconds}");
    }
}
