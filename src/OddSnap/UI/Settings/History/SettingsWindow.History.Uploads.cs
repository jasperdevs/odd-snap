using System.IO;
using System.Windows;
using System.Windows.Controls;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private bool _suppressHistoryUploadFilterEvents;

    private bool IsHistoryUploadFilterActive()
    {
        if (!IsLoaded)
            return false;

        return GetHistoryUploadStateFilter() != HistoryUploadStateFilter.All ||
               !string.IsNullOrWhiteSpace(GetHistoryUploadProviderFilter());
    }

    private IEnumerable<HistoryItemVM> ApplyHistoryUploadFilter(IEnumerable<HistoryItemVM> items)
    {
        var state = GetHistoryUploadStateFilter();
        var provider = GetHistoryUploadProviderFilter();

        foreach (var item in items)
        {
            var entry = item.Entry;
            if (state == HistoryUploadStateFilter.Uploaded && string.IsNullOrWhiteSpace(entry.UploadUrl))
                continue;
            if (state == HistoryUploadStateFilter.NotUploaded &&
                (!string.IsNullOrWhiteSpace(entry.UploadUrl) || !string.IsNullOrWhiteSpace(entry.UploadError)))
            {
                continue;
            }
            if (state == HistoryUploadStateFilter.Failed && string.IsNullOrWhiteSpace(entry.UploadError))
                continue;
            if (!string.IsNullOrWhiteSpace(provider) &&
                !string.Equals(entry.UploadProvider, provider, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return item;
        }
    }

    private void RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryItemVM> items)
        => RefreshHistoryUploadProviderFilterItems(items.Select(item => item.Entry));

    private void RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryEntry> entries)
    {
        if (!IsLoaded || HistoryUploadProviderCombo is null)
            return;

        var selectedProvider = GetHistoryUploadProviderFilter();
        var providers = entries
            .Select(entry => entry.UploadProvider)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressHistoryUploadFilterEvents = true;
        try
        {
            HistoryUploadProviderCombo.Items.Clear();
            HistoryUploadProviderCombo.Items.Add(new ComboBoxItem { Content = "All providers", Tag = "" });
            foreach (var provider in providers)
                HistoryUploadProviderCombo.Items.Add(new ComboBoxItem { Content = provider, Tag = provider });

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

        var supportsUploadFilter = HistoryCategoryCombo.SelectedIndex is 0 or 2 or 4;
        HistoryUploadFilterCombo.Visibility = supportsUploadFilter ? Visibility.Visible : Visibility.Collapsed;
        HistoryUploadProviderCombo.Visibility = supportsUploadFilter ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HistoryUploadFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryUploadFilterEvents)
            return;

        LoadCurrentHistoryTab(preserveTransientState: true);
    }

    private void HistoryUploadProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressHistoryUploadFilterEvents)
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
            ? item.Tag as string ?? ""
            : "";

    private async Task RetryHistoryUploadAsync(HistoryItemVM vm)
    {
        var entry = vm.Entry;
        if (!File.Exists(entry.FilePath))
        {
            ToastWindow.ShowError("Upload failed", "File no longer exists.");
            return;
        }

        var destination = _settingsService.Settings.ImageUploadDestination;
        var uploadSettings = _settingsService.Settings.ImageUploadSettings;
        if (destination == UploadDestination.None || UploadService.IsAiChatDestination(destination))
        {
            entry.UploadError = "Choose an upload destination in Settings -> Uploads.";
            _historyService.SaveEntry(entry);
            LoadCurrentHistoryTab(preserveTransientState: true);
            ToastWindow.ShowError("Upload not configured", entry.UploadError);
            return;
        }

        if (!UploadService.HasCredentials(destination, uploadSettings))
        {
            entry.UploadProvider = UploadService.GetName(destination);
            entry.UploadError = "No API key configured.";
            _historyService.SaveEntry(entry);
            LoadCurrentHistoryTab(preserveTransientState: true);
            ToastWindow.ShowError("Upload not configured", entry.UploadError);
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
            ClipboardService.CopyTextToClipboard(result.Url);
            ToastWindow.Show("Uploaded", "Link copied");
        }
        else
        {
            entry.UploadProvider = UploadService.GetName(destination);
            entry.UploadError = string.IsNullOrWhiteSpace(result.Error) ? "Upload failed." : result.Error;
            _historyService.SaveEntry(entry);
            ToastWindow.ShowError(result.IsRateLimit ? "Upload rate-limited" : "Upload failed", entry.UploadError);
        }

        LoadCurrentHistoryTab(preserveTransientState: true);
    }

    private enum HistoryUploadStateFilter
    {
        All,
        Uploaded,
        NotUploaded,
        Failed
    }
}
