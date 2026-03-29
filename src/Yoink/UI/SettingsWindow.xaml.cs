using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;

namespace Yoink.UI;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, BitmapImage> ThumbCache = new();
    private static readonly Dictionary<string, BitmapImage> LogoCache = new();

    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    public event Action? HotkeyChanged;

    public SettingsWindow(SettingsService settingsService, HistoryService historyService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        InitializeComponent();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(16),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false
        });
        WireHotkeyBoxes();
        LoadSettings();
        Loaded += (_, _) => ApplyMicaBackdrop();
        Activated += (_, _) =>
        {
            if (HistoryTab.IsChecked == true) LoadCurrentHistoryTab();
        };
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
        OuterBorder.Background = Theme.Brush(Theme.BgPrimary);
        OuterBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        TitleBarBorder.Background = Theme.Brush(Theme.TitleBar);
        TitleText.Foreground = Theme.Brush(Theme.TextPrimary);
        Foreground = Theme.Brush(Theme.TextPrimary);
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        HotkeyBox.Text = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);
        OcrHotkeyBox.Text = HotkeyFormatter.Format(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        PickerHotkeyBox.Text = HotkeyFormatter.Format(s.PickerHotkeyModifiers, s.PickerHotkeyKey);
        ScanHotkeyBox.Text = HotkeyFormatter.Format(s.ScanHotkeyModifiers, s.ScanHotkeyKey);
        GifHotkeyBox.Text = HotkeyFormatter.Format(s.GifHotkeyModifiers, s.GifHotkeyKey);

        DefaultCaptureModeCombo.SelectedIndex = s.DefaultCaptureMode == Yoink.Models.CaptureMode.Freeform ? 1 : 0;
        AfterCaptureCombo.SelectedIndex = (int)s.AfterCapture;
        SaveToFileCheck.IsChecked = s.SaveToFile;
        SaveDirBox.Text = s.SaveDirectory;
        SaveDirPanel.Visibility = s.SaveToFile ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
        SaveHistoryCheck.IsChecked = s.SaveHistory;
        HistoryRetentionCombo.SelectedIndex = (int)s.HistoryRetention;
        MuteSoundsCheck.IsChecked = s.MuteSounds;
        CompressHistoryCheck.IsChecked = s.CompressHistory;
        CrosshairGuidesCheck.IsChecked = s.ShowCrosshairGuides;
        ShowToolNumberBadgesCheck.IsChecked = s.ShowToolNumberBadges;
        ToastPositionCombo.SelectedIndex = (int)s.ToastPosition;

        // Upload settings
        UploadDestCombo.SelectedIndex = (int)s.ImageUploadDestination;
        AutoUploadScreenshotsCheck.IsChecked = s.AutoUploadScreenshots;
        LoadUploadSettingsIntoUi(s.ImageUploadSettings);
        UpdateUploadSettingsVisibility();

        PopulateToolToggles();
    }

    private UploadSettings ActiveUploadSettings => _settingsService.Settings.ImageUploadSettings;

    private void LoadUploadSettingsIntoUi(UploadSettings s)
    {
        ImgurClientIdBox.Text = s.ImgurClientId;
        ImgurTokenBox.Text = s.ImgurAccessToken;
        ImgBBKeyBox.Text = s.ImgBBApiKey;
        GyazoTokenBox.Text = s.GyazoAccessToken;
        DropboxTokenBox.Text = s.DropboxAccessToken;
        DropboxPathBox.Text = s.DropboxPathPrefix;
        GoogleDriveTokenBox.Text = s.GoogleDriveAccessToken;
        GoogleDriveFolderBox.Text = s.GoogleDriveFolderId;
        OneDriveTokenBox.Text = s.OneDriveAccessToken;
        OneDriveFolderBox.Text = s.OneDriveFolder;
        AzureBlobSasBox.Text = s.AzureBlobSasUrl;
        GitHubTokenBox.Text = s.GitHubToken;
        GitHubRepoBox.Text = s.GitHubRepo;
        GitHubBranchBox.Text = s.GitHubBranch;
        ImmichUrlBox.Text = s.ImmichBaseUrl;
        ImmichApiKeyBox.Text = s.ImmichApiKey;
        FtpUrlBox.Text = s.FtpUrl;
        FtpUsernameBox.Text = s.FtpUsername;
        FtpPasswordBox.Text = s.FtpPassword;
        FtpPublicUrlBox.Text = s.FtpPublicUrl;
        SftpHostBox.Text = s.SftpHost;
        SftpPortBox.Text = s.SftpPort.ToString();
        SftpUsernameBox.Text = s.SftpUsername;
        SftpPasswordBox.Text = s.SftpPassword;
        SftpRemotePathBox.Text = s.SftpRemotePath;
        SftpPublicUrlBox.Text = s.SftpPublicUrl;
        WebDavUrlBox.Text = s.WebDavUrl;
        WebDavUsernameBox.Text = s.WebDavUsername;
        WebDavPasswordBox.Text = s.WebDavPassword;
        WebDavPublicUrlBox.Text = s.WebDavPublicUrl;
        S3EndpointBox.Text = s.S3Endpoint;
        S3BucketBox.Text = s.S3Bucket;
        S3RegionBox.Text = s.S3Region;
        S3AccessKeyBox.Text = s.S3AccessKey;
        S3SecretKeyBox.Text = s.S3SecretKey;
        S3PublicUrlBox.Text = s.S3PublicUrl;
        CustomUrlBox.Text = s.CustomUploadUrl;
        CustomFieldBox.Text = s.CustomFileFormName;
        CustomJsonPathBox.Text = s.CustomResponseUrlPath;
        CustomHeadersBox.Text = s.CustomHeaders;
    }

    private void PopulateToolToggles()
    {
        ToolTogglePanel.Children.Clear();
        var enabled = _settingsService.Settings.EnabledTools ?? ToolDef.DefaultEnabledIds();
        foreach (var tool in ToolDef.AllTools)
        {
            var cb = new CheckBox
            {
                Content = tool.Label,
                FontSize = 12,
                IsChecked = enabled.Contains(tool.Id),
                Tag = tool.Id,
                Margin = new Thickness(0, 0, 14, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            cb.Checked += ToolToggle_Changed;
            cb.Unchecked += ToolToggle_Changed;
            ToolTogglePanel.Children.Add(cb);
        }
    }

    private void ToolToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabledIds = new List<string>();
        foreach (CheckBox cb in ToolTogglePanel.Children)
            if (cb.IsChecked == true)
                enabledIds.Add((string)cb.Tag);
        // Must have at least one capture tool
        if (!enabledIds.Any(id => ToolDef.AllTools.Any(t => t.Id == id && t.Group == 0)))
        {
            ((CheckBox)sender).IsChecked = true;
            return;
        }
        _settingsService.Settings.EnabledTools = enabledIds;
        _settingsService.Save();
    }

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
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UploadsPanel.Visibility = UploadsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryTab.IsChecked == true) LoadCurrentHistoryTab();
    }

    private void HistorySubTabChanged(object sender, RoutedEventArgs e)
    {
        LoadCurrentHistoryTab();
    }

    private void LoadCurrentHistoryTab()
    {
        ImagesPanel.Visibility = Visibility.Collapsed;
        GifsPanel.Visibility = Visibility.Collapsed;
        TextPanel.Visibility = Visibility.Collapsed;
        ColorsPanel.Visibility = Visibility.Collapsed;

        if (ImagesSubTab.IsChecked == true)
        {
            ImagesPanel.Visibility = Visibility.Visible;
            LoadHistory();
        }
        else if (GifsSubTab.IsChecked == true)
        {
            GifsPanel.Visibility = Visibility.Visible;
            LoadGifHistory();
        }
        else if (TextSubTab.IsChecked == true)
        {
            TextPanel.Visibility = Visibility.Visible;
            LoadOcrHistory();
        }
        else if (ColorsSubTab.IsChecked == true)
        {
            ColorsPanel.Visibility = Visibility.Visible;
            LoadColorHistory();
        }
    }

    // ─── Hotkey recording ─────────────────────────────────────────

    private readonly Dictionary<System.Windows.Controls.TextBox, bool> _recordingFlags = new();

    private void WireHotkeyBoxes()
    {
        RecordHotkey(HotkeyBox,
            s => s.HotkeyModifiers, s => s.HotkeyKey,
            (s, m, k) => { s.HotkeyModifiers = m; s.HotkeyKey = k; });
        RecordHotkey(OcrHotkeyBox,
            s => s.OcrHotkeyModifiers, s => s.OcrHotkeyKey,
            (s, m, k) => { s.OcrHotkeyModifiers = m; s.OcrHotkeyKey = k; });
        RecordHotkey(PickerHotkeyBox,
            s => s.PickerHotkeyModifiers, s => s.PickerHotkeyKey,
            (s, m, k) => { s.PickerHotkeyModifiers = m; s.PickerHotkeyKey = k; });
        RecordHotkey(ScanHotkeyBox,
            s => s.ScanHotkeyModifiers, s => s.ScanHotkeyKey,
            (s, m, k) => { s.ScanHotkeyModifiers = m; s.ScanHotkeyKey = k; });
        RecordHotkey(GifHotkeyBox,
            s => s.GifHotkeyModifiers, s => s.GifHotkeyKey,
            (s, m, k) => { s.GifHotkeyModifiers = m; s.GifHotkeyKey = k; });

    }

    private void RecordHotkey(
        System.Windows.Controls.TextBox box,
        Func<AppSettings, uint> getMod, Func<AppSettings, uint> getKey,
        Action<AppSettings, uint, uint> setModKey)
    {
        box.GotFocus += (_, _) =>
        {
            _recordingFlags[box] = true;
            box.Text = "Press keys...";
        };
        box.LostFocus += (_, _) =>
        {
            _recordingFlags[box] = false;
            box.Text = HotkeyFormatter.Format(getMod(_settingsService.Settings), getKey(_settingsService.Settings));
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (!_recordingFlags.GetValueOrDefault(box)) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierOnly(key)) return;

            uint mod = GetModifiers();
            if (mod == 0) return;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            setModKey(_settingsService.Settings, mod, vk);
            _settingsService.Save();
            box.Text = HotkeyFormatter.Format(mod, vk);
            _recordingFlags[box] = false;
            Keyboard.ClearFocus();
            HotkeyChanged?.Invoke();
        };
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }
    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }
    private void OcrHotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }
    private void OcrHotkeyBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void OcrHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }
    private void PickerHotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }
    private void PickerHotkeyBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void PickerHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }
    private void ScanHotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }
    private void ScanHotkeyBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void ScanHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }
    private void GifHotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }
    private void GifHotkeyBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void GifHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }

    private void ClearCaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.HotkeyModifiers = 0;
        _settingsService.Settings.HotkeyKey = 0;
        _settingsService.Save();
        HotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private void ClearOcrHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.OcrHotkeyModifiers = 0;
        _settingsService.Settings.OcrHotkeyKey = 0;
        _settingsService.Save();
        OcrHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private void ClearPickerHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.PickerHotkeyModifiers = 0;
        _settingsService.Settings.PickerHotkeyKey = 0;
        _settingsService.Save();
        PickerHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private void ClearScanHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.ScanHotkeyModifiers = 0;
        _settingsService.Settings.ScanHotkeyKey = 0;
        _settingsService.Save();
        ScanHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private void ClearGifHotkey_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.GifHotkeyModifiers = 0;
        _settingsService.Settings.GifHotkeyKey = 0;
        _settingsService.Save();
        GifHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private static bool IsModifierOnly(Key k) =>
        k is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape;

    private static uint GetModifiers()
    {
        uint m = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) m |= Native.User32.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) m |= Native.User32.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) m |= Native.User32.MOD_SHIFT;
        return m;
    }

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

    private void CompressHistoryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.CompressHistory = CompressHistoryCheck.IsChecked == true;
        _settingsService.Save();
        _historyService.CompressHistory = _settingsService.Settings.CompressHistory;
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

    private void CrosshairGuidesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ShowCrosshairGuides = CrosshairGuidesCheck.IsChecked == true;
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
                ToastWindow.Show("Upload failed", result.Error);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("Upload error", ex.Message);
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
            TestUploadBtn.Content = "Test Upload";
            TestUploadBtn.IsEnabled = true;
        }
    }

    // ─── Screenshot History (date-grouped) ─────────────────────────

    private bool _selectMode;
    private List<HistoryItemVM> _historyItems = new();
    private List<HistoryItemVM> _gifItems = new();
    private List<HistoryItemVM> _allHistoryItems = new();
    private List<HistoryItemVM> _allGifItems = new();
    private int _historyRenderCount;
    private int _gifRenderCount;
    private const int HistoryPageSize = 60;

    private void LoadHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        HistoryStack.Children.Clear();

        var entries = _historyService.ImageEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            try { totalBytes += new FileInfo(e.FilePath).Length; } catch { }
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} capture{(entries.Count == 1 ? "" : "s")} \u00B7 {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No captures yet";

        _allHistoryItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e, ThumbPath = e.FilePath,
            Dimensions = e.Width > 0 ? $"{e.Width} x {e.Height}" : "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _historyRenderCount = Math.Min(HistoryPageSize, _allHistoryItems.Count);
        RenderHistoryItems();
    }

    private void RenderHistoryItems()
    {
        HistoryStack.Children.Clear();
        _historyItems = _allHistoryItems.Take(_historyRenderCount).ToList();
        var groups = _historyItems.GroupBy(i => i.Entry.CapturedAt.Date).OrderByDescending(g => g.Key);
        foreach (var group in groups)
        {
            string label = group.Key == DateTime.Today ? "Today"
                : group.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d, yyyy");

            HistoryStack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Opacity = 0.45, Margin = new Thickness(6, 10, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var item in group)
            {
                var thumb = CreateHistoryCard(item);
                wrap.Children.Add(thumb);
            }
            HistoryStack.Children.Add(wrap);
        }
    }

    private void HistoryPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_historyRenderCount >= _allHistoryItems.Count) return;
        _historyRenderCount = Math.Min(_historyRenderCount + HistoryPageSize, _allHistoryItems.Count);
        RenderHistoryItems();
    }

    private Border CreateHistoryCard(HistoryItemVM vm)
    {
        var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        // Lazy load with fade-in
        img.Loaded += (_, _) =>
        {
            LoadThumbAsync(img, vm.ThumbPath);
            img.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };

        // Copy button overlay (visible on hover)
        var copyBtn = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = 0, IsHitTestVisible = true,
            ToolTip = "Copy to clipboard",
            Child = new TextBlock
            {
                Text = "\U0001F4CB", FontSize = 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };
        copyBtn.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                System.Windows.Clipboard.SetText(vm.Entry.UploadUrl);
                ToastWindow.Show("Copied", vm.Entry.UploadUrl);
                return;
            }
            if (!File.Exists(vm.Entry.FilePath)) return;
            using var bmp = new System.Drawing.Bitmap(vm.Entry.FilePath);
            Services.ClipboardService.CopyToClipboard(bmp);
            ToastWindow.Show("Copied", $"{vm.Dimensions} screenshot copied");
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(95) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imgContainer = new Grid();
        imgContainer.Children.Add(img);
        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) imgContainer.Children.Add(badge);
        }
        imgContainer.Children.Add(copyBtn);
        Grid.SetRow(imgContainer, 0);
        grid.Children.Add(imgContainer);

        var info = new StackPanel { Margin = new Thickness(8, 5, 8, 6) };
        info.Children.Add(new TextBlock
        {
            Text = vm.Entry.FileName,
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock { Text = vm.TimeAgo, FontSize = 9.5, Opacity = 0.3 });
        Grid.SetRow(info, 1);
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 152, Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = grid, Tag = vm,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };

        // Clip to rounded rect
        card.SizeChanged += (s, _) =>
        {
            var b = (Border)s!;
            b.Clip = new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, b.ActualWidth, b.ActualHeight), 10, 10);
        };

        // Hover: scale up, show border, show copy button
        card.MouseEnter += (s, _) =>
        {
            var b = (Border)s!;
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 255, 255, 255));
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        card.MouseLeave += (s, _) =>
        {
            var b = (Border)s!;
            if (vm.IsSelected)
                b.BorderBrush = Theme.StrokeBrush();
            else
                b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 255, 255, 255));
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            if (_selectMode) { vm.IsSelected = !vm.IsSelected; UpdateCardSelection(card, vm); return; }
            // Open upload URL if available, otherwise open file
            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vm.Entry.UploadUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    if (File.Exists(vm.Entry.FilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = vm.Entry.FilePath,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else if (File.Exists(vm.Entry.FilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vm.Entry.FilePath,
                    UseShellExecute = true
                });
            }
        };

        card.MouseRightButtonDown += (s, e) =>
        {
            if (!_selectMode) { _selectMode = true; SelectBtn.Content = "Done"; DeleteSelectedBtn.Visibility = Visibility.Visible; }
            vm.IsSelected = !vm.IsSelected;
            UpdateCardSelection(card, vm);
        };

        return card;
    }

    private static void UpdateCardSelection(Border card, HistoryItemVM vm)
    {
        card.BorderThickness = new Thickness(vm.IsSelected ? Theme.StrokeThickness : 0);
        card.BorderBrush = vm.IsSelected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
    }

    private void ToggleSelectMode(object sender, RoutedEventArgs e)
    {
        _selectMode = !_selectMode;
        SelectBtn.Content = _selectMode ? "Done" : "Select";
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_selectMode) LoadCurrentHistoryTab();
    }

    private void DeleteAllClick(object sender, RoutedEventArgs e)
    {
        string tab = ImagesSubTab.IsChecked == true ? "images"
            : GifsSubTab.IsChecked == true ? "GIFs"
            : TextSubTab.IsChecked == true ? "text history" : "colors";
        if (MessageBox.Show($"Delete all {tab}?", "Confirm 1/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"Really delete all {tab}?", "Confirm 2/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show($"This cannot be undone. Delete all {tab}?", "Confirm 3/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (ImagesSubTab.IsChecked == true) _historyService.ClearImages();
        else if (GifsSubTab.IsChecked == true) _historyService.ClearGifs();
        else if (TextSubTab.IsChecked == true) _historyService.ClearOcr();
        else _historyService.ClearColors();

        LoadCurrentHistoryTab();
    }

    private void DeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (ImagesSubTab.IsChecked == true)
        {
            var toDelete = _historyItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteEntry(entry);
            LoadHistory();
        }
        else if (GifsSubTab.IsChecked == true)
        {
            var toDelete = _gifItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteEntry(entry);
            LoadGifHistory();
        }
        else if (TextSubTab.IsChecked == true)
        {
            var toDelete = OcrStack.Children.OfType<Border>().Where(b => b.Tag as bool? == true).ToList();
            foreach (var card in toDelete)
            {
                int idx = OcrStack.Children.IndexOf(card);
                if (idx >= 0 && idx < _historyService.OcrEntries.Count)
                    _historyService.DeleteOcrEntry(_historyService.OcrEntries[idx]);
            }
            LoadOcrHistory();
        }
        else if (ColorsSubTab.IsChecked == true)
        {
            var toDelete = ColorStack.Children.OfType<StackPanel>().Select(s => s.Tag).OfType<ColorHistoryEntry>().ToList();
            foreach (var entry in toDelete)
                _historyService.DeleteColorEntry(entry);
            LoadColorHistory();
        }
    }

    // ─── GIF History ──────────────────────────────────────────────

    private void LoadGifHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        GifStack.Children.Clear();

        var entries = _historyService.GifEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            try { totalBytes += new FileInfo(e.FilePath).Length; } catch { }
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} GIF{(entries.Count == 1 ? "" : "s")} \u00B7 {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No GIF recordings yet";

        _allGifItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e, ThumbPath = e.FilePath,
            Dimensions = "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _gifRenderCount = Math.Min(HistoryPageSize, _allGifItems.Count);
        RenderGifItems();
    }

    private void RenderGifItems()
    {
        GifStack.Children.Clear();
        _gifItems = _allGifItems.Take(_gifRenderCount).ToList();
        var groups = _gifItems.GroupBy(i => i.Entry.CapturedAt.Date).OrderByDescending(g => g.Key);
        foreach (var group in groups)
        {
            string label = group.Key == DateTime.Today ? "Today"
                : group.Key == DateTime.Today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d, yyyy");

            GifStack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Opacity = 0.45, Margin = new Thickness(6, 10, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var item in group)
            {
                var card = CreateGifCard(item);
                wrap.Children.Add(card);
            }
            GifStack.Children.Add(wrap);
        }
    }

    private void GifPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_gifRenderCount >= _allGifItems.Count) return;
        _gifRenderCount = Math.Min(_gifRenderCount + HistoryPageSize, _allGifItems.Count);
        RenderGifItems();
    }

    private Border CreateGifCard(HistoryItemVM vm)
    {
        var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        // Static first-frame thumbnail loaded async
        img.Loaded += (_, _) =>
        {
            LoadThumbAsync(img, vm.ThumbPath);
            img.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };

        // GIF badge (bottom-left)
        var gifBadge = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 6),
            Child = new TextBlock
            {
                Text = "GIF", FontSize = 9, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            }
        };

        // Copy button overlay (visible on hover)
        var copyBtn = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = 0, IsHitTestVisible = true,
            ToolTip = "Copy to clipboard",
            Child = new TextBlock
            {
                Text = "\U0001F4CB", FontSize = 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };

        var filePath = vm.Entry.FilePath;
        copyBtn.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            try
            {
                if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
                {
                    System.Windows.Clipboard.SetText(vm.Entry.UploadUrl);
                    ToastWindow.Show("Copied", vm.Entry.UploadUrl);
                    return;
                }
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(filePath);
                System.Windows.Clipboard.SetFileDropList(files);
                ToastWindow.Show("Copied", "GIF copied to clipboard");
            }
            catch { }
        };

        // Layout: image area (95px) + info row below — matches image cards
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(95) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imgContainer = new Grid();
        imgContainer.Children.Add(img);
        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) imgContainer.Children.Add(badge);
        }
        imgContainer.Children.Add(gifBadge);
        imgContainer.Children.Add(copyBtn);
        Grid.SetRow(imgContainer, 0);
        grid.Children.Add(imgContainer);

        // File size for GIFs
        string sizeStr = "";
        try { sizeStr = FormatStorageSize(new FileInfo(filePath).Length); } catch { }

        var info = new StackPanel { Margin = new Thickness(8, 5, 8, 6) };
        info.Children.Add(new TextBlock
        {
            Text = vm.Entry.FileName,
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock { Text = vm.TimeAgo, FontSize = 9.5, Opacity = 0.3 });
        Grid.SetRow(info, 1);
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 152, Margin = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = grid, Tag = vm,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };

        // Clip to rounded rect
        card.SizeChanged += (s, _) =>
        {
            var b = (Border)s!;
            b.Clip = new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, b.ActualWidth, b.ActualHeight), 10, 10);
        };

        // Hover: scale up, show border, show copy
        card.MouseEnter += (s, _) =>
        {
            var b = (Border)s!;
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 255, 255, 255));
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.03, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        card.MouseLeave += (s, _) =>
        {
            var b = (Border)s!;
            if (vm.IsSelected)
                b.BorderBrush = Theme.StrokeBrush();
            else
                b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 255, 255, 255));
            var st = (ScaleTransform)b.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            copyBtn.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            if (_selectMode) { vm.IsSelected = !vm.IsSelected; UpdateCardSelection(card, vm); return; }
            // Open upload URL if available, otherwise open file
            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = vm.Entry.UploadUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    if (File.Exists(filePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
        };

        card.MouseRightButtonDown += (s, e) =>
        {
            if (!_selectMode) { _selectMode = true; SelectBtn.Content = "Done"; DeleteSelectedBtn.Visibility = Visibility.Visible; }
            vm.IsSelected = !vm.IsSelected;
            UpdateCardSelection(card, vm);
        };

        return card;
    }

    // ─── OCR History ───────────────────────────────────────────────

    private void LoadOcrHistory()
    {
        OcrStack.Children.Clear();
        var entries = _historyService.OcrEntries;
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No text captures yet";
        HistoryCountText.Text = $"{entries.Count} text capture{(entries.Count == 1 ? "" : "s")}";
        DeleteSelectedBtn.Visibility = _selectMode && TextSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in entries)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 4),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            if (_selectMode)
            {
                card.BorderThickness = new Thickness(Theme.StrokeThickness);
                card.BorderBrush = Theme.StrokeBrush();
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            var preview = entry.Text.Length > 80 ? entry.Text[..80] + "..." : entry.Text;
            textStack.Children.Add(new TextBlock
            {
                Text = preview, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40
            });
            textStack.Children.Add(new TextBlock
            {
                Text = FormatTimeAgo(entry.CapturedAt), FontSize = 10, Opacity = 0.3,
                Margin = new Thickness(0, 3, 0, 0)
            });
            grid.Children.Add(textStack);

            var copyBtn = new Button
            {
                Content = "Copy", FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(copyBtn, 1);
            var capturedText = entry.Text;
            copyBtn.Click += (_, _) => System.Windows.Clipboard.SetText(capturedText);
            grid.Children.Add(copyBtn);

            card.Child = grid;
            if (_selectMode)
            {
                card.MouseLeftButtonDown += (_, _) =>
                {
                    if (card.Tag as bool? == true)
                    {
                        card.Tag = false;
                        card.BorderThickness = new Thickness(0);
                    }
                    else
                    {
                        card.Tag = true;
                        card.BorderThickness = new Thickness(Theme.StrokeThickness);
                        card.BorderBrush = Theme.StrokeBrush();
                    }
                };
            }
            OcrStack.Children.Add(card);
        }
    }

    // ─── Color History ──────────────────────────────────────────────

    private void LoadColorHistory()
    {
        ColorStack.Children.Clear();
        var entries = _historyService.ColorEntries;
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No colors yet";
        HistoryCountText.Text = $"{entries.Count} color{(entries.Count == 1 ? "" : "s")}";
        DeleteSelectedBtn.Visibility = _selectMode && ColorsSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in entries)
        {
            byte r = 0, g = 0, b = 0;
            try
            {
                r = Convert.ToByte(entry.Hex[..2], 16);
                g = Convert.ToByte(entry.Hex[2..4], 16);
                b = Convert.ToByte(entry.Hex[4..6], 16);
            }
            catch { }

            var swatch = new Border
            {
                Width = 56, Height = 56,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = entry.Hex,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8, ShadowDepth = 2, Opacity = 0.25, Color = System.Windows.Media.Colors.Black
                }
            };

            var hexLabel = new TextBlock
            {
                Text = entry.Hex, FontSize = 9,
                Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                Opacity = 0.5, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var stack = new StackPanel { Margin = new Thickness(4) };
            stack.Children.Add(swatch);
            stack.Children.Add(hexLabel);

            if (_selectMode)
            {
                var selected = false;
                swatch.BorderThickness = new Thickness(0);
                swatch.MouseLeftButtonDown += (_, _) =>
                {
                    selected = !selected;
                    swatch.BorderThickness = new Thickness(selected ? Theme.StrokeThickness : 0);
                    swatch.BorderBrush = selected ? Theme.StrokeBrush() : null;
                    stack.Tag = selected ? entry : null;
                };
            }
            else
            {
                swatch.MouseLeftButtonDown += (_, _) =>
                {
                    System.Windows.Clipboard.SetText(entry.Hex);
                    ToastWindow.Show("Copied", entry.Hex);
                };
            }

            ColorStack.Children.Add(stack);
        }
    }

    // ─── Lazy thumbnail loading ────────────────────────────────────

    private static void LoadThumbAsync(Image img, string path)
    {
        // Already loaded (e.g. re-layout)
        if (img.Source != null) return;

        lock (ThumbCache)
        {
            if (ThumbCache.TryGetValue(path, out var cached))
            {
                img.Source = cached;
                return;
            }
        }

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = 320;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                lock (ThumbCache) ThumbCache[path] = bmp;
                img.Dispatcher.BeginInvoke(() => img.Source = bmp);
            }
            catch { }
        });
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
        if (logoSource == null) return null;

        return new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 24, 24, 24)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(28, 255, 255, 255)),
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

    private static string FormatTimeAgo(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalSeconds < 60) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MMM d, yyyy");
    }

}
