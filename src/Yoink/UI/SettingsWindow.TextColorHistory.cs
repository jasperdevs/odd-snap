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
    private string _ocrSearchQuery = "";
    private UIElement? _ocrSearchSurface;
    private List<OcrHistoryEntry> _filteredOcrEntries = new();
    private int _ocrRenderCount;
    private DateTime? _ocrLastRenderedDate;
    private string _colorSearchQuery = "";
    private UIElement? _colorSearchSurface;
    private List<ColorHistoryEntry> _filteredColorEntries = new();
    private int _colorRenderCount;
    private DateTime? _colorLastRenderedDate;

    private void LoadOcrHistory()
    {
        EnsureOcrSearchSurface();
        ClearHistoryListPreservingSearch(OcrStack, _ocrSearchSurface);

        var allEntries = _historyService.OcrEntries;

        // Filter entries by search query
        var query = _ocrSearchQuery.Trim();
        var entries = string.IsNullOrWhiteSpace(query)
            ? allEntries
            : allEntries.Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = allEntries.Count == 0 ? "No text captures yet"
            : entries.Count == 0 ? "No text captures match your search" : "";

        if (string.IsNullOrWhiteSpace(query))
            HistoryCountText.Text = $"{entries.Count} text capture{(entries.Count == 1 ? "" : "s")}";
        else
            HistoryCountText.Text = $"{entries.Count} of {allEntries.Count} text capture{(allEntries.Count == 1 ? "" : "s")}";

        DeleteSelectedBtn.Visibility = _selectMode && HistoryCategoryCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _filteredOcrEntries = entries.ToList();
        _ocrRenderCount = Math.Min(HistoryPageSize, _filteredOcrEntries.Count);
        _ocrLastRenderedDate = null;
        AppendOcrHistoryEntries(_filteredOcrEntries.Take(_ocrRenderCount));
    }

    private void LoadColorHistory()
    {
        EnsureColorSearchSurface();
        ClearHistoryListPreservingSearch(ColorStack, _colorSearchSurface);

        var allEntries = _historyService.ColorEntries;
        var query = _colorSearchQuery.Trim();
        var entries = string.IsNullOrWhiteSpace(query)
            ? allEntries
            : allEntries.Where(entry => ColorMatchesQuery(entry, query)).ToList();

        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = allEntries.Count == 0 ? "No colors yet"
            : entries.Count == 0 ? "No colors match your search" : "";
        HistoryCountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{entries.Count} color{(entries.Count == 1 ? "" : "s")}"
            : $"{entries.Count} of {allEntries.Count} color{(allEntries.Count == 1 ? "" : "s")}";
        DeleteSelectedBtn.Visibility = _selectMode && HistoryCategoryCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        _filteredColorEntries = entries.ToList();
        _colorRenderCount = Math.Min(HistoryPageSize, _filteredColorEntries.Count);
        _colorLastRenderedDate = null;
        AppendColorHistoryEntries(_filteredColorEntries.Take(_colorRenderCount));
    }

    private void OcrPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 260) return;
        if (_ocrRenderCount >= _filteredOcrEntries.Count) return;
        var previousCount = _ocrRenderCount;
        _ocrRenderCount = Math.Min(_ocrRenderCount + HistoryPageSize, _filteredOcrEntries.Count);
        AppendOcrHistoryEntries(_filteredOcrEntries.Skip(previousCount).Take(_ocrRenderCount - previousCount));
    }

    private void ColorsPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 260) return;
        if (_colorRenderCount >= _filteredColorEntries.Count) return;
        var previousCount = _colorRenderCount;
        _colorRenderCount = Math.Min(_colorRenderCount + HistoryPageSize, _filteredColorEntries.Count);
        AppendColorHistoryEntries(_filteredColorEntries.Skip(previousCount).Take(_colorRenderCount - previousCount));
    }

    private void EnsureOcrSearchSurface()
    {
        if (_ocrSearchSurface != null && OcrStack.Children.Count > 0 && OcrStack.Children[0] == _ocrSearchSurface)
            return;

        OcrStack.Children.Clear();
        _ocrSearchSurface = CreateHistorySearchSurface(
            "Search text captures...",
            _ocrSearchQuery,
            text =>
            {
                _ocrSearchQuery = text;
                LoadOcrHistory();
            });
        OcrStack.Children.Add(_ocrSearchSurface);
    }

    private void EnsureColorSearchSurface()
    {
        if (_colorSearchSurface != null && ColorStack.Children.Count > 0 && ColorStack.Children[0] == _colorSearchSurface)
            return;

        ColorStack.Children.Clear();
        _colorSearchSurface = CreateHistorySearchSurface(
            "Search hex, RGB, or color names...",
            _colorSearchQuery,
            text =>
            {
                _colorSearchQuery = text;
                LoadColorHistory();
            });
        ColorStack.Children.Add(_colorSearchSurface);
    }

    private UIElement CreateHistorySearchSurface(string placeholderText, string initialText, Action<string> onTextChanged)
    {
        var outer = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var border = new Border
        {
            Background = TryFindResource("ThemeInputBackgroundBrush") as System.Windows.Media.Brush
                ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            BorderBrush = TryFindResource("ThemeInputBorderBrush") as System.Windows.Media.Brush
                ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(21, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 6, 10, 6)
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        layout.Children.Add(new TextBlock
        {
            Text = "\uE721",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.35,
            Foreground = Theme.Brush(Theme.TextPrimary)
        });

        var inputHost = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(inputHost, 1);

        var searchBox = new System.Windows.Controls.TextBox
        {
            Text = initialText,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            FontSize = 12,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Theme.Brush(Theme.TextPrimary),
            CaretBrush = Theme.Brush(Theme.TextPrimary)
        };

        var placeholder = new TextBlock
        {
            Text = placeholderText,
            FontSize = 12,
            Opacity = 0.28,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrWhiteSpace(initialText) ? Visibility.Visible : Visibility.Collapsed,
            Foreground = Theme.Brush(Theme.TextPrimary)
        };

        inputHost.Children.Add(searchBox);
        inputHost.Children.Add(placeholder);
        layout.Children.Add(inputHost);
        border.Child = layout;
        outer.Children.Add(border);

        searchBox.TextChanged += (_, _) =>
        {
            var text = searchBox.Text ?? "";
            placeholder.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
            onTextChanged(text);
        };
        searchBox.GotKeyboardFocus += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        searchBox.LostKeyboardFocus += (_, _) =>
            placeholder.Visibility = string.IsNullOrWhiteSpace(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        return outer;
    }

    private static void ClearHistoryListPreservingSearch(StackPanel target, UIElement? searchGrid)
    {
        if (searchGrid != null && target.Children.Count > 0 && target.Children[0] == searchGrid)
        {
            while (target.Children.Count > 1)
                target.Children.RemoveAt(target.Children.Count - 1);
            return;
        }

        target.Children.Clear();
        if (searchGrid != null)
            target.Children.Add(searchGrid);
    }

    private void AppendOcrHistoryEntries(IEnumerable<OcrHistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            AppendSectionHeaderIfNeeded(OcrStack, entry.CapturedAt.Date, ref _ocrLastRenderedDate);
            OcrStack.Children.Add(CreateOcrHistoryCard(entry));
        }
    }

    private Border CreateOcrHistoryCard(OcrHistoryEntry entry)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
        };

        card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 255, 255, 255));
        card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255));

        var capturedText = entry.Text;
        bool isLong = capturedText.Length > 120;
        bool expanded = false;

        var textBlock = new System.Windows.Controls.TextBox
        {
            Text = isLong ? capturedText[..120] + "..." : capturedText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 60,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.IBeam,
            Foreground = Theme.Brush(Theme.TextPrimary),
        };

        var bottomRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        bottomRow.Children.Add(new TextBlock
        {
            Text = FormatTimeAgo(entry.CapturedAt),
            FontSize = 10,
            Opacity = 0.3,
            VerticalAlignment = VerticalAlignment.Center
        });

        var btnPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        DockPanel.SetDock(btnPanel, Dock.Right);

        if (isLong)
        {
            var showMoreBtn = new Button
            {
                Content = "Show more",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            showMoreBtn.Click += (_, _) =>
            {
                expanded = !expanded;
                if (expanded)
                {
                    textBlock.Text = capturedText;
                    textBlock.MaxHeight = double.PositiveInfinity;
                    showMoreBtn.Content = "Show less";
                }
                else
                {
                    textBlock.Text = capturedText[..120] + "...";
                    textBlock.MaxHeight = 60;
                    showMoreBtn.Content = "Show more";
                }
            };
            btnPanel.Children.Add(showMoreBtn);
        }

        var copyBtn = new Button
        {
            Content = "Copy all",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        copyBtn.Click += (_, _) =>
        {
            ClipboardService.CopyTextToClipboard(capturedText);
            ToastWindow.Show("Copied", "Text copied");
        };
        btnPanel.Children.Add(copyBtn);
        bottomRow.Children.Insert(0, btnPanel);

        var textStack = new StackPanel();
        textStack.Children.Add(textBlock);
        textStack.Children.Add(bottomRow);

        var badge = CreateSelectionBadge(false);
        var root = new Grid();
        root.Children.Add(textStack);
        root.Children.Add(badge);
        card.Child = root;

        bool selected = false;
        if (_selectMode)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
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
        return card;
    }

    private void AppendColorHistoryEntries(IEnumerable<ColorHistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            AppendSectionHeaderIfNeeded(ColorStack, entry.CapturedAt.Date, ref _colorLastRenderedDate);
            ColorStack.Children.Add(CreateColorHistoryCard(entry));
        }
    }

    private Border CreateColorHistoryCard(ColorHistoryEntry entry)
    {
        byte r = 0, g = 0, b = 0;
        try
        {
            r = Convert.ToByte(entry.Hex[..2], 16);
            g = Convert.ToByte(entry.Hex[2..4], 16);
            b = Convert.ToByte(entry.Hex[4..6], 16);
        }
        catch { }

        var swatchColor = System.Windows.Media.Color.FromRgb(r, g, b);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 3),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 255, 255, 255));
        card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatch = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(swatchColor),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(swatch, 0);
        grid.Children.Add(swatch);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"#{entry.Hex}",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Theme.Brush(Theme.TextPrimary),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, Segoe UI Variable Text"),
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"RGB({r}, {g}, {b}) · {FormatTimeAgo(entry.CapturedAt)}",
            FontSize = 10,
            Opacity = 0.35,
            Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        var copyBtn = new Button
        {
            Content = "Copy",
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var capturedHex = entry.Hex;
        copyBtn.Click += (_, _) =>
        {
            ClipboardService.CopyTextToClipboard(capturedHex);
            ToastWindow.Show("Copied", capturedHex);
        };
        Grid.SetColumn(copyBtn, 2);
        grid.Children.Add(copyBtn);

        var badge = CreateSelectionBadge(false);
        var root = new Grid();
        root.Children.Add(grid);
        root.Children.Add(badge);
        card.Child = root;

        var selected = false;
        if (_selectMode)
        {
            card.BorderThickness = new Thickness(0);
            card.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                selected = !selected;
                card.Tag = selected ? entry : null;
                UpdateSelectableCardSelection(card, badge, selected);
            };
        }
        else
        {
            card.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ClipboardService.CopyTextToClipboard(capturedHex);
                ToastWindow.Show("Copied", capturedHex);
            };
        }

        UpdateSelectableCardSelection(card, badge, selected);
        return card;
    }

    private void AppendSectionHeaderIfNeeded(StackPanel target, DateTime date, ref DateTime? lastRenderedDate)
    {
        if (lastRenderedDate == date)
            return;

        if (target.Children.Count > 1)
        {
            target.Children.Add(new Border
            {
                Height = 1,
                Background = Theme.Brush(Theme.BorderSubtle),
                Margin = new Thickness(6, 14, 6, 0)
            });
        }

        target.Children.Add(new TextBlock
        {
            Text = FormatHistoryGroupLabel(date),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
            Foreground = Theme.Brush(Theme.TextPrimary),
            Opacity = 0.45,
            Margin = new Thickness(6, 12, 0, 6)
        });

        lastRenderedDate = date;
    }

    private static bool ColorMatchesQuery(ColorHistoryEntry entry, string query)
    {
        var searchable = BuildColorSearchText(entry);
        var terms = query.Split(new[] { ' ', '\t', ',', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildColorSearchText(ColorHistoryEntry entry)
    {
        if (!TryParseHexColor(entry.Hex, out var r, out var g, out var b))
            return entry.Hex;

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entry.Hex,
            $"#{entry.Hex}",
            $"{r}",
            $"{g}",
            $"{b}",
            $"rgb({r},{g},{b})",
            $"rgb({r}, {g}, {b})"
        };

        foreach (var token in GetColorSemanticTokens(r, g, b))
            tokens.Add(token);

        return string.Join(' ', tokens);
    }

    private static IEnumerable<string> GetColorSemanticTokens(byte r, byte g, byte b)
    {
        var red = r / 255d;
        var green = g / 255d;
        var blue = b / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var value = max;
        var saturation = max == 0 ? 0 : delta / max;
        double hue = 0;

        if (delta > 0.0001)
        {
            if (Math.Abs(max - red) < 0.0001)
                hue = 60 * (((green - blue) / delta + 6) % 6);
            else if (Math.Abs(max - green) < 0.0001)
                hue = 60 * (((blue - red) / delta) + 2);
            else
                hue = 60 * (((red - green) / delta) + 4);
        }

        var tokens = new List<string>();

        if (value <= 0.12)
            tokens.AddRange(new[] { "black", "dark", "neutral" });
        else if (saturation <= 0.12)
        {
            tokens.Add("neutral");
            if (value >= 0.88)
                tokens.AddRange(new[] { "white", "light", "offwhite" });
            else if (value >= 0.68)
                tokens.AddRange(new[] { "silver", "gray", "grey", "light" });
            else if (value >= 0.38)
                tokens.AddRange(new[] { "gray", "grey", "muted" });
            else
                tokens.AddRange(new[] { "gray", "grey", "dark", "charcoal" });
        }
        else
        {
            if (hue < 15 || hue >= 345)
                tokens.AddRange(value < 0.4 ? new[] { "red", "maroon", "warm" } : new[] { "red", "warm" });
            else if (hue < 35)
                tokens.AddRange(value < 0.55 && saturation < 0.75 ? new[] { "brown", "orange", "warm" } : new[] { "orange", "warm" });
            else if (hue < 60)
                tokens.AddRange(saturation < 0.45 ? new[] { "beige", "tan", "warm" } : new[] { "yellow", "gold", "warm" });
            else if (hue < 160)
                tokens.AddRange(new[] { "green", "cool" });
            else if (hue < 200)
                tokens.AddRange(new[] { "teal", "cyan", "cool" });
            else if (hue < 255)
                tokens.AddRange(new[] { "blue", "cool" });
            else if (hue < 290)
                tokens.AddRange(new[] { "purple", "violet", "cool" });
            else if (hue < 330)
                tokens.AddRange(new[] { "pink", "magenta", "warm" });
            else
                tokens.AddRange(new[] { "red", "pink", "warm" });

            if (value >= 0.82)
                tokens.Add("light");
            else if (value <= 0.28)
                tokens.Add("dark");

            if (saturation >= 0.72)
                tokens.Add("vibrant");
            else if (saturation <= 0.28)
                tokens.Add("muted");
        }

        return tokens;
    }

    private static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length != 6)
            return false;

        try
        {
            r = Convert.ToByte(normalized[..2], 16);
            g = Convert.ToByte(normalized[2..4], 16);
            b = Convert.ToByte(normalized[4..6], 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void UpdateSelectableCardSelection(Border card, Border badge, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? Theme.StrokeThickness : 0);
        card.BorderBrush = selected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
        badge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }
}
