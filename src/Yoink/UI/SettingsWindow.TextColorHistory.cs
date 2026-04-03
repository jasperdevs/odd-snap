using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yoink.Models;
using Yoink.Services;
using Button = System.Windows.Controls.Button;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void LoadOcrHistory()
    {
        OcrStack.Children.Clear();
        var entries = _historyService.OcrEntries;
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No text captures yet";
        HistoryCountText.Text = $"{entries.Count} text capture{(entries.Count == 1 ? "" : "s")}";
        DeleteSelectedBtn.Visibility = _selectMode && HistoryCategoryCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

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
                Text = preview,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40
            });
            textStack.Children.Add(new TextBlock
            {
                Text = FormatTimeAgo(entry.CapturedAt),
                FontSize = 10,
                Opacity = 0.3,
                Margin = new Thickness(0, 3, 0, 0)
            });
            grid.Children.Add(textStack);

            var copyBtn = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(copyBtn, 1);
            var capturedText = entry.Text;
            copyBtn.Click += (_, _) =>
            {
                ClipboardService.CopyTextToClipboard(capturedText);
                ToastWindow.Show("Copied", "Text copied");
            };
            grid.Children.Add(copyBtn);

            var badge = CreateSelectionBadge(false);
            var root = new Grid();
            root.Children.Add(grid);
            root.Children.Add(badge);
            card.Child = root;

            bool selected = false;
            if (_selectMode)
            {
                card.BorderThickness = new Thickness(0);
                card.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    selected = !selected;
                    card.Tag = selected;
                    UpdateSelectableCardSelection(card, badge, selected);
                };
            }

            UpdateSelectableCardSelection(card, badge, selected);
            OcrStack.Children.Add(card);
        }
    }

    private void LoadColorHistory()
    {
        ColorStack.Children.Clear();
        var entries = _historyService.ColorEntries;
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No colors yet";
        HistoryCountText.Text = $"{entries.Count} color{(entries.Count == 1 ? "" : "s")}";
        DeleteSelectedBtn.Visibility = _selectMode && HistoryCategoryCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

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
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = entry.Hex,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.25,
                    Color = System.Windows.Media.Colors.Black
                }
            };

            var hexLabel = new TextBlock
            {
                Text = entry.Hex,
                FontSize = 9,
                Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                Opacity = 0.5,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var badge = CreateSelectionBadge(false);
            var swatchGrid = new Grid();
            swatchGrid.Children.Add(swatch);
            swatchGrid.Children.Add(badge);

            var stack = new StackPanel { Margin = new Thickness(4) };
            stack.Children.Add(swatchGrid);
            stack.Children.Add(hexLabel);

            var selected = false;
            if (_selectMode)
            {
                swatch.BorderThickness = new Thickness(0);
                swatch.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    selected = !selected;
                    UpdateSelectableCardSelection(swatch, badge, selected);
                    stack.Tag = selected ? entry : null;
                };
            }

            var copyColorBtn = new Button
            {
                Content = "Copy",
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            copyColorBtn.Click += (_, _) =>
            {
                ClipboardService.CopyTextToClipboard(entry.Hex);
                ToastWindow.Show("Copied", entry.Hex);
            };

            stack.Children.Add(copyColorBtn);

            UpdateSelectableCardSelection(swatch, badge, selected);
            ColorStack.Children.Add(stack);
        }
    }

    private static void UpdateSelectableCardSelection(Border card, Border badge, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? Theme.StrokeThickness : 0);
        card.BorderBrush = selected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
        badge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }
}
