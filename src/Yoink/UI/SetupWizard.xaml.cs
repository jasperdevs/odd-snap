using System.Windows;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SetupWizard : Window
{
    private readonly SettingsService _settingsService;
    private int _page = 1;

    // Temp hotkey state
    private uint _hkMod, _hkKey, _ocrMod, _ocrKey, _pickerMod, _pickerKey;

    public SetupWizard(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Theme.Refresh(); // Must be before InitializeComponent so resources are correct
        InitializeComponent();
        ApplyTheme(); // Set dynamic resources before any controls render

        var s = settingsService.Settings;
        _hkMod = s.HotkeyModifiers; _hkKey = s.HotkeyKey;
        _ocrMod = s.OcrHotkeyModifiers; _ocrKey = s.OcrHotkeyKey;
        _pickerMod = s.PickerHotkeyModifiers; _pickerKey = s.PickerHotkeyKey;

        SetupHotkeyBox.Text = HotkeyFormatter.Format(_hkMod, _hkKey);
        SetupOcrHotkeyBox.Text = HotkeyFormatter.Format(_ocrMod, _ocrKey);
        SetupPickerHotkeyBox.Text = HotkeyFormatter.Format(_pickerMod, _pickerKey);

        PopulateTools(); // Now Theme is initialized, colors will be correct
    }

    private void OnSourceInit(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.Dwm.DisableBackdrop(hwnd);
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        // Update all dynamic resource brushes
        Resources["WizBg"] = Theme.Brush(Theme.BgPrimary);
        Resources["WizCardBg"] = Theme.Brush(Theme.BgCard);
        Resources["WizFg"] = Theme.Brush(Theme.TextPrimary);
        Resources["WizFgMuted"] = Theme.Brush(Theme.TextSecondary);
        Resources["WizBorder"] = Theme.Brush(Theme.WindowBorder);
        Resources["WizInputBg"] = Theme.Brush(Theme.BgSecondary);

        // Button colors: primary is inverted (light bg on dark, dark bg on light)
        Resources["WizBtnPrimaryBg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(240, 240, 240)
            : System.Windows.Media.Color.FromRgb(30, 30, 30));
        Resources["WizBtnPrimaryFg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(26, 26, 26)
            : System.Windows.Media.Color.FromRgb(240, 240, 240));
        Resources["WizBtnSecondaryBg"] = Theme.Brush(Theme.AccentSubtle);
        Resources["WizBtnSecondaryFg"] = Theme.Brush(Theme.TextPrimary);

        Foreground = Theme.Brush(Theme.TextPrimary);
    }

    private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            DragMove();
    }

    private void PopulateTools()
    {
        SetupToolPanel.Children.Clear();
        var enabled = _settingsService.Settings.EnabledTools ?? ToolDef.DefaultEnabledIds();
        var captureTools = ToolDef.AllTools.Where(t => t.Group == 0).ToArray();
        var annotationTools = ToolDef.AllTools.Where(t => t.Group == 1).ToArray();

        AddGroupLabel("Capture tools");
        foreach (var tool in captureTools)
            AddToolCheckBox(tool, enabled.Contains(tool.Id));

        AddGroupLabel("Annotation tools");
        foreach (var tool in annotationTools)
            AddToolCheckBox(tool, enabled.Contains(tool.Id));
    }

    private void AddGroupLabel(string text)
    {
        SetupToolPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
            Foreground = Theme.Brush(Theme.TextPrimary),
            Opacity = 0.4,
            Margin = new Thickness(0, 8, 0, 4),
        });
    }

    private void AddToolCheckBox(ToolDef tool, bool isOn)
    {
        var cb = new System.Windows.Controls.CheckBox
        {
            Content = tool.Label,
            FontSize = 12.5,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
            IsChecked = isOn,
            Tag = tool.Id,
            Margin = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(4, 4, 0, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Theme.Brush(Theme.TextPrimary),
        };
        SetupToolPanel.Children.Add(cb);
    }

    private void SetupHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var (mod, key) = HotkeyFormatter.Parse(e);
        if (key == 0) return;
        _hkMod = mod; _hkKey = key;
        SetupHotkeyBox.Text = HotkeyFormatter.Format(mod, key);
    }

    private void SetupOcrHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var (mod, key) = HotkeyFormatter.Parse(e);
        if (key == 0) return;
        _ocrMod = mod; _ocrKey = key;
        SetupOcrHotkeyBox.Text = HotkeyFormatter.Format(mod, key);
    }

    private void SetupPickerHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var (mod, key) = HotkeyFormatter.Parse(e);
        if (key == 0) return;
        _pickerMod = mod; _pickerKey = key;
        SetupPickerHotkeyBox.Text = HotkeyFormatter.Format(mod, key);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 1)
        {
            _page = 2;
            Page1.Visibility = Visibility.Collapsed;
            Page2.Visibility = Visibility.Visible;
            BackBtn.Visibility = Visibility.Visible;
            NextBtn.Content = "Get Started";
            Dot1.Opacity = 0.2;
            Dot2.Opacity = 0.7;
        }
        else
        {
            // Save everything
            var s = _settingsService.Settings;
            s.HotkeyModifiers = _hkMod; s.HotkeyKey = _hkKey;
            s.OcrHotkeyModifiers = _ocrMod; s.OcrHotkeyKey = _ocrKey;
            s.PickerHotkeyModifiers = _pickerMod; s.PickerHotkeyKey = _pickerKey;

            var enabledIds = new List<string>();
            foreach (var child in SetupToolPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.Tag is string id && cb.IsChecked == true)
                    enabledIds.Add(id);
            }
            s.EnabledTools = enabledIds;
            s.HasCompletedSetup = true;
            _settingsService.Save();
            DialogResult = true;
            Close();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _page = 1;
        Page1.Visibility = Visibility.Visible;
        Page2.Visibility = Visibility.Collapsed;
        BackBtn.Visibility = Visibility.Collapsed;
        NextBtn.Content = "Next";
        Dot1.Opacity = 0.7;
        Dot2.Opacity = 0.2;
    }
}
