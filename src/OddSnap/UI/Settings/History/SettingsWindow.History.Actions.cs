using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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
            UpdateReindexAllButtonLabel(status, "Image search indexing is already running.");
        }
        else if (total >= HistoryVirtualizationThreshold)
        {
            ReindexAllProgressPanel.Visibility = Visibility.Collapsed;
            ReindexAllBtn.Content = "Refresh index";
            ReindexAllBtn.IsEnabled = total > 0;
            UpdateReindexAllButtonLabel("Refresh image search index", "Refresh the image search index for all screenshot history items.");
        }
        else
        {
            int indexed = _imageSearchIndexService.CountReadyEntries(entries, ocrTag);
            if (indexed < total)
            {
                ReindexAllProgressPanel.Visibility = Visibility.Visible;
                ReindexAllBtn.Content = $"Index {total - indexed} remaining";
                ReindexAllBtn.IsEnabled = true;
                UpdateReindexAllButtonLabel("Index remaining screenshots", $"Index {total - indexed} screenshots for History search.");
            }
            else
            {
                ReindexAllProgressPanel.Visibility = Visibility.Collapsed;
                ReindexAllBtn.Content = $"{indexed}/{total} indexed";
                ReindexAllBtn.IsEnabled = false;
                UpdateReindexAllButtonLabel("Image search index complete", "All visible screenshot history items are indexed.");
            }
        }
    }

    private void UpdateReindexAllButtonLabel(string automationName, string helpText)
    {
        ReindexAllBtn.ToolTip = helpText;
        AutomationProperties.SetName(ReindexAllBtn, automationName);
        AutomationProperties.SetHelpText(ReindexAllBtn, helpText);
    }

    private void UpdateImageSearchPlaceholderText()
    {
        if (!IsLoaded)
            return;

        string placeholder;
        string automationName;
        string helpText;

        if (HistoryCategoryCombo.SelectedIndex == 1)
        {
            placeholder = "Search text captures";
            automationName = "Text history search";
            helpText = "Search saved OCR text captures.";
        }
        else if (HistoryCategoryCombo.SelectedIndex == 3)
        {
            placeholder = "Search hex, RGB, or color names";
            automationName = "Color history search";
            helpText = "Search saved colors by hex value, RGB values, or color names.";
        }
        else if (HistoryCategoryCombo.SelectedIndex == 5)
        {
            placeholder = "Search QR/barcode text, links, or formats";
            automationName = "Code history search";
            helpText = "Search saved QR and barcode text, links, or code formats.";
        }
        else
        {
            var isIndexing = _imageSearchIndexService.StatusText.StartsWith("Indexing screenshots", StringComparison.OrdinalIgnoreCase);
            placeholder = isIndexing
                ? "Search screenshots (indexing...)"
                : "Search screenshots";
            automationName = "Screenshot history search";
            helpText = isIndexing
                ? "Search screenshots while the image search index continues updating."
                : "Search screenshots by file name or indexed OCR text.";
        }

        ImageSearchPlaceholder.Text = placeholder;
        ImageSearchBox.ToolTip = helpText;
        AutomationProperties.SetName(ImageSearchBox, automationName);
        AutomationProperties.SetHelpText(ImageSearchBox, helpText);
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
        if (!IsLoaded || _suppressImageSearchSourceEvents)
            return;

        var previous = _settingsService.Settings.ImageSearchExactMatch;
        var selected = ImageSearchExactMatchCheck.IsChecked == true;
        UpdateImageSearchPreference(
            "settings.image-search-exact-match",
            "Search exact match",
            previous,
            selected,
            value => _settingsService.Settings.ImageSearchExactMatch = value,
            value => ImageSearchExactMatchCheck.IsChecked = value);
    }

    private void ImageSearchSourcesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressImageSearchSourceEvents)
            return;

        var previous = _settingsService.Settings.ImageSearchSources;
        var selected = GetImageSearchSourcesFromUi();
        UpdateImageSearchPreference(
            "settings.image-search-sources",
            "Search sources",
            previous,
            selected,
            value => _settingsService.Settings.ImageSearchSources = value,
            RestoreImageSearchSourceChecks);
    }

    private void UpdateImageSearchPreference<T>(
        string diagnosticKey,
        string label,
        T previous,
        T current,
        Action<T> setValue,
        Action<T> restoreUi)
    {
        try
        {
            setValue(current);
            _settingsService.Save();
            UpdateImageSearchSourceSummary();
            CancelImageSearchWork();

            if (HistoryCategoryCombo.SelectedIndex == 0)
                ApplyImageSearchFilter();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError(diagnosticKey, ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"{diagnosticKey}-rollback", rollbackEx);
            }

            _suppressImageSearchSourceEvents = true;
            try
            {
                restoreUi(previous);
            }
            finally
            {
                _suppressImageSearchSourceEvents = false;
            }

            UpdateImageSearchSourceSummary();
            HistorySearchStatusText.Text = $"{label} change was not saved. Previous setting restored.";
            ToastWindow.ShowError(
                $"{label} failed",
                $"The previous search setting was restored. Check Settings -> History and try again.\n{ex.Message}");
        }
    }

    private void RestoreImageSearchSourceChecks(ImageSearchSourceOptions sources)
    {
        ImageSearchFileNameCheck.IsChecked = (sources & ImageSearchSourceOptions.FileName) != 0;
        ImageSearchOcrCheck.IsChecked = (sources & ImageSearchSourceOptions.Ocr) != 0;
    }

    private void ImageSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (!IsLoaded || _suppressHistorySearchBoxTextEvents)
                return;

            SetImageSearchRowAutoHidden(false);
            var text = ImageSearchBox.Text ?? "";
            ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(text) && !ImageSearchBox.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (HistoryCategoryCombo.SelectedIndex == 0)
            {
                _imageSearchQuery = text;
                ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_imageSearchQuery) && !ImageSearchBox.IsKeyboardFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (string.IsNullOrWhiteSpace(_imageSearchQuery))
                {
                    CancelImageSearchWork();
                    ApplyImageSearchFilter();
                    return;
                }

                SetImageSearchLoading(true);
                QueueImageSearchRefresh();
            }
            else if (HistoryCategoryCombo.SelectedIndex == 1)
            {
                _ocrSearchQuery = text;
                ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_ocrSearchQuery) && !ImageSearchBox.IsKeyboardFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _ocrSearchDebounceTimer.Stop();
                _ocrSearchDebounceTimer.Tick -= FlushOcrSearchDebounce;
                _ocrSearchDebounceTimer.Tick += FlushOcrSearchDebounce;
                _ocrSearchDebounceTimer.Start();
            }
            else if (HistoryCategoryCombo.SelectedIndex == 3)
            {
                _colorSearchQuery = text;
                ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_colorSearchQuery) && !ImageSearchBox.IsKeyboardFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _colorSearchDebounceTimer.Stop();
                _colorSearchDebounceTimer.Tick -= FlushColorSearchDebounce;
                _colorSearchDebounceTimer.Tick += FlushColorSearchDebounce;
                _colorSearchDebounceTimer.Start();
            }
            else if (HistoryCategoryCombo.SelectedIndex == 5)
            {
                _codeSearchQuery = text;
                ImageSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_codeSearchQuery) && !ImageSearchBox.IsKeyboardFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _codeSearchDebounceTimer.Stop();
                _codeSearchDebounceTimer.Tick -= FlushCodeSearchDebounce;
                _codeSearchDebounceTimer.Tick += FlushCodeSearchDebounce;
                _codeSearchDebounceTimer.Start();
            }
        }
        catch (Exception ex)
        {
            SetImageSearchLoading(false, forceIndexed: true);
            HistorySearchStatusText.Text = "Search failed. Edit the query or retry from History.";
            ToastWindow.ShowError(
                "Search failed",
                $"OddSnap could not update history search. Edit the query or retry from History.\n{ex.Message}");
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
            if (HistoryCategoryCombo.SelectedIndex == 0)
                ApplyImageSearchFilter();
            else if (HistoryCategoryCombo.SelectedIndex == 1)
                LoadOcrHistory();
            else if (HistoryCategoryCombo.SelectedIndex == 3)
                LoadColorHistory();
            else if (HistoryCategoryCombo.SelectedIndex == 5)
                LoadCodeHistory();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            SetImageSearchLoading(false, forceIndexed: true);
            HistorySearchStatusText.Text = "Search failed. Edit the query or retry from History.";
            ToastWindow.ShowError(
                "Search failed",
                $"OddSnap could not update history search. Edit the query or retry from History.\n{ex.Message}");
        }
    }

    private void ReindexAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ReindexAllBtn.IsEnabled)
            return;

        ReindexAllBtn.IsEnabled = false;
        ReindexAllBtn.Content = "Starting index...";
        ReindexAllProgressPanel.Visibility = Visibility.Visible;
        ReindexAllProgressBar.Visibility = Visibility.Visible;
        HistorySearchStatusText.Text = "Starting image index refresh...";

        try
        {
            _imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);
            UpdateImageSearchStatus();
            UpdateImageSearchActionButtons();
            UpdateImageSearchPlaceholderText();
            QueueImageIndexRefresh();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.history-reindex-refresh", ex);
            SetImageSearchLoading(false, forceIndexed: true);
            HistorySearchStatusText.Text = "Index refresh failed. Existing search data is still available.";
            UpdateImageSearchActionButtons();
            ToastWindow.ShowError(
                "Index refresh failed",
                $"OddSnap could not refresh the image search index. Existing search data is still available; try again from History.\n{ex.Message}");
        }
    }

    private void ImageSearchFiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        ImageSearchFiltersMenu.PlacementTarget = ImageSearchFiltersBtn;
        ImageSearchFiltersMenu.IsOpen = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            ImageSearchFileNameCheck.Focus();
            Keyboard.Focus(ImageSearchFileNameCheck);
        });
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
        var appendCount = _historyRenderCount - previousCount;
        if (appendCount <= 0)
            return;
        var appended = _filteredHistoryItems.GetRange(previousCount, appendCount);

        _historyItems.AddRange(appended);
        AppendGroupedHistoryItems(HistoryStack, appended, CreateHistoryCard);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && HistoryTab.IsChecked == true && HistoryCategoryCombo.SelectedIndex == 0)
                ImagesPanel.ScrollToVerticalOffset(previousOffset);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
