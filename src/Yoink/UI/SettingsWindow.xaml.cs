using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yoink.Models;
using Yoink.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;

namespace Yoink.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private bool _isRecordingHotkey;

    public event Action? HotkeyChanged;

    public SettingsWindow(SettingsService settingsService, HistoryService historyService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;
        HotkeyBox.Text = FormatHotkey(s.HotkeyModifiers, s.HotkeyKey);
        OcrHotkeyBox.Text = FormatHotkey(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        AfterCaptureCombo.SelectedIndex = (int)s.AfterCapture;
        SaveToFileCheck.IsChecked = s.SaveToFile;
        SaveDirBox.Text = s.SaveDirectory;
        SaveDirPanel.Visibility = s.SaveToFile ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
        SaveHistoryCheck.IsChecked = s.SaveHistory;
    }

    // ─── Tabs ──────────────────────────────────────────────────────

    private void TabChanged(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = HistoryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OcrPanel.Visibility = OcrTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryTab.IsChecked == true) LoadHistory();
        if (OcrTab.IsChecked == true) LoadOcrHistory();
    }

    // ─── Hotkey ────────────────────────────────────────────────────

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        HotkeyBox.Text = "Press keys...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        HotkeyBox.Text = FormatHotkey(_settingsService.Settings.HotkeyModifiers, _settingsService.Settings.HotkeyKey);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key)) return;

        uint mod = GetModifiers();
        if (mod == 0) return;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _settingsService.Settings.HotkeyModifiers = mod;
        _settingsService.Settings.HotkeyKey = vk;
        _settingsService.Save();
        HotkeyBox.Text = FormatHotkey(mod, vk);
        _isRecordingHotkey = false;
        Keyboard.ClearFocus();
        HotkeyChanged?.Invoke();
    }

    // ─── OCR Hotkey ────────────────────────────────────────────────

    private bool _isRecordingOcr;

    private void OcrHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingOcr = true;
        OcrHotkeyBox.Text = "Press keys...";
    }

    private void OcrHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingOcr = false;
        OcrHotkeyBox.Text = FormatHotkey(_settingsService.Settings.OcrHotkeyModifiers, _settingsService.Settings.OcrHotkeyKey);
    }

    private void OcrHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingOcr) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key)) return;

        uint mod = GetModifiers();
        if (mod == 0) return;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _settingsService.Settings.OcrHotkeyModifiers = mod;
        _settingsService.Settings.OcrHotkeyKey = vk;
        _settingsService.Save();
        OcrHotkeyBox.Text = FormatHotkey(mod, vk);
        _isRecordingOcr = false;
        Keyboard.ClearFocus();
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

    // ─── Screenshot History (date-grouped) ─────────────────────────

    private bool _selectMode;
    private List<HistoryItemVM> _historyItems = new();

    private void LoadHistory()
    {
        _selectMode = false;
        SelectBtn.Content = "Select";
        DeleteSelectedBtn.Visibility = Visibility.Collapsed;
        HistoryStack.Children.Clear();

        var entries = _historyService.Entries;
        HistoryCountText.Text = $"{entries.Count} capture{(entries.Count == 1 ? "" : "s")}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _historyItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e, ThumbPath = e.FilePath,
            Dimensions = $"{e.Width} x {e.Height}",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        // Group by date
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

    private Border CreateHistoryCard(HistoryItemVM vm)
    {
        var img = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(vm.ThumbPath);
            bmp.DecodePixelWidth = 160;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch { }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(img, 0);
        grid.Children.Add(img);

        var info = new StackPanel { Margin = new Thickness(7, 4, 7, 5) };
        info.Children.Add(new TextBlock { Text = vm.Dimensions, FontSize = 10.5 });
        info.Children.Add(new TextBlock { Text = vm.TimeAgo, FontSize = 9.5, Opacity = 0.3 });
        Grid.SetRow(info, 1);
        grid.Children.Add(info);

        var card = new Border
        {
            Width = 148, Margin = new Thickness(3),
            CornerRadius = new CornerRadius(7), ClipToBounds = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = grid, Tag = vm
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            if (_selectMode) { vm.IsSelected = !vm.IsSelected; UpdateCardSelection(card, vm); return; }
            if (!File.Exists(vm.Entry.FilePath)) return;
            var viewer = new ImageViewerWindow(vm.Entry.FilePath, _historyService, vm.Entry);
            viewer.Owner = this;
            viewer.ShowDialog();
            LoadHistory();
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
        card.BorderThickness = new Thickness(vm.IsSelected ? 2 : 0);
        card.BorderBrush = vm.IsSelected
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255))
            : System.Windows.Media.Brushes.Transparent;
    }

    private void ToggleSelectMode(object sender, RoutedEventArgs e)
    {
        _selectMode = !_selectMode;
        SelectBtn.Content = _selectMode ? "Done" : "Select";
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_selectMode) LoadHistory();
    }

    private void DeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var toDelete = _historyItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
        foreach (var entry in toDelete)
            _historyService.DeleteEntry(entry);
        LoadHistory();
    }

    private void HistorySelectToggle(object sender, RoutedEventArgs e) { }
    private void HistoryItemClick(object sender, MouseButtonEventArgs e) { }
    private void HistoryItemRightClick(object sender, MouseButtonEventArgs e) { }

    // ─── OCR History ───────────────────────────────────────────────

    private void LoadOcrHistory()
    {
        OcrStack.Children.Clear();
        var entries = _historyService.OcrEntries;
        OcrEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            OcrStack.Children.Add(card);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────

    private static string FormatTimeAgo(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalSeconds < 60) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MMM d, yyyy");
    }

    private static string FormatHotkey(uint mod, uint vk)
    {
        var parts = new List<string>();
        if ((mod & Native.User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & Native.User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & Native.User32.MOD_SHIFT) != 0) parts.Add("Shift");
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        parts.Add(key switch { Key.Oem3 => "`", _ => key.ToString() });
        return string.Join(" + ", parts);
    }
}

internal sealed class HistoryItemVM : System.ComponentModel.INotifyPropertyChanged
{
    public HistoryEntry Entry { get; set; } = null!;
    public string ThumbPath { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string TimeAgo { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
