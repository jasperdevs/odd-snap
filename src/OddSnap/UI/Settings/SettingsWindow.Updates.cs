using System.Diagnostics;
using System.IO;
using System.Windows;
using Velopack;
using Velopack.Sources;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private const int VelopackUpdateCheckTimeoutSeconds = 20;
    private const int VelopackUpdateDownloadTimeoutSeconds = 180;

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
            if (_isClosed)
                return;

            UpdateStatusText.Text = LocalizationService.Translate("Update failed");
            UpdateDetailText.Text = LocalizationService.Translate("Try checking again, or open the latest release manually.");
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError(
                LocalizationService.Translate("Update failed"),
                LocalizationService.Translate("OddSnap could not finish the update. Try checking again, or open the release manually from Settings -> Updates.") + "\n" + ex.Message);
        }
        finally
        {
            ResetUpdateActionGuardAfterCooldown();
        }
    }

    private async Task RefreshUpdateStatusAsync(bool isManualCheck)
    {
        if (_updateCheckInFlight || _isClosed)
            return;

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = LocalizationService.Translate("Checking...");
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = LocalizationService.Translate("Checking GitHub releases...");
        UpdateDetailText.Text = LocalizationService.Translate("Looking for the newest production build.");
        SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);

        try
        {
            _latestUpdate = await UpdateService.CheckForUpdatesAsync(forceRefresh: isManualCheck);
            if (_isClosed)
                return;

            UpdateStatusText.Text = _latestUpdate.StatusMessage;
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);

            if (_latestUpdate.IsUpdateAvailable)
            {
                var published = _latestUpdate.PublishedAt.HasValue
                    ? string.Format(LocalizationService.Translate("Published {0}"), FormatTimeAgo(_latestUpdate.PublishedAt.Value.LocalDateTime))
                    : LocalizationService.Translate("Published recently");
                UpdateDetailText.Text = string.Format(LocalizationService.Translate("Current build: {0}. {1}."), UpdateService.GetCurrentVersionLabel(), published);
                DownloadUpdateButton.Content = CanInstallUpdate()
                    ? LocalizationService.Translate("Update now")
                    : LocalizationService.Translate("Open release");
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateDetailText.Text = string.Format(LocalizationService.Translate("Current build: {0}"), UpdateService.GetCurrentVersionLabel());
                if (isManualCheck)
                    ToastWindow.Show("OddSnap is up to date", UpdateService.GetCurrentVersionLabel());
            }
        }
        catch (Exception ex)
        {
            if (_isClosed)
                return;

            _latestUpdate = null;
            UpdateStatusText.Text = LocalizationService.Translate("Update check failed");
            UpdateDetailText.Text = LocalizationService.Translate("Check your connection and try again from Settings -> Updates.");
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            if (isManualCheck)
            {
                ToastWindow.ShowError(
                    LocalizationService.Translate("Update check failed"),
                    LocalizationService.Translate("OddSnap could not check GitHub Releases. Check your connection and try again.") + "\n" + ex.Message);
            }
        }
        finally
        {
            _updateCheckInFlight = false;
            if (!_isClosed)
            {
                CheckUpdatesButton.IsEnabled = true;
                if (!_updateActionInProgress)
                    DownloadUpdateButton.IsEnabled = true;
                CheckUpdatesButton.Content = LocalizationService.Translate("Check now");
            }
        }
    }

    private static bool OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError(
                LocalizationService.Translate("Open failed"),
                LocalizationService.Translate("No update link is available."));
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError(
                LocalizationService.Translate("Open failed"),
                LocalizationService.Translate("The update link is not a valid web link."));
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
                ToastWindow.ShowError(
                    LocalizationService.Translate("Open failed"),
                    LocalizationService.Translate("Windows did not open the update link. Copy the link from Settings -> Updates and open it manually."));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                LocalizationService.Translate("Open failed"),
                LocalizationService.Translate("OddSnap could not open the update link. Copy the link from Settings -> Updates and open it manually.") + "\n" + ex.Message);
            return false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_latestUpdate is null || _isClosed)
            return;

        var latestUpdate = _latestUpdate;

        if (_updateCheckInFlight)
            return;

        if (!CanInstallUpdate())
        {
            var opened = OpenExternalUrl(GetUpdateFallbackUrl(latestUpdate));
            UpdateStatusText.Text = opened
                ? LocalizationService.Translate("Release opened")
                : LocalizationService.Translate("Open release failed");
            UpdateDetailText.Text = opened
                ? LocalizationService.Translate("Use the installer from GitHub Releases to update this build.")
                : LocalizationService.Translate("Windows did not open the release link. Try checking again or open GitHub Releases manually.");
            return;
        }

        _updateCheckInFlight = true;
        CheckUpdatesButton.IsEnabled = false;
        DownloadUpdateButton.IsEnabled = false;
        CheckUpdatesButton.Content = LocalizationService.Translate("Checking...");
        DownloadUpdateButton.Content = LocalizationService.Translate("Updating...");
        UpdateStatusText.Text = LocalizationService.Translate("Preparing update...");
        UpdateDetailText.Text = LocalizationService.Translate("OddSnap will close, update, and reopen automatically.");
        SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);

        try
        {
            var manager = CreateVelopackUpdateManager();
            var update = await WaitForUpdateOperationAsync(
                manager.CheckForUpdatesAsync(),
                TimeSpan.FromSeconds(VelopackUpdateCheckTimeoutSeconds),
                $"Automatic update check timed out after {VelopackUpdateCheckTimeoutSeconds} seconds.");
            if (_isClosed)
                return;

            if (update is null)
            {
                UpdateStatusText.Text = LocalizationService.Translate("You're up to date");
                UpdateDetailText.Text = UpdateService.GetCurrentVersionLabel();
                SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
                return;
            }

            UpdateStatusText.Text = LocalizationService.Translate("Downloading update...");
            SetLoadingTextShimmer(UpdateStatusText, true, 1.0, 1.0);
            using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(VelopackUpdateDownloadTimeoutSeconds));
            await manager.DownloadUpdatesAsync(update, progress =>
            {
                if (_isClosed)
                    return;

                _ = TryPostToSettingsDispatcher(() =>
                {
                    if (_isClosed)
                        return;

                    UpdateStatusText.Text = string.Format(LocalizationService.Translate("Downloading update... {0}%"), progress);
                    UpdateDetailText.Text = LocalizationService.Translate("Keep OddSnap open until the download finishes.");
                }, diagnosticKey: "settings.update-download-progress");
            }, downloadTimeout.Token);
            if (_isClosed)
                return;

            ToastWindow.Show("Updating OddSnap", "OddSnap will close, update, and reopen.");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (OperationCanceledException ex) when (!_isClosed)
        {
            UpdateStatusText.Text = LocalizationService.Translate("Update timed out");
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError(
                LocalizationService.Translate("Update timed out"),
                string.Format(LocalizationService.Translate("OddSnap could not finish downloading the update after {0} seconds. OddSnap will open the latest setup download so you can install it manually."), VelopackUpdateDownloadTimeoutSeconds) + "\n" + ex.Message);
            var opened = OpenExternalUrl(GetUpdateFallbackUrl(latestUpdate));
            UpdateDetailText.Text = opened
                ? LocalizationService.Translate("Opened the latest setup download so you can install it manually.")
                : LocalizationService.Translate("Automatic update timed out and Windows did not open the fallback download.");
        }
        catch (Exception ex)
        {
            if (_isClosed)
                return;

            UpdateStatusText.Text = LocalizationService.Translate("Update failed");
            SetLoadingTextShimmer(UpdateStatusText, false, 1.0, 1.0);
            ToastWindow.ShowError(
                LocalizationService.Translate("Update failed"),
                LocalizationService.Translate("OddSnap could not install the update automatically. OddSnap will try to open the latest setup download.") + "\n" + ex.Message);
            var opened = OpenExternalUrl(GetUpdateFallbackUrl(latestUpdate));
            UpdateDetailText.Text = opened
                ? LocalizationService.Translate("Opened the latest setup download so you can install it manually.")
                : LocalizationService.Translate("Automatic update failed and Windows did not open the fallback download.");
        }
        finally
        {
            _updateCheckInFlight = false;
            if (!_isClosed)
            {
                CheckUpdatesButton.IsEnabled = true;
                if (!_updateActionInProgress)
                    DownloadUpdateButton.IsEnabled = true;
                CheckUpdatesButton.Content = LocalizationService.Translate("Check now");
            }
        }
    }

    private static async Task<T> WaitForUpdateOperationAsync<T>(Task<T> operation, TimeSpan timeout, string timeoutMessage)
    {
        using var timeoutCts = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, timeoutCts.Token);
        var completed = await Task.WhenAny(operation, timeoutTask);
        if (completed == operation)
        {
            timeoutCts.Cancel();
            return await operation;
        }

        _ = operation.ContinueWith(
            task =>
            {
                if (task.Exception is not null)
                    AppDiagnostics.LogWarning("settings.update.late-fault", task.Exception.GetBaseException().Message, task.Exception.GetBaseException());
            },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        throw new TimeoutException(timeoutMessage);
    }

    private void ResetUpdateActionGuardAfterCooldown()
    {
        RunAfterSettingsCooldown(UpdateActionCooldownMs, () =>
        {
            _updateActionInProgress = false;
            if (!_updateCheckInFlight)
                DownloadUpdateButton.IsEnabled = true;
        }, "settings.update-cooldown");
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
