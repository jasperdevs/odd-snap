using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private bool _isRecordingHotkey;
    private uint _pendingModifiers;
    private uint _pendingKey;

    public event Action? HotkeyChanged;

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;

        HotkeyBox.Text = FormatHotkey(s.HotkeyModifiers, s.HotkeyKey);
        AfterCaptureCombo.SelectedIndex = (int)s.AfterCapture;
        SaveToFileCheck.IsChecked = s.SaveToFile;
        SaveDirBox.Text = s.SaveDirectory;
        SaveDirPanel.Visibility = s.SaveToFile ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        HotkeyBox.Text = "Press a key combination...";
        HotkeyHint.Visibility = Visibility.Visible;
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        HotkeyHint.Visibility = Visibility.Collapsed;
        // Revert to current setting if nothing was pressed
        HotkeyBox.Text = FormatHotkey(
            _settingsService.Settings.HotkeyModifiers,
            _settingsService.Settings.HotkeyKey);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
            return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        // Build modifier flags
        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Native.User32.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Native.User32.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Native.User32.MOD_SHIFT;

        // Need at least one modifier
        if (modifiers == 0)
            return;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        _pendingModifiers = modifiers;
        _pendingKey = vk;

        // Apply immediately
        _settingsService.Settings.HotkeyModifiers = modifiers;
        _settingsService.Settings.HotkeyKey = vk;
        _settingsService.Save();

        HotkeyBox.Text = FormatHotkey(modifiers, vk);
        _isRecordingHotkey = false;
        HotkeyHint.Visibility = Visibility.Collapsed;

        // Move focus away
        FocusManager.SetFocusedElement(this, this);
        Keyboard.ClearFocus();

        HotkeyChanged?.Invoke();
    }

    private void AfterCaptureCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AfterCapture = (AfterCaptureAction)AfterCaptureCombo.SelectedIndex;
        _settingsService.Save();
    }

    private void SaveToFileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = SaveToFileCheck.IsChecked == true;
        _settingsService.Settings.SaveToFile = isChecked;
        SaveDirPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        _settingsService.Save();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where to save screenshots",
            SelectedPath = _settingsService.Settings.SaveDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settingsService.Settings.SaveDirectory = dialog.SelectedPath;
            SaveDirBox.Text = dialog.SelectedPath;
            _settingsService.Save();
        }
    }

    private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = StartWithWindowsCheck.IsChecked == true;
        _settingsService.Settings.StartWithWindows = isChecked;
        _settingsService.Save();
        SetStartWithWindows(isChecked);
    }

    private static void SetStartWithWindows(bool enable)
    {
        const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "Yoink";

        using var key = Registry.CurrentUser.OpenSubKey(keyName, true);
        if (key is null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
                key.SetValue(valueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(valueName, false);
        }
    }

    private static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & Native.User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Native.User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.User32.MOD_SHIFT) != 0) parts.Add("Shift");

        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        string keyName = key switch
        {
            Key.Oem3 => "`",  // backtick/tilde key
            _ => key.ToString()
        };

        parts.Add(keyName);
        return string.Join(" + ", parts);
    }
}
