using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private bool _suppressHistoryUploadFilterEvents;
    private readonly HashSet<string> _historyUploadPathsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _historyUploadPathsInProgressGate = new();

    private bool IsHistoryUploadFilterActive()
    {
        if (!IsLoaded || !SupportsHistoryUploadFilter())
            return false;

        return GetHistoryUploadStateFilter() != HistoryUploadStateFilter.All ||
               !string.IsNullOrWhiteSpace(GetHistoryUploadProviderFilter());
    }

    private IEnumerable<HistoryItemVM> ApplyHistoryUploadFilter(IEnumerable<HistoryItemVM> items)
    {
        if (!SupportsHistoryUploadFilter())
        {
            foreach (var item in items)
                yield return item;
            yield break;
        }

        var state = GetHistoryUploadStateFilter();
        var provider = GetHistoryUploadProviderFilter();

        foreach (var item in items)
        {
            var entry = item.Entry;
            var entryProvider = NormalizeHistoryUploadProvider(entry.UploadProvider);
            if (state == HistoryUploadStateFilter.Uploaded &&
                (string.IsNullOrWhiteSpace(entry.UploadUrl) || !string.IsNullOrWhiteSpace(entry.UploadError)))
            {
                continue;
            }
            if (state == HistoryUploadStateFilter.NotUploaded &&
                (!string.IsNullOrWhiteSpace(entry.UploadUrl) || !string.IsNullOrWhiteSpace(entry.UploadError)))
            {
                continue;
            }
            if (state == HistoryUploadStateFilter.Failed && string.IsNullOrWhiteSpace(entry.UploadError))
                continue;
            if (!string.IsNullOrWhiteSpace(provider) &&
                !string.Equals(entryProvider, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return item;
        }
    }

    private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryItemVM> items)
        => RefreshHistoryUploadProviderFilterItems(items.Select(item => item.Entry));

    private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryEntry> entries)
    {
        if (!IsLoaded || HistoryUploadProviderCombo is null)
            return false;

        var selectedProvider = GetHistoryUploadProviderFilter();
        var providers = entries
            .Select(entry => NormalizeHistoryUploadProvider(entry.UploadProvider))
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressHistoryUploadFilterEvents = true;
        try
        {
            HistoryUploadProviderCombo.Items.Clear();
            HistoryUploadProviderCombo.Items.Add(CreateHistoryUploadProviderFilterItem("All providers", ""));
            foreach (var provider in providers)
                HistoryUploadProviderCombo.Items.Add(CreateHistoryUploadProviderFilterItem(provider, provider));

            var selectedIndex = 0;
            for (var i = 1; i < HistoryUploadProviderCombo.Items.Count; i++)
            {
                if (HistoryUploadProviderCombo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, selectedProvider, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            HistoryUploadProviderCombo.SelectedIndex = selectedIndex;
            return selectedIndex == 0 && !string.IsNullOrWhiteSpace(selectedProvider);
        }
        finally
        {
            _suppressHistoryUploadFilterEvents = false;
        }
    }

    private void UpdateHistoryUploadFilterUi()
    {
        if (!IsLoaded)
            return;

        var supportsUploadFilter = SupportsHistoryUploadFilter();
        HistoryUploadFilterCombo.Visibility = supportsUploadFilter ? Visibility.Visible : Visibility.Collapsed;
        HistoryUploadProviderCombo.Visibility = supportsUploadFilter ? Visibility.Visible : Visibility.Collapsed;

        var categoryLabel = HistoryCategoryCombo.SelectedIndex switch
        {
            0 => "screenshot history",
            2 => "video/GIF history",
            4 => "sticker history",
            _ => "file-backed history"
        };
        var categoryName = HistoryCategoryCombo.SelectedIndex switch
        {
            0 => "Screenshot",
            2 => "Video/GIF",
            4 => "Sticker",
            _ => "Upload"
        };
        var stateHelp = supportsUploadFilter
            ? $"Filter {categoryLabel} by upload state."
            : "Upload filters are available for screenshots, videos/GIFs, and stickers.";
        var providerHelp = supportsUploadFilter
            ? $"Filter {categoryLabel} by upload provider."
            : "Upload provider filters are available for screenshots, videos/GIFs, and stickers.";

        HistoryUploadFilterCombo.ToolTip = stateHelp;
        HistoryUploadProviderCombo.ToolTip = providerHelp;
        AutomationProperties.SetName(HistoryUploadFilterCombo, $"{categoryName} upload state filter");
        AutomationProperties.SetName(HistoryUploadProviderCombo, $"{categoryName} upload provider filter");
        AutomationProperties.SetHelpText(HistoryUploadFilterCombo, stateHelp);
        AutomationProperties.SetHelpText(HistoryUploadProviderCombo, providerHelp);
    }

    private bool SupportsHistoryUploadFilter()
        => HistoryCategoryCombo.SelectedIndex is 0 or 2 or 4;

    private void HistoryUploadFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryUploadFilterEvents || !SupportsHistoryUploadFilter())
            return;

        LoadCurrentHistoryTab(preserveTransientState: true);
    }

    private void HistoryUploadProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryUploadFilterEvents || !SupportsHistoryUploadFilter())
            return;

        LoadCurrentHistoryTab(preserveTransientState: true);
    }

    private HistoryUploadStateFilter GetHistoryUploadStateFilter()
    {
        if (HistoryUploadFilterCombo?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse(tag, out HistoryUploadStateFilter parsed))
        {
            return parsed;
        }

        return HistoryUploadStateFilter.All;
    }

    private string GetHistoryUploadProviderFilter()
        => HistoryUploadProviderCombo?.SelectedItem is ComboBoxItem item
            ? NormalizeHistoryUploadProvider(item.Tag as string)
            : "";

    private static string NormalizeHistoryUploadProvider(string? provider)
        => string.IsNullOrWhiteSpace(provider) ? "" : provider.Trim();

    private static ComboBoxItem CreateHistoryUploadProviderFilterItem(string label, string provider)
    {
        var helpText = string.IsNullOrWhiteSpace(provider)
            ? "Show history items from every upload provider."
            : $"Show history items uploaded with {provider}.";
        var item = new ComboBoxItem
        {
            Content = label,
            Tag = provider,
            ToolTip = helpText
        };
        AutomationProperties.SetName(item, label);
        AutomationProperties.SetHelpText(item, helpText);
        return item;
    }

    private bool IsHistoryUploadInProgress(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        lock (_historyUploadPathsInProgressGate)
            return _historyUploadPathsInProgress.Contains(filePath);
    }

    private bool TryBeginHistoryUpload(string filePath)
    {
        lock (_historyUploadPathsInProgressGate)
            return _historyUploadPathsInProgress.Add(filePath);
    }

    private void EndHistoryUpload(string filePath)
    {
        lock (_historyUploadPathsInProgressGate)
            _historyUploadPathsInProgress.Remove(filePath);
    }

    private async Task RetryHistoryUploadAsync(HistoryItemVM vm)
    {
        var entry = vm.Entry;
        if (!File.Exists(entry.FilePath))
        {
            entry.UploadProvider = GetHistoryUploadAttemptProvider(UploadDestination.None);
            entry.UploadError = "File no longer exists.";
            _historyService.SaveEntry(entry);
            ToastWindow.ShowError("Upload failed", BuildHistoryUploadMissingFileToastBody(entry.FilePath), entry.FilePath);
            LoadCurrentHistoryTab(preserveTransientState: true);
            return;
        }

        if (!TryBeginHistoryUpload(entry.FilePath))
        {
            ToastWindow.Show("Upload already running", Path.GetFileName(entry.FilePath));
            return;
        }

        var shouldReloadHistory = false;
        var destination = UploadDestination.None;
        try
        {
            destination = _settingsService.Settings.ImageUploadDestination;
            var uploadSettings = _settingsService.Settings.ImageUploadSettings;
            if (destination == UploadDestination.None || UploadService.IsAiChatDestination(destination))
            {
                entry.UploadProvider = GetHistoryUploadAttemptProvider(destination);
                entry.UploadError = "Choose an upload destination in Settings -> Uploads.";
                _historyService.SaveEntry(entry);
                shouldReloadHistory = true;
                ToastWindow.ShowError("Upload not configured", BuildHistoryUploadConfigurationToastBody(entry.UploadError), entry.FilePath);
                return;
            }

            var configurationError = UploadService.GetConfigurationError(destination, uploadSettings);
            if (!string.IsNullOrWhiteSpace(configurationError))
            {
                entry.UploadProvider = UploadService.GetName(destination);
                entry.UploadError = configurationError;
                _historyService.SaveEntry(entry);
                shouldReloadHistory = true;
                ToastWindow.ShowError("Upload not configured", BuildHistoryUploadConfigurationToastBody(entry.UploadError), entry.FilePath);
                return;
            }

            ToastWindow.Show("Uploading", Path.GetFileName(entry.FilePath));
            var result = await UploadService.UploadAsync(entry.FilePath, destination, uploadSettings);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Url))
            {
                entry.UploadUrl = result.Url;
                entry.UploadProvider = string.IsNullOrWhiteSpace(result.ProviderName)
                    ? UploadService.GetName(destination)
                    : result.ProviderName;
                entry.UploadError = null;
                _historyService.SaveEntry(entry);
                shouldReloadHistory = true;
                try
                {
                    ClipboardService.CopyTextToClipboard(result.Url);
                    ToastWindow.Show("Uploaded", "Link copied");
                }
                catch (Exception ex)
                {
                    ToastWindow.ShowError("Uploaded, copy failed", BuildHistoryUploadCopyFailureToastBody(ex.Message), entry.FilePath);
                }
            }
            else
            {
                var providerName = UploadService.GetName(destination);
                entry.UploadProvider = providerName;
                entry.UploadError = string.IsNullOrWhiteSpace(result.Error) ? "Upload failed." : result.Error;
                _historyService.SaveEntry(entry);
                shouldReloadHistory = true;
                ToastWindow.ShowError(
                    result.IsRateLimit ? "Upload rate-limited" : "Upload failed",
                    BuildHistoryUploadFailureToastBody(providerName, entry.UploadError, result.IsRateLimit),
                    entry.FilePath);
            }
        }
        catch (Exception ex)
        {
            entry.UploadProvider = GetHistoryUploadAttemptProvider(destination);
            entry.UploadError = string.IsNullOrWhiteSpace(ex.Message) ? "Upload failed." : ex.Message;
            _historyService.SaveEntry(entry);
            shouldReloadHistory = true;
            ToastWindow.ShowError(
                "Upload error",
                BuildHistoryUploadUnexpectedErrorToastBody(entry.UploadProvider, entry.UploadError),
                entry.FilePath);
        }
        finally
        {
            EndHistoryUpload(entry.FilePath);
            if (shouldReloadHistory)
                LoadCurrentHistoryTab(preserveTransientState: true);
        }
    }

    private static string GetHistoryUploadAttemptProvider(UploadDestination destination)
        => destination == UploadDestination.None ? "Upload" : UploadService.GetName(destination);

    private static string BuildHistoryUploadFailureToastBody(string providerName, string error, bool isRateLimit)
    {
        var providerLabel = string.IsNullOrWhiteSpace(providerName) ? "Upload" : providerName;
        var recovery = isRateLimit
            ? "Try another upload destination or wait before retrying."
            : $"Check {providerLabel} settings or try another upload destination.";

        return $"{providerLabel}: {error}\n{recovery}";
    }

    private static string BuildHistoryUploadCopyFailureToastBody(string details)
    {
        const string recovery = "The upload finished, but OddSnap could not copy the link. Open History and copy the upload link manually.";
        return string.IsNullOrWhiteSpace(details) ? recovery : $"{recovery}\n{details}";
    }

    private static string BuildHistoryUploadConfigurationToastBody(string details)
    {
        const string recovery = "Check Settings -> Uploads, then retry from History.";
        return string.IsNullOrWhiteSpace(details) ? recovery : $"{recovery}\n{details}";
    }

    private static string BuildHistoryUploadMissingFileToastBody(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var detail = string.IsNullOrWhiteSpace(fileName)
            ? "The saved file is no longer on disk."
            : $"The saved file is no longer on disk: {fileName}";
        const string recovery = "Restore the file or capture it again, then retry the upload from History.";

        return $"{detail}\n{recovery}";
    }

    private static string BuildHistoryUploadUnexpectedErrorToastBody(string providerName, string error)
    {
        var providerLabel = string.IsNullOrWhiteSpace(providerName) ? "Upload" : providerName;
        var detail = string.IsNullOrWhiteSpace(error) ? "Upload failed." : error;
        const string recovery = "The file is still saved. Check Settings -> Uploads, then retry from History or try another destination.";

        return $"{providerLabel}: {detail}\n{recovery}";
    }

    private enum HistoryUploadStateFilter
    {
        All,
        Uploaded,
        NotUploaded,
        Failed
    }
}
