using System.Diagnostics;
using System.IO;
using System.Windows;
using Velopack;
using Velopack.Sources;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUpdateStatusAsync(true);
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null || _updateActionInProgress)
            return;

        _updateActionInProgress = true;
        DownloadUpdateButton.IsEnabled = false;
        try
        {
            await InstallUpdateAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Update failed";
            UpdateDetailText.Text = "Try checking again, or open the latest release manually.";
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError(
                "Update failed",
                $"OddSnap could not finish the update. Try checking again, or open the release manually from Settings -> Updates.\n{ex.Message}");
        }
        finally
        {
            ResetUpdateActionGuardAfterCooldown();
        }
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
                    ToastWindow.Show("OddSnap is up to date", UpdateService.GetCurrentVersionLabel());
            }
        }
        catch (Exception ex)
        {
            _latestUpdate = null;
            UpdateStatusText.Text = "Update check failed";
            UpdateDetailText.Text = "Check your connection and try again from Settings -> Updates.";
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            if (isManualCheck)
            {
                ToastWindow.ShowError(
                    "Update check failed",
                    $"OddSnap could not check GitHub Releases. Check your connection and try again.\n{ex.Message}");
            }
        }
        finally
        {
            _updateCheckInFlight = false;
            CheckUpdatesButton.IsEnabled = true;
            if (!_updateActionInProgress)
                DownloadUpdateButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check now";
        }
    }

    private static bool OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open failed", "No update link is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open failed", "The update link is not a valid web link.");
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            if (process is null)
            {
                ToastWindow.ShowError("Open failed", "Windows did not open the update link. Copy the link from Settings -> Updates and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open failed",
                $"OddSnap could not open the update link. Copy the link from Settings -> Updates and open it manually.\n{ex.Message}");
            return false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_latestUpdate is null)
            return;

        if (_updateCheckInFlight)
            return;

        if (!CanInstallUpdate())
        {
            var opened = OpenExternalUrl(GetUpdateFallbackUrl(_latestUpdate));
            UpdateStatusText.Text = opened ? "Release opened" : "Open release failed";
            UpdateDetailText.Text = opened
                ? "Use the installer from GitHub Releases to update this build."
                : "Windows did not open the release link. Try checking again or open GitHub Releases manually.";
            return;
        }

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        DownloadUpdateButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        DownloadUpdateButton.Content = "Updating...";
        UpdateStatusText.Text = "Preparing update...";
        UpdateDetailText.Text = "OddSnap will close, update, and reopen automatically.";
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
            ToastWindow.Show("Updating OddSnap", "OddSnap will close, update, and reopen.");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Update failed";
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError(
                "Update failed",
                $"OddSnap could not install the update automatically. OddSnap will try to open the latest setup download.\n{ex.Message}");
            var opened = OpenExternalUrl(GetUpdateFallbackUrl(_latestUpdate));
            UpdateDetailText.Text = opened
                ? "Opened the latest setup download so you can install it manually."
                : "Automatic update failed and Windows did not open the fallback download.";
        }
        finally
        {
            _updateCheckInFlight = false;
            CheckUpdatesButton.IsEnabled = true;
            if (!_updateActionInProgress)
                DownloadUpdateButton.IsEnabled = true;
            CheckUpdatesButton.Content = "Check now";
        }
    }

    private void ResetUpdateActionGuardAfterCooldown()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UpdateActionCooldownMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _updateActionInProgress = false;
            if (!_updateCheckInFlight)
                DownloadUpdateButton.IsEnabled = true;
        };
        timer.Start();
    }

    private static bool CanInstallUpdate()
    {
        return !InstallService.LooksLikeBuildOutputPath(InstallService.GetRunningAppDirectory());
    }

    private static string GetUpdateFallbackUrl(UpdateCheckResult update)
    {
        return string.IsNullOrWhiteSpace(update.DownloadUrl)
            ? update.ReleaseUrl
            : update.DownloadUrl;
    }

    private static UpdateManager CreateVelopackUpdateManager()
    {
        var source = new GithubSource("https://github.com/jasperdevs/odd-snap", accessToken: null, prerelease: false);
        return new UpdateManager(source, new UpdateOptions
        {
            ExplicitChannel = UpdateService.GetRuntimeChannel()
        });
    }
}
