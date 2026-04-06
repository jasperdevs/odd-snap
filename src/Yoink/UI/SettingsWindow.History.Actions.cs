using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

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

        var entries = _historyService.ImageEntries;
        var ocrTag = _settingsService.Settings.OcrLanguageTag;
        int total = entries.Count;
        int indexed = _imageSearchIndexService.CountReadyEntries(entries, ocrTag);

        if (isIndexing)
        {
            ReindexAllBtn.Content = status;
            ReindexAllBtn.IsEnabled = false;
        }
        else if (indexed < total)
        {
            ReindexAllBtn.Content = $"Index {total - indexed} remaining";
            ReindexAllBtn.IsEnabled = true;
        }
        else
        {
            ReindexAllBtn.Content = $"{indexed}/{total} indexed";
            ReindexAllBtn.IsEnabled = false;
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
        var parts = new List<string>(4);
        if (ImageSearchFileNameCheck.IsChecked)
            parts.Add("Name");
        if (ImageSearchOcrCheck.IsChecked)
            parts.Add("OCR");
        if (ImageSearchSemanticCheck.IsChecked)
            parts.Add(_semanticRuntimeInstalled ? "Semantic" : "Semantic setup");
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
            ImageSearchSemanticCheck.IsChecked = (sources & ImageSearchSourceOptions.Semantic) != 0;
            ImageSearchSemanticCheck.IsEnabled = !_settingsService.Settings.ImageSearchExactMatch && _semanticRuntimeInstalled;
            ImageSearchSemanticCheck.ToolTip = _semanticRuntimeInstalled
                ? "Use local semantic embeddings when the runtime is installed."
                : LocalClipRuntimeService.SetupHelpText;
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
        if (ImageSearchSemanticCheck.IsChecked)
            sources |= ImageSearchSourceOptions.Semantic;
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

            ImageSearchSemanticCheck.IsEnabled = !_settingsService.Settings.ImageSearchExactMatch && _semanticRuntimeInstalled;
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
            SetImageSearchLoading(false, forceSemantic: true);
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
            SetImageSearchLoading(false, forceSemantic: true);
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

        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 300) return;
        if (_historyRenderCount >= _filteredHistoryItems.Count) return;
        var previousCount = _historyRenderCount;
        _historyRenderCount = Math.Min(_historyRenderCount + HistoryPageSize, _filteredHistoryItems.Count);
        var appended = _filteredHistoryItems.Skip(previousCount).Take(_historyRenderCount - previousCount).ToList();
        _historyItems.AddRange(appended);
        AppendGroupedHistoryItems(HistoryStack, appended, CreateHistoryCard);
    }
}
