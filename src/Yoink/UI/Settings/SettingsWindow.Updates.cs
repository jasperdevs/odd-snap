using System.Diagnostics;
using System.IO;
using System.Windows;
using Velopack;
using Velopack.Sources;
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

        await InstallUpdateAsync();
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
        SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);

        try
        {
            _latestUpdate = await UpdateService.CheckForUpdatesAsync(forceRefresh: isManualCheck);
            UpdateStatusText.Text = _latestUpdate.StatusMessage;
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);

            if (_latestUpdate.IsUpdateAvailable)
            {
                var published = _latestUpdate.PublishedAt.HasValue
                    ? $"Published {FormatTimeAgo(_latestUpdate.PublishedAt.Value.LocalDateTime)}"
                    : "Published recently";
                UpdateDetailText.Text = $"Current build: {UpdateService.GetCurrentVersionLabel()}. {published}.";
                DownloadUpdateButton.Content = CanInstallUpdate() ? "Update now" : "Open release";
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
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
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

    private async Task InstallUpdateAsync()
    {
        if (_latestUpdate is null)
            return;

        if (_updateCheckInFlight)
            return;

        if (!CanInstallUpdate())
        {
            OpenExternalUrl(_latestUpdate.ReleaseUrl);
            return;
        }

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        DownloadUpdateButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        DownloadUpdateButton.Content = "Updating...";
        UpdateStatusText.Text = "Preparing update...";
        UpdateDetailText.Text = "Yoink will close, update, and reopen automatically.";
        SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);

        try
        {
            var manager = CreateVelopackUpdateManager();
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                UpdateStatusText.Text = "You're up to date";
                UpdateDetailText.Text = UpdateService.GetCurrentVersionLabel();
                SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
                return;
            }

            UpdateStatusText.Text = "Downloading update...";
            SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);
            await manager.DownloadUpdatesAsync(update);
            ToastWindow.Show("Updating Yoink", "Yoink will close, update, and reopen.");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Update failed";
            UpdateDetailText.Text = "Open the GitHub release and install the latest setup manually.";
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError("Update failed", ex.Message);
        }
        finally
        {
            _updateCheckInFlight = false;
            CheckUpdatesButton.IsEnabled = true;
            DownloadUpdateButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check now";
        }
    }

    private static bool CanInstallUpdate()
    {
        return !InstallService.LooksLikeBuildOutputPath(InstallService.GetRunningAppDirectory());
    }

    private static UpdateManager CreateVelopackUpdateManager()
    {
        var source = new GithubSource("jasperdevs", "yoink", prerelease: false);
        return new UpdateManager(source);
    }
}
