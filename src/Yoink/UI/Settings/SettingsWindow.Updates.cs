using System.Diagnostics;
using System.IO;
using System.Windows;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUpdateStatusAsync(true);
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null)
            return;

        OpenExternalUrl(_latestUpdate.ReleaseUrl);
        await Task.CompletedTask;
    }

    private async Task RefreshUpdateStatusAsync(bool isManualCheck)
    {
        if (_updateCheckInFlight)
            return;

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Checking GitHub releases...";
        UpdateDetailText.Text = "Looking for the newest production build.";

        try
        {
            _latestUpdate = await UpdateService.CheckForUpdatesAsync(forceRefresh: isManualCheck);
            UpdateStatusText.Text = _latestUpdate.StatusMessage;

            if (_latestUpdate.IsUpdateAvailable)
            {
                var published = _latestUpdate.PublishedAt.HasValue
                    ? $"Published {FormatTimeAgo(_latestUpdate.PublishedAt.Value.LocalDateTime)}"
                    : "Published recently";
                UpdateDetailText.Text = $"Current build: {UpdateService.GetCurrentVersionLabel()}. {published}. Download the latest release to update.";
                DownloadUpdateButton.Content = "Open release";
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateDetailText.Text = $"Current build: {UpdateService.GetCurrentVersionLabel()}";
                if (isManualCheck)
                    ToastWindow.Show("Yoink is up to date", UpdateService.GetCurrentVersionLabel());
            }
        }
        catch (Exception ex)
        {
            _latestUpdate = null;
            UpdateStatusText.Text = "Update check failed";
            UpdateDetailText.Text = ex.Message;
            if (isManualCheck)
                ToastWindow.ShowError("Update check failed", ex.Message);
        }
        finally
        {
            _updateCheckInFlight = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check now";
        }
    }

    private static void OpenExternalUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
