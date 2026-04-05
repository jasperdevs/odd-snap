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
    private const int MaxThumbCacheEntries = 32;
    private static readonly Dictionary<string, BitmapSource> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ThumbCacheOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> ThumbCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapImage> LogoCache = new();
    private static readonly SemaphoreSlim ThumbDecodeGate = new(4);
    private static readonly HashSet<string> ThumbInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _historyMonitorTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.5)
    };
    private readonly System.Windows.Threading.DispatcherTimer _imageIndexRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1.25)
    };
    private readonly System.Windows.Threading.DispatcherTimer _imageSearchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(700)
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
        _historyService.Changed += HistoryService_Changed;
        _imageSearchIndexService.Changed += ImageSearchIndexService_Changed;
        _imageSearchIndexService.StatusChanged += ImageSearchIndexService_StatusChanged;
        _historyMonitorTimer.Tick += (_, _) => PollHistoryChanges();
        _imageIndexRefreshTimer.Tick += (_, _) => FlushQueuedImageIndexRefresh();
        _imageSearchDebounceTimer.Tick += (_, _) => FlushQueuedImageSearchRefresh();
        _semanticSearchTimer.Tick += (_, _) => FlushQueuedSemanticSearchRefresh();
        Activated += (_, _) =>
        {
            ApplyThemeColors();
            UpdateLocalEngineUi();
        };
        Closed += (_, _) =>
        {
            _historyService.Changed -= HistoryService_Changed;
            _imageSearchIndexService.Changed -= ImageSearchIndexService_Changed;
            _imageSearchIndexService.StatusChanged -= ImageSearchIndexService_StatusChanged;
            CancelImageSearchWork();
            _imageIndexRefreshTimer.Stop();
            _imageSearchDebounceTimer.Stop();
            _semanticSearchTimer.Stop();
            _historyMonitorTimer.Stop();
            ClearThumbCache();
        };
    }

    private void HistoryService_Changed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded || HistoryTab.IsChecked != true)
                return;

            LoadCurrentHistoryTab();
            PrimeHistoryFingerprint();
        });
    }

    private void ImageSearchIndexService_Changed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                RefreshImageSearchTexts();
                QueueImageIndexRefresh();
                UpdateImageSearchStatus();
                UpdateImageSearchActionButtons();
                UpdateImageSearchPlaceholderText();
            }
            catch
            {
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
            catch
            {
                SetImageSearchLoading(false, forceSemantic: true);
            }
        });
    }

    private void QueueImageIndexRefresh()
    {
        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
            return;

        if (!_settingsService.Settings.ShowImageSearchBar)
            return;

        if (string.IsNullOrWhiteSpace(_imageSearchQuery))
            return;

        if (_settingsService.Settings.ImageSearchSources == ImageSearchSourceOptions.None)
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

        if (!_settingsService.Settings.ShowImageSearchBar)
            return;

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
        if (!IsLoaded || HistoryTab.IsChecked != true)
        {
            _historyMonitorTimer.Stop();
            return;
        }

        var fingerprint = _historyService.GetDiskFingerprint(_settingsService.Settings.SaveDirectory);
        if (fingerprint == _lastHistoryFingerprint)
            return;

        RefreshHistoryFromDisk();
    }

    private void RefreshHistoryFromDisk()
    {
        _historyService.Load();
        _historyService.RecoverFromDirectories(_settingsService.Settings.SaveDirectory);
        _historyService.PruneByRetention(_settingsService.Settings.HistoryRetention);
        LoadCurrentHistoryTab();
        PrimeHistoryFingerprint();
    }

    private void PrimeHistoryFingerprint()
    {
        _lastHistoryFingerprint = _historyService.GetDiskFingerprint(_settingsService.Settings.SaveDirectory);
    }

    private void UpdateHistoryMonitorState()
    {
        if (HistoryTab.IsChecked == true)
        {
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

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => Close();

    private void MinimizeBtn_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBtn_Enter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border b) b.Background = Theme.Brush(Theme.AccentHover);
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
        catch { }
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

        UpdateFileNamePreview();
    }

    private void FileNameTemplateCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (FileNameTemplateCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            _settingsService.Settings.FileNameTemplate = tag;
            _settingsService.Save();
            UpdateFileNamePreview();
        }
    }

    private void UpdateFileNamePreview()
    {
        var template = _settingsService.Settings.FileNameTemplate;
        FileNamePreviewText.Text = Helpers.FileNameTemplate.Format(template) + ".png";
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
