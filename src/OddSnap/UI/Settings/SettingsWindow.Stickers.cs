using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void StickerProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;

        var previousProvider = ActiveStickerSettings.Provider;
        var selectedProvider = (StickerProvider)StickerProviderCombo.SelectedIndex;

        try
        {
            ActiveStickerSettings.Provider = selectedProvider;
            UpdateStickerProviderVisibility();
            UpdateLocalEngineUi();
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.sticker-provider", ex);
            ActiveStickerSettings.Provider = previousProvider;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.sticker-provider-rollback", rollbackEx);
            }

            _suppressStickerSettingChange = true;
            try
            {
                StickerProviderCombo.SelectedIndex = (int)previousProvider;
                UpdateStickerProviderVisibility();
                UpdateStickerExecutionUi();
                UpdateLocalEngineUi();
            }
            finally
            {
                _suppressStickerSettingChange = false;
            }

            ShowStickerProviderSaveFailed(ex);
        }
    }

    private void ShowStickerProviderSaveFailed(Exception ex)
    {
        SetStickerRemovalStatus("Sticker provider change was not saved. Previous provider restored.");
        ToastWindow.ShowError(
            "Sticker provider failed",
            $"The previous sticker provider was restored. Check Settings -> Stickers and try again.\n{ex.Message}");
    }

    private void StickerLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;
        UpdateStickerExecutionUi();
    }

    private LocalStickerEngine GetSelectedLocalStickerEngine()
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        return executionProvider == StickerExecutionProvider.Gpu
            ? GetSelectedStickerEngine(StickerLocalGpuEngineCombo)
            : GetSelectedStickerEngine(StickerLocalCpuEngineCombo);
    }

    private void StickerInstallDriversBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_stickerRuntimeMutationInProgress)
            return;

        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        if (RembgRuntimeService.TryGetCachedStatus(executionProvider, out var runtimeReady, out _) && runtimeReady)
        {
            if (!ThemedConfirmDialog.Confirm(
                    this,
                    "Uninstall rembg",
                    $"Uninstall the {RembgRuntimeService.GetSetupTargetName(executionProvider)} runtime?\n\nDownloaded models stay available, but this runtime will need to be installed again before local sticker captures can use it.",
                    "Uninstall",
                    "Cancel",
                    danger: true))
            {
                SetStickerRuntimeCancellationStatus("Sticker runtime uninstall canceled. Runtime was left installed.");
                return;
            }

            bool removed = false;
            var completed = RunStickerRuntimeMutation(() =>
            {
                removed = RembgRuntimeService.RemoveRuntime(executionProvider);
                if (removed)
                    ToastWindow.Show("Sticker runtime", "Uninstalled rembg runtime.");
                else
                    ToastWindow.ShowError(
                        "Sticker runtime error",
                        "The rembg runtime was not removed. Close active sticker captures and try again from Settings -> Stickers.");
            });
            if (completed && !removed)
                SetStickerRuntimeRemovalStatus("Sticker runtime was not removed. Close active sticker captures and try again.");
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                GetStickerRuntimeJobKey(executionProvider),
                RembgRuntimeService.GetSetupTargetName(executionProvider),
                $"Installing {RembgRuntimeService.GetSetupTargetName(executionProvider)}...",
                "Sticker runtime ready",
                $"{RembgRuntimeService.GetSetupTargetName(executionProvider)} finished installing.",
                "Sticker runtime setup failed"),
            (progress, cancellationToken) => RembgRuntimeService.EnsureInstalledAsync(executionProvider, progress, cancellationToken));

        if (!started)
            ToastWindow.Show("Sticker runtime", "That setup is already running in the background.");

        UpdateLocalEngineUi();
    }

    private bool RunStickerRuntimeMutation(Action mutation)
    {
        if (_stickerRuntimeMutationInProgress)
            return false;

        string? failureMessage = null;
        bool completed = false;
        _stickerRuntimeMutationInProgress = true;
        StickerInstallDriversBtn.IsEnabled = false;
        StickerDownloadRembgBtn.IsEnabled = false;
        StickerRemoveAllModelsBtn.IsEnabled = false;
        try
        {
            mutation();
            completed = true;
        }
        catch (Exception ex)
        {
            failureMessage = "Sticker runtime action failed. Check Settings -> Stickers and try again.";
            ToastWindow.ShowError(
                "Sticker runtime error",
                $"The sticker runtime action did not finish. Check Settings -> Stickers and try again.\n{ex.Message}");
        }
        finally
        {
            _stickerRuntimeMutationInProgress = false;
            UpdateLocalEngineUi();
            if (!string.IsNullOrWhiteSpace(failureMessage))
                SetStickerRuntimeRemovalStatus(failureMessage);
        }

        return completed;
    }

    private void StickerShadowCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStickerBooleanSetting(
            StickerShadowCheck,
            "shadow",
            "shadow",
            () => ActiveStickerSettings.AddShadow,
            value => ActiveStickerSettings.AddShadow = value);
    }

    private void StickerStrokeCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStickerBooleanSetting(
            StickerStrokeCheck,
            "stroke",
            "stroke",
            () => ActiveStickerSettings.AddStroke,
            value => ActiveStickerSettings.AddStroke = value);
    }

    private void StickerRemoveBgKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStickerPasswordSetting(
            StickerRemoveBgKeyBox,
            "Remove.bg API key",
            "removebg-key",
            () => ActiveStickerSettings.RemoveBgApiKey,
            value => ActiveStickerSettings.RemoveBgApiKey = value);
    }

    private void StickerPhotoroomKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStickerPasswordSetting(
            StickerPhotoroomKeyBox,
            "Photoroom API key",
            "photoroom-key",
            () => ActiveStickerSettings.PhotoroomApiKey,
            value => ActiveStickerSettings.PhotoroomApiKey = value);
    }

    private void UpdateStickerBooleanSetting(System.Windows.Controls.CheckBox checkBox, string label, string diagnosticSuffix, Func<bool> getValue, Action<bool> setValue)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;

        var previous = getValue();
        var selected = checkBox.IsChecked == true;

        try
        {
            setValue(selected);
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError($"settings.sticker-{diagnosticSuffix}", ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"settings.sticker-{diagnosticSuffix}-rollback", rollbackEx);
            }

            _suppressStickerSettingChange = true;
            try
            {
                checkBox.IsChecked = previous;
            }
            finally
            {
                _suppressStickerSettingChange = false;
            }

            ShowStickerSettingSaveFailed(label, ex);
        }
    }

    private void UpdateStickerPasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string label, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)
    {
        if (!IsLoaded || _suppressStickerSettingChange) return;

        var previous = getValue();

        try
        {
            setValue(passwordBox.Password);
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError($"settings.sticker-{diagnosticSuffix}", ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"settings.sticker-{diagnosticSuffix}-rollback", rollbackEx);
            }

            _suppressStickerSettingChange = true;
            try
            {
                passwordBox.Password = previous;
            }
            finally
            {
                _suppressStickerSettingChange = false;
            }

            ShowStickerSettingSaveFailed(label, ex);
        }
    }

    private void ShowStickerSettingSaveFailed(string label, Exception ex)
    {
        SetStickerRemovalStatus($"{label} change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            "Sticker setting failed",
            $"The previous sticker setting was restored. Check Settings -> Stickers and try again.\n{ex.Message}");
    }

    private void StickerDownloadRembgBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalStickerEngine();

        if (LocalStickerEngineService.IsModelDownloaded(engine))
        {
            var engineLabel = LocalStickerEngineService.GetEngineLabel(engine);
            if (!ThemedConfirmDialog.Confirm(
                    this,
                    "Remove sticker model",
                    $"Remove the downloaded {engineLabel} sticker model?\n\nIt will need to be downloaded again before local sticker captures can use it.",
                    "Remove",
                    "Cancel"))
            {
                SetStickerRemovalStatus($"Sticker model removal canceled. Kept {engineLabel}.");
                return;
            }

            RunStickerModelRemoval(() =>
            {
                bool removed = LocalStickerEngineService.RemoveDownloadedModel(engine);
                SetStickerRemovalStatus(removed ? "Model removed." : "Sticker model was not removed. Check Settings -> Stickers and try again.");
                if (removed)
                    ToastWindow.Show("Sticker engine", "Removed the local sticker model.");
                else
                    ToastWindow.ShowError("Sticker engine error", "OddSnap could not remove the local sticker model. Try again from Settings -> Stickers, or remove the model files manually.");
            });
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                GetStickerModelJobKey(engine),
                LocalStickerEngineService.GetEngineLabel(engine),
                $"Preparing {LocalStickerEngineService.GetEngineLabel(engine)}...",
                "Sticker model ready",
                $"Downloaded {LocalStickerEngineService.GetEngineLabel(engine)}.",
                "Sticker model download failed"),
            async (progress, cancellationToken) =>
            {
                var modelProgress = new Progress<LocalStickerEngineDownloadProgress>(p => progress.Report(p.StatusMessage));
                var result = await LocalStickerEngineService.DownloadModelAsync(engine, executionProvider, modelProgress, cancellationToken);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ModelPath))
                    throw new InvalidOperationException(result.Message);
            });

        if (!started)
            ToastWindow.Show("Sticker engine", "That model is already downloading in the background.");

        UpdateLocalEngineUi();
    }

    private void StickerOpenLocalEngineRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_stickerProjectOpenInProgress)
            return;

        _stickerProjectOpenInProgress = true;
        StickerOpenLocalEngineRepoBtn.IsEnabled = false;
        try
        {
            var engine = GetSelectedLocalStickerEngine();
            OpenStickerProjectUrl(LocalStickerEngineService.GetProjectUrl(engine));
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open project failed",
                $"OddSnap could not open the sticker project link. Check Settings -> Stickers and try again.\n{ex.Message}");
        }
        finally
        {
            ResetStickerProjectOpenGuardAfterCooldown();
        }
    }

    private static bool OpenStickerProjectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open project failed", "No sticker project link is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open project failed", "The sticker project link is not a valid web link.");
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
                ToastWindow.ShowError("Open project failed", "Windows did not open the sticker project link. Copy the link from Settings -> Stickers and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open project failed",
                $"Windows could not open the sticker project link. Copy the link from Settings -> Stickers and open it manually.\n{ex.Message}");
            return false;
        }
    }

    private void ResetStickerProjectOpenGuardAfterCooldown()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LocalEngineProjectOpenCooldownMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _stickerProjectOpenInProgress = false;
            UpdateLocalEngineUi();
        };
        timer.Start();
    }

    private void StickerRemoveAllModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_stickerModelRemovalInProgress)
            return;

        if (!ThemedConfirmDialog.Confirm(
                this,
                "Remove Models",
                "Remove all downloaded local sticker models?\n\nThey will be downloaded again the next time you use them.",
                "Remove",
                "Cancel"))
        {
            SetStickerRemovalStatus("Sticker model removal canceled. Downloaded models were left in place.");
            return;
        }

        RunStickerModelRemoval(() =>
        {
            bool removed = RembgRuntimeService.RemoveAllCachedModels();
            SetStickerRemovalStatus(removed ? "All models removed." : "Sticker models were not removed. Check Settings -> Stickers and try again.");
            if (removed)
                ToastWindow.Show("Sticker engine", "Removed all downloaded local sticker models.");
            else
                ToastWindow.ShowError("Sticker engine error", "OddSnap could not remove the downloaded sticker models. Try again from Settings -> Stickers, or remove the model files manually.");
        });
    }

    private void RunStickerModelRemoval(Action removeAction)
    {
        if (_stickerModelRemovalInProgress)
            return;

        _stickerModelRemovalInProgress = true;
        StickerDownloadRembgBtn.IsEnabled = false;
        StickerRemoveAllModelsBtn.IsEnabled = false;
        try
        {
            removeAction();
        }
        catch (Exception ex)
        {
            SetStickerRemovalStatus("Sticker model removal failed. Check Settings -> Stickers and try again.");
            ToastWindow.ShowError(
                "Sticker engine error",
                $"The local sticker model files were not removed. Check Settings -> Stickers and try again.\n{ex.Message}");
        }
        finally
        {
            ResetStickerModelRemovalGuardAfterCooldown();
        }
    }

    private void SetStickerRemovalStatus(string message)
    {
        StickerLocalEngineProgress.Visibility = Visibility.Collapsed;
        StickerLocalEngineProgress.IsIndeterminate = false;
        StickerLocalEngineProgress.Value = 0;
        StickerLocalEngineProgressText.Visibility = Visibility.Visible;
        StickerLocalEngineProgressText.Text = message;
    }

    private void SetStickerRuntimeRemovalStatus(string message)
    {
        StickerLocalEngineStatusText.Text = "Runtime uninstall failed";
        SetStickerRemovalStatus(message);
    }

    private void SetStickerRuntimeCancellationStatus(string message)
    {
        StickerLocalEngineStatusText.Text = "Runtime uninstall canceled";
        SetStickerRemovalStatus(message);
    }

    private void ResetStickerModelRemovalGuardAfterCooldown()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LocalEngineProjectOpenCooldownMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _stickerModelRemovalInProgress = false;
            UpdateLocalEngineUi();
        };
        timer.Start();
    }

    private void StickerCopyErrorBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalStickerEngine();
        if (!TryGetStickerJobError(executionProvider, engine, out var error))
            return;

        try
        {
            ClipboardService.CopyTextToClipboard(error);
            ToastWindow.Show("Copied", "Sticker details copied to clipboard.");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the sticker details. Check Settings -> Stickers and try again.\n{ex.Message}");
        }
    }
}
