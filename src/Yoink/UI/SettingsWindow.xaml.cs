using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using TextBox = System.Windows.Controls.TextBox;

namespace Yoink.UI;

public partial class SettingsWindow : Window
{
    private const int MaxThumbCacheEntries = 32;
    private static readonly Dictionary<string, BitmapImage> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ThumbCacheOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> ThumbCacheNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapImage> LogoCache = new();
    private static readonly SemaphoreSlim ThumbDecodeGate = new(4);
    private static readonly HashSet<string> ThumbInflight = new(StringComparer.OrdinalIgnoreCase);

    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private UpdateCheckResult? _latestUpdate;
    private bool _updateCheckInFlight;
    public event Action? HotkeyChanged;
    public event Action? UninstallRequested;

    public SettingsWindow(SettingsService settingsService, HistoryService historyService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
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
        Activated += (_, _) =>
        {
            ApplyThemeColors();
            if (HistoryTab.IsChecked == true) LoadCurrentHistoryTab();
            UpdateLocalEngineUi();
        };
        Closed += (_, _) => ClearThumbCache();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => Close();

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
            // Disable system backdrop entirely so transparent background works
            Native.Dwm.DisableBackdrop(hwnd);
        }
        catch { }
        ApplyThemeColors();
    }

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
        LoadOcrLanguageOptions(s.OcrLanguageTag);

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
        MuteSoundsCheck.IsChecked = s.MuteSounds;
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        DetectWindowsCheck.IsChecked = s.DetectWindows;
        DetectControlsCheck.IsChecked = s.DetectControls;
        DetectControlsCheck.IsEnabled = s.DetectWindows;
        ShowToolNumberBadgesCheck.IsChecked = s.ShowToolNumberBadges;
        AskFileNameCheck.IsChecked = s.AskForFileNameOnSave;
        ToastPositionCombo.SelectedIndex = (int)s.ToastPosition;
        WindowDetectionCombo.SelectedIndex = (int)s.WindowDetection;
        CaptureDelayCombo.SelectedIndex = s.CaptureDelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        AutoPinPreviewsCheck.IsChecked = s.AutoPinPreviews;
        SoundPackCombo.SelectedIndex = (int)s.SoundPack;
        RecordingFormatCombo.SelectedIndex = (int)s.RecordingFormat;
        RecordingQualityCombo.SelectedIndex = (int)s.RecordingQuality;
        RecordingFpsCombo.SelectedIndex = s.RecordingFps switch { 15 => 0, 24 => 1, 30 => 2, 60 => 3, _ => 2 };
        RecordMicCheck.IsChecked = s.RecordMicrophone;
        RecordDesktopAudioCheck.IsChecked = s.RecordDesktopAudio;
        PopulateAudioDevices();

        // Toast duration combo
        double dur = s.ToastDurationSeconds;
        int durIdx = dur switch { 1.5 => 0, 2.0 => 1, 2.5 => 2, 3.0 => 3, 4.0 => 4, 5.0 => 5, _ => 2 };
        ToastDurationCombo.SelectedIndex = durIdx;

        // Upload settings
        UploadDestCombo.SelectedIndex = (int)s.ImageUploadDestination;
        AutoUploadScreenshotsCheck.IsChecked = s.AutoUploadScreenshots;
        AutoUploadGifsCheck.IsChecked = s.AutoUploadGifs;
        AutoUploadVideosCheck.IsChecked = s.AutoUploadVideos;
        LoadUploadSettingsIntoUi(s.ImageUploadSettings);
        LoadStickerSettingsIntoUi(s.StickerUploadSettings);
        UpdateUploadSettingsVisibility();
        UpdateUploadTabVisibility();
        VersionText.Text = $"Yoink {UpdateService.GetCurrentVersionLabel()}";

        PopulateToolToggles();
        UpdateCaptureFormatControls();
        UpdateRecordingFormatVisibility();
    }

    // ─── Unified tool list — delegates to shared ToolListBuilder ───

    // ExtraTools exposed for SettingsWindow.Hotkeys.cs conflict detection
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

    // ─── Tabs ──────────────────────────────────────────────────────

    private void TabChanged(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = HotkeysTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = CaptureTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = RecordingTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        // ToolbarPanel merged into HotkeysPanel
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UploadsPanel.Visibility = UploadsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = AboutTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryTab.IsChecked == true) LoadCurrentHistoryTab();
        if (UploadsTab.IsChecked == true) UpdateUploadTabVisibility();
    }

    private void HistoryCategoryCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        LoadCurrentHistoryTab();
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
        VideosPanel.Visibility = Visibility.Collapsed;

        switch (HistoryCategoryCombo.SelectedIndex)
        {
            case 0: ImagesPanel.Visibility = Visibility.Visible; LoadHistory(); break;
            case 1: TextPanel.Visibility = Visibility.Visible; LoadOcrHistory(); break;
            case 2: GifsPanel.Visibility = Visibility.Visible; LoadGifHistory(); break;
            case 3: ColorsPanel.Visibility = Visibility.Visible; LoadColorHistory(); break;
            case 4: StickersPanel.Visibility = Visibility.Visible; LoadStickerHistory(); break;
            case 5: VideosPanel.Visibility = Visibility.Visible; LoadVideoHistory(); break;
        }
    }

    private void LoadVideoHistory()
    {
        VideoStack.Children.Clear();
        var baseDir = _settingsService.Settings.SaveDirectory;
        var videoDir = Path.Combine(baseDir, "Videos");
        var dirs = new[] { videoDir, baseDir }.Where(Directory.Exists).ToArray();
        if (dirs.Length == 0) { ShowVideoEmpty(); return; }
        var files = dirs.SelectMany(EnumerateVideoFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetCreationTime)
            .Take(50)
            .ToArray();
        if (files.Length == 0) { ShowVideoEmpty(); return; }

        var wrap = new System.Windows.Controls.WrapPanel();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            string sizeStr = info.Length > 1024 * 1024
                ? $"{info.Length / 1024.0 / 1024.0:F1} MB"
                : $"{info.Length / 1024:N0} KB";
            string label = info.Extension.TrimStart('.').ToUpper();
            string timeAgo = FormatTimeAgo(info.CreationTime);

            var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var thumbPath = GetVideoThumbnailPath(file);
            img.Loaded += (_, _) =>
            {
                LoadThumbAsync(img, thumbPath, file);
                img.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            };

            // File location button overlay
            var locBtn = CreateFileLocationButton(file);

            // Duration/format badge
            var badge = new Border
            {
                Background = Theme.Brush(Theme.SectionIconBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = $"{label} · {sizeStr}",
                    FontSize = 9, Foreground = Theme.Brush(Theme.TextPrimary),
                    FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imgContainer = new Grid();
            imgContainer.Children.Add(img);
            imgContainer.Children.Add(badge);
            imgContainer.Children.Add(locBtn);
            Grid.SetRow(imgContainer, 0);
            grid.Children.Add(imgContainer);

            var infoPanel = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
            infoPanel.Children.Add(new TextBlock
            {
                Text = info.Name, FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = timeAgo, FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                Opacity = 0.3
            });
            Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);

            var card = new Border
            {
                Width = 168, Margin = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Background = Theme.Brush(Theme.BgCard),
                BorderBrush = Theme.Brush(Theme.BorderSubtle),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = grid,
            };
            bool isDraggingFile = false;
            card.SizeChanged += (s, _) =>
            {
                var b = (Border)s!;
                b.Clip = new System.Windows.Media.RectangleGeometry(
                    new System.Windows.Rect(0, 0, b.ActualWidth, b.ActualHeight), 10, 10);
            };
            var filePath = file;
            if (File.Exists(filePath))
                AttachFileDragHandlers(card, card, filePath, () => !_selectMode, v => isDraggingFile = v);

            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_selectMode || isDraggingFile)
                    return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = filePath, UseShellExecute = true }); }
                catch { }
            };
            wrap.Children.Add(card);
        }
        VideoStack.Children.Add(wrap);
    }

    private static IEnumerable<string> EnumerateVideoFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private void ShowVideoEmpty()
    {
        VideoStack.Children.Add(new TextBlock
        {
            Text = "No video recordings yet",
            FontSize = 13, Opacity = 0.2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0),
        });
    }

    private static string GetVideoThumbnailPath(string videoPath)
    {
        var thumbDir = Path.Combine(Path.GetDirectoryName(videoPath)!, ".thumbs");
        Directory.CreateDirectory(thumbDir);
        return Path.Combine(thumbDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
    }

    private static async Task<string> EnsureVideoThumbnailAsync(string videoPath, string thumbPath)
    {
        if (File.Exists(thumbPath))
            return thumbPath;

        var ffmpeg = Capture.VideoRecorder.FindFfmpeg();
        if (ffmpeg == null)
            return videoPath;

        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -i \"{videoPath}\" -vframes 1 -q:v 4 \"{thumbPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (proc == null)
                return videoPath;

            await proc.WaitForExitAsync();
            return File.Exists(thumbPath) ? thumbPath : videoPath;
        }
        catch
        {
            return videoPath;
        }
    }

    private Border CreateFileLocationButton(string filePath)
    {
        var btn = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = 0, IsHitTestVisible = true,
            ToolTip = "Show in folder",
            Child = new TextBlock
            {
                Text = "\uE838", // folder icon
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Theme.Brush(Theme.TextPrimary),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            }
        };
        btn.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            if (File.Exists(filePath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        };
        btn.MouseEnter += (s, _) => ((Border)s!).BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
        btn.MouseLeave += (s, _) => ((Border)s!).BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        return btn;
    }

    private static string FormatTimeAgo(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dt.ToString("MMM d");
    }

    // Old individual hotkey handlers removed — unified tool list handles everything

    // ─── Settings controls ─────────────────────────────────────────

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
            ? Yoink.Models.CaptureMode.Freeform
            : Yoink.Models.CaptureMode.Rectangle;
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

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUpdateStatusAsync(true);
    }

    private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null)
            return;

        OpenExternalUrl(string.IsNullOrWhiteSpace(_latestUpdate.DownloadUrl)
            ? _latestUpdate.ReleaseUrl
            : _latestUpdate.DownloadUrl);
    }

    private async Task RefreshUpdateStatusAsync(bool isManualCheck)
    {
        if (_updateCheckInFlight)
            return;

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Checking GitHub releases...";
        UpdateDetailText.Text = "Looking for the newest production build.";

        try
        {
            _latestUpdate = await UpdateService.CheckForUpdatesAsync();
            UpdateStatusText.Text = _latestUpdate.StatusMessage;

            if (_latestUpdate.IsUpdateAvailable)
            {
                var published = _latestUpdate.PublishedAt.HasValue
                    ? $"Published {FormatTimeAgo(_latestUpdate.PublishedAt.Value.LocalDateTime)}"
                    : "Published recently";
                var asset = string.IsNullOrWhiteSpace(_latestUpdate.AssetName)
                    ? "Open the release page to download it."
                    : $"Suggested download: {_latestUpdate.AssetName}";
                UpdateDetailText.Text = $"{published}. {asset}";
                DownloadUpdateButton.Content = string.IsNullOrWhiteSpace(_latestUpdate.AssetName)
                    ? "Open release"
                    : "Download update";
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateDetailText.Text = $"Current build: {UpdateService.GetCurrentVersionLabel()}";
                if (isManualCheck)
                    ToastWindow.Show("Yoink is up to date", UpdateService.GetCurrentVersionLabel());
            }
        }
        catch (Exception ex)
        {
            _latestUpdate = null;
            UpdateStatusText.Text = "Update check failed";
            UpdateDetailText.Text = ex.Message;
            if (isManualCheck)
                ToastWindow.ShowError("Update check failed", ex.Message);
        }
        finally
        {
            _updateCheckInFlight = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check now";
        }
    }

    private static void OpenExternalUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

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

    private void SoundPackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.SoundPack = (SoundPack)SoundPackCombo.SelectedIndex;
        _settingsService.Save();
        SoundService.SetPack(_settingsService.Settings.SoundPack);
        // Play a sample so user hears the change
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
        VideoOnlySettings.Visibility = isGif ? Visibility.Collapsed : Visibility.Visible;
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
        if (RecordingFpsCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
            if (int.TryParse(tag, out int fps))
            {
                _settingsService.Settings.RecordingFps = fps;
                _settingsService.Save();
            }
    }

    private void PopulateAudioDevices()
    {
        MicDeviceCombo.Items.Clear();
        var mics = AudioService.GetMicrophones();
        foreach (var mic in mics)
        {
            var item = new System.Windows.Controls.ComboBoxItem { Content = mic.Name, Tag = mic.Id };
            MicDeviceCombo.Items.Add(item);
            if (mic.Id == _settingsService.Settings.MicrophoneDeviceId)
                MicDeviceCombo.SelectedItem = item;
        }
        if (MicDeviceCombo.SelectedIndex < 0 && MicDeviceCombo.Items.Count > 0)
            MicDeviceCombo.SelectedIndex = 0;

        DesktopAudioDeviceCombo.Items.Clear();
        var outputs = AudioService.GetDesktopAudioDevices();
        foreach (var dev in outputs)
        {
            var item = new System.Windows.Controls.ComboBoxItem { Content = dev.Name, Tag = dev.Id };
            DesktopAudioDeviceCombo.Items.Add(item);
            if (dev.Id == _settingsService.Settings.DesktopAudioDeviceId)
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
        if (MicDeviceCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            _settingsService.Settings.MicrophoneDeviceId = item.Tag as string;
            _settingsService.Save();
        }
    }

    private void DesktopAudioDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (DesktopAudioDeviceCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
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

            var json = JsonSerializer.Serialize(_settingsService.Settings, new JsonSerializerOptions { WriteIndented = true });
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
            var imported = JsonSerializer.Deserialize<AppSettings>(json);
            if (imported is null)
            {
                ToastWindow.ShowError("Import failed", "Invalid settings file.");
                return;
            }

            _settingsService.Settings = imported;
            _settingsService.Save();
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

        _settingsService.Settings = new Models.AppSettings();
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

    private void DetectWindowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool enabled = DetectWindowsCheck.IsChecked == true;
        _settingsService.Settings.DetectWindows = enabled;
        DetectControlsCheck.IsEnabled = enabled;
        if (!enabled)
        {
            DetectControlsCheck.IsChecked = false;
            _settingsService.Settings.DetectControls = false;
        }
        _settingsService.Save();
    }

    private void DetectControlsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.DetectControls = DetectControlsCheck.IsChecked == true;
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
        if (ToastDurationCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
        {
            if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs))
            {
                _settingsService.Settings.ToastDurationSeconds = secs;
                _settingsService.Save();
                ToastWindow.SetDuration(secs);
            }
        }
    }

    private void AutoPinPreviewsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoPinPreviews = AutoPinPreviewsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void WindowDetectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (WindowDetectionCombo.SelectedIndex < 0) WindowDetectionCombo.SelectedIndex = 1;
        _settingsService.Settings.WindowDetection = (WindowDetectionMode)WindowDetectionCombo.SelectedIndex;
        _settingsService.Save();
    }

    private void CaptureDelayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.CaptureDelaySeconds = CaptureDelayCombo.SelectedIndex switch { 1 => 3, 2 => 5, 3 => 10, _ => 0 };
        _settingsService.Save();
    }

    // ─── Upload settings ────────────────────────────────────────────

    private void UploadDestCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ImageUploadDestination = (Services.UploadDestination)UploadDestCombo.SelectedIndex;
        _settingsService.Save();
        UpdateUploadSettingsVisibility();
    }

    private void UpdateUploadSettingsVisibility()
    {
        var dest = (Services.UploadDestination)UploadDestCombo.SelectedIndex;
        ImgurSettings.Visibility = dest == Services.UploadDestination.Imgur ? Visibility.Visible : Visibility.Collapsed;
        ImgBBSettings.Visibility = dest == Services.UploadDestination.ImgBB ? Visibility.Visible : Visibility.Collapsed;
        CatboxSettings.Visibility = dest == Services.UploadDestination.Catbox ? Visibility.Visible : Visibility.Collapsed;
        LitterboxSettings.Visibility = dest == Services.UploadDestination.Litterbox ? Visibility.Visible : Visibility.Collapsed;
        GyazoSettings.Visibility = dest == Services.UploadDestination.Gyazo ? Visibility.Visible : Visibility.Collapsed;
        FileIoSettings.Visibility = dest == Services.UploadDestination.FileIo ? Visibility.Visible : Visibility.Collapsed;
        UguuSettings.Visibility = dest == Services.UploadDestination.Uguu ? Visibility.Visible : Visibility.Collapsed;
        TransferSettings.Visibility = dest == Services.UploadDestination.TransferSh ? Visibility.Visible : Visibility.Collapsed;
        DropboxSettings.Visibility = dest == Services.UploadDestination.Dropbox ? Visibility.Visible : Visibility.Collapsed;
        GoogleDriveSettings.Visibility = dest == Services.UploadDestination.GoogleDrive ? Visibility.Visible : Visibility.Collapsed;
        OneDriveSettings.Visibility = dest == Services.UploadDestination.OneDrive ? Visibility.Visible : Visibility.Collapsed;
        AzureSettings.Visibility = dest == Services.UploadDestination.AzureBlob ? Visibility.Visible : Visibility.Collapsed;
        GitHubSettings.Visibility = dest == Services.UploadDestination.GitHub ? Visibility.Visible : Visibility.Collapsed;
        ImmichSettings.Visibility = dest == Services.UploadDestination.Immich ? Visibility.Visible : Visibility.Collapsed;
        FtpSettings.Visibility = dest == Services.UploadDestination.Ftp ? Visibility.Visible : Visibility.Collapsed;
        SftpSettings.Visibility = dest == Services.UploadDestination.Sftp ? Visibility.Visible : Visibility.Collapsed;
        WebDavSettings.Visibility = dest == Services.UploadDestination.WebDav ? Visibility.Visible : Visibility.Collapsed;
        S3Settings.Visibility = dest == Services.UploadDestination.S3Compatible ? Visibility.Visible : Visibility.Collapsed;
        CustomUploadSettings.Visibility = dest == Services.UploadDestination.CustomHttp ? Visibility.Visible : Visibility.Collapsed;
        TestUploadCard.Visibility = dest != Services.UploadDestination.None ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoUploadScreenshotsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadScreenshots = AutoUploadScreenshotsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void AutoUploadGifsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadGifs = AutoUploadGifsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void AutoUploadVideosCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadVideos = AutoUploadVideosCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void StickerProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.Provider = (StickerProvider)StickerProviderCombo.SelectedIndex;
        UpdateStickerProviderVisibility();
        UpdateLocalEngineUi();
        _settingsService.Save();
    }

    private void StickerLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateStickerExecutionUi();
    }

    private LocalStickerEngine GetSelectedLocalStickerEngine()
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        return executionProvider == StickerExecutionProvider.Gpu
            ? GetSelectedStickerEngine(StickerLocalGpuEngineCombo)
            : GetSelectedStickerEngine(StickerLocalCpuEngineCombo);
    }

    private async void StickerInstallDriversBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
            StickerInstallDriversBtn.IsEnabled = false;
            await RembgRuntimeService.EnsureInstalledAsync(executionProvider);
            ToastWindow.Show("rembg", $"{RembgRuntimeService.GetSetupTargetName(executionProvider)} complete.");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("rembg setup failed", ex.Message);
        }
        finally
        {
            StickerInstallDriversBtn.IsEnabled = true;
        }
    }

    private void StickerShadowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.AddShadow = StickerShadowCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void StickerStrokeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.AddStroke = StickerStrokeCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void StickerRemoveBgKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.RemoveBgApiKey = StickerRemoveBgKeyBox.Text;
        _settingsService.Save();
    }

    private void StickerPhotoroomKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.PhotoroomApiKey = StickerPhotoroomKeyBox.Text;
        _settingsService.Save();
    }

    private async void StickerDownloadRembgBtn_Click(object sender, RoutedEventArgs e)
    {
        var engine = GetSelectedLocalStickerEngine();

        if (LocalStickerEngineService.IsModelDownloaded(engine))
        {
            bool removed = LocalStickerEngineService.RemoveDownloadedModel(engine);
            SetStickerDownloadUi(false, null, removed ? "Model removed." : "Couldn't remove the model.");
            if (removed)
                ToastWindow.Show("Sticker engine", "Removed the local sticker model.");
            else
                ToastWindow.ShowError("Sticker engine error", "Couldn't remove the local sticker model.");
            UpdateLocalEngineUi();
            return;
        }

        SetStickerDownloadUi(true, 0, "Preparing download...");
        try
        {
            var progress = new Progress<LocalStickerEngineDownloadProgress>(p =>
            {
                SetStickerDownloadUi(true, p.TotalBytes is > 0 ? p.Percent : null, p.StatusMessage);
            });

            var result = await LocalStickerEngineService.DownloadModelAsync(engine, progress);
            if (!result.Success || string.IsNullOrWhiteSpace(result.ModelPath))
            {
                SetStickerDownloadUi(false, null, result.Message);
                ToastWindow.ShowError("Sticker engine error", result.Message);
                return;
            }

            SetStickerDownloadUi(false, 100, "Download complete. The model is ready to use.");
            ToastWindow.Show("Sticker engine", $"Downloaded {LocalStickerEngineService.GetEngineLabel(engine)}. Sticker captures will now use it automatically.");
            UpdateLocalEngineUi();
        }
        catch (Exception ex)
        {
            SetStickerDownloadUi(false, null, ex.Message);
            ToastWindow.ShowError("Sticker engine error", ex.Message);
        }
    }

    private void StickerOpenLocalEngineRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var engine = GetSelectedLocalStickerEngine();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LocalStickerEngineService.GetProjectUrl(engine),
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void StickerRemoveAllModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Remove all downloaded local sticker models?\n\nThey will be downloaded again the next time you use them.",
                "Remove Models",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        bool removed = RembgRuntimeService.RemoveAllCachedModels();
        if (removed)
        {
            ToastWindow.Show("Sticker engine", "Removed all downloaded local sticker models.");
            UpdateLocalEngineUi();
        }
        else
        {
            ToastWindow.ShowError("Sticker engine error", "Couldn't remove the downloaded models.");
        }
    }

    private void ImgurClientIdBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgurClientId = ImgurClientIdBox.Text;
        _settingsService.Save();
    }

    private void ImgurTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgurAccessToken = ImgurTokenBox.Text;
        _settingsService.Save();
    }

    private void ImgBBKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgBBApiKey = ImgBBKeyBox.Text;
        _settingsService.Save();
    }

    private void GyazoTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GyazoAccessToken = GyazoTokenBox.Text;
        _settingsService.Save();
    }

    private void DropboxTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.DropboxAccessToken = DropboxTokenBox.Text;
        _settingsService.Save();
    }

    private void DropboxPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.DropboxPathPrefix = DropboxPathBox.Text;
        _settingsService.Save();
    }

    private void GoogleDriveTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GoogleDriveAccessToken = GoogleDriveTokenBox.Text;
        _settingsService.Save();
    }

    private void GoogleDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GoogleDriveFolderId = GoogleDriveFolderBox.Text;
        _settingsService.Save();
    }

    private void OneDriveTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.OneDriveAccessToken = OneDriveTokenBox.Text;
        _settingsService.Save();
    }

    private void OneDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.OneDriveFolder = OneDriveFolderBox.Text;
        _settingsService.Save();
    }

    private void AzureBlobSasBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AzureBlobSasUrl = AzureBlobSasBox.Text;
        _settingsService.Save();
    }

    private void GitHubTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubToken = GitHubTokenBox.Text;
        _settingsService.Save();
    }

    private void GitHubRepoBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubRepo = GitHubRepoBox.Text;
        _settingsService.Save();
    }

    private void GitHubBranchBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubBranch = GitHubBranchBox.Text;
        _settingsService.Save();
    }

    private void ImmichUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImmichBaseUrl = ImmichUrlBox.Text;
        _settingsService.Save();
    }

    private void ImmichApiKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImmichApiKey = ImmichApiKeyBox.Text;
        _settingsService.Save();
    }

    private void FtpUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpUrl = FtpUrlBox.Text; _settingsService.Save(); }
    private void FtpUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpUsername = FtpUsernameBox.Text; _settingsService.Save(); }
    private void FtpPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpPassword = FtpPasswordBox.Text; _settingsService.Save(); }
    private void FtpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpPublicUrl = FtpPublicUrlBox.Text; _settingsService.Save(); }
    private void SftpHostBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpHost = SftpHostBox.Text; _settingsService.Save(); }
    private void SftpPortBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; if (int.TryParse(SftpPortBox.Text, out var port)) ActiveUploadSettings.SftpPort = port; _settingsService.Save(); }
    private void SftpUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpUsername = SftpUsernameBox.Text; _settingsService.Save(); }
    private void SftpPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpPassword = SftpPasswordBox.Text; _settingsService.Save(); }
    private void SftpRemotePathBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpRemotePath = SftpRemotePathBox.Text; _settingsService.Save(); }
    private void SftpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpPublicUrl = SftpPublicUrlBox.Text; _settingsService.Save(); }
    private void WebDavUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavUrl = WebDavUrlBox.Text; _settingsService.Save(); }
    private void WebDavUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavUsername = WebDavUsernameBox.Text; _settingsService.Save(); }
    private void WebDavPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavPassword = WebDavPasswordBox.Text; _settingsService.Save(); }
    private void WebDavPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavPublicUrl = WebDavPublicUrlBox.Text; _settingsService.Save(); }

    private void S3EndpointBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Endpoint = S3EndpointBox.Text;
        _settingsService.Save();
    }

    private void S3BucketBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Bucket = S3BucketBox.Text;
        _settingsService.Save();
    }

    private void S3RegionBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Region = S3RegionBox.Text;
        _settingsService.Save();
    }

    private void S3AccessKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3AccessKey = S3AccessKeyBox.Text;
        _settingsService.Save();
    }

    private void S3SecretKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3SecretKey = S3SecretKeyBox.Text;
        _settingsService.Save();
    }

    private void S3PublicUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3PublicUrl = S3PublicUrlBox.Text;
        _settingsService.Save();
    }

    private void CustomUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomUploadUrl = CustomUrlBox.Text;
        _settingsService.Save();
    }

    private void CustomFieldBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomFileFormName = CustomFieldBox.Text;
        _settingsService.Save();
    }

    private void CustomJsonPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomResponseUrlPath = CustomJsonPathBox.Text;
        _settingsService.Save();
    }

    private void CustomHeadersBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomHeaders = CustomHeadersBox.Text;
        _settingsService.Save();
    }

    private async void TestUpload_Click(object sender, RoutedEventArgs e)
    {
        TestUploadBtn.Content = "Uploading...";
        TestUploadBtn.IsEnabled = false;

        // Create a tiny 1x1 test image
        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yoink_test.png");
        try
        {
            using (var bmp = new System.Drawing.Bitmap(1, 1))
                bmp.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

            var result = await Services.UploadService.UploadAsync(
                tempPath,
                _settingsService.Settings.ImageUploadDestination,
                ActiveUploadSettings);

            if (result.Success)
                ToastWindow.Show("Upload works", result.Url);
            else
                ToastWindow.ShowError("Upload failed", result.Error);
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Upload error", ex.Message);
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
            TestUploadBtn.Content = "Test Upload";
            TestUploadBtn.IsEnabled = true;
        }
    }

    private static void LoadThumbAsync(Image img, string path)
        => LoadThumbAsync(img, path, null);

    private static void LoadThumbAsync(Image img, string path, string? sourcePath)
    {
        if (img.Source != null) return;

        var cacheKey = sourcePath ?? path;

        if (TryGetThumbFromCache(cacheKey, out var cached))
        {
            img.Source = cached;
            return;
        }

        lock (ThumbInflight)
        {
            if (!ThumbInflight.Add(cacheKey))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ThumbDecodeGate.WaitAsync();
                try
                {
                    var loadPath = path;
                    if (!File.Exists(loadPath) && sourcePath != null)
                        loadPath = await EnsureVideoThumbnailAsync(sourcePath, path);

                    if (!File.Exists(loadPath))
                        return;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(loadPath);
                    bmp.DecodePixelWidth = 240;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    StoreThumbInCache(cacheKey, bmp);
                    _ = img.Dispatcher.BeginInvoke(() =>
                    {
                        if (img.Source == null)
                            img.Source = bmp;
                    });
                }
                finally
                {
                    ThumbDecodeGate.Release();
                }
            }
            catch { }
            finally
            {
                lock (ThumbInflight)
                    ThumbInflight.Remove(cacheKey);
            }
        });
    }

    // ─── Color History ──────────────────────────────────────────────

    private static bool TryGetThumbFromCache(string path, out BitmapImage? image)
    {
        lock (ThumbCache)
        {
            if (!ThumbCache.TryGetValue(path, out var cached))
            {
                image = null;
                return false;
            }

            TouchThumbCache(path);
            image = cached;
            return true;
        }
    }

    private static void StoreThumbInCache(string path, BitmapImage image)
    {
        lock (ThumbCache)
        {
            ThumbCache[path] = image;
            TouchThumbCache(path);

            while (ThumbCacheOrder.Count > MaxThumbCacheEntries)
            {
                var oldest = ThumbCacheOrder.Last;
                if (oldest is null)
                    break;

                ThumbCacheOrder.RemoveLast();
                ThumbCacheNodes.Remove(oldest.Value);
                ThumbCache.Remove(oldest.Value);
            }
        }
    }

    private static void TouchThumbCache(string path)
    {
        if (ThumbCacheNodes.TryGetValue(path, out var existing))
            ThumbCacheOrder.Remove(existing);

        ThumbCacheNodes[path] = ThumbCacheOrder.AddFirst(path);
    }

    internal static void ClearThumbCache()
    {
        lock (ThumbCache)
        {
            ThumbCache.Clear();
            ThumbCacheOrder.Clear();
            ThumbCacheNodes.Clear();
        }
        LogoCache.Clear();
    }

    private static BitmapImage? LoadPackImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        lock (LogoCache)
        {
            if (LogoCache.TryGetValue(relativePath, out var cached))
                return cached;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            lock (LogoCache) LogoCache[relativePath] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static FrameworkElement? CreateProviderBadge(string? providerOrPath, bool isPath = false)
    {
        string logoPath = isPath ? (providerOrPath ?? string.Empty) : UploadService.GetHistoryLogoPath(providerOrPath);
        var logoSource = LoadPackImage(logoPath);
        if (logoSource == null)
        {
            if (string.IsNullOrWhiteSpace(providerOrPath)) return null;

            string text = providerOrPath.Trim();
            if (!isPath)
            {
                text = text switch
                {
                    "Remove.bg" => "RBG",
                    "Photoroom" => "PR",
                    "Local" => "LCL",
                    _ => text.Length <= 4 ? text.ToUpperInvariant() : text[..4].ToUpperInvariant()
                };
            }

            return new Border
            {
                MinWidth = 24,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = Theme.Brush(Theme.SectionIconBg),
                BorderBrush = Theme.StrokeBrush(),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 6, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Brush(Theme.TextPrimary),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                }
            };
        }

        return new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = Theme.Brush(Theme.SectionIconBg),
            BorderBrush = Theme.StrokeBrush(),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 6, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Image
            {
                Source = logoSource,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    // ─── Helpers ───────────────────────────────────────────────────

    private static string FormatStorageSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

}
