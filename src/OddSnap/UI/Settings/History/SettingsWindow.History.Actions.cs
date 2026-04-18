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
    private void UpdateImageSearchActionButtons()
    {
        if (!IsLoaded)
            return;

        var isImages = HistoryCategoryCombo.SelectedIndex == 0;
        var status = _imageSearchIndexService.StatusText;
        var isIndexing = status.StartsWith("Indexing screenshots", StringComparison.OrdinalIgnoreCase);

        ReindexAllProgressBar.Visibility = isIndexing ? Visibility.Visible : Visibility.Collapsed;

        var entries = _allImageHistoryEntries.Count > 0
            ? _allImageHistoryEntries
            : _historyService.ImageEntries;
        var ocrTag = _settingsService.Settings.OcrLanguageTag;
        int total = entries.Count;

        if (isIndexing)
        {
            ReindexAllProgressPanel.Visibility = Visibility.Visible;
            ReindexAllBtn.Content = status;
            ReindexAllBtn.IsEnabled = false;
        }
        else if (total >= HistoryVirtualizationThreshold)
        {
            ReindexAllProgressPanel.Visibility = Visibility.Collapsed;
            ReindexAllBtn.Content = "Refresh index";
            ReindexAllBtn.IsEnabled = total > 0;
        }
        else
        {
            int indexed = _imageSearchIndexService.CountReadyEntries(entries, ocrTag);
            if (indexed < total)
            {
                ReindexAllProgressPanel.Visibility = Visibility.Visible;
                ReindexAllBtn.Content = $"Index {total - indexed} remaining";
                ReindexAllBtn.IsEnabled = true;
            }
            else
            {
                ReindexAllProgressPanel.Visibility = Visibility.Collapsed;
                ReindexAllBtn.Content = $"{indexed}/{total} indexed";
                ReindexAllBtn.IsEnabled = false;
            }
        }
    }

    private void UpdateImageSearchPlaceholderText()
    {
        if (!IsLoaded)
            return;

        var isIndexing = _imageSearchIndexService.StatusText.StartsWith("Indexing screenshots", StringComparison.OrdinalIgnoreCase);
        ImageSearchPlaceholder.Text = isIndexing
            ? "Search screenshots (indexing...)"
            : "Search screenshots";
    }

    private void UpdateImageSearchSourceSummary()
    {
        var parts = new List<string>(3);
        if (ImageSearchFileNameCheck.IsChecked)
            parts.Add("Name");
        if (ImageSearchOcrCheck.IsChecked)
            parts.Add("OCR");
        if (ImageSearchExactMatchCheck.IsChecked)
            parts.Add("Exact");

        ImageSearchFiltersSummaryText.Text = parts.Count == 0 ? "None" : string.Join(", ", parts);
    }

    private void LoadImageSearchSources()
    {
        var sources = _settingsService.Settings.ImageSearchSources;
        _suppressImageSearchSourceEvents = true;
        try
        {
            ImageSearchFileNameCheck.IsChecked = (sources & ImageSearchSourceOptions.FileName) != 0;
            ImageSearchOcrCheck.IsChecked = (sources & ImageSearchSourceOptions.Ocr) != 0;
            ImageSearchExactMatchCheck.IsChecked = _settingsService.Settings.ImageSearchExactMatch;
        }
        finally
        {
            _suppressImageSearchSourceEvents = false;
        }

        UpdateImageSearchSourceSummary();
    }

    private ImageSearchSourceOptions GetImageSearchSourcesFromUi()
    {
        var sources = ImageSearchSourceOptions.None;
        if (ImageSearchFileNameCheck.IsChecked)
            sources |= ImageSearchSourceOptions.FileName;
        if (ImageSearchOcrCheck.IsChecked)
            sources |= ImageSearchSourceOptions.Ocr;
        return sources;
    }

    private void ImageSearchExactMatchCheck_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsLoaded)
                return;

            _settingsService.Settings.ImageSearchExactMatch = ImageSearchExactMatchCheck.IsChecked == true;
            _settingsService.Save();

            UpdateImageSearchSourceSummary();
            CancelImageSearchWork();

            if (HistoryCategoryCombo.SelectedIndex == 0)
                ApplyImageSearchFilter();
        }
        catch (Exception ex)
        {
            HistorySearchStatusText.Text = "Search failed";
            ToastWindow.ShowError("Search failed", ex.Message);
        }
    }

    private void ImageSearchSourcesCheck_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsLoaded || _suppressImageSearchSourceEvents)
                return;

            _settingsService.Settings.ImageSearchSources = GetImageSearchSourcesFromUi();
            _settingsService.Save();
            UpdateImageSearchSourceSummary();

        if (HistoryCategoryCombo.SelectedIndex == 0)
            ApplyImageSearchFilter();
        }
        catch (Exception ex)
        {
            HistorySearchStatusText.Text = "Search failed";
            ToastWindow.ShowError("Search failed", ex.Message);
        }
    }

    private void ImageSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (!IsLoaded)
                return;

            SetImageSearchRowAutoHidden(false);
            _imageSearchQuery = ImageSearchBox.Text ?? "";
            ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_imageSearchQuery) && !ImageSearchBox.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (HistoryCategoryCombo.SelectedIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(_imageSearchQuery))
                {
                    CancelImageSearchWork();
                    ApplyImageSearchFilter();
                    return;
                }

                SetImageSearchLoading(true);
                QueueImageSearchRefresh();
            }
        }
        catch (Exception ex)
        {
            HistorySearchStatusText.Text = "Search failed";
            SetImageSearchLoading(false, forceIndexed: true);
            ToastWindow.ShowError("Search failed", ex.Message);
        }
    }

    private void ImageSearchBox_FocusChanged(object sender, RoutedEventArgs e)
    {
        if (ImageSearchBox.IsKeyboardFocused)
            SetImageSearchRowAutoHidden(false);

        ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(ImageSearchBox.Text) && !ImageSearchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ImageSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || string.IsNullOrWhiteSpace(ImageSearchBox.Text))
            return;

        try
        {
            CancelImageSearchWork();
            ImageSearchBox.Clear();
            ImageSearchBox.Focus();
            ApplyImageSearchFilter();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HistorySearchStatusText.Text = "Search failed";
            SetImageSearchLoading(false, forceIndexed: true);
            ToastWindow.ShowError("Search failed", ex.Message);
        }
    }

    private void ReindexAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);
        UpdateImageSearchStatus();
        UpdateImageSearchActionButtons();
        UpdateImageSearchPlaceholderText();
        QueueImageIndexRefresh();
    }

    private void ImageSearchFiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        ImageSearchFiltersMenu.PlacementTarget = ImageSearchFiltersBtn;
        ImageSearchFiltersMenu.IsOpen = true;
    }

    private void HistoryPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (HistoryCategoryCombo.SelectedIndex == 0 && _settingsService.Settings.ShowImageSearchBar)
        {
            var shouldHideSearch = e.VerticalOffset > 18 &&
                                   !ImageSearchBox.IsKeyboardFocused &&
                                   string.IsNullOrWhiteSpace(_imageSearchQuery);
            SetImageSearchRowAutoHidden(shouldHideSearch);
        }

        if (_useVirtualizedImageHistory)
        {
            UpdateVirtualizedHistoryViewport();
            return;
        }

        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 360)
            return;

        if (HistoryCategoryCombo.SelectedIndex == 0 && string.IsNullOrWhiteSpace(_imageSearchQuery))
        {
            AppendNextImageHistoryPage();
            return;
        }

        if (_historyRenderCount >= _filteredHistoryItems.Count)
            return;

        var previousOffset = ImagesPanel.VerticalOffset;
        var previousCount = _historyRenderCount;
        _historyRenderCount = Math.Min(_historyRenderCount + HistoryAppendPageSize, _filteredHistoryItems.Count);
        var appended = _filteredHistoryItems.Skip(previousCount).Take(_historyRenderCount - previousCount).ToList();
        if (appended.Count == 0)
            return;

        _historyItems.AddRange(appended);
        AppendGroupedHistoryItems(HistoryStack, appended, CreateHistoryCard);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 0)
                ImagesPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
