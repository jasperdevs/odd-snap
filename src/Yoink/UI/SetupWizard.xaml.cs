using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace Yoink.UI;

public partial class SetupWizard : Window
{
    private readonly SettingsService _settingsService;
    private int _page = 1;
    private const int TotalPages = 3;
    private readonly Border[] _dots;
    private readonly Grid[] _pages;

    private static readonly (string id, string label, char icon)[] CaptureHotkeys =
    {
        ("rect",           "Screenshot",        '\uE257'),
        ("ocr",            "Text capture",      '\uE53C'),
        ("picker",         "Color picker",      '\uE13E'),
        ("sticker",        "Sticker",           ToolGlyphs.StickerGlyph),
        ("upscale",        "Upscale",           ToolGlyphs.UpscaleGlyph),
        ("_record",        "Record",             ToolGlyphs.RecordGlyph),
    };

    public SetupWizard(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Theme.Refresh();
        InitializeComponent();
        ApplyTheme();

        _dots = new[] { Dot1, Dot2, Dot3 };
        _pages = new[] { Page1, Page2, Page3 };

        BuildHotkeyRows();
        LoadDefaults();
    }

    private void BuildHotkeyRows()
    {
        var segoe = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName);
        var iconColor = Theme.IsDark
            ? System.Drawing.Color.FromArgb(160, 255, 255, 255)
            : System.Drawing.Color.FromArgb(170, 0, 0, 0);
        var s = _settingsService.Settings;

