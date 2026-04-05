using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Native;
using TextBox = System.Windows.Controls.TextBox;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private readonly Dictionary<TextBox, bool> _recordingFlags = new();

    private void WireHotkeyBox(TextBox box, string toolId, string? _ = null)
    {
        var (mod0, key0) = _settingsService.Settings.GetToolHotkey(toolId);
        box.Text = HotkeyFormatter.Format(mod0, key0);

        box.GotFocus += (_, _) =>
        {
            _recordingFlags[box] = true;
            box.Text = "Press keys...";
        };
        box.LostFocus += (_, _) =>
        {
            _recordingFlags[box] = false;
            var (m, k) = _settingsService.Settings.GetToolHotkey(toolId);
            box.Text = HotkeyFormatter.Format(m, k);
        };
        void AcceptKey(Key rawKey)
        {
            if (!_recordingFlags.GetValueOrDefault(box)) return;
            if (IsModifierOnly(rawKey)) return;

            uint mod = GetModifiers();
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(rawKey);
            if (vk == 0) return;

            var conflict = FindHotkeyConflict(toolId, mod, vk);
            if (conflict != null)
            {
                var combo = HotkeyFormatter.Format(mod, vk);
                var result = MessageBox.Show(
                    $"{combo} is already used by \"{conflict}\".\n\nReplace it?",
                    "Hotkey conflict",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    _recordingFlags[box] = false;
                    var (cm, ck) = _settingsService.Settings.GetToolHotkey(toolId);
                    box.Text = HotkeyFormatter.Format(cm, ck);
                    Keyboard.ClearFocus();
                    return;
                }
                ClearConflictingHotkey(mod, vk, toolId);
            }

            _settingsService.Settings.SetToolHotkey(toolId, mod, vk);
            _settingsService.Save();
            box.Text = HotkeyFormatter.Format(mod, vk);
            _recordingFlags[box] = false;
            Keyboard.ClearFocus();
            HotkeyChanged?.Invoke();
            if (conflict != null) PopulateToolToggles();
        }

        box.PreviewKeyDown += (_, e) =>
        {
            if (!_recordingFlags.GetValueOrDefault(box)) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            AcceptKey(key);
        };
        // PrintScreen and some special keys only arrive on KeyUp
        box.PreviewKeyUp += (_, e) =>
        {
            if (!_recordingFlags.GetValueOrDefault(box)) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Snapshot or Key.Pause or Key.Cancel)
            {
                e.Handled = true;
                AcceptKey(key);
            }
        };
    }

    private void ClearHotkey(string toolId, TextBox box, string? _ = null)
    {
        _settingsService.Settings.SetToolHotkey(toolId, 0, 0);
        _settingsService.Save();
        box.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private string? FindHotkeyConflict(string excludeToolId, uint mod, uint key)
    {
        var s = _settingsService.Settings;
        foreach (var t in ToolDef.AllTools)
        {
            if (t.Id == excludeToolId) continue;
            var (m, k) = s.GetToolHotkey(t.Id);
            if (m == mod && k == key) return t.Label;
        }
        foreach (var (id, label, _) in ExtraTools)
        {
            if (id == excludeToolId) continue;
            var (m, k) = s.GetToolHotkey(id);
            if (m == mod && k == key) return label;
        }
        return null;
    }

    private void ClearConflictingHotkey(uint mod, uint key, string excludeId)
    {
        var s = _settingsService.Settings;
        foreach (var t in ToolDef.AllTools)
        {
            if (t.Id == excludeId) continue;
            var (m, k) = s.GetToolHotkey(t.Id);
            if (m == mod && k == key) s.SetToolHotkey(t.Id, 0, 0);
        }
        foreach (var (id, _, _) in ExtraTools)
        {
            if (id == excludeId) continue;
            var (m, k) = s.GetToolHotkey(id);
            if (m == mod && k == key) s.SetToolHotkey(id, 0, 0);
        }
        _settingsService.Save();
    }

    private static bool IsModifierOnly(Key k) =>
        k is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape;

    private static uint GetModifiers()
    {
        uint m = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) m |= User32.MOD_WIN;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) m |= User32.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) m |= User32.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) m |= User32.MOD_SHIFT;
        return m;
    }

    private void WireHotkeyBoxes() { }
}
