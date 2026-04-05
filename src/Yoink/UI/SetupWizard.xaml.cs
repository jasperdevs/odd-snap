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
    private const int TotalPages = 4;
    private readonly Border[] _dots;
    private readonly Grid[] _pages;

    private static readonly (string id, string label, char icon)[] CaptureHotkeys =
    {
        ("rect",           "Screenshot",        '\uE257'),
        ("ocr",            "Text capture",      '\uE53C'),
        ("picker",         "Color picker",      '\uE13E'),
        ("sticker",        "Sticker",           ToolGlyphs.StickerGlyph),
        ("_record",        "Record",             ToolGlyphs.RecordGlyph),
    };

    public SetupWizard(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Theme.Refresh();
        InitializeComponent();
        ApplyTheme();

        _dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        _pages = new[] { Page1, Page2, Page3, Page4 };

        BuildHotkeyRows();
        LoadDefaults();
    }

    // ── Page 1: Hotkey rows ──────────────────────────────────────

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
                IsReadOnly = true,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                FontFamily = segoe,
                Padding = new Thickness(10, 7, 10, 7),
                MinWidth = 130,
                TextAlignment = TextAlignment.Center,
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
        box.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
                return;

            uint mod = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mod |= Native.User32.MOD_WIN;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mod |= Native.User32.MOD_ALT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mod |= Native.User32.MOD_CONTROL;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mod |= Native.User32.MOD_SHIFT;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            _settingsService.Settings.SetToolHotkey(toolId, mod, vk);
            box.Text = HotkeyFormatter.Format(mod, vk);
            recording = false;
            Keyboard.ClearFocus();
        };
    }

    // ── Defaults ─────────────────────────────────────────────────

    private void LoadDefaults()
    {
        var s = _settingsService.Settings;

        // Page 2: Capture
        WizAfterCombo.SelectedIndex = (int)s.AfterCapture;
        WizCrosshairCheck.IsChecked = s.ShowCrosshairGuides;
        WizCaptureMagnifierCheck.IsChecked = s.ShowCaptureMagnifier;
        WizMuteCheck.IsChecked = s.MuteSounds;

        // Page 3: Recording
        WizRecordFormatCombo.SelectedIndex = (int)s.RecordingFormat;
        WizRecordQualityCombo.SelectedIndex = (int)s.RecordingQuality;
        // Select FPS combo by matching Tag
        for (int i = 0; i < WizRecordFpsCombo.Items.Count; i++)
        {
            if (WizRecordFpsCombo.Items[i] is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int fps) && fps == s.RecordingFps)
            { WizRecordFpsCombo.SelectedIndex = i; break; }
        }
        if (WizRecordFpsCombo.SelectedIndex < 0) WizRecordFpsCombo.SelectedIndex = 2; // default 30
        WizRecordMicCheck.IsChecked = s.RecordMicrophone;
        WizRecordDesktopAudioCheck.IsChecked = s.RecordDesktopAudio;
    }

    // ── Navigation ───────────────────────────────────────────────

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
                var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)));
                _pages[i].BeginAnimation(OpacityProperty, fadeIn);
            }
            else
            {
                _pages[i].Visibility = Visibility.Collapsed;
            }

            var dotTarget = i == page - 1 ? 0.7 : 0.2;
            _dots[i].BeginAnimation(OpacityProperty,
                new DoubleAnimation(dotTarget, new Duration(TimeSpan.FromMilliseconds(200))));
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
                s.AfterCapture = (AfterCaptureAction)WizAfterCombo.SelectedIndex;
                s.ShowCrosshairGuides = WizCrosshairCheck.IsChecked == true;
                s.ShowCaptureMagnifier = WizCaptureMagnifierCheck.IsChecked == true;
                s.MuteSounds = WizMuteCheck.IsChecked == true;
                _settingsService.Save();
                break;
            case 3:
                s.RecordingFormat = (RecordingFormat)WizRecordFormatCombo.SelectedIndex;
                s.RecordingQuality = (RecordingQuality)WizRecordQualityCombo.SelectedIndex;
                if (WizRecordFpsCombo.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag is string fpsTag && int.TryParse(fpsTag, out int fpsVal))
                    s.RecordingFps = fpsVal;
                s.RecordMicrophone = WizRecordMicCheck.IsChecked == true;
                s.RecordDesktopAudio = WizRecordDesktopAudioCheck.IsChecked == true;
                _settingsService.Save();
                break;
            case 4:
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

    // ── Chrome ───────────────────────────────────────────────────

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
}