        foreach (var (id, label, icon) in CaptureHotkeys)
        {
            var row = new Border();
            row.SetResourceReference(StyleProperty, "WizRow");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (icon != '\0')
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = ToolIcons.RenderToolIconWpf(id, icon, iconColor, 20),
                    Width = 18, Height = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
                left.Children.Add(img);
            }
            left.Children.Add(new TextBlock
            {
                Text = label, FontSize = 13, FontFamily = segoe,
                Foreground = (System.Windows.Media.Brush)FindResource("WizFg"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var hkBox = new TextBox
            {
                IsReadOnly = true, FontSize = 12, FontWeight = FontWeights.Medium,
                FontFamily = segoe, Padding = new Thickness(10, 7, 10, 7),
                MinWidth = 130, TextAlignment = TextAlignment.Center,
                Background = (System.Windows.Media.Brush)FindResource("WizInputBg"),
                Foreground = (System.Windows.Media.Brush)FindResource("WizFg"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("WizBorder"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var template = (ControlTemplate?)TryFindResource("WizHotkeyBoxTemplate");
            if (template != null) hkBox.Template = template;
            var (mod, key) = s.GetToolHotkey(id);
            hkBox.Text = HotkeyFormatter.Format(mod, key);
            hkBox.Tag = id;
            WireHotkey(hkBox, id);
            Grid.SetColumn(hkBox, 1);
            grid.Children.Add(hkBox);

            row.Child = grid;
            HotkeyPanel.Children.Add(row);
        }
    }

    private void WireHotkey(TextBox box, string toolId)
    {
        bool recording = false;
        box.GotFocus += (_, _) => { recording = true; box.Text = "Press keys..."; };
        box.LostFocus += (_, _) =>
        {
            recording = false;
            var (m, k) = _settingsService.Settings.GetToolHotkey(toolId);
            box.Text = HotkeyFormatter.Format(m, k);
        };
        void HandleKey(Key rawKey)
        {
            if (!recording) return;
            var key = rawKey == Key.System ? Key.None : rawKey;
            if (key == Key.None) return;
            if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
                return;

            uint mod = HotkeyFormatter.GetActiveModifiers();
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return;

            _settingsService.Settings.SetToolHotkey(toolId, mod, vk);
            box.Text = HotkeyFormatter.Format(mod, vk);
            recording = false;
            Keyboard.ClearFocus();
        }

        box.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            HandleKey(key);
        };
        // PrintScreen and some special keys only arrive on KeyUp
        box.PreviewKeyUp += (_, e) =>
        {
            if (!recording) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Snapshot or Key.Pause or Key.Cancel)
            {
                e.Handled = true;
                HandleKey(key);
            }
        };
    }

    private void LoadDefaults()
    {
        var s = _settingsService.Settings;
        WizCrosshairCheck.IsChecked = s.ShowCrosshairGuides;
        WizCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        WizMuteCheck.IsChecked = s.MuteSounds;
        WizSaveToFileCheck.IsChecked = s.SaveToFile;
        WizCaptureFormatCombo.SelectedIndex = (int)s.CaptureImageFormat;

        // Max size combo by tag
        for (int i = 0; i < WizCaptureSizeCombo.Items.Count; i++)
        {
            if (WizCaptureSizeCombo.Items[i] is ComboBoxItem item && item.Tag is string tag &&
                int.TryParse(tag, out int val) && val == s.CaptureMaxLongEdge)
            { WizCaptureSizeCombo.SelectedIndex = i; break; }
        }
        if (WizCaptureSizeCombo.SelectedIndex < 0) WizCaptureSizeCombo.SelectedIndex = 0;

        WizSaveDirText.Text = s.SaveDirectory;
    }

    private void BrowseSaveDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = _settingsService.Settings.SaveDirectory,
            Description = "Choose where screenshots are saved",
            ShowNewFolderButton = true,
        };
        var owner = new WindowHandleWrapper(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
        {
            _settingsService.Settings.SaveDirectory = dlg.SelectedPath;
            WizSaveDirText.Text = dlg.SelectedPath;
        }
    }

    private void GoToPage(int page)
    {
        SaveCurrentPage();
        _page = page;

        for (int i = 0; i < _pages.Length; i++)
        {
            if (i == page - 1)
            {
                _pages[i].Opacity = 0;
                _pages[i].Visibility = Visibility.Visible;
                _pages[i].BeginAnimation(OpacityProperty,
                    Motion.FromTo(0, 1, 220, Motion.SmoothOut));
            }
            else
                _pages[i].Visibility = Visibility.Collapsed;

            _dots[i].BeginAnimation(OpacityProperty,
                Motion.To(i == page - 1 ? 0.7 : 0.2, 220, Motion.SmoothOut));
        }
        BackBtn.Visibility = page > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = page == TotalPages ? "Get Started" : "Next";
    }

    private void SaveCurrentPage()
    {
        var s = _settingsService.Settings;
        switch (_page)
        {
            case 1:
                _settingsService.Save();
                break;
            case 2:
                s.ShowCrosshairGuides = WizCrosshairCheck.IsChecked == true;
                s.ShowCaptureMagnifier = WizCaptureMagnifierCheck.IsChecked == true;
                s.MuteSounds = WizMuteCheck.IsChecked == true;
                s.SaveToFile = WizSaveToFileCheck.IsChecked == true;
                s.CaptureImageFormat = (CaptureImageFormat)WizCaptureFormatCombo.SelectedIndex;
                if (WizCaptureSizeCombo.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag is string sizeTag && int.TryParse(sizeTag, out int sizeVal))
                    s.CaptureMaxLongEdge = sizeVal;
                _settingsService.Save();
                break;
            case 3:
                s.HasCompletedSetup = true;
                _settingsService.Save();
                break;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < TotalPages)
            GoToPage(_page + 1);
        else
        {
            SaveCurrentPage();
            DialogResult = true;
            Close();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1) GoToPage(_page - 1);
    }

    private void OnSourceInit(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.Dwm.DisableBackdrop(hwnd);
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        Resources["WizBg"] = Theme.Brush(Theme.BgPrimary);
        Resources["WizCardBg"] = Theme.Brush(Theme.BgCard);
        Resources["WizFg"] = Theme.Brush(Theme.TextPrimary);
        Resources["WizFgMuted"] = Theme.Brush(Theme.TextSecondary);
        Resources["WizBorder"] = Theme.Brush(Theme.WindowBorder);
        Resources["WizInputBg"] = Theme.Brush(Theme.BgSecondary);
        Resources["WizBtnPrimaryBg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(240, 240, 240) : System.Windows.Media.Color.FromRgb(30, 30, 30));
        Resources["WizBtnPrimaryFg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(26, 26, 26) : System.Windows.Media.Color.FromRgb(240, 240, 240));
        Resources["WizBtnPrimaryBorder"] = Theme.Brush(Theme.BorderSubtle);
        Resources["WizBtnSecondaryBg"] = Theme.Brush(Theme.AccentSubtle);
        Resources["WizBtnSecondaryFg"] = Theme.Brush(Theme.TextPrimary);
        Resources["WizShadowColor"] = Theme.IsDark
            ? System.Windows.Media.Color.FromArgb(128, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(72, 0, 0, 0);
        Foreground = Theme.Brush(Theme.TextPrimary);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentPage();
        _settingsService.Settings.HasCompletedSetup = true;
        _settingsService.Save();
        Tag = "OpenSettings";
        DialogResult = true;
        Close();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private sealed class WindowHandleWrapper : System.Windows.Forms.IWin32Window
    {
        public WindowHandleWrapper(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
