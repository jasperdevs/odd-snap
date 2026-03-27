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
        InitializeComponent();
        _settingsService = settingsService;
        var s = settingsService.Settings;
        _hkMod = s.HotkeyModifiers; _hkKey = s.HotkeyKey;
        _ocrMod = s.OcrHotkeyModifiers; _ocrKey = s.OcrHotkeyKey;
        _pickerMod = s.PickerHotkeyModifiers; _pickerKey = s.PickerHotkeyKey;

        SetupHotkeyBox.Text = HotkeyFormatter.Format(_hkMod, _hkKey);
        SetupOcrHotkeyBox.Text = HotkeyFormatter.Format(_ocrMod, _ocrKey);
        SetupPickerHotkeyBox.Text = HotkeyFormatter.Format(_pickerMod, _pickerKey);

        PopulateTools();
    }

    private void OnSourceInit(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.Dwm.DisableBackdrop(hwnd);
    }

    private void PopulateTools()
    {
        SetupToolPanel.Children.Clear();
        var enabled = _settingsService.Settings.EnabledTools ?? ToolDef.DefaultEnabledIds();
        foreach (var tool in ToolDef.AllTools)
        {
            var cb = new System.Windows.Controls.CheckBox
            {
                Content = tool.Label,
                FontSize = 13,
                IsChecked = enabled.Contains(tool.Id),
                Tag = tool.Id,
                Margin = new Thickness(0, 0, 16, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = System.Windows.Media.Brushes.White,
            };
            SetupToolPanel.Children.Add(cb);
        }
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
            PageIndicator.Text = "2 / 2";
        }
        else
        {
            // Save everything
            var s = _settingsService.Settings;
            s.HotkeyModifiers = _hkMod; s.HotkeyKey = _hkKey;
            s.OcrHotkeyModifiers = _ocrMod; s.OcrHotkeyKey = _ocrKey;
            s.PickerHotkeyModifiers = _pickerMod; s.PickerHotkeyKey = _pickerKey;

            var enabledIds = new List<string>();
            foreach (System.Windows.Controls.CheckBox cb in SetupToolPanel.Children)
                if (cb.IsChecked == true)
                    enabledIds.Add((string)cb.Tag);
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
        PageIndicator.Text = "1 / 2";
    }
}
