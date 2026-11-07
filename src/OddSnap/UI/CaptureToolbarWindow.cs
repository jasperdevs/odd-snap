using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;

namespace OddSnap.UI;

internal sealed class CaptureToolbarWindow : Window
{
    public event Action<string>? ToolRequested;

    public CaptureToolbarWindow(AppSettings settings)
    {
        Title = LocalizationService.Translate("Capture");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var area = PopupWindowHelper.GetCurrentWorkArea();
        var panel = new WrapPanel { Width = Math.Min(720, Math.Max(180, area.Width / UiScale.Current - 40)) };
        foreach (var tool in ToolbarLayout.GetOrderedCaptureActions(settings))
        {
            var label = LocalizationService.Translate(tool.Label);
            var button = new Button
            {
                Content = label,
                ToolTip = label,
                Margin = new Thickness(3),
                Padding = new Thickness(12, 8, 12, 8),
                Style = TryFindResource("ChromeButtonBase") as Style
            };
            AutomationProperties.SetName(button, label);
            button.SetResourceReference(Control.BackgroundProperty, "ChromeButtonBackground");
            button.SetResourceReference(Control.ForegroundProperty, "ChromeButtonForeground");
            button.SetResourceReference(Control.BorderBrushProperty, "ChromeButtonBorderBrush");
            button.SetResourceReference(Control.PaddingProperty, "SettingsControlPadding");
            button.SetResourceReference(Control.FontSizeProperty, "SettingsControlFontSize");
            button.SetResourceReference(Control.MinHeightProperty, "SettingsControlHeight");
            var (modifiers, key) = settings.GetToolHotkey(tool.Id);
            if (key != 0) button.ToolTip = label + " (" + HotkeyFormatter.Format(modifiers, key) + ")";
            button.Click += (_, _) =>
            {
                Close();
                ToolRequested?.Invoke(tool.Id);
            };
            panel.Children.Add(button);
        }

        var border = new Border { Child = panel, Padding = new Thickness(8), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "ThemeCardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowBorderBrush");
        Content = border;
        UiScale.ApplyToWindow(this, border, scaleWindowBounds: false);
        OddSnapWindowChrome.ApplyRoundedCorners(this, 7);
        void RefreshTheme() => Theme.ApplyTo(Resources);
        RefreshTheme();
        Theme.Changed += RefreshTheme;
        Closed += (_, _) => Theme.Changed -= RefreshTheme;
        Loaded += (_, _) =>
        {
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            Top = area.Top + 20;
            (panel.Children.OfType<Button>().FirstOrDefault())?.Focus();
        };
        Deactivated += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }
}
