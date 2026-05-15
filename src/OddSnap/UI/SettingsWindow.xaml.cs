using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CaptureMode = OddSnap.Models.CaptureMode;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow : Window
{
    private const string OpenSourceLocalTranslationJobKey = "runtime:translation-open-source-local";
    private const string ArgosTranslationJobKey = "runtime:translation-argos";
    private const int UpdateActionCooldownMs = 900;
    private const int LocalEngineProjectOpenCooldownMs = 900;
    private static readonly (string Token, string Label)[] FileNameTokens =
    [
        ("{year}", "Year"),
        ("{month}", "Month"),
        ("{day}", "Day"),
        ("{hour}", "Hour"),
        ("{min}", "Minute"),
        ("{sec}", "Second"),
        ("{date}", "Date"),
        ("{time}", "Time"),
        ("{datetime}", "Date time"),
        ("{w}", "Width"),
        ("{h}", "Height"),
        ("{aspect}", "Aspect"),
        ("{rand}", "Random"),
    ];
    private static readonly SemaphoreSlim ThumbDecodeGate = new(2);
    private readonly System.Windows.Threading.DispatcherTimer _historyMonitorTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.5)
    };
    private readonly System.Windows.Threading.DispatcherTimer _imageIndexRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1.25)
    };
    private readonly System.Windows.Threading.DispatcherTimer _historyRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly System.Windows.Threading.DispatcherTimer _imageSearchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly ImageSearchIndexService _imageSearchIndexService;
    private UpdateCheckResult? _latestUpdate;
    private bool _updateCheckInFlight;
    private string? _lastHistoryFingerprint;
    private bool _pendingImageSearchTextRefresh;
    private bool _pendingHistoryDiskRefresh;
    private bool _pendingHistoryUiRefresh;
    private bool _pendingHistoryDataRefresh;
    private bool _historyRefreshInProgress;
    private CancellationTokenSource? _historyLoadCts;
    private int _historyLoadVersion;
    private bool _historyLoadInProgress;
    private bool _historyImageCacheReady;
    private bool _deferHistoryMonitor;
    private bool _pendingTrayHistoryOpen;
    private bool _trayHistoryOpenScheduled;
    private int _historyTabLoadVersion;
    private bool _historyTabLoadScheduled;
    private bool _historyTabLoadPreserveTransientState;
    private bool _testUploadInProgress;
    private bool _aiRedirectTestInProgress;
    private bool _suppressUploadDestChange;
    private bool _suppressUploadFieldChange;
    private bool _suppressAutoUploadChange;
    private bool _suppressAiRedirectProviderChange;
    private bool _suppressAiRedirectLensUploadDestChange;
    private bool _suppressAiRedirectLensUploadSyncChange;
    private bool _suppressCaptureSavePreferenceChange;
    private bool _suppressToastPreferenceChange;
    private bool _suppressGeneralPreferenceChange;
    private bool _suppressRecordingPreferenceChange;
    private bool _suppressHistoryPreferenceChange;
    private bool _suppressUpdatePreferenceChange;
    private bool _suppressOcrPreferenceChange;
    private bool _updateActionInProgress;
    private bool ImageIndexResetInProgress { get; set; }
    private bool _stickerProjectOpenInProgress;
    private bool _upscaleProjectOpenInProgress;
    private bool _stickerModelRemovalInProgress;
    private bool _upscaleModelRemovalInProgress;
    private bool _stickerRuntimeMutationInProgress;
    private bool _upscaleRuntimeMutationInProgress;
    private bool _suppressStickerSettingChange;
    private bool _suppressUpscaleSettingChange;
    private bool _openSourceTranslationRuntimeActionInProgress;
    private bool _argosTranslationRuntimeActionInProgress;
    private bool _suppressStartWithWindowsChange;

    public event Action? HotkeyChanged;
    public event Action? UninstallRequested;
    public event Action? LocalizationChanged;

    public SettingsWindow(SettingsService settingsService, HistoryService historyService, ImageSearchIndexService imageSearchIndexService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        _imageSearchIndexService = imageSearchIndexService;
        InitializeComponent();
        OddSnapWindowChrome.Apply(this);
        Theme.Refresh();
        Theme.ApplyTo(Application.Current.Resources);
        ApplyThemeColors();
        LoadStaticFluentIcons();
        LoadFileNameTokenButtons();
        LoadSettings();
        Loaded += (_, _) => EnsureSettingsWindowFitsWorkArea();
        Loaded += (_, _) => ApplyMicaBackdrop();
        Loaded += async (_, _) => await RefreshUpdateStatusAsync(false);
        ContentRendered += (_, _) => TryProcessPendingTrayHistoryOpen();
        _historyService.Changed += HistoryService_Changed;
        _imageSearchIndexService.Changed += ImageSearchIndexService_Changed;
        _imageSearchIndexService.StatusChanged += ImageSearchIndexService_StatusChanged;
        BackgroundRuntimeJobService.Changed += BackgroundRuntimeJobService_Changed;
        _historyMonitorTimer.Tick += (_, _) => PollHistoryChanges();
        _historyRefreshTimer.Tick += async (_, _) => await FlushQueuedHistoryRefreshAsync();
        _imageIndexRefreshTimer.Tick += (_, _) => FlushQueuedImageIndexRefresh();
        _imageSearchDebounceTimer.Tick += (_, _) => FlushQueuedImageSearchRefresh();
        Activated += (_, _) =>
        {
            ApplyThemeColors();
            LoadStaticFluentIcons();
            UpdateLocalEngineUi();
            UpdateUpscaleLocalEngineUi();
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 0)
                UpdateVirtualizedHistoryViewport();
        };
        Closed += (_, _) =>
        {
            _historyService.Changed -= HistoryService_Changed;
            _imageSearchIndexService.Changed -= ImageSearchIndexService_Changed;
            _imageSearchIndexService.StatusChanged -= ImageSearchIndexService_StatusChanged;
            BackgroundRuntimeJobService.Changed -= BackgroundRuntimeJobService_Changed;
            _historyLoadCts?.Cancel();
            _historyLoadCts?.Dispose();
            CancelImageSearchWork();
            _imageIndexRefreshTimer.Stop();
            _historyRefreshTimer.Stop();
            _imageSearchDebounceTimer.Stop();
            _ocrSearchDebounceTimer.Stop();
            _colorSearchDebounceTimer.Stop();
            _historyMonitorTimer.Stop();
            ReleaseHistoryUiState();
            TrimThumbCache(24);
        };
    }

    private void EnsureSettingsWindowFitsWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        const double screenMargin = 12d;
        var maxWidth = Math.Max(360d, workArea.Width - screenMargin * 2d);
        var maxHeight = Math.Max(360d, workArea.Height - screenMargin * 2d);

        MinWidth = Math.Min(MinWidth, maxWidth);
        MinHeight = Math.Min(MinHeight, maxHeight);

        if (Width > maxWidth)
            Width = maxWidth;
        if (Height > maxHeight)
            Height = maxHeight;

        var minLeft = workArea.Left + screenMargin;
        var minTop = workArea.Top + screenMargin;
        var maxLeft = workArea.Right - Width - screenMargin;
        var maxTop = workArea.Bottom - Height - screenMargin;

        Left = Math.Min(Math.Max(Left, minLeft), Math.Max(minLeft, maxLeft));
        Top = Math.Min(Math.Max(Top, minTop), Math.Max(minTop, maxTop));
    }

    private async void BackgroundRuntimeJobService_Changed(string key)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => BackgroundRuntimeJobService_Changed(key));
            return;
        }

        if (!IsLoaded)
            return;

        try
        {
            if (_ocrTabLoaded)
                await CheckModelStatusAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.background-runtime-ocr-changed", ex);
            SetTranslationRuntimeStatusRefreshFailed(ex.Message);
        }

        try
        {
            UpdateLocalEngineUi();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.background-runtime-sticker-changed", ex);
            SetStickerRuntimeStatusRefreshFailed(ex.Message);
        }

        try
        {
            UpdateUpscaleLocalEngineUi();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.background-runtime-upscale-changed", ex);
            SetUpscaleRuntimeStatusRefreshFailed(ex.Message);
        }
    }

    private void SetStickerRuntimeStatusRefreshFailed(string message)
    {
        StickerLocalEngineStatusText.Text = "Runtime status refresh failed";
        StickerLocalEngineProgress.Visibility = Visibility.Collapsed;
        StickerLocalEngineProgress.IsIndeterminate = false;
        StickerLocalEngineProgress.Value = 0;
        StickerLocalEngineProgressText.Visibility = Visibility.Visible;
        StickerLocalEngineProgressText.Text = BuildRuntimeRefreshFailureDetail("Settings -> Stickers", message);
        SetLoadingTextShimmer(StickerLocalEngineStatusText, false, 1.0, 0.35);
        SetLoadingTextShimmer(StickerLocalEngineProgressText, false, 0.9, 0.25);
    }

    private void SetUpscaleRuntimeStatusRefreshFailed(string message)
    {
        UpscaleLocalEngineStatusText.Text = "Runtime status refresh failed";
        UpscaleLocalEngineProgress.Visibility = Visibility.Collapsed;
        UpscaleLocalEngineProgress.IsIndeterminate = false;
        UpscaleLocalEngineProgress.Value = 0;
        UpscaleLocalEngineProgressText.Visibility = Visibility.Visible;
        UpscaleLocalEngineProgressText.Text = BuildRuntimeRefreshFailureDetail("Settings -> Upscale", message);
        SetLoadingTextShimmer(UpscaleLocalEngineStatusText, false, 1.0, 0.35);
        SetLoadingTextShimmer(UpscaleLocalEngineProgressText, false, 0.9, 0.25);
    }

    private static string BuildRuntimeRefreshFailureDetail(string settingsPanel, string message)
    {
        var recovery = "Check " + settingsPanel + " and try again.";
        return string.IsNullOrWhiteSpace(message) ? recovery : recovery + " Details were logged.";
    }

    private static string GetStickerRuntimeJobKey(StickerExecutionProvider provider)
        => $"runtime:sticker-rembg:{provider}";

    private static string GetStickerModelJobKey(LocalStickerEngine engine)
        => $"runtime:sticker-model:{engine}";

    private static string GetUpscaleRuntimeJobKey(UpscaleExecutionProvider provider)
        => $"runtime:upscale-onnx:{provider}";

    private static string GetUpscaleModelJobKey(LocalUpscaleEngine engine)
        => $"runtime:upscale-model:{engine}";

    public void OpenHistoryFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(OpenHistoryFromTray, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        _pendingTrayHistoryOpen = true;
        HistoryTab.IsChecked = true;
        if (IsLoaded)
            ApplyMainTabSelection();
        TryProcessPendingTrayHistoryOpen();
    }

    private void LoadStaticFluentIcons()
    {
        var color = Theme.IsDark
            ? System.Drawing.Color.FromArgb(210, 255, 255, 255)
            : System.Drawing.Color.FromArgb(170, 0, 0, 0);
        ImageSearchIcon.Source = Helpers.FluentIcons.RenderWpf("search", color, 18);
    }

    private void TryProcessPendingTrayHistoryOpen()
    {
        if (!_pendingTrayHistoryOpen || !IsLoaded || !IsVisible || _trayHistoryOpenScheduled)
            return;

        _trayHistoryOpenScheduled = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _trayHistoryOpenScheduled = false;
            if (!_pendingTrayHistoryOpen || !IsLoaded || !IsVisible)
                return;

            _pendingTrayHistoryOpen = false;
            HistoryTab.IsChecked = true;
            ApplyMainTabSelection();
            Activate();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void HistoryService_Changed()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                InvalidateHistoryCategoryCaches();
                _pendingHistoryDataRefresh = true;
                QueueHistoryRefresh(reloadFromDisk: false);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("settings.history-service-changed", ex);
                _pendingHistoryDataRefresh = false;
                _pendingHistoryUiRefresh = false;
                _pendingHistoryDiskRefresh = false;
                _historyRefreshTimer.Stop();
                if (IsLoaded && HistoryTab.IsChecked == true)
                    ShowHistoryEmptyState("Couldn't refresh history", "Retry loading history. If it still fails, check the app log.", showRetry: true);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ImageSearchIndexService_Changed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateImageSearchStatus();
                UpdateImageSearchActionButtons();
                UpdateImageSearchPlaceholderText();
                QueueImageIndexRefresh();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("settings.image-search-index-changed", ex);
                SetImageSearchLoading(false, forceIndexed: true);
                HistorySearchStatusText.Text = "Search failed";
            }
        });
    }

    private void ImageSearchIndexService_StatusChanged(string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
                    return;

                UpdateImageSearchStatus();
                UpdateImageSearchActionButtons();
                UpdateImageSearchPlaceholderText();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("settings.image-search-status", ex);
                SetImageSearchLoading(false, forceIndexed: true);
                HistorySearchStatusText.Text = "Search failed";
            }
        });
    }

    private void QueueImageIndexRefresh()
    {
        _pendingImageSearchTextRefresh = true;

        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        _imageIndexRefreshTimer.Stop();
        _imageIndexRefreshTimer.Start();
    }

    private void QueueImageSearchRefresh()
    {
        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        if (!_settingsService.Settings.ShowImageSearchBar)
            return;

        _imageSearchDebounceTimer.Stop();
        _imageSearchDebounceTimer.Start();
    }

    private void FlushQueuedImageIndexRefresh()
    {
        _imageIndexRefreshTimer.Stop();

        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        if (_pendingImageSearchTextRefresh)
        {
            var relevantItems = _historyItems.Count > 0
                ? _historyItems
                : _filteredHistoryItems.Count > 0
                    ? _filteredHistoryItems
                    : _allHistoryItems;
            RefreshImageSearchTexts(relevantItems);
            _pendingImageSearchTextRefresh = false;
        }

        ApplyImageSearchFilter();
    }

    private void FlushQueuedImageSearchRefresh()
    {
        _imageSearchDebounceTimer.Stop();

        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        if (!_settingsService.Settings.ShowImageSearchBar)
            return;

        ApplyImageSearchFilter();
    }

    private void PollHistoryChanges()
    {
        if (!IsLoaded || HistoryTab.IsChecked != true || _deferHistoryMonitor || _historyLoadInProgress)
        {
            _historyMonitorTimer.Stop();
            return;
        }

        var fingerprint = _historyService.GetDiskFingerprint(_settingsService.Settings.SaveDirectory);
        if (fingerprint == _lastHistoryFingerprint)
            return;

        QueueHistoryRefresh(reloadFromDisk: true);
    }

    private void RefreshHistoryFromDisk()
    {
        _historyService.RecoverFromDirectories(_settingsService.Settings.SaveDirectory);
        _historyService.PruneByRetention(_settingsService.Settings.HistoryRetention);
    }

    private void PrimeHistoryFingerprint()
    {
        _lastHistoryFingerprint = _historyService.GetDiskFingerprint(_settingsService.Settings.SaveDirectory);
    }

    private bool CanReuseLoadedImageHistory()
    {
        if (!_historyImageCacheReady || _historyLoadInProgress || _pendingHistoryDiskRefresh)
            return false;

        if (_allHistoryItems.Count == 0)
            return false;

        var fingerprint = _historyService.GetDiskFingerprint(_settingsService.Settings.SaveDirectory);
        return string.Equals(fingerprint, _lastHistoryFingerprint, StringComparison.Ordinal);
    }

    private void UpdateHistoryMonitorState()
    {
        if (HistoryTab.IsChecked == true)
        {
            if (_deferHistoryMonitor || _historyLoadInProgress)
            {
                _historyMonitorTimer.Stop();
                return;
            }

            PrimeHistoryFingerprint();
            if (!_historyMonitorTimer.IsEnabled)
                _historyMonitorTimer.Start();
        }
        else
        {
            _historyMonitorTimer.Stop();
            _lastHistoryFingerprint = null;
        }
    }

    private void QueueHistoryRefresh(bool reloadFromDisk)
    {
        if (!IsLoaded || HistoryTab.IsChecked != true)
            return;

        if (reloadFromDisk)
            _historyImageCacheReady = false;

        _pendingHistoryDiskRefresh |= reloadFromDisk;
        _pendingHistoryUiRefresh = true;
        _historyRefreshTimer.Stop();
        _historyRefreshTimer.Start();
    }

    private void ScheduleHistoryTabLoad(bool preserveTransientState = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ScheduleHistoryTabLoad(preserveTransientState), System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        _historyTabLoadVersion++;
        _historyTabLoadPreserveTransientState |= preserveTransientState;
        if (_historyTabLoadScheduled)
            return;

        _historyTabLoadScheduled = true;
        var scheduledVersion = _historyTabLoadVersion;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _historyTabLoadScheduled = false;
            if (!IsLoaded || HistoryTab.IsChecked != true || scheduledVersion != _historyTabLoadVersion)
                return;

            var preserveState = _historyTabLoadPreserveTransientState;
            _historyTabLoadPreserveTransientState = false;
            LoadCurrentHistoryTab(preserveTransientState: preserveState);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private async Task FlushQueuedHistoryRefreshAsync()
    {
        _historyRefreshTimer.Stop();

        if (!IsLoaded || HistoryTab.IsChecked != true)
            return;

        if (_historyRefreshInProgress || _historyLoadInProgress)
        {
            _historyRefreshTimer.Start();
            return;
        }

        _historyRefreshInProgress = true;
        try
        {
            var reloadFromDisk = _pendingHistoryDiskRefresh;
            var refreshLoadedData = _pendingHistoryDataRefresh;
            _pendingHistoryDiskRefresh = false;
            _pendingHistoryDataRefresh = false;
            _pendingHistoryUiRefresh = false;

            if (reloadFromDisk)
                await Task.Run(RefreshHistoryFromDisk);

            if (!reloadFromDisk &&
                refreshLoadedData &&
                HistoryCategoryCombo.SelectedIndex == 0 &&
                TryRefreshLoadedImageHistoryIncrementally())
            {
                PrimeHistoryFingerprint();
            }
            else
            {
                ScheduleHistoryTabLoad(preserveTransientState: true);
                PrimeHistoryFingerprint();
            }
        }
        finally
        {
            _historyRefreshInProgress = false;
            if (_pendingHistoryDiskRefresh || _pendingHistoryDataRefresh || _pendingHistoryUiRefresh)
                _historyRefreshTimer.Start();
        }
    }

    private void TitleBar_CloseRequested(object? sender, EventArgs e) => Close();

    private void ApplyMicaBackdrop()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.Dwm.DisableBackdrop(hwnd);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings.apply-backdrop", ex.Message, ex);
        }
        ApplyThemeColors();
    }

    private void AfterCaptureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.AfterCapture;
        var selected = AfterCaptureCombo.SelectedIndex switch
        {
            0 => AfterCaptureAction.CopyToClipboard,
            2 => AfterCaptureAction.PreviewOnly,
            _ => AfterCaptureAction.PreviewAndCopy
        };

        UpdateCaptureSavePreference(
            "settings.after-capture",
            "After capture",
            previous,
            selected,
            value => _settingsService.Settings.AfterCapture = value,
            value => AfterCaptureCombo.SelectedIndex = value switch
            {
                AfterCaptureAction.CopyToClipboard => 0,
                AfterCaptureAction.PreviewOnly => 2,
                _ => 1
            });
    }

    private void DefaultCaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.DefaultCaptureMode;
        var selected = DefaultCaptureModeCombo.SelectedIndex switch
        {
            1 => CaptureMode.Center,
            2 => CaptureMode.Freeform,
            _ => CaptureMode.Rectangle
        };

        UpdateCaptureSavePreference(
            "settings.default-capture-mode",
            "Default capture tool",
            previous,
            selected,
            value => _settingsService.Settings.DefaultCaptureMode = value,
            value => DefaultCaptureModeCombo.SelectedIndex = value switch
            {
                CaptureMode.Center => 1,
                CaptureMode.Freeform => 2,
                _ => 0
            },
            notifyHotkeyChanged: true);
    }

    private void CenterAspectRatioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.CenterSelectionAspectRatio;
        var selectedIndex = Math.Clamp(CenterAspectRatioCombo.SelectedIndex, 0, 5);
        var selected = (CenterSelectionAspectRatio)selectedIndex;

        UpdateCaptureSavePreference(
            "settings.center-aspect-ratio",
            "Center aspect ratio",
            previous,
            selected,
            value => _settingsService.Settings.CenterSelectionAspectRatio = value,
            value => CenterAspectRatioCombo.SelectedIndex = (int)value,
            () => CenterAspectRatioCombo.SelectedIndex = selectedIndex);
    }

    private void SaveToFileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.SaveToFile;
        var selected = SaveToFileCheck.IsChecked == true;
        UpdateCaptureSavePreference(
            "settings.save-to-file",
            "Save screenshots",
            previous,
            selected,
            value => _settingsService.Settings.SaveToFile = value,
            value =>
            {
                SaveToFileCheck.IsChecked = value;
                SaveDirPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            },
            () => SaveDirPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed);
    }

    private void AskFileNameCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.AskForFileNameOnSave;
        var selected = AskFileNameCheck.IsChecked == true;
        UpdateCaptureSavePreference(
            "settings.ask-file-name",
            "Ask for file name",
            previous,
            selected,
            value => _settingsService.Settings.AskForFileNameOnSave = value,
            value => AskFileNameCheck.IsChecked = value);
    }

    private void LoadFileNameTemplate(string currentTemplate)
    {
        FileNameTemplateBox.Text = currentTemplate;
        UpdateFileNameTemplatePreview(currentTemplate);
    }

    private void SetSaveDirectoryPath(string path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "" : path;
        SaveDirBox.Text = value;
        AutomationProperties.SetHelpText(
            SaveDirBox,
            string.IsNullOrWhiteSpace(value)
                ? "No save folder selected."
                : $"Current save folder: {value}");
    }

    private void FileNameTemplateBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.FileNameTemplate;
        var template = FileNameTemplateBox.Text;
        UpdateCaptureSavePreference(
            "settings.file-name-template",
            "File name pattern",
            previous,
            template,
            value => _settingsService.Settings.FileNameTemplate = value,
            value =>
            {
                FileNameTemplateBox.Text = value;
                UpdateFileNameTemplatePreview(value);
            },
            () => UpdateFileNameTemplatePreview(template));
    }

    private void UpdateCaptureSavePreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi,
        Action? applyCurrentUi = null,
        bool notifyHotkeyChanged = false)
    {
        try
        {
            setValue(current);
            if (applyCurrentUi != null)
            {
                _suppressCaptureSavePreferenceChange = true;
                try
                {
                    applyCurrentUi();
                }
                finally
                {
                    _suppressCaptureSavePreferenceChange = false;
                }
            }

            _settingsService.Save();
            SetCaptureSavePreferenceStatus(string.Empty);
            if (notifyHotkeyChanged)
                HotkeyChanged?.Invoke();
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

            _suppressCaptureSavePreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressCaptureSavePreferenceChange = false;
            }

            ShowCaptureSavePreferenceFailed(label, ex);
        }
    }

    private void ShowCaptureSavePreferenceFailed(string label, Exception ex)
    {
        SetCaptureSavePreferenceStatus($"{label} change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            $"{label} failed",
            $"The previous capture setting was restored. Check Settings -> Capture and try again.\n{ex.Message}");
    }

    private void SetCaptureSavePreferenceStatus(string message)
    {
        CaptureSavePreferenceStatusText.Text = message;
        CaptureSavePreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateFileNameTemplatePreview(string template)
    {
        if (FileNameTemplatePreviewText is null)
            return;

        FileNameTemplatePreviewText.Text = $"Preview: {Helpers.FileNameTemplate.FormatExample(template)}.png";
    }

    private void LoadFileNameTokenButtons()
    {
        FileNameTokenPanel.Children.Clear();
        foreach (var (token, label) in FileNameTokens)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = token,
                ToolTip = $"Insert {label} token",
                FontSize = 11,
                MinHeight = 28,
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Tag = token,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            AutomationProperties.SetName(button, $"Insert {label} token");
            AutomationProperties.SetHelpText(button, token);
            button.Click += FileNameTokenButton_Click;
            FileNameTokenPanel.Children.Add(button);
        }
    }

    private void FileNameTokenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string token })
            return;

        var box = FileNameTemplateBox;
        var text = box.Text ?? "";
        var start = Math.Clamp(box.SelectionStart, 0, text.Length);
        var length = Math.Clamp(box.SelectionLength, 0, text.Length - start);
        var insert = NeedsLeadingSeparator(text, start) ? "-" + token : token;

        box.Text = text.Remove(start, length).Insert(start, insert);
        box.Focus();
        box.SelectionStart = start + insert.Length;
        box.SelectionLength = 0;
    }

    private void FileNameTemplateBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || box.SelectionLength > 0)
            return;

        var text = box.Text ?? "";
        var caret = box.SelectionStart;
        var range = e.Key switch
        {
            Key.Back => FindTokenRangeBeforeCaret(text, caret),
            Key.Delete => FindTokenRangeAfterCaret(text, caret),
            _ => null
        };

        if (range is not { } tokenRange)
            return;

        box.Text = text.Remove(tokenRange.Start, tokenRange.Length);
        box.SelectionStart = tokenRange.Start;
        box.SelectionLength = 0;
        e.Handled = true;
    }

    private static bool NeedsLeadingSeparator(string text, int insertionIndex)
        => insertionIndex > 0
            && !char.IsWhiteSpace(text[insertionIndex - 1])
            && text[insertionIndex - 1] is not '_' and not '-' and not '.' and not '(';

    private static RangeSpec? FindTokenRangeBeforeCaret(string text, int caret)
    {
        foreach (var (token, _) in FileNameTokens)
        {
            var start = caret - token.Length;
            if (start >= 0 && string.Equals(text.Substring(start, token.Length), token, StringComparison.OrdinalIgnoreCase))
                return new RangeSpec(start, token.Length);
        }

        return null;
    }

    private static RangeSpec? FindTokenRangeAfterCaret(string text, int caret)
    {
        foreach (var (token, _) in FileNameTokens)
        {
            if (caret + token.Length <= text.Length && string.Equals(text.Substring(caret, token.Length), token, StringComparison.OrdinalIgnoreCase))
                return new RangeSpec(caret, token.Length);
        }

        return null;
    }

    private sealed record RangeSpec(int Start, int Length);

    private void MonthlyFoldersCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.SaveInMonthlyFolders;
        var selected = MonthlyFoldersCheck.IsChecked == true;
        UpdateCaptureSavePreference(
            "settings.monthly-folders",
            "Monthly folders",
            previous,
            selected,
            value => _settingsService.Settings.SaveInMonthlyFolders = value,
            value => MonthlyFoldersCheck.IsChecked = value);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose save folder",
            SelectedPath = _settingsService.Settings.SaveDirectory,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var previous = _settingsService.Settings.SaveDirectory;
            var selectedPath = dlg.SelectedPath;
            UpdateCaptureSavePreference(
                "settings.save-directory",
                "Save folder",
                previous,
                selectedPath,
                value => _settingsService.Settings.SaveDirectory = value,
                SetSaveDirectoryPath,
                () => SetSaveDirectoryPath(selectedPath));
        }
    }

    private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressStartWithWindowsChange) return;
        bool on = StartWithWindowsCheck.IsChecked == true;
        bool previous = _settingsService.Settings.StartWithWindows;

        try
        {
            UninstallService.SetStartupEntry(on);
            _settingsService.Settings.StartWithWindows = on;
            _settingsService.Save();
            SetStartupPreferenceStatus(string.Empty);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.start-with-windows", ex);
            try
            {
                UninstallService.SetStartupEntry(previous);
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.start-with-windows-rollback", rollbackEx);
            }

            _settingsService.Settings.StartWithWindows = previous;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.start-with-windows-save-rollback", rollbackEx);
            }

            _suppressStartWithWindowsChange = true;
            try
            {
                StartWithWindowsCheck.IsChecked = previous;
            }
            finally
            {
                _suppressStartWithWindowsChange = false;
            }

            ShowStartupPreferenceFailed(ex);
        }
    }

    private void ShowStartupPreferenceFailed(Exception ex)
    {
        SetStartupPreferenceStatus("Startup setting change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            "Startup setting failed",
            $"The previous startup setting was restored. Check Settings -> About and try again.\n{ex.Message}");
    }

    private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressUpdatePreferenceChange) return;

        var previous = _settingsService.Settings.AutoCheckForUpdates;
        var selected = AutoUpdateCheck.IsChecked == true;
        UpdateUpdatePreference(
            "settings.auto-update",
            "Auto update checks",
            previous,
            selected,
            value => _settingsService.Settings.AutoCheckForUpdates = value,
            value => AutoUpdateCheck.IsChecked = value);
    }

    private void UpdateUpdatePreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            SetUpdatePreferenceStatus(string.Empty);
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

            _suppressUpdatePreferenceChange = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressUpdatePreferenceChange = false;
            }

            ShowUpdatePreferenceFailed(label, ex);
        }
    }

    private void ShowUpdatePreferenceFailed(string label, Exception ex)
    {
        SetUpdatePreferenceStatus($"{label} change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            $"{label} failed",
            $"The previous update setting was restored. Check Settings -> About and try again.\n{ex.Message}");
    }

    private void SetUpdatePreferenceStatus(string message)
    {
        AboutPreferenceStatusText.Text = message;
        AboutPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetStartupPreferenceStatus(string message)
    {
        StartupPreferenceStatusText.Text = message;
        StartupPreferenceStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CaptureFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;

        var previous = _settingsService.Settings.CaptureImageFormat;
        var selected = (CaptureImageFormat)CaptureFormatCombo.SelectedIndex;
        UpdateCaptureSavePreference(
            "settings.capture-format",
            "Capture format",
            previous,
            selected,
            value => _settingsService.Settings.CaptureImageFormat = value,
            value =>
            {
                CaptureFormatCombo.SelectedIndex = (int)value;
                _historyService.CaptureImageFormat = value;
                UpdateCaptureFormatControls();
            },
            () =>
            {
                _historyService.CaptureImageFormat = selected;
                UpdateCaptureFormatControls();
            });
    }

    private void JpegQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;
        var selected = JpegQualityCombo.SelectedItem as ComboBoxItem;
        var tag = selected?.Tag?.ToString();
        var quality = int.TryParse(tag, out var value) ? value : 85;
        var previous = _settingsService.Settings.JpegQuality;
        var selectedIndex = JpegQualityCombo.SelectedIndex;
        UpdateCaptureSavePreference(
            "settings.jpeg-quality",
            "JPG quality",
            previous,
            quality,
            value => _settingsService.Settings.JpegQuality = value,
            value =>
            {
                JpegQualityCombo.SelectedIndex = value switch
                {
                    >= 95 => 0,
                    >= 90 => 1,
                    >= 85 => 2,
                    >= 75 => 3,
                    _ => 4
                };
                _historyService.JpegQuality = value;
            },
            () =>
            {
                JpegQualityCombo.SelectedIndex = selectedIndex;
                _historyService.JpegQuality = quality;
            });
    }

    private void CaptureSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;
        var selected = CaptureSizeCombo.SelectedItem as ComboBoxItem;
        var tag = selected?.Tag?.ToString();
        var maxLongEdge = int.TryParse(tag, out var value) ? value : 0;
        var previous = _settingsService.Settings.CaptureMaxLongEdge;
        var selectedIndex = CaptureSizeCombo.SelectedIndex;
        UpdateCaptureSavePreference(
            "settings.capture-size",
            "Max image size",
            previous,
            maxLongEdge,
            value => _settingsService.Settings.CaptureMaxLongEdge = value,
            value => CaptureSizeCombo.SelectedIndex = value switch
            {
                2160 => 1,
                1440 => 2,
                1080 => 3,
                720 => 4,
                480 => 5,
                _ => 0
            },
            () => CaptureSizeCombo.SelectedIndex = selectedIndex);
    }
}
