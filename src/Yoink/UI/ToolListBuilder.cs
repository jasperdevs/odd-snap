using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Yoink.UI;

/// <summary>
/// Shared builder that creates the unified tool list (icon + checkbox + hotkey box)
/// used by both SettingsWindow and SetupWizard.
/// </summary>
public static class ToolListBuilder
{
    public static readonly (string id, string label, char icon)[] ExtraTools =
    {
        ("_fullscreen",    "Fullscreen capture",  '\0'),
        ("_activeWindow",  "Active window",       '\0'),
        ("_scrollCapture", "Scroll capture",      '\0'),
        ("_record",        "Record",              ToolGlyphs.RecordGlyph),
    };

    private static readonly Dictionary<TextBox, bool> RecordingFlags = new();

    public static void Build(StackPanel panel, SettingsService settingsService, FrameworkElement owner, Action? hotkeyChanged = null)
    {
        panel.Children.Clear();
        var s = settingsService.Settings;
        var enabled = s.EnabledTools ?? ToolDef.DefaultEnabledIds();
        // Icon color for rendering lucide glyphs to bitmaps
        var iconColor = Theme.IsDark ? System.Drawing.Color.FromArgb(160, 255, 255, 255) : System.Drawing.Color.FromArgb(170, 0, 0, 0);
        var segoe = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName);

        void AddHeader(string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                FontFamily = segoe,
                Opacity = 0.4,
                Margin = new Thickness(0, 10, 0, 6),
            });
        }

        void AddToolRow(string toolId, string label, char icon, bool hasToolbarToggle, bool showHotkey)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 0, 0, 3),
                BorderThickness = new Thickness(1),
            };
            card.SetResourceReference(Border.BackgroundProperty, "ThemeCardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowBorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (icon != '\0')
            {
                // Render via WinForms PrivateFontCollection — works for ALL codepoints
                var img = new System.Windows.Controls.Image
                {
                    Source = ToolIcons.RenderToolIconWpf(toolId, icon, iconColor, 20),
                    Width = 18, Height = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
                left.Children.Add(img);
            }

            if (hasToolbarToggle)
            {
                var cb = new CheckBox
                {
                    IsChecked = enabled.Contains(toolId),
                    Tag = toolId,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand,
                };
                cb.Checked += (_, _) => SaveEnabledTools(panel, settingsService);
                cb.Unchecked += (_, _) => SaveEnabledTools(panel, settingsService);
                left.Children.Add(cb);
            }

            left.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontFamily = segoe,
                VerticalAlignment = VerticalAlignment.Center,
            });

            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            if (showHotkey)
            {
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var hkBox = new TextBox();
                hkBox.SetResourceReference(TextBox.StyleProperty, "HotkeyBox");
                WireHotkeyBox(hkBox, toolId, settingsService, hotkeyChanged, () => Build(panel, settingsService, owner, hotkeyChanged));

                var clearBtn = new Button { Content = "X" };
                clearBtn.SetResourceReference(Button.StyleProperty, "ClearBtn");
                var capturedBox = hkBox;
                var capturedId = toolId;
                clearBtn.Click += (_, _) =>
                {
                    settingsService.Settings.SetToolHotkey(capturedId, 0, 0);
                    settingsService.Save();
                    capturedBox.Text = HotkeyFormatter.Format(settingsService.Settings.GetToolHotkey(capturedId).mod, settingsService.Settings.GetToolHotkey(capturedId).key);
                    hotkeyChanged?.Invoke();
                };

                right.Children.Add(hkBox);
                right.Children.Add(clearBtn);

                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
            }

            card.Child = grid;
            panel.Children.Add(card);
        }

        AddHeader("Capture tools");
        foreach (var t in ToolDef.AllTools.Where(t => t.Group == 0))
            AddToolRow(t.Id, t.Label, t.Icon, true, true);

        AddHeader("Capture actions");
        foreach (var (id, label, icon) in ExtraTools)
            AddToolRow(id, label, icon, false, true);

        AddHeader("Annotation tools");
        foreach (var t in ToolDef.AllTools.Where(t => t.Group == 1))
            AddToolRow(t.Id, t.Label, t.Icon, true, true);
    }

    private static void SaveEnabledTools(StackPanel panel, SettingsService svc)
    {
        var enabledIds = new System.Collections.Generic.List<string>();
        foreach (var card in panel.Children.OfType<Border>())
        {
            if (card.Child is not Grid g) continue;
            foreach (var sp in g.Children.OfType<StackPanel>())
            foreach (var cb in sp.Children.OfType<CheckBox>())
            {
                if (cb.Tag is string id && cb.IsChecked == true)
                    enabledIds.Add(id);
            }
        }
        if (!enabledIds.Any(id => ToolDef.AllTools.Any(t => t.Id == id && t.Group == 0)))
            return; // must keep at least one capture tool
        svc.Settings.EnabledTools = enabledIds;
        svc.Save();
    }

    private static void WireHotkeyBox(TextBox box, string toolId, SettingsService svc, Action? hotkeyChanged, Action rebuild)
    {
        var (mod0, key0) = svc.Settings.GetToolHotkey(toolId);
        box.Text = HotkeyFormatter.Format(mod0, key0);

        box.GotFocus += (_, _) =>
        {
            RecordingFlags[box] = true;
            box.Text = "Press keys...";
        };
        box.LostFocus += (_, _) =>
        {
            RecordingFlags[box] = false;
            var (m, k) = svc.Settings.GetToolHotkey(toolId);
            box.Text = HotkeyFormatter.Format(m, k);
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (!RecordingFlags.GetValueOrDefault(box)) return;
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

            svc.Settings.SetToolHotkey(toolId, mod, vk);
            svc.Save();
            box.Text = HotkeyFormatter.Format(mod, vk);
            RecordingFlags[box] = false;
            Keyboard.ClearFocus();
            hotkeyChanged?.Invoke();
        };
    }
}
