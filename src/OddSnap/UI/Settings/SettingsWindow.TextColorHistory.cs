using System.Drawing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OddSnap.Models;
using OddSnap.Services;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private static readonly System.Windows.Media.Brush HistoryCardIdleBrush = CreateFrozenHistoryBrush(System.Windows.Media.Color.FromArgb(12, 255, 255, 255));
    private static readonly System.Windows.Media.Brush HistoryCardHoverBrush = CreateFrozenHistoryBrush(System.Windows.Media.Color.FromArgb(24, 255, 255, 255));
    private static readonly System.Windows.Media.Brush HistoryCardFocusBrush = CreateFrozenHistoryBrush(System.Windows.Media.Color.FromArgb(150, 255, 255, 255));

    private static System.Windows.Media.Brush CreateFrozenHistoryBrush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private string _ocrSearchQuery = "";
    private List<OcrHistoryEntry> _filteredOcrEntries = new();
    private int _ocrRenderCount;
    private DateTime? _ocrLastRenderedDate;
    private string _colorSearchQuery = "";
    private List<ColorHistoryEntry> _filteredColorEntries = new();
    private int _colorRenderCount;
    private DateTime? _colorLastRenderedDate;
    private readonly Dictionary<OcrHistoryEntry, Border> _ocrHistoryCardCache = new();
    private readonly Dictionary<ColorHistoryEntry, Border> _colorHistoryCardCache = new();
    private readonly System.Windows.Threading.DispatcherTimer _ocrSearchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private readonly System.Windows.Threading.DispatcherTimer _colorSearchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };

    private void LoadOcrHistory()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OcrStack.Children.Clear();

        var allEntries = _historyService.OcrEntries;
        PruneOcrSearchCache(allEntries);

        var query = _ocrSearchQuery.Trim();
        List<OcrHistoryEntry> entries = string.IsNullOrWhiteSpace(query)
            ? new List<OcrHistoryEntry>(allEntries)
            : allEntries.Where(e => GetOcrSearchText(e).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (entries.Count == 0)
        {
            if (allEntries.Count == 0)
                ShowHistoryEmptyState("No text captures yet", "OCR results will appear here after text capture.");
            else
                ShowHistoryEmptyState("No text captures match your search", "Search matched 0 text captures.");
        }
        else
        {
            HideHistoryEmptyState();
        }

        if (string.IsNullOrWhiteSpace(query))
            HistoryCountText.Text = $"{entries.Count} text capture{(entries.Count == 1 ? "" : "s")}";
        else
            HistoryCountText.Text = $"{entries.Count} of {allEntries.Count} text capture{(allEntries.Count == 1 ? "" : "s")}";

        _filteredOcrEntries = entries;
        _ocrRenderCount = Math.Min(HistoryInitialPageSize, _filteredOcrEntries.Count);
        _ocrLastRenderedDate = null;
        AppendOcrHistoryEntries(_filteredOcrEntries, 0, _ocrRenderCount);
        UpdateHistoryActionButtons();
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.load-text",
            $"items={_filteredOcrEntries.Count} rendered={_ocrRenderCount} query={!string.IsNullOrWhiteSpace(query)} elapsedMs={sw.ElapsedMilliseconds}");
    }

    private void LoadColorHistory()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ColorStack.Children.Clear();

        var allEntries = _historyService.ColorEntries;
        PruneColorSearchCache(allEntries);
        var query = _colorSearchQuery.Trim();
        var queryTerms = SplitHistorySearchTerms(query);
        List<ColorHistoryEntry> entries = string.IsNullOrWhiteSpace(query)
            ? new List<ColorHistoryEntry>(allEntries)
            : allEntries.Where(entry => ColorMatchesCachedTerms(entry, queryTerms)).ToList();

        if (entries.Count == 0)
        {
            if (allEntries.Count == 0)
                ShowHistoryEmptyState("No colors yet", "Picked colors will appear here.");
            else
                ShowHistoryEmptyState("No colors match your search", "Search matched 0 saved colors.");
        }
        else
        {
            HideHistoryEmptyState();
        }
        HistoryCountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{entries.Count} color{(entries.Count == 1 ? "" : "s")}"
            : $"{entries.Count} of {allEntries.Count} color{(allEntries.Count == 1 ? "" : "s")}";
        _filteredColorEntries = entries;
        _colorRenderCount = Math.Min(HistoryInitialPageSize, _filteredColorEntries.Count);
        _colorLastRenderedDate = null;
        AppendColorHistoryEntries(_filteredColorEntries, 0, _colorRenderCount);
        UpdateHistoryActionButtons();
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.load-colors",
            $"items={_filteredColorEntries.Count} rendered={_colorRenderCount} query={!string.IsNullOrWhiteSpace(query)} elapsedMs={sw.ElapsedMilliseconds}");
    }

    private void OcrPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 360) return;
        AppendNextOcrHistoryPage();
    }

    private void ColorsPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 360) return;
        AppendNextColorHistoryPage();
    }

    private void AppendNextOcrHistoryPage()
    {
        if (_ocrRenderCount >= _filteredOcrEntries.Count)
            return;

        var previousOffset = TextPanel.VerticalOffset;
        var previousCount = _ocrRenderCount;
        _ocrRenderCount = Math.Min(_ocrRenderCount + HistoryAppendPageSize, _filteredOcrEntries.Count);
        AppendOcrHistoryEntries(_filteredOcrEntries, previousCount, _ocrRenderCount - previousCount);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 1)
                TextPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AppendNextColorHistoryPage()
    {
        if (_colorRenderCount >= _filteredColorEntries.Count)
            return;

        var previousOffset = ColorsPanel.VerticalOffset;
        var previousCount = _colorRenderCount;
        _colorRenderCount = Math.Min(_colorRenderCount + HistoryAppendPageSize, _filteredColorEntries.Count);
        AppendColorHistoryEntries(_filteredColorEntries, previousCount, _colorRenderCount - previousCount);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 3)
                ColorsPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FlushOcrSearchDebounce(object? sender, EventArgs e)
    {
        _ocrSearchDebounceTimer.Stop();
        if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 1)
            LoadOcrHistory();
    }

    private void FlushColorSearchDebounce(object? sender, EventArgs e)
    {
        _colorSearchDebounceTimer.Stop();
        if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 3)
            LoadColorHistory();
    }

    private void AppendOcrHistoryEntries(IReadOnlyList<OcrHistoryEntry> entries, int start, int count)
    {
        var end = start + count;
        for (int i = start; i < end; i++)
        {
            var entry = entries[i];
            AppendSectionHeaderIfNeeded(OcrStack, entry.CapturedAt.Date, ref _ocrLastRenderedDate);
            OcrStack.Children.Add(GetOrCreateOcrHistoryCard(entry));
        }
    }

    private Border GetOrCreateOcrHistoryCard(OcrHistoryEntry entry)
    {
        if (_ocrHistoryCardCache.TryGetValue(entry, out var existing))
        {
            DetachElementFromParent(existing);
            if (!_selectMode)
                existing.Tag = false;
            RefreshSelectableCardSelection(existing);
            return existing;
        }

        var card = CreateOcrHistoryCard(entry);
        _ocrHistoryCardCache[entry] = card;
        return card;
    }

    private Border CreateOcrHistoryCard(OcrHistoryEntry entry)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 6),
            Background = HistoryCardIdleBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = true,
            ToolTip = "Copy this text history item",
            DataContext = entry
        };
        AutomationProperties.SetName(card, "Text history item");
        AutomationProperties.SetHelpText(card, "Press Enter or Space to copy this text item. In select mode, press Enter or Space to select it.");

        card.MouseEnter += (_, _) =>
        {
            card.Background = HistoryCardHoverBrush;
            card.BorderBrush = HistoryCardFocusBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            if (!card.IsKeyboardFocusWithin)
            {
                card.Background = HistoryCardIdleBrush;
                card.BorderBrush = Brushes.Transparent;
            }
        };
        card.GotKeyboardFocus += (_, _) =>
        {
            card.Background = HistoryCardHoverBrush;
            card.BorderBrush = HistoryCardFocusBrush;
        };
        card.LostKeyboardFocus += (_, _) =>
        {
            if (card.IsMouseOver)
                return;

            card.Background = HistoryCardIdleBrush;
            card.BorderBrush = Brushes.Transparent;
        };

        var capturedText = entry.Text;
        bool isLong = capturedText.Length > 220 || capturedText.Count(ch => ch == '\n') > 3;
        bool expanded = false;

        var textBlock = new TextBlock
        {
            Text = capturedText,
            FontSize = 12,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = isLong ? 74 : double.PositiveInfinity,
            ClipToBounds = true,
            Foreground = Theme.Brush(Theme.TextPrimary),
            Opacity = 0.92
        };
        textBlock.ToolTip = capturedText;
        AutomationProperties.SetName(textBlock, "Recognized text");
        AutomationProperties.SetHelpText(textBlock, capturedText);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var capturedTimeText = FormatTimeAgo(entry.CapturedAt);
        var capturedBlock = new TextBlock
        {
            Text = capturedTimeText,
            FontSize = 10,
            Opacity = 0.3,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Captured {capturedTimeText}"
        };
        AutomationProperties.SetName(capturedBlock, "Text capture time");
        AutomationProperties.SetHelpText(capturedBlock, capturedTimeText);
        footer.Children.Add(capturedBlock);

        var btnPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        Grid.SetColumn(btnPanel, 1);

        if (isLong)
        {
            var showMoreBtn = new Button
            {
                Content = "Show more",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Expand this text history item"
            };
            UpdateShowMoreTextButtonLabel(showMoreBtn, expanded);
            showMoreBtn.Click += (_, _) =>
            {
                expanded = !expanded;
                if (expanded)
                {
                    textBlock.MaxHeight = double.PositiveInfinity;
                    showMoreBtn.Content = "Show less";
                }
                else
                {
                    textBlock.MaxHeight = 74;
                    showMoreBtn.Content = "Show more";
                }

                UpdateShowMoreTextButtonLabel(showMoreBtn, expanded);
            };
            btnPanel.Children.Add(showMoreBtn);
        }

        var copyBtn = new Button
        {
            Content = "Copy all",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Copy all text"
        };
        AutomationProperties.SetName(copyBtn, "Copy text history item");
        AutomationProperties.SetHelpText(copyBtn, "Copy all text from this history item.");
        copyBtn.Click += (_, _) => CopyTextHistoryItem();

        void CopyTextHistoryItem()
        {
            try
            {
                ClipboardService.CopyTextToClipboard(capturedText);
                ToastWindow.Show("Copied", "Text copied");
            }
            catch (Exception ex)
            {
                ToastWindow.ShowError(
                    "Copy failed",
                    $"OddSnap could not copy this text history item. Try again from Settings -> History, or copy the visible text manually.\n{ex.Message}");
            }
        }
        btnPanel.Children.Add(copyBtn);
        footer.Children.Add(btnPanel);

        var textStack = new StackPanel();
        textStack.Children.Add(textBlock);
        textStack.Children.Add(footer);

        var badge = CreateSelectionBadge(false);
        var root = new Grid();
        root.Children.Add(textStack);
        root.Children.Add(badge);
        card.Child = root;

        card.Cursor = System.Windows.Input.Cursors.Hand;
        void ToggleSelection()
        {
            var selected = card.Tag is true;
            selected = !selected;
            card.Tag = selected;
            UpdateSelectableCardSelection(card, badge, selected);
            UpdateHistoryActionButtons();
        }

        card.MouseLeftButtonDown += (_, e) =>
        {
            if (!_selectMode)
                return;

            e.Handled = true;
            ToggleSelection();
        };

        card.KeyDown += (_, e) =>
        {
            if (!IsHistoryCardActivationKey(e))
                return;

            e.Handled = true;
            if (_selectMode)
                ToggleSelection();
            else
                CopyTextHistoryItem();
        };

        UpdateSelectableCardSelection(card, badge, selected: false);
        return card;
    }

    private void AppendColorHistoryEntries(IReadOnlyList<ColorHistoryEntry> entries, int start, int count)
    {
        var end = start + count;
        for (int i = start; i < end; i++)
        {
            var entry = entries[i];
            AppendSectionHeaderIfNeeded(ColorStack, entry.CapturedAt.Date, ref _colorLastRenderedDate);
            ColorStack.Children.Add(GetOrCreateColorHistoryCard(entry));
        }
    }

    private Border GetOrCreateColorHistoryCard(ColorHistoryEntry entry)
    {
        if (_colorHistoryCardCache.TryGetValue(entry, out var existing))
        {
            DetachElementFromParent(existing);
            if (!_selectMode)
                existing.Tag = null;
            RefreshSelectableCardSelection(existing);
            return existing;
        }

        var card = CreateColorHistoryCard(entry);
        _colorHistoryCardCache[entry] = card;
        return card;
    }

    private Border CreateColorHistoryCard(ColorHistoryEntry entry)
    {
        var hasValidColor = TryParseHexColor(entry.Hex, out var r, out var g, out var b);
        var displayHex = FormatColorHexForDisplay(entry.Hex);
        if (!hasValidColor)
        {
            AppDiagnostics.LogWarning(
                "history.color.invalid",
                $"Could not parse saved color value '{entry.Hex}'.");
        }

        var swatchColor = hasValidColor
            ? System.Windows.Media.Color.FromRgb(r, g, b)
            : System.Windows.Media.Color.FromArgb(0, 0, 0, 0);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 3),
            Background = HistoryCardIdleBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = true,
            ToolTip = "Copy this color value",
            DataContext = entry
        };
        AutomationProperties.SetName(card, $"Color history item {displayHex}");
        AutomationProperties.SetHelpText(card, "Press Enter or Space to copy this color. In select mode, press Enter or Space to select it.");

        card.MouseEnter += (_, _) =>
        {
            card.Background = HistoryCardHoverBrush;
            card.BorderBrush = HistoryCardFocusBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            if (!card.IsKeyboardFocusWithin)
            {
                card.Background = HistoryCardIdleBrush;
                card.BorderBrush = Brushes.Transparent;
            }
        };
        card.GotKeyboardFocus += (_, _) =>
        {
            card.Background = HistoryCardHoverBrush;
            card.BorderBrush = HistoryCardFocusBrush;
        };
        card.LostKeyboardFocus += (_, _) =>
        {
            if (card.IsMouseOver)
                return;

            card.Background = HistoryCardIdleBrush;
            card.BorderBrush = Brushes.Transparent;
        };

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
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = hasValidColor ? $"Color preview {displayHex}" : "Invalid color preview"
        };
        var swatchHelpText = hasValidColor
            ? $"Preview of saved color {displayHex}."
            : $"Saved color value {entry.Hex} could not be parsed.";
        AutomationProperties.SetName(swatch, $"Color swatch {displayHex}");
        AutomationProperties.SetHelpText(swatch, swatchHelpText);
        Grid.SetColumn(swatch, 0);
        grid.Children.Add(swatch);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var hexBlock = new TextBlock
        {
            Text = displayHex,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Theme.Brush(Theme.TextPrimary),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, Segoe UI Variable Text"),
            ToolTip = displayHex
        };
        AutomationProperties.SetName(hexBlock, "Color hex value");
        AutomationProperties.SetHelpText(hexBlock, displayHex);
        infoStack.Children.Add(hexBlock);
        var colorMetadataText = hasValidColor
            ? $"RGB({r}, {g}, {b}) · {FormatTimeAgo(entry.CapturedAt)}"
            : $"Invalid color · {FormatTimeAgo(entry.CapturedAt)}";
        var metadataBlock = new TextBlock
        {
            Text = colorMetadataText,
            FontSize = 10,
            Opacity = 0.35,
            Margin = new Thickness(0, 1, 0, 0),
            ToolTip = colorMetadataText
        };
        AutomationProperties.SetName(metadataBlock, "Color details");
        AutomationProperties.SetHelpText(metadataBlock, colorMetadataText);
        infoStack.Children.Add(metadataBlock);
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        var copyBtn = new Button
        {
            Content = "Copy",
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Copy this color value"
        };
        AutomationProperties.SetName(copyBtn, "Copy color value");
        AutomationProperties.SetHelpText(copyBtn, "Copy this color value to the clipboard.");
        var capturedHex = entry.Hex;
        var badge = CreateSelectionBadge(false);
        copyBtn.Click += (_, _) => CopyColorValue();

        void ToggleSelection()
        {
            var selected = card.Tag is ColorHistoryEntry;
            selected = !selected;
            card.Tag = selected ? entry : null;
            UpdateSelectableCardSelection(card, badge, selected);
            UpdateHistoryActionButtons();
        }

        void CopyColorValue()
        {
            try
            {
                ClipboardService.CopyTextToClipboard(capturedHex);
                ToastWindow.Show("Copied", capturedHex);
            }
            catch (Exception ex)
            {
                ToastWindow.ShowError(
                    "Copy failed",
                    $"OddSnap could not copy this color history item. Try again from Settings -> History, or copy the visible color value manually.\n{ex.Message}");
            }
        }
        Grid.SetColumn(copyBtn, 2);
        grid.Children.Add(copyBtn);

        var root = new Grid();
        root.Children.Add(grid);
        root.Children.Add(badge);
        card.Child = root;

        card.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (_selectMode)
            {
                ToggleSelection();
                return;
            }

            CopyColorValue();
        };

        card.KeyDown += (_, e) =>
        {
            if (!IsHistoryCardActivationKey(e))
                return;

            e.Handled = true;
            if (_selectMode)
                ToggleSelection();
            else
                CopyColorValue();
        };

        UpdateSelectableCardSelection(card, badge, selected: false);
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

    private bool ColorMatchesCachedTerms(ColorHistoryEntry entry, IReadOnlyList<string> terms)
    {
        var searchable = GetColorSearchText(entry);
        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SplitHistorySearchTerms(string query)
        => query.Split(new[] { ' ', '\t', ',', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string GetOcrSearchText(OcrHistoryEntry entry)
    {
        if (_ocrSearchTextCache.TryGetValue(entry, out var cached))
            return cached;

        _ocrSearchTextCache[entry] = entry.Text;
        return entry.Text;
    }

    private string GetColorSearchText(ColorHistoryEntry entry)
    {
        if (_colorSearchTextCache.TryGetValue(entry, out var cached))
            return cached;

        var searchText = BuildColorSearchText(entry);
        _colorSearchTextCache[entry] = searchText;
        return searchText;
    }

    private void PruneOcrSearchCache(IReadOnlyCollection<OcrHistoryEntry> currentEntries)
    {
        if (_ocrSearchTextCache.Count <= currentEntries.Count + 64 &&
            _ocrHistoryCardCache.Count <= currentEntries.Count + 64)
            return;

        var current = currentEntries.ToHashSet();
        foreach (var entry in _ocrSearchTextCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _ocrSearchTextCache.Remove(entry);
        foreach (var entry in _ocrHistoryCardCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _ocrHistoryCardCache.Remove(entry);
    }

    private void PruneColorSearchCache(IReadOnlyCollection<ColorHistoryEntry> currentEntries)
    {
        if (_colorSearchTextCache.Count <= currentEntries.Count + 64 &&
            _colorHistoryCardCache.Count <= currentEntries.Count + 64)
            return;

        var current = currentEntries.ToHashSet();
        foreach (var entry in _colorSearchTextCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _colorSearchTextCache.Remove(entry);
        foreach (var entry in _colorHistoryCardCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _colorHistoryCardCache.Remove(entry);
    }

    private static string BuildColorSearchText(ColorHistoryEntry entry)
    {
        var displayHex = FormatColorHexForDisplay(entry.Hex);
        if (!TryParseHexColor(entry.Hex, out var r, out var g, out var b))
            return string.IsNullOrWhiteSpace(entry.Hex)
                ? displayHex
                : string.Join(' ', entry.Hex, displayHex);

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entry.Hex,
            displayHex,
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

    private static bool IsHistoryCardActivationKey(KeyEventArgs e)
        => e.Key is Key.Enter or Key.Space;

    private static void UpdateShowMoreTextButtonLabel(Button button, bool expanded)
    {
        var name = expanded ? "Show less text" : "Show more text";
        var helpText = expanded
            ? "Collapse this text history item."
            : "Expand this text history item.";
        button.ToolTip = helpText;
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, helpText);
    }

    private static string FormatColorHexForDisplay(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "#";

        var normalized = hex.Trim();
        return normalized.StartsWith("#", StringComparison.Ordinal)
            ? normalized
            : "#" + normalized;
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

    private void UpdateSelectableCardSelection(Border card, Border badge, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? Theme.StrokeThickness : 0);
        card.BorderBrush = selected ? Theme.StrokeBrush() : System.Windows.Media.Brushes.Transparent;
        badge.Visibility = _selectMode || selected ? Visibility.Visible : Visibility.Collapsed;
        badge.Opacity = selected ? 1 : 0.45;
        UpdateSelectionBadgeAccessibility(badge, selected);
        if (badge.Tag is UIElement check)
            check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
    }
}
