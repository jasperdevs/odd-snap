using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;
using Image = System.Windows.Controls.Image;
using WpfPoint = System.Windows.Point;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private bool _selectMode;
    private List<HistoryItemVM> _historyItems = new();
    private List<HistoryItemVM> _filteredHistoryItems = new();
    private List<HistoryItemVM> _gifItems = new();
    private List<HistoryItemVM> _stickerItems = new();
    private List<HistoryItemVM> _allHistoryItems = new();
    private Dictionary<string, HistoryItemVM> _allHistoryItemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private List<HistoryItemVM> _allGifItems = new();
    private List<HistoryItemVM> _allStickerItems = new();
    private string _imageSearchQuery = "";
    private bool _suppressImageSearchSourceEvents;
    private CancellationTokenSource? _searchFilterCts;
    private int _searchFilterVersion;
    private string _lastImmediateSearchQuery = "";
    private ImageSearchSourceOptions _lastImmediateSearchSources = ImageSearchSourceOptions.None;
    private bool _lastImmediateSearchExactMatch;
    private List<HistoryItemVM> _lastImmediateSearchResults = new();
    private int _historyRenderCount;
    private int _gifRenderCount;
    private int _stickerRenderCount;
    private bool _imageSearchRowAutoHidden;
    private const int HistoryPageSize = 60;
    private const double HistoryCardFullWidth = 174d;
    private const double HistoryVirtualRowHeight = 156d;
    private const int HistoryVirtualRowBuffer = 3;
    private const int HistoryPrefetchRowBuffer = 2;
    private const int HistoryPrefetchLimit = 48;
    private bool _useVirtualizedImageHistory;
    private int _virtualizedHistoryColumns = 1;
    private int _virtualizedHistoryStartIndex = -1;
    private int _virtualizedHistoryEndIndex = -1;
    private Border? _historyTopSpacer;
    private Border? _historyBottomSpacer;
    private WrapPanel? _historyVirtualizedPanel;

    private bool ShouldUseVirtualizedImageHistory(IReadOnlyCollection<HistoryItemVM> items)
        => false;

    private sealed record PreparedHistoryItemData(
        HistoryEntry Entry,
        string ThumbPath,
        string Dimensions,
        string TimeAgo,
        string FileNameSearchText,
        string NormalizedFileNameSearchText,
        string SearchText,
        string NormalizedSearchText,
        string ImageSearchStatusText,
        string ImageSearchDiagnosticsText,
        string ImageSearchMatchText,
        bool IsSelected);

    private async void LoadHistory()
    {
        _historyLoadCts?.Cancel();
        _historyLoadCts?.Dispose();
        _historyLoadCts = new CancellationTokenSource();
        var cancellationToken = _historyLoadCts.Token;
        var version = ++_historyLoadVersion;
        _historyLoadInProgress = true;
        _deferHistoryMonitor = true;
        HistoryStack.Children.Clear();
        HistoryEmptyText.Visibility = Visibility.Collapsed;
        HistoryEmptyLabel.Text = "Loading captures...";
        HistoryCountText.Text = "Loading captures...";
        _imageSearchRowAutoHidden = false;
        _lastImmediateSearchQuery = "";
        _lastImmediateSearchSources = ImageSearchSourceOptions.None;
        _lastImmediateSearchExactMatch = false;
        _lastImmediateSearchResults = new List<HistoryItemVM>();

        try
        {
            var entries = _historyService.ImageEntries;
            var selectedPaths = _allHistoryItems
                .Where(i => i.IsSelected)
                .Select(i => i.Entry.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var searchEnabled = _settingsService.Settings.ShowImageSearchBar;
            var query = _imageSearchQuery;
            var sources = _settingsService.Settings.ImageSearchSources;
            var exactMatch = _settingsService.Settings.ImageSearchExactMatch;

            var prepared = await Task.Run(() =>
            {
                var rows = new List<PreparedHistoryItemData>(entries.Count);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileNameSearch = Path.GetFileNameWithoutExtension(entry.FileName);
                    var searchText = _imageSearchIndexService.BuildSearchText(entry.FilePath, entry.FileName);
                    var diagnostics = searchEnabled
                        ? _imageSearchIndexService.GetDiagnostics(entry.FilePath, entry.FileName, query, sources, exactMatch)
                        : new ImageSearchRecordDiagnostics();

                    rows.Add(new PreparedHistoryItemData(
                        entry,
                        entry.FilePath,
                        entry.Width > 0 ? $"{entry.Width} x {entry.Height}" : "",
                        FormatTimeAgo(entry.CapturedAt),
                        fileNameSearch,
                        ImageSearchQueryMatcher.Normalize(fileNameSearch),
                        searchText,
                        ImageSearchQueryMatcher.Normalize(searchText),
                        diagnostics.StatusText,
                        diagnostics.DetailsText,
                        diagnostics.MatchText,
                        selectedPaths.Contains(entry.FilePath)));
                }

                return rows;
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested || version != _historyLoadVersion || !IsLoaded)
                return;

            var previousItemsByPath = _allHistoryItemsByPath;
            _allHistoryItems = new List<HistoryItemVM>(prepared.Count);
            _allHistoryItemsByPath = new Dictionary<string, HistoryItemVM>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in prepared)
            {
                var vm = previousItemsByPath.TryGetValue(row.Entry.FilePath, out var existing)
                    ? existing
                    : new HistoryItemVM();

                vm.Card = null;
                vm.ThumbnailImage = null;
                vm.SelectionBadge = null;

                vm.Entry = row.Entry;
                vm.ThumbPath = row.ThumbPath;
                vm.Dimensions = row.Dimensions;
                vm.TimeAgo = row.TimeAgo;
                vm.FileNameSearchText = row.FileNameSearchText;
                vm.NormalizedFileNameSearchText = row.NormalizedFileNameSearchText;
                vm.SearchText = row.SearchText;
                vm.NormalizedSearchText = row.NormalizedSearchText;
                vm.OcrSearchText = "";
                vm.SemanticSearchText = "";
                vm.ImageSearchStatusText = row.ImageSearchStatusText;
                vm.ImageSearchDiagnosticsText = row.ImageSearchDiagnosticsText;
                vm.ImageSearchMatchText = row.ImageSearchMatchText;
                vm.IsSelected = row.IsSelected;
                if (vm.ThumbnailLoaded && IsStaleHistoryPlaceholder(vm.ThumbnailSource, row.Entry.Kind))
                {
                    vm.ThumbnailLoaded = false;
                    vm.ThumbnailSource = null;
                }
                if ((vm.ThumbnailSource is null || !vm.ThumbnailLoaded) &&
                    TryGetThumbFromCache(row.Entry.FilePath, out var cachedThumb))
                {
                    vm.ThumbnailSource = cachedThumb;
                    vm.ThumbnailLoaded = true;
                }

                _allHistoryItems.Add(vm);
                _allHistoryItemsByPath[row.Entry.FilePath] = vm;
            }

            ApplyImageSearchFilter();
            _historyImageCacheReady = true;
            PrimeHistoryFingerprint();
            DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
            if (_settingsService.Settings.AutoIndexImages)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 0)
                            _imageSearchIndexService.RequestSync(entries, _settingsService.Settings.OcrLanguageTag);
                    }
                    catch { }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (version == _historyLoadVersion)
            {
                _historyLoadInProgress = false;
                _deferHistoryMonitor = false;
                UpdateHistoryMonitorState();
                if (_pendingHistoryDiskRefresh || _pendingHistoryUiRefresh)
                {
                    _historyRefreshTimer.Stop();
                    _historyRefreshTimer.Start();
                }
            }
        }
    }

    private void RenderHistoryItems()
    {
        _useVirtualizedImageHistory = ShouldUseVirtualizedImageHistory(_filteredHistoryItems);
        if (_useVirtualizedImageHistory)
        {
            RenderVirtualizedHistoryItems(resetScrollPosition: true);
            return;
        }

        HistoryStack.Children.Clear();
        _historyItems = _filteredHistoryItems.Take(_historyRenderCount).ToList();
        AppendGroupedHistoryItems(HistoryStack, _historyItems, CreateHistoryCard);
        PrimeHistoryThumbnailLoads(_filteredHistoryItems.Take(_historyRenderCount + 12));
    }

    private void RenderVirtualizedHistoryItems(bool resetScrollPosition)
    {
        EnsureHistoryVirtualizedElements();
        _historyItems.Clear();
        _virtualizedHistoryStartIndex = -1;
        _virtualizedHistoryEndIndex = -1;

        if (resetScrollPosition)
            ImagesPanel.ScrollToVerticalOffset(0);

        UpdateVirtualizedHistoryViewport();
    }

    private void EnsureHistoryVirtualizedElements()
    {
        if (_historyTopSpacer is not null &&
            _historyBottomSpacer is not null &&
            _historyVirtualizedPanel is not null &&
            HistoryStack.Children.Count == 3 &&
            ReferenceEquals(HistoryStack.Children[0], _historyTopSpacer) &&
            ReferenceEquals(HistoryStack.Children[1], _historyVirtualizedPanel) &&
            ReferenceEquals(HistoryStack.Children[2], _historyBottomSpacer))
            return;

        _historyTopSpacer = new Border { Height = 0 };
        _historyVirtualizedPanel = new WrapPanel();
        _historyBottomSpacer = new Border { Height = 0 };

        HistoryStack.Children.Clear();
        HistoryStack.Children.Add(_historyTopSpacer);
        HistoryStack.Children.Add(_historyVirtualizedPanel);
        HistoryStack.Children.Add(_historyBottomSpacer);
    }

    private void UpdateVirtualizedHistoryViewport()
    {
        if (!_useVirtualizedImageHistory || _historyVirtualizedPanel is null || _historyTopSpacer is null || _historyBottomSpacer is null)
            return;

        var totalCount = _filteredHistoryItems.Count;
        if (totalCount == 0)
        {
            _historyVirtualizedPanel.Children.Clear();
            _historyTopSpacer.Height = 0;
            _historyBottomSpacer.Height = 0;
            _historyItems.Clear();
            return;
        }

        var availableWidth = ImagesPanel.ViewportWidth > 0 ? ImagesPanel.ViewportWidth : ImagesPanel.ActualWidth;
        var columns = Math.Max(1, (int)Math.Floor(Math.Max(HistoryCardFullWidth, availableWidth - 6) / HistoryCardFullWidth));
        _virtualizedHistoryColumns = columns;

        var totalRows = (int)Math.Ceiling(totalCount / (double)columns);
        var viewportHeight = ImagesPanel.ViewportHeight > 0 ? ImagesPanel.ViewportHeight : 600d;
        var visibleRows = Math.Max(1, (int)Math.Ceiling(viewportHeight / HistoryVirtualRowHeight));
        var firstVisibleRow = Math.Max(0, (int)Math.Floor(ImagesPanel.VerticalOffset / HistoryVirtualRowHeight));
        var startRow = Math.Max(0, firstVisibleRow - HistoryVirtualRowBuffer);
        var endRowExclusive = Math.Min(totalRows, firstVisibleRow + visibleRows + HistoryVirtualRowBuffer);
        var startIndex = Math.Min(totalCount, startRow * columns);
        var endIndex = Math.Min(totalCount, endRowExclusive * columns);

        if (startIndex == _virtualizedHistoryStartIndex && endIndex == _virtualizedHistoryEndIndex)
            return;

        _virtualizedHistoryStartIndex = startIndex;
        _virtualizedHistoryEndIndex = endIndex;
        _historyTopSpacer.Height = startRow * HistoryVirtualRowHeight;
        _historyBottomSpacer.Height = Math.Max(0, (totalRows - endRowExclusive) * HistoryVirtualRowHeight);

        var visibleItems = _filteredHistoryItems.Skip(startIndex).Take(endIndex - startIndex).ToList();
        _historyItems = visibleItems;
        _historyVirtualizedPanel.Children.Clear();
        foreach (var item in visibleItems)
            _historyVirtualizedPanel.Children.Add(GetOrCreateHistoryCard(item));

        var prefetchItems = visibleItems
            .Concat(_filteredHistoryItems.Skip(endIndex).Take(columns * HistoryPrefetchRowBuffer))
            .DistinctBy(item => item.Entry.FilePath)
            .ToList();
        PrimeHistoryThumbnailLoads(prefetchItems);
    }

    private static void PrimeHistoryThumbnailLoads(IEnumerable<HistoryItemVM> items)
    {
        int queued = 0;
        foreach (var item in items)
        {
            if (queued >= HistoryPrefetchLimit)
                break;

            if (item.ThumbnailLoaded && item.ThumbnailSource != null)
                continue;

            queued++;
            PrimeThumbLoad(item);
        }
    }

    private Border CreateHistoryCard(HistoryItemVM vm)
    {
        var shell = BuildMediaCardShell(vm, () =>
        {
            if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))
            {
                ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);
                ToastWindow.Show("Copied", vm.Entry.UploadUrl);
                return;
            }
            if (!File.Exists(vm.Entry.FilePath)) return;
            using var bmp = new Bitmap(vm.Entry.FilePath);
            Services.ClipboardService.CopyToClipboard(bmp);
            ToastWindow.Show("Copied", $"{vm.Dimensions} screenshot copied");
        });

        if (!string.IsNullOrEmpty(vm.Entry.UploadProvider))
        {
            var badge = CreateProviderBadge(vm.Entry.UploadProvider);
            if (badge != null) shell.ImageContainer.Children.Add(badge);
        }

        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = vm.Entry.FileName,
            FontSize = 11,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var visibleStatus = ShouldShowHistoryCardStatus(vm.ImageSearchStatusText) ? vm.ImageSearchStatusText : "";
        var timeAndStatus = string.IsNullOrWhiteSpace(visibleStatus)
            ? vm.TimeAgo
            : $"{vm.TimeAgo} · {visibleStatus}";
        shell.InfoPanel.Children.Add(new TextBlock
        {
            Text = timeAndStatus,
            FontSize = 10,
            FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
            Opacity = 0.3,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (_settingsService.Settings.ShowImageSearchDiagnostics)
        {
            if (!string.IsNullOrWhiteSpace(vm.ImageSearchMatchText))
            {
                shell.InfoPanel.Children.Add(new TextBlock
                {
                    Text = vm.ImageSearchMatchText,
                    FontSize = 9.5,
                    FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                    Opacity = 0.38,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (!string.IsNullOrWhiteSpace(vm.ImageSearchDiagnosticsText))
                shell.Card.ToolTip = vm.ImageSearchDiagnosticsText;
        }
        return shell.Card;
    }

    private Border GetOrCreateHistoryCard(HistoryItemVM vm)
    {
        if (vm.Card is Border existing)
        {
            DetachElementFromParent(existing);
            UpdateCardSelection(vm);
            RefreshCardThumbnail(vm);
            return existing;
        }

        return CreateHistoryCard(vm);
    }

    private static void RefreshCardThumbnail(HistoryItemVM vm)
    {
        if (vm.ThumbnailImage is not Image image)
            return;

        if (vm.ThumbnailLoaded && IsStaleHistoryPlaceholder(vm.ThumbnailSource, vm.Entry.Kind))
        {
            vm.ThumbnailLoaded = false;
            vm.ThumbnailSource = null;
        }

        if ((vm.ThumbnailSource is null || !vm.ThumbnailLoaded) &&
            TryGetThumbFromCache(vm.Entry.FilePath, out var cachedThumb))
        {
            vm.ThumbnailSource = cachedThumb;
            vm.ThumbnailLoaded = true;
        }

        image.Source = vm.ThumbnailSource ?? GetHistoryPlaceholder(vm.Entry.Kind);
        image.Opacity = 1;

        if (!vm.ThumbnailLoaded || vm.ThumbnailSource is null || IsStaleHistoryPlaceholder(vm.ThumbnailSource, vm.Entry.Kind))
            LoadThumbAsync(image, vm);
    }

    private static bool ShouldShowHistoryCardStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.Equals("OCR ready", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("Indexed", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("No text", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("OCR error", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("OCR failed", StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateCardSelection(HistoryItemVM vm)
    {
        if (vm.Card is null)
            return;

        if (vm.SelectionBadge != null)
            vm.SelectionBadge.Visibility = vm.IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSelectMode(object sender, RoutedEventArgs e)
    {
        _selectMode = !_selectMode;
        SelectBtn.Content = _selectMode ? "Done" : "Select";
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        LoadCurrentHistoryTab();
        UpdateImageSearchActionButtons();
    }

    private void DeleteAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CancelImageSearchWork();
            string tab = HistoryCategoryCombo.SelectedIndex == 0 ? "images"
                : HistoryCategoryCombo.SelectedIndex == 2 ? "videos/GIFs"
                : HistoryCategoryCombo.SelectedIndex == 1 ? "text history"
                : HistoryCategoryCombo.SelectedIndex == 3 ? "colors" : "stickers";
            if (MessageBox.Show($"Delete all {tab}?", "Confirm 1/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (MessageBox.Show($"Really delete all {tab}?", "Confirm 2/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (MessageBox.Show($"This cannot be undone. Delete all {tab}?", "Confirm 3/3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (HistoryCategoryCombo.SelectedIndex == 0) _historyService.ClearImages();
            else if (HistoryCategoryCombo.SelectedIndex == 2) DeleteMediaItems(_allGifItems);
            else if (HistoryCategoryCombo.SelectedIndex == 1) _historyService.ClearOcr();
            else if (HistoryCategoryCombo.SelectedIndex == 3) _historyService.ClearColors();
            else _historyService.ClearStickers();

            _selectMode = false;
            SelectBtn.Content = "Select";
            DeleteSelectedBtn.Visibility = Visibility.Collapsed;

            LoadCurrentHistoryTab();
            UpdateImageSearchActionButtons();
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Delete failed", ex.Message);
        }
    }

    private void DeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CancelImageSearchWork();
            _selectMode = false;
            SelectBtn.Content = "Select";
            DeleteSelectedBtn.Visibility = Visibility.Collapsed;

            if (HistoryCategoryCombo.SelectedIndex == 0)
            {
                var toDelete = _historyItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
                _historyService.DeleteEntries(toDelete);
            }
            else if (HistoryCategoryCombo.SelectedIndex == 2)
            {
                DeleteMediaItems(_gifItems.Where(i => i.IsSelected).ToList());
            }
            else if (HistoryCategoryCombo.SelectedIndex == 1)
            {
                var toDelete = OcrStack.Children.OfType<Border>()
                    .Where(b => b.Tag is true)
                    .ToList();
                // Map selected cards to their OcrHistoryEntry by matching text content
                var allEntries = _historyService.OcrEntries;
                var entriesToDelete = new List<OcrHistoryEntry>();
                foreach (var card in toDelete)
                {
                    if (card.Child is Grid root && root.Children.OfType<StackPanel>().FirstOrDefault() is { } stack)
                    {
                        var textBox = stack.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
                        if (textBox != null)
                        {
                            var match = allEntries.FirstOrDefault(e =>
                                e.Text == textBox.Text || e.Text.StartsWith(textBox.Text.TrimEnd('.', ' ')));
                            if (match != null) entriesToDelete.Add(match);
                        }
                    }
                }
                _historyService.DeleteOcrEntries(entriesToDelete);
            }
            else if (HistoryCategoryCombo.SelectedIndex == 3)
            {
                var toDelete = ColorStack.Children.OfType<Border>()
                    .Select(s => s.Tag).OfType<ColorHistoryEntry>().ToList();
                _historyService.DeleteColorEntries(toDelete);
            }
            else if (HistoryCategoryCombo.SelectedIndex == 4)
            {
                var toDelete = _stickerItems.Where(i => i.IsSelected).Select(i => i.Entry).ToList();
                _historyService.DeleteEntries(toDelete);
            }

            LoadCurrentHistoryTab();
            UpdateImageSearchActionButtons();
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Delete failed", ex.Message);
        }
    }

    private void LoadStickerHistory()
    {
        StickerStack.Children.Clear();

        var entries = _historyService.StickerEntries;
        long totalBytes = 0;
        foreach (var e in entries)
            totalBytes += e.FileSizeBytes > 0 ? e.FileSizeBytes : TryGetFileLength(e.FilePath);
        var sizeStr = FormatStorageSize(totalBytes);
        HistoryCountText.Text = $"{entries.Count} sticker{(entries.Count == 1 ? "" : "s")} · {sizeStr}";
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyLabel.Text = "No stickers yet";

        _allStickerItems = entries.Select(e => new HistoryItemVM
        {
            Entry = e,
            ThumbPath = e.FilePath,
            Dimensions = e.Width > 0 ? $"{e.Width} x {e.Height}" : "",
            TimeAgo = FormatTimeAgo(e.CapturedAt)
        }).ToList();

        _stickerRenderCount = Math.Min(HistoryPageSize, _allStickerItems.Count);
        RenderStickerItems();
        DeleteSelectedBtn.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderStickerItems()
    {
        StickerStack.Children.Clear();
        _stickerItems = _allStickerItems.Take(_stickerRenderCount).ToList();
        AppendGroupedHistoryItems(StickerStack, _stickerItems, CreateHistoryCard);
        PrimeHistoryThumbnailLoads(_stickerItems);
    }

    private void StickerPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_stickerRenderCount >= _allStickerItems.Count) return;
        var previousCount = _stickerRenderCount;
        _stickerRenderCount = Math.Min(_stickerRenderCount + HistoryPageSize, _allStickerItems.Count);
        var appended = _allStickerItems.Skip(previousCount).Take(_stickerRenderCount - previousCount).ToList();
        _stickerItems.AddRange(appended);
        AppendGroupedHistoryItems(StickerStack, appended, CreateHistoryCard);
        PrimeHistoryThumbnailLoads(_allStickerItems.Take(Math.Min(_stickerRenderCount + 12, _allStickerItems.Count)));
    }

    private void AppendGroupedHistoryItems(System.Windows.Controls.Panel target, IEnumerable<HistoryItemVM> items, Func<HistoryItemVM, Border> cardFactory)
    {
        WrapPanel? currentWrap = target.Children.Count > 0 ? target.Children[target.Children.Count - 1] as WrapPanel : null;
        DateTime? currentDate = currentWrap?.Tag is DateTime tagDate ? tagDate : null;

        foreach (var item in items)
        {
            var itemDate = item.Entry.CapturedAt.Date;
            if (currentWrap is null || currentDate != itemDate)
            {
                if (target.Children.Count > 0)
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
                    Text = FormatHistoryGroupLabel(itemDate),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new System.Windows.Media.FontFamily(UiChrome.PreferredFamilyName),
                    Foreground = Theme.Brush(Theme.TextPrimary),
                    Opacity = 0.45,
                    Margin = new Thickness(6, 12, 0, 6)
                });

                currentWrap = new WrapPanel { Tag = itemDate };
                target.Children.Add(currentWrap);
                currentDate = itemDate;
            }

            currentWrap.Children.Add(cardFactory(item));
        }
    }

    private static string FormatHistoryGroupLabel(DateTime date) =>
        date == DateTime.Today ? "Today"
        : date == DateTime.Today.AddDays(-1) ? "Yesterday"
        : date.ToString("MMMM d, yyyy");

    private static long TryGetFileLength(string filePath)
    {
        try { return new FileInfo(filePath).Length; }
        catch { return 0; }
    }
}
