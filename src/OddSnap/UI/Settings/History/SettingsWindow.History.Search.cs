using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void RefreshImageSearchTexts()
    {
        RefreshImageSearchTexts(_allHistoryItems);
    }

    private void RefreshImageSearchTexts(IEnumerable<HistoryItemVM> items)
    {
        foreach (var item in items)
        {
            HydrateHistoryItemSearchMetadata(item);
            RefreshHistoryCardTextMetadata(item);
        }

        UpdateImageSearchPlaceholderText();
    }

    private void ApplyImageSearchFilter()
    {
        if (HistoryCategoryCombo.SelectedIndex != 0)
            return;

        var sources = _settingsService.Settings.ImageSearchSources;
        var exactMatch = _settingsService.Settings.ImageSearchExactMatch;

        if (!_settingsService.Settings.ShowImageSearchBar)
        {
            CancelImageSearchWork();
            ApplyImmediateImageFilter("", sources, exactMatch);
            return;
        }

        var query = _imageSearchQuery.Trim();
        CancelImageSearchWork();
        if (string.IsNullOrWhiteSpace(query) || sources == ImageSearchSourceOptions.None)
        {
            EnsureMaterializedImageHistoryItems(_historyRenderCount <= 0 ? ImageHistoryPageSize : _historyRenderCount);
            ApplyImmediateImageFilter(query, sources, exactMatch);
            SetImageSearchLoading(false, forceIndexed: true);
            return;
        }

        // Show a lightweight local result set immediately, then refine with the indexed search.
        ApplyImmediateImageFilter(query, sources, exactMatch);
        SetImageSearchLoading(true, forceIndexed: true);
        _searchFilterCts = new CancellationTokenSource();
        _ = ApplyIndexedImageSearchAsync(++_searchFilterVersion, query, sources, _searchFilterCts.Token);
    }

    private void ApplyImmediateImageFilter(string query, ImageSearchSourceOptions sources, bool exactMatch)
    {
        var rankedItems = ApplyHistoryUploadFilter(RankLocalImageItems(query, sources, exactMatch)).ToList();
        var filteredItems = FilterSearchResultsForLoadedThumbnails(rankedItems, query);
        var shouldVirtualize = ShouldUseVirtualizedImageHistory(filteredItems);
        var renderModeChanged = _useVirtualizedImageHistory != shouldVirtualize;
        var resultSetChanged = !HasSameHistorySequence(_filteredHistoryItems, filteredItems);
        _filteredHistoryItems = filteredItems;
        var desiredRenderCount = string.IsNullOrWhiteSpace(query)
            ? Math.Max(_historyRenderCount, ImageHistoryPageSize)
            : HistoryAppendPageSize;
        _historyRenderCount = Math.Min(desiredRenderCount, _filteredHistoryItems.Count);

        long visibleBytes = 0;
        foreach (var item in _filteredHistoryItems)
            visibleBytes += GetHistoryItemFileSize(item);

        var uploadFilterActive = IsHistoryUploadFilterActive();
        var searchEnabled = sources != ImageSearchSourceOptions.None;
        var usingSearch = searchEnabled && !string.IsNullOrWhiteSpace(query);
        var usingSearchAndUploadFilter = usingSearch && uploadFilterActive;
        var pendingThumbnailMatches = usingSearch && rankedItems.Count > 0 && filteredItems.Count == 0;
        var pendingThumbnailMatchCount = usingSearch
            ? Math.Max(0, rankedItems.Count - filteredItems.Count)
            : 0;
        var sizeStr = FormatStorageSize(visibleBytes);
        var totalCount = _allImageHistoryEntries.Count;
        if (usingSearch && pendingThumbnailMatchCount > 0)
        {
            HistoryCountText.Text = FormatImageSearchVisibleCountText(_filteredHistoryItems.Count, rankedItems.Count, sizeStr);
        }
        else if (usingSearch)
        {
            HistoryCountText.Text = FormatImageSearchMatchCountText(_filteredHistoryItems.Count, uploadFilterActive, sizeStr);
        }
        else if (uploadFilterActive)
        {
            HistoryCountText.Text = FormatImageUploadFilterCountText(_filteredHistoryItems.Count, totalCount, sizeStr);
        }
        else
        {
            var loadedCount = _filteredHistoryItems.Count;
            var loadedPrefix = totalCount > loadedCount
                ? $"{loadedCount} of {totalCount} captures loaded"
                : $"{loadedCount} capture{(loadedCount == 1 ? "" : "s")}";
            HistoryCountText.Text = $"{loadedPrefix} · {sizeStr}";
        }

        if (_filteredHistoryItems.Count == 0)
        {
            if (!searchEnabled && !string.IsNullOrWhiteSpace(query))
                ShowHistoryEmptyState("Search sources are off", "Enable file name or OCR search to use this query.");
            else if (pendingThumbnailMatches)
                ShowHistoryEmptyState("Loading matching screenshots", "Thumbnail previews are loading. Results will appear shortly.");
            else if (usingSearchAndUploadFilter)
                ShowHistoryEmptyState("No screenshots match this search and filter", "Search and upload filters matched 0 saved screenshots.");
            else if (usingSearch)
                ShowHistoryEmptyState("No screenshots match your search", "Search matched 0 saved screenshots.");
            else if (uploadFilterActive)
                ShowHistoryEmptyState("No screenshots match this filter", "Filter matched 0 saved screenshots.");
            else
                ShowHistoryEmptyState("No captures yet", "Screenshots will appear here after capture.");
        }
        else
        {
            HideHistoryEmptyState();
        }

        if (resultSetChanged || renderModeChanged)
            RenderHistoryItems();
        else if (_useVirtualizedImageHistory)
            UpdateVirtualizedHistoryViewport();
        UpdateImageSearchStatus();
        UpdateImageSearchActionButtons();
        UpdateHistoryActionButtons();
    }

    private List<HistoryItemVM> RankLocalImageItems(string query, ImageSearchSourceOptions sources, bool exactMatch)
    {
        var normalizedQuery = ImageSearchQueryMatcher.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            if (IsHistoryUploadFilterActive())
                EnsureAllImageHistoryItemsMaterialized();
            else
                EnsureMaterializedImageHistoryItems(_historyRenderCount <= 0 ? ImageHistoryPageSize : _historyRenderCount);
            var fullList = _allHistoryItems.ToList();
            RememberImmediateSearch(normalizedQuery, sources, exactMatch, fullList);
            return fullList;
        }

        var allowFileName = sources.HasFlag(ImageSearchSourceOptions.FileName);
        var allowOcr = sources.HasFlag(ImageSearchSourceOptions.Ocr);
        if (!allowFileName && !allowOcr)
        {
            EnsureMaterializedImageHistoryItems(_historyRenderCount <= 0 ? ImageHistoryPageSize : _historyRenderCount);
            var fullList = _allHistoryItems.ToList();
            RememberImmediateSearch(normalizedQuery, sources, exactMatch, fullList);
            return fullList;
        }

        EnsureAllImageHistoryItemsMaterialized();
        IEnumerable<HistoryItemVM> candidateItems = _allHistoryItems;
        if (CanReuseImmediateSearchScope(normalizedQuery, sources, exactMatch))
            candidateItems = _lastImmediateSearchResults;

        if (allowOcr && candidateItems.Count() < HistoryVirtualizationThreshold)
            HydrateHistoryItemsForSearch(candidateItems);

        var rankedItems = candidateItems
            .Select(item => new
            {
                Item = item,
                Score = ScoreLocalImageItem(
                    normalizedQuery,
                    item,
                    allowFileName,
                    allowOcr && item.SearchMetadataHydrated,
                    exactMatch)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Entry.CapturedAt)
            .Select(x => x.Item)
            .ToList();

        RememberImmediateSearch(normalizedQuery, sources, exactMatch, rankedItems);
        return rankedItems;
    }

    private static int ScoreLocalImageItem(string normalizedQuery, HistoryItemVM item, bool allowFileName, bool allowOcr, bool exactMatch)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return 1;

        var searchableText = allowOcr ? item.NormalizedSearchText : "";
        var fileName = allowFileName ? item.NormalizedFileNameSearchText : "";
        return ImageSearchQueryMatcher.ScorePreNormalized(normalizedQuery, searchableText, fileName, exactMatch);
    }

    private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)
    {
        bool searchFailed = false;
        try
        {
            var exactMatch = _settingsService.Settings.ImageSearchExactMatch;
            if (string.IsNullOrWhiteSpace(query) || sources == ImageSearchSourceOptions.None)
                return;

            var entries = _historyService.ImageEntries;
            var rankedEntries = await Task.Run(
                () => _imageSearchIndexService.SearchAsync(
                    entries,
                    query,
                    sources,
                    exactMatch,
                    cancellationToken),
                cancellationToken);

            if (!IsLoaded || version != _searchFilterVersion || cancellationToken.IsCancellationRequested)
                return;

            EnsureAllImageHistoryItemsMaterialized();
            var filtered = new List<HistoryItemVM>(rankedEntries.Count);
            foreach (var entry in rankedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_allHistoryItemsByPath.TryGetValue(entry.FilePath, out var vm))
                    continue;

                filtered.Add(vm);
            }

            if (!IsLoaded || version != _searchFilterVersion || cancellationToken.IsCancellationRequested)
                return;

            var uploadFilteredItems = ApplyHistoryUploadFilter(filtered).ToList();
            var filteredItems = FilterSearchResultsForLoadedThumbnails(uploadFilteredItems, query);
            var pendingThumbnailMatches = uploadFilteredItems.Count > 0 && filteredItems.Count == 0;
            var pendingThumbnailMatchCount = Math.Max(0, uploadFilteredItems.Count - filteredItems.Count);
            var uploadFilterActive = IsHistoryUploadFilterActive();
            long visibleBytes = 0;
            foreach (var item in filteredItems)
                visibleBytes += GetHistoryItemFileSize(item);

            var shouldVirtualize = ShouldUseVirtualizedImageHistory(filteredItems);
            var renderModeChanged = _useVirtualizedImageHistory != shouldVirtualize;
            var resultSetChanged = !HasSameHistorySequence(_filteredHistoryItems, filteredItems);
            _filteredHistoryItems = filteredItems;
            _historyRenderCount = Math.Min(HistoryAppendPageSize, _filteredHistoryItems.Count);

            var sizeStr = FormatStorageSize(visibleBytes);
            HistoryCountText.Text = pendingThumbnailMatchCount > 0
                ? FormatImageSearchVisibleCountText(_filteredHistoryItems.Count, uploadFilteredItems.Count, sizeStr)
                : FormatImageSearchMatchCountText(_filteredHistoryItems.Count, uploadFilterActive, sizeStr);

            if (_filteredHistoryItems.Count == 0 && pendingThumbnailMatches)
                ShowHistoryEmptyState("Loading matching screenshots", "Thumbnail previews are loading. Results will appear shortly.");
            else if (_filteredHistoryItems.Count == 0 && uploadFilterActive)
                ShowHistoryEmptyState("No screenshots match this search and filter", "Search and upload filters matched 0 saved screenshots.");
            else if (_filteredHistoryItems.Count == 0)
                ShowHistoryEmptyState("No screenshots match your search", "Search matched 0 saved screenshots.");
            else
                HideHistoryEmptyState();

            if (resultSetChanged || renderModeChanged)
                RenderHistoryItems();
            else if (_useVirtualizedImageHistory)
                UpdateVirtualizedHistoryViewport();
            UpdateImageSearchStatus();
            SetImageSearchLoading(false, forceIndexed: true);
            UpdateImageSearchActionButtons();
            UpdateHistoryActionButtons();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            searchFailed = true;
            AppDiagnostics.LogError("settings.image-search", ex);
        }
        finally
        {
            if (version == _searchFilterVersion)
                SetImageSearchLoading(false, forceIndexed: true);

            if (version == _searchFilterVersion && searchFailed)
                HistorySearchStatusText.Text = "Search failed";
        }
    }

    private static string FormatImageSearchVisibleCountText(int visibleCount, int matchedCount, string sizeText)
    {
        var matchLabel = matchedCount == 1 ? "match" : "matches";
        return $"{visibleCount} visible of {matchedCount} {matchLabel} · {sizeText}";
    }

    private static string FormatImageSearchMatchCountText(int matchedCount, bool uploadFilterActive, string sizeText)
    {
        var matchLabel = matchedCount == 1 ? "match" : "matches";
        var sourceLabel = uploadFilterActive ? "search/filter" : "search";
        return $"{matchedCount} {sourceLabel} {matchLabel} · {sizeText}";
    }

    private static string FormatImageUploadFilterCountText(int filteredCount, int totalCount, string sizeText)
    {
        var captureLabel = totalCount == 1 ? "capture" : "captures";
        return $"{filteredCount} of {totalCount} {captureLabel} shown by filter · {sizeText}";
    }

    private List<HistoryItemVM> FilterSearchResultsForLoadedThumbnails(List<HistoryItemVM> rankedItems, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return rankedItems;

        var visible = new List<HistoryItemVM>(rankedItems.Count);
        int queued = 0;
        foreach (var item in rankedItems)
        {
            if (item.ThumbnailLoaded && item.ThumbnailSource != null)
            {
                visible.Add(item);
                continue;
            }

            if (queued < 48)
            {
                queued++;
                PrimeThumbLoad(item, () =>
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (!IsLoaded || HistoryTab.IsChecked != true || HistoryCategoryCombo.SelectedIndex != 0)
                            return;

                        if (string.IsNullOrWhiteSpace(_imageSearchQuery))
                            return;

                        QueueImageSearchRefresh();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                });
            }
        }

        return visible;
    }

    private static bool HasSameHistorySequence(IReadOnlyList<HistoryItemVM> left, IReadOnlyList<HistoryItemVM> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!left[i].Entry.FilePath.Equals(right[i].Entry.FilePath, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void UpdateImageSearchStatus()
    {
        if (HistoryCategoryCombo.SelectedIndex != 0)
        {
            HistorySearchStatusText.Text = "";
            return;
        }

        if (!_settingsService.Settings.ShowImageSearchBar)
        {
            HistorySearchStatusText.Text = "";
            return;
        }

        var status = _imageSearchIndexService.StatusText;
        if (status.StartsWith("Indexing screenshots", StringComparison.OrdinalIgnoreCase))
        {
            HistorySearchStatusText.Text = status;
            return;
        }

        if (_settingsService.Settings.ImageSearchExactMatch)
        {
            HistorySearchStatusText.Text = "";
            return;
        }

        var sources = _settingsService.Settings.ImageSearchSources;
        if (sources == ImageSearchSourceOptions.None)
        {
            HistorySearchStatusText.Text = "Search off";
            return;
        }

        if (string.IsNullOrWhiteSpace(_imageSearchQuery))
        {
            HistorySearchStatusText.Text = "";
            return;
        }

        HistorySearchStatusText.Text = "";
    }

    private void SetImageSearchLoading(bool isLoading, bool forceIndexed = false)
    {
        ImageSearchLoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryCategoryCombo.SelectedIndex == 0)
            UpdateImageSearchStatus();
    }

    private void CancelImageSearchWork()
    {
        _imageSearchDebounceTimer.Stop();
        _searchFilterCts?.Cancel();
        _searchFilterCts?.Dispose();
        _searchFilterCts = null;
        SetImageSearchLoading(false, forceIndexed: true);
    }

    private bool CanReuseImmediateSearchScope(string normalizedQuery, ImageSearchSourceOptions sources, bool exactMatch)
    {
        return !string.IsNullOrWhiteSpace(_lastImmediateSearchQuery) &&
               sources == _lastImmediateSearchSources &&
               exactMatch == _lastImmediateSearchExactMatch &&
               normalizedQuery.StartsWith(_lastImmediateSearchQuery, StringComparison.Ordinal);
    }

    private void RememberImmediateSearch(string normalizedQuery, ImageSearchSourceOptions sources, bool exactMatch, List<HistoryItemVM> results)
    {
        _lastImmediateSearchQuery = normalizedQuery;
        _lastImmediateSearchSources = sources;
        _lastImmediateSearchExactMatch = exactMatch;
        _lastImmediateSearchResults = results;
    }

    private void UpdateImageSearchUi()
    {
        var isImages = HistoryCategoryCombo.SelectedIndex == 0;
        var isText = HistoryCategoryCombo.SelectedIndex == 1;
        var isColors = HistoryCategoryCombo.SelectedIndex == 3;
        var isCodes = HistoryCategoryCombo.SelectedIndex == 5;
        var showSearch = (isImages && _settingsService.Settings.ShowImageSearchBar && !_imageSearchRowAutoHidden) ||
                         isText ||
                         isColors ||
                         isCodes;
        ImageSearchRow.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;
        ImageSearchFiltersBtn.Visibility = isImages ? Visibility.Visible : Visibility.Collapsed;
        if (!isImages)
            ImageSearchLoadingBar.Visibility = Visibility.Collapsed;
        if (showSearch)
        {
            if (isImages)
                LoadImageSearchSources();

            var expectedText = isImages
                ? _imageSearchQuery
                : isText
                    ? _ocrSearchQuery
                    : isColors
                        ? _colorSearchQuery
                        : _codeSearchQuery;
            if (!string.Equals(ImageSearchBox.Text, expectedText, StringComparison.Ordinal))
            {
                _suppressHistorySearchBoxTextEvents = true;
                try { ImageSearchBox.Text = expectedText; }
                finally { _suppressHistorySearchBoxTextEvents = false; }
            }

            UpdateImageSearchPlaceholderText();
            ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(ImageSearchBox.Text) && !ImageSearchBox.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else
        {
            if (isImages)
                CancelImageSearchWork();
            HistorySearchStatusText.Text = "";
            ImageSearchPlaceholder.Visibility = Visibility.Collapsed;
        }

        UpdateImageSearchActionButtons();
        UpdateImageSearchPlaceholderText();
        UpdateHistoryActionButtons();
    }

    private void SetImageSearchRowAutoHidden(bool hidden)
    {
        if (_imageSearchRowAutoHidden == hidden)
            return;

        _imageSearchRowAutoHidden = hidden;
        UpdateImageSearchUi();
    }

}
