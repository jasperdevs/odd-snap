using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using CaptureMode = Yoink.Models.CaptureMode;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow : Window
{
    private const string SemanticRuntimeJobKey = "runtime:semantic-search";
    private const string OpenSourceLocalTranslationJobKey = "runtime:translation-open-source-local";
    private const string ArgosTranslationJobKey = "runtime:translation-argos";
    private static readonly SemaphoreSlim ThumbDecodeGate = new(4);
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
    private readonly System.Windows.Threading.DispatcherTimer _semanticSearchTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400)
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

    public event Action? HotkeyChanged;
    public event Action? UninstallRequested;

    public SettingsWindow(SettingsService settingsService, HistoryService historyService, ImageSearchIndexService imageSearchIndexService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        _imageSearchIndexService = imageSearchIndexService;
        InitializeComponent();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });
        Theme.Refresh();
        Theme.ApplyTo(Application.Current.Resources);
        ApplyThemeColors();
        WireHotkeyBoxes();
        LoadSettings();
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
        _semanticSearchTimer.Tick += (_, _) => FlushQueuedSemanticSearchRefresh();
        Activated += (_, _) =>
        {
            ApplyThemeColors();
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
            _semanticSearchTimer.Stop();
            _ocrSearchDebounceTimer.Stop();
            _colorSearchDebounceTimer.Stop();
            _historyMonitorTimer.Stop();
        };
    }

    private void BackgroundRuntimeJobService_Changed(string key)
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
            _ = RefreshSemanticRuntimeStatusAsync();
            if (_ocrTabLoaded)
                _ = CheckModelStatusAsync();
            UpdateLocalEngineUi();
            UpdateUpscaleLocalEngineUi();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.background-runtime-changed", ex);
        }
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
        Dispatcher.BeginInvoke(() =>
        {
            InvalidateHistoryCategoryCaches();
            _pendingHistoryDataRefresh = true;
            QueueHistoryRefresh(reloadFromDisk: false);
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
                SetImageSearchLoading(false, forceSemantic: true);
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
                SetImageSearchLoading(false, forceSemantic: true);
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

    private void FlushQueuedSemanticSearchRefresh()
    {
        _semanticSearchTimer.Stop();

        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        if (!ApplySemanticSearchIfNeeded())
            SetImageSearchLoading(false, forceSemantic: true);
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

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => Close();

    private void MinimizeBtn_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBtn_Enter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border b) return;
        b.Background = Theme.Brush(ReferenceEquals(b, CloseTitleBtn) ? Theme.DangerHover : Theme.AccentHover);
    }

    private void TitleBtn_Leave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
    }

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
        if (!IsLoaded) return;
        _settingsService.Settings.AfterCapture = (AfterCaptureAction)AfterCaptureCombo.SelectedIndex;
        _settingsService.Save();
    }

    private void DefaultCaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.DefaultCaptureMode = DefaultCaptureModeCombo.SelectedIndex == 1
            ? CaptureMode.Freeform
            : CaptureMode.Rectangle;
        _settingsService.Save();
        HotkeyChanged?.Invoke();
    }

    private void SaveToFileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = SaveToFileCheck.IsChecked == true;
        _settingsService.Settings.SaveToFile = on;
        SaveDirPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _settingsService.Save();
    }

    private void AskFileNameCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AskForFileNameOnSave = AskFileNameCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void LoadFileNameTemplateCombo(string currentTemplate)
    {
        FileNameTemplateCombo.Items.Clear();
        foreach (var preset in Helpers.FileNameTemplate.Presets)
        {
            var example = Helpers.FileNameTemplate.FormatExample(preset) + ".png";
            var item = new System.Windows.Controls.ComboBoxItem { Content = example, Tag = preset };
            FileNameTemplateCombo.Items.Add(item);
        }

        var match = FileNameTemplateCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == currentTemplate);
        if (match != null)
            FileNameTemplateCombo.SelectedItem = match;
        else if (FileNameTemplateCombo.Items.Count > 0)
            FileNameTemplateCombo.SelectedIndex = 0;

    }

    private void FileNameTemplateCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (FileNameTemplateCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            _settingsService.Settings.FileNameTemplate = tag;
            _settingsService.Save();
        }
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
            _settingsService.Settings.SaveDirectory = dlg.SelectedPath;
            SaveDirBox.Text = dlg.SelectedPath;
            _settingsService.Save();
        }
    }

    private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = StartWithWindowsCheck.IsChecked == true;
        _settingsService.Settings.StartWithWindows = on;
        _settingsService.Save();
        const string rk = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(rk, true);
        if (key is null) return;
        if (on) { var exe = Environment.ProcessPath; if (exe != null) key.SetValue("Yoink", $"\"{exe}\""); }
        else key.DeleteValue("Yoink", false);
    }

    private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoCheckForUpdates = AutoUpdateCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void CaptureFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.CaptureImageFormat = (CaptureImageFormat)CaptureFormatCombo.SelectedIndex;
        _settingsService.Save();
        _historyService.CaptureImageFormat = _settingsService.Settings.CaptureImageFormat;
        UpdateCaptureFormatControls();
    }

    private void JpegQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var selected = JpegQualityCombo.SelectedItem as ComboBoxItem;
        var tag = selected?.Tag?.ToString();
        _settingsService.Settings.JpegQuality = int.TryParse(tag, out var value) ? value : 85;
        _settingsService.Save();
        _historyService.JpegQuality = _settingsService.Settings.JpegQuality;
    }

    private void CaptureSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var selected = CaptureSizeCombo.SelectedItem as ComboBoxItem;
        var tag = selected?.Tag?.ToString();
        _settingsService.Settings.CaptureMaxLongEdge = int.TryParse(tag, out var value) ? value : 0;
        _settingsService.Save();
    }
}
