using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OddSnap.Helpers;
using OddSnap.Services;
using ZXing;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private string _codeSearchQuery = "";
    private List<CodeHistoryEntry> _filteredCodeEntries = new();
    private int _codeRenderCount;
    private DateTime? _codeLastRenderedDate;
    private readonly Dictionary<CodeHistoryEntry, Border> _codeHistoryCardCache = new();
    private readonly Dictionary<CodeHistoryEntry, BitmapSource> _codePreviewCache = new();
    private readonly System.Windows.Threading.DispatcherTimer _codeSearchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };

    private void LoadCodeHistory()
    {
        var sw = Stopwatch.StartNew();
        CodeStack.Children.Clear();

        var allEntries = _historyService.CodeEntries;
        PruneCodeSearchCache(allEntries);

        var query = _codeSearchQuery.Trim();
        var queryTerms = SplitHistorySearchTerms(query);
        List<CodeHistoryEntry> entries = string.IsNullOrWhiteSpace(query)
            ? new List<CodeHistoryEntry>(allEntries)
            : allEntries.Where(entry => CodeMatchesCachedTerms(entry, queryTerms)).ToList();

        if (entries.Count == 0)
        {
            if (allEntries.Count == 0)
                ShowHistoryEmptyState("No QR/barcode scans yet", "Scanned codes will appear here.");
            else
                ShowHistoryEmptyState("No QR/barcode scans match your search", "Search matched 0 saved codes.");
        }
        else
        {
            HideHistoryEmptyState();
        }
        HistoryCountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{entries.Count} code{(entries.Count == 1 ? "" : "s")}"
            : $"{entries.Count} of {allEntries.Count} code{(allEntries.Count == 1 ? "" : "s")}";
        _filteredCodeEntries = entries;
        _codeRenderCount = Math.Min(HistoryInitialPageSize, _filteredCodeEntries.Count);
        _codeLastRenderedDate = null;
        AppendCodeHistoryEntries(_filteredCodeEntries, 0, _codeRenderCount);
        UpdateHistoryActionButtons();
        sw.Stop();
        AppDiagnostics.LogInfo(
            "history.load-codes",
            $"items={_filteredCodeEntries.Count} rendered={_codeRenderCount} query={!string.IsNullOrWhiteSpace(query)} elapsedMs={sw.ElapsedMilliseconds}");
    }

    private void CodesPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 360) return;
        AppendNextCodeHistoryPage();
    }

    private void AppendNextCodeHistoryPage()
    {
        if (_codeRenderCount >= _filteredCodeEntries.Count)
            return;

        var previousOffset = CodesPanel.VerticalOffset;
        var previousCount = _codeRenderCount;
        _codeRenderCount = Math.Min(_codeRenderCount + HistoryAppendPageSize, _filteredCodeEntries.Count);
        AppendCodeHistoryEntries(_filteredCodeEntries, previousCount, _codeRenderCount - previousCount);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 5)
                CodesPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FlushCodeSearchDebounce(object? sender, EventArgs e)
    {
        _codeSearchDebounceTimer.Stop();
        if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 5)
            LoadCodeHistory();
    }

    private void AppendCodeHistoryEntries(IReadOnlyList<CodeHistoryEntry> entries, int start, int count)
    {
        var end = start + count;
        for (int i = start; i < end; i++)
        {
            var entry = entries[i];
            AppendSectionHeaderIfNeeded(CodeStack, entry.CapturedAt.Date, ref _codeLastRenderedDate);
            CodeStack.Children.Add(GetOrCreateCodeHistoryCard(entry));
        }
    }

    private Border GetOrCreateCodeHistoryCard(CodeHistoryEntry entry)
    {
        if (_codeHistoryCardCache.TryGetValue(entry, out var existing))
        {
            DetachElementFromParent(existing);
            if (!_selectMode)
                existing.Tag = null;
            RefreshSelectableCardSelection(existing);
            return existing;
        }

        var card = CreateCodeHistoryCard(entry);
        _codeHistoryCardCache[entry] = card;
        return card;
    }

    private Border CreateCodeHistoryCard(CodeHistoryEntry entry)
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
            ToolTip = "Copy this QR/barcode text",
            DataContext = entry
        };

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

        var isQr = string.Equals(entry.Format, BarcodeFormat.QR_CODE.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Format, BarcodeFormat.AZTEC.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Format, BarcodeFormat.DATA_MATRIX.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entry.Format, BarcodeFormat.PDF_417.ToString(), StringComparison.OrdinalIgnoreCase);

        var preview = new System.Windows.Controls.Image
        {
            Width = isQr ? 56 : 88,
            Height = 56,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
        preview.Source = GetOrCreateCodePreview(entry);
        Grid.SetColumn(preview, 0);
        grid.Children.Add(preview);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var formatLabel = HumanizeBarcodeFormat(entry.Format);
        var isUrl = TryNormalizeUrl(entry.Text, out var url);
        AutomationProperties.SetName(card, $"{formatLabel} history item");
        AutomationProperties.SetHelpText(card, "Press Enter or Space to copy this QR/barcode text. In select mode, press Enter or Space to select it.");
        preview.ToolTip = $"{formatLabel} preview";
        AutomationProperties.SetName(preview, $"{formatLabel} preview");
        AutomationProperties.SetHelpText(preview, $"Preview image for this {formatLabel} history item.");

        var primary = new TextBlock
        {
            Text = entry.Text,
            FontSize = 12,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36,
            ClipToBounds = true,
            Foreground = Theme.Brush(Theme.TextPrimary),
            Opacity = 0.92
        };
        primary.ToolTip = entry.Text;
        AutomationProperties.SetName(primary, $"{formatLabel} text");
        AutomationProperties.SetHelpText(primary, entry.Text);
        infoStack.Children.Add(primary);

        var metadataText = $"{formatLabel} · {FormatTimeAgo(entry.CapturedAt)}";
        var metadata = new TextBlock
        {
            Text = metadataText,
            FontSize = 10,
            Opacity = 0.35,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = metadataText
        };
        AutomationProperties.SetName(metadata, "Code metadata");
        AutomationProperties.SetHelpText(metadata, metadataText);
        infoStack.Children.Add(metadata);

        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        var btnPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(btnPanel, 2);

        if (isUrl && !string.IsNullOrEmpty(url))
        {
            var openBtn = new Button
            {
                Content = "Open",
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Open this code URL"
            };
            AutomationProperties.SetName(openBtn, "Open code URL");
            AutomationProperties.SetHelpText(openBtn, "Open this QR/barcode URL in your default browser.");
            openBtn.Click += (_, _) => TryOpenExternalUrl(url);
            btnPanel.Children.Add(openBtn);
        }

        var copyBtn = new Button
        {
            Content = "Copy",
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Copy this QR/barcode text"
        };
        AutomationProperties.SetName(copyBtn, "Copy code text");
        AutomationProperties.SetHelpText(copyBtn, "Copy this QR/barcode text to the clipboard.");
        var capturedText = entry.Text;
        copyBtn.Click += (_, _) => CopyCodeText();
        btnPanel.Children.Add(copyBtn);
        grid.Children.Add(btnPanel);

        var badge = CreateSelectionBadge(false);
        var root = new Grid();
        root.Children.Add(grid);
        root.Children.Add(badge);
        card.Child = root;

        void ToggleSelection()
        {
            var selected = card.Tag is CodeHistoryEntry;
            selected = !selected;
            card.Tag = selected ? entry : null;
            UpdateSelectableCardSelection(card, badge, selected);
            UpdateHistoryActionButtons();
        }

        void CopyCodeText()
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
                    $"OddSnap could not copy this QR/barcode history item. Try again from Settings -> History, or copy the visible decoded value manually.\n{ex.Message}");
            }
        }

        card.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (_selectMode)
            {
                ToggleSelection();
                return;
            }

            CopyCodeText();
        };

        card.KeyDown += (_, e) =>
        {
            if (!IsHistoryCardActivationKey(e))
                return;

            e.Handled = true;
            if (_selectMode)
                ToggleSelection();
            else
                CopyCodeText();
        };

        UpdateSelectableCardSelection(card, badge, selected: false);
        return card;
    }

    private BitmapSource GetOrCreateCodePreview(CodeHistoryEntry entry)
    {
        if (_codePreviewCache.TryGetValue(entry, out var cached))
            return cached;

        try
        {
            var format = ParseBarcodeFormat(entry.Format);
            using var bmp = BarcodeService.RenderPreview(entry.Text, format);
            var src = BitmapPerf.ToBitmapSource(bmp);
            _codePreviewCache[entry] = src;
            return src;
        }
        catch
        {
            using var fallback = new Bitmap(64, 64);
            using var g = Graphics.FromImage(fallback);
            g.Clear(System.Drawing.Color.Transparent);
            var src = BitmapPerf.ToBitmapSource(fallback);
            _codePreviewCache[entry] = src;
            return src;
        }
    }

    private void PruneCodeSearchCache(IReadOnlyCollection<CodeHistoryEntry> currentEntries)
    {
        if (_codeSearchTextCache.Count <= currentEntries.Count + 64 &&
            _codeHistoryCardCache.Count <= currentEntries.Count + 64 &&
            _codePreviewCache.Count <= currentEntries.Count + 64)
            return;

        var current = currentEntries.ToHashSet();
        foreach (var entry in _codeSearchTextCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _codeSearchTextCache.Remove(entry);
        foreach (var entry in _codeHistoryCardCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _codeHistoryCardCache.Remove(entry);
        foreach (var entry in _codePreviewCache.Keys.Where(entry => !current.Contains(entry)).ToList())
            _codePreviewCache.Remove(entry);
    }

    private bool CodeMatchesCachedTerms(CodeHistoryEntry entry, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return true;

        var searchable = GetCodeSearchText(entry);
        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private string GetCodeSearchText(CodeHistoryEntry entry)
    {
        if (_codeSearchTextCache.TryGetValue(entry, out var cached))
            return cached;

        var searchText = BuildCodeSearchText(entry);
        _codeSearchTextCache[entry] = searchText;
        return searchText;
    }

    private static string BuildCodeSearchText(CodeHistoryEntry entry)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entry.Text,
            entry.Format,
            HumanizeBarcodeFormat(entry.Format)
        };

        if (TryNormalizeUrl(entry.Text, out var url) && !string.IsNullOrEmpty(url))
        {
            tokens.Add(url);
            tokens.Add("link");
            tokens.Add("url");
            try
            {
                var parsed = new Uri(url);
                tokens.Add(parsed.Host);
                tokens.Add(parsed.Scheme);
            }
            catch { }
        }

        switch (entry.Format?.ToUpperInvariant())
        {
            case "QR_CODE":
                tokens.Add("qr");
                tokens.Add("qrcode");
                break;
            case "AZTEC":
            case "DATA_MATRIX":
            case "PDF_417":
                tokens.Add("2d");
                tokens.Add("barcode");
                break;
            default:
                tokens.Add("barcode");
                tokens.Add("1d");
                break;
        }

        return string.Join(' ', tokens);
    }

    private static string HumanizeBarcodeFormat(string? format)
    {
        return format?.ToUpperInvariant() switch
        {
            "QR_CODE" => "QR Code",
            "AZTEC" => "Aztec",
            "DATA_MATRIX" => "Data Matrix",
            "PDF_417" => "PDF 417",
            "CODE_128" => "Code 128",
            "CODE_39" => "Code 39",
            "CODE_93" => "Code 93",
            "CODABAR" => "Codabar",
            "ITF" => "ITF",
            "EAN_13" => "EAN-13",
            "EAN_8" => "EAN-8",
            "UPC_A" => "UPC-A",
            "UPC_E" => "UPC-E",
            _ => string.IsNullOrWhiteSpace(format) ? "Code" : format
        };
    }

    private static BarcodeFormat ParseBarcodeFormat(string? format)
    {
        if (Enum.TryParse<BarcodeFormat>(format, ignoreCase: true, out var parsed))
            return parsed;
        return BarcodeFormat.QR_CODE;
    }

    private static bool TryNormalizeUrl(string text, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Contains(' ') || trimmed.Contains('\n'))
            return false;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            url = uri.AbsoluteUri;
            return true;
        }

        if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var withScheme))
        {
            url = withScheme.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool TryOpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open failed", "No code URL is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open failed", "The code URL is not a valid web link.");
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            if (process is null)
            {
                ToastWindow.ShowError("Open failed", "Windows did not open the code URL. Copy it from Settings -> History and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open failed",
                $"OddSnap could not open the code URL. Copy it from Settings -> History and open it manually.\n{ex.Message}");
            return false;
        }
    }
}
