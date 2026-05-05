using System.Windows;
using System.Windows.Controls;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private void UpscaleProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;

        var previousProvider = ActiveUpscaleSettings.Provider;
        var selectedProvider = (UpscaleProvider)UpscaleProviderCombo.SelectedIndex;

        try
        {
            ActiveUpscaleSettings.Provider = selectedProvider;
            UpdateUpscaleProviderVisibility();
            UpdateUpscaleLocalEngineUi();
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.upscale-provider", ex);
            ActiveUpscaleSettings.Provider = previousProvider;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.upscale-provider-rollback", rollbackEx);
            }

            _suppressUpscaleSettingChange = true;
            try
            {
                UpscaleProviderCombo.SelectedIndex = (int)previousProvider;
                UpdateUpscaleProviderVisibility();
                UpdateUpscaleExecutionUi();
                UpdateUpscaleLocalEngineUi();
            }
            finally
            {
                _suppressUpscaleSettingChange = false;
            }

            ShowUpscaleProviderSaveFailed(ex);
        }
    }

    private void ShowUpscaleProviderSaveFailed(Exception ex)
    {
        SetUpscaleRemovalStatus("Upscale provider change was not saved. Previous provider restored.");
        ToastWindow.ShowError(
            "Upscale provider failed",
            $"The previous upscale provider was restored. Check Settings -> Upscale and try again.\n{ex.Message}");
    }

    private void UpscaleDeepAiApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUpscalePasswordSetting(
            UpscaleDeepAiApiKeyBox,
            "DeepAI API key",
            "deepai-key",
            () => ActiveUpscaleSettings.DeepAiApiKey,
            value => ActiveUpscaleSettings.DeepAiApiKey = value);
    }

    private void UpscaleLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;
        UpdateUpscaleLocalEngineUi();
    }

    private void UpscaleLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;
        UpdateUpscaleLocalEngineUi();
    }

    private void UpscaleLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;
        UpdateUpscaleExecutionUi();
    }

    private void UpscaleDefaultScaleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;
        if (UpscaleDefaultScaleCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var scale))
        {
            var previousScale = ActiveUpscaleSettings.ScaleFactor;

            try
            {
                ActiveUpscaleSettings.ScaleFactor = scale;
                _settingsService.Save();
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogError("settings.upscale-default-scale", ex);
                ActiveUpscaleSettings.ScaleFactor = previousScale;
                try
                {
                    _settingsService.Save();
                }
                catch (Exception rollbackEx)
                {
                    AppDiagnostics.LogError("settings.upscale-default-scale-rollback", rollbackEx);
                }

                _suppressUpscaleSettingChange = true;
                try
                {
                    UpdateUpscaleDefaultScaleUi(ActiveUpscaleSettings.GetActiveLocalEngine());
                }
                finally
                {
                    _suppressUpscaleSettingChange = false;
                }

                ShowUpscaleSettingSaveFailed("Default scale", ex);
            }
        }
    }

    private void UpdateUpscalePasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string label, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)
    {
        if (!IsLoaded || _suppressUpscaleSettingChange) return;

        var previous = getValue();

        try
        {
            setValue(passwordBox.Password);
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError($"settings.upscale-{diagnosticSuffix}", ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"settings.upscale-{diagnosticSuffix}-rollback", rollbackEx);
            }

            _suppressUpscaleSettingChange = true;
            try
            {
                passwordBox.Password = previous;
            }
            finally
            {
                _suppressUpscaleSettingChange = false;
            }

            ShowUpscaleSettingSaveFailed(label, ex);
        }
    }

    private void ShowUpscaleSettingSaveFailed(string label, Exception ex)
    {
        SetUpscaleRemovalStatus($"{label} change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            "Upscale setting failed",
            $"The previous upscale setting was restored. Check Settings -> Upscale and try again.\n{ex.Message}");
    }

    private LocalUpscaleEngine GetSelectedLocalUpscaleEngine()
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        return executionProvider == UpscaleExecutionProvider.Gpu
            ? GetSelectedUpscaleEngine(UpscaleLocalGpuEngineCombo)
            : GetSelectedUpscaleEngine(UpscaleLocalCpuEngineCombo);
    }

    private void UpscaleInstallDriversBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_upscaleRuntimeMutationInProgress)
            return;

        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        if (UpscaleRuntimeService.TryGetCachedStatus(executionProvider, out var runtimeReady, out _) && runtimeReady)
        {
            if (!ThemedConfirmDialog.Confirm(
                    this,
                    "Uninstall runtime",
                    $"Uninstall the {UpscaleRuntimeService.GetSetupTargetName(executionProvider)} runtime?\n\nDownloaded models stay available, but this runtime will need to be installed again before local upscale captures can use it.",
                    "Uninstall",
                    "Cancel",
                    danger: true))
            {
                SetUpscaleRuntimeCancellationStatus("Upscale runtime uninstall canceled. Runtime was left installed.");
                return;
            }

            bool removed = false;
            var completed = RunUpscaleRuntimeMutation(() =>
            {
                removed = UpscaleRuntimeService.RemoveRuntime(executionProvider);
                if (removed)
                    ToastWindow.Show("Upscale runtime", "Uninstalled the upscale runtime.");
                else
                    ToastWindow.ShowError(
                        "Upscale runtime error",
                        "The upscale runtime was not removed. Close active upscale captures and try again from Settings -> Upscale.");
            });
            if (completed && !removed)
                SetUpscaleRuntimeRemovalStatus("Upscale runtime was not removed. Close active upscale captures and try again.");
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                GetUpscaleRuntimeJobKey(executionProvider),
                UpscaleRuntimeService.GetSetupTargetName(executionProvider),
                $"Installing {UpscaleRuntimeService.GetSetupTargetName(executionProvider)}...",
                "Upscale runtime ready",
                $"{UpscaleRuntimeService.GetSetupTargetName(executionProvider)} finished installing.",
                "Upscale runtime setup failed"),
            (progress, cancellationToken) => UpscaleRuntimeService.EnsureInstalledAsync(executionProvider, progress, cancellationToken));

        if (!started)
            ToastWindow.Show("Upscale runtime", "That setup is already running in the background.");

        UpdateUpscaleLocalEngineUi();
    }

    private bool RunUpscaleRuntimeMutation(Action mutation)
    {
        if (_upscaleRuntimeMutationInProgress)
            return false;

        string? failureMessage = null;
        bool completed = false;
        _upscaleRuntimeMutationInProgress = true;
        UpscaleInstallDriversBtn.IsEnabled = false;
        UpscaleDownloadModelBtn.IsEnabled = false;
        UpscaleRemoveAllModelsBtn.IsEnabled = false;
        try
        {
            mutation();
            completed = true;
        }
        catch (Exception ex)
        {
            failureMessage = "Upscale runtime action failed. Check Settings -> Upscale and try again.";
            ToastWindow.ShowError(
                "Upscale runtime error",
                $"The upscale runtime action did not finish. Check Settings -> Upscale and try again.\n{ex.Message}");
        }
        finally
        {
            _upscaleRuntimeMutationInProgress = false;
            UpdateUpscaleLocalEngineUi();
            if (!string.IsNullOrWhiteSpace(failureMessage))
                SetUpscaleRuntimeRemovalStatus(failureMessage);
        }

        return completed;
    }

    private void UpscaleDownloadModelBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalUpscaleEngine();

        if (LocalUpscaleEngineService.IsModelDownloaded(engine))
        {
            var engineLabel = LocalUpscaleEngineService.GetEngineLabel(engine);
            if (!ThemedConfirmDialog.Confirm(
                    this,
                    "Remove upscale model",
                    $"Remove the downloaded {engineLabel} upscale model?\n\nIt will need to be downloaded again before local upscale captures can use it.",
                    "Remove",
                    "Cancel"))
            {
                SetUpscaleRemovalStatus($"Upscale model removal canceled. Kept {engineLabel}.");
                return;
            }

            RunUpscaleModelRemoval(() =>
            {
                bool removed = LocalUpscaleEngineService.RemoveDownloadedModel(engine);
                SetUpscaleRemovalStatus(removed ? "Model removed." : "Upscale model was not removed. Check Settings -> Upscale and try again.");
                if (removed)
                    ToastWindow.Show("Upscale engine", "Removed the local upscale model.");
                else
                    ToastWindow.ShowError("Upscale engine error", "OddSnap could not remove the local upscale model. Try again from Settings -> Upscale, or remove the model files manually.");
            });
            return;
        }

        var started = BackgroundRuntimeJobService.Start(
            new BackgroundRuntimeJobOptions(
                GetUpscaleModelJobKey(engine),
                LocalUpscaleEngineService.GetEngineLabel(engine),
                $"Preparing {LocalUpscaleEngineService.GetEngineLabel(engine)}...",
                "Upscale model ready",
                $"Downloaded {LocalUpscaleEngineService.GetEngineLabel(engine)}.",
                "Upscale model download failed"),
            async (progress, cancellationToken) =>
            {
                var modelProgress = new Progress<LocalUpscaleEngineDownloadProgress>(p => progress.Report(p.StatusMessage));
                var result = await LocalUpscaleEngineService.DownloadModelAsync(engine, executionProvider, modelProgress, cancellationToken);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ModelPath))
                    throw new InvalidOperationException(result.Message);
            });

        if (!started)
            ToastWindow.Show("Upscale engine", "That model is already downloading in the background.");

        UpdateUpscaleLocalEngineUi();
    }

    private void UpscaleOpenLocalEngineRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_upscaleProjectOpenInProgress)
            return;

        _upscaleProjectOpenInProgress = true;
        UpscaleOpenLocalEngineRepoBtn.IsEnabled = false;
        try
        {
            var engine = GetSelectedLocalUpscaleEngine();
            OpenUpscaleProjectUrl(LocalUpscaleEngineService.GetProjectUrl(engine));
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open project failed",
                $"OddSnap could not open the upscale project link. Check Settings -> Upscale and try again.\n{ex.Message}");
        }
        finally
        {
            ResetUpscaleProjectOpenGuardAfterCooldown();
        }
    }

    private static bool OpenUpscaleProjectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open project failed", "No upscale project link is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open project failed", "The upscale project link is not a valid web link.");
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
                ToastWindow.ShowError("Open project failed", "Windows did not open the upscale project link. Copy the link from Settings -> Upscale and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Open project failed",
                $"Windows could not open the upscale project link. Copy the link from Settings -> Upscale and open it manually.\n{ex.Message}");
            return false;
        }
    }

    private void ResetUpscaleProjectOpenGuardAfterCooldown()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LocalEngineProjectOpenCooldownMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _upscaleProjectOpenInProgress = false;
            UpdateUpscaleLocalEngineUi();
        };
        timer.Start();
    }

    private void UpscaleRemoveAllModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_upscaleModelRemovalInProgress)
            return;

        if (!ThemedConfirmDialog.Confirm(
                this,
                "Remove Models",
                "Remove all downloaded local upscale models?\n\nThey will be downloaded again the next time you use them.",
                "Remove",
                "Cancel"))
        {
            SetUpscaleRemovalStatus("Upscale model removal canceled. Downloaded models were left in place.");
            return;
        }

        RunUpscaleModelRemoval(() =>
        {
            bool removed = UpscaleRuntimeService.RemoveAllCachedModels();
            SetUpscaleRemovalStatus(removed ? "All models removed." : "Upscale models were not removed. Check Settings -> Upscale and try again.");
            if (removed)
                ToastWindow.Show("Upscale engine", "Removed all downloaded local upscale models.");
            else
                ToastWindow.ShowError("Upscale engine error", "OddSnap could not remove the downloaded upscale models. Try again from Settings -> Upscale, or remove the model files manually.");
        });
    }

    private void RunUpscaleModelRemoval(Action removeAction)
    {
        if (_upscaleModelRemovalInProgress)
            return;

        _upscaleModelRemovalInProgress = true;
        UpscaleDownloadModelBtn.IsEnabled = false;
        UpscaleRemoveAllModelsBtn.IsEnabled = false;
        try
        {
            removeAction();
        }
        catch (Exception ex)
        {
            SetUpscaleRemovalStatus("Upscale model removal failed. Check Settings -> Upscale and try again.");
            ToastWindow.ShowError(
                "Upscale engine error",
                $"The local upscale model files were not removed. Check Settings -> Upscale and try again.\n{ex.Message}");
        }
        finally
        {
            ResetUpscaleModelRemovalGuardAfterCooldown();
        }
    }

    private void SetUpscaleRemovalStatus(string message)
    {
        UpscaleLocalEngineProgress.Visibility = Visibility.Collapsed;
        UpscaleLocalEngineProgress.IsIndeterminate = false;
        UpscaleLocalEngineProgress.Value = 0;
        UpscaleLocalEngineProgressText.Visibility = Visibility.Visible;
        UpscaleLocalEngineProgressText.Text = message;
    }

    private void SetUpscaleRuntimeRemovalStatus(string message)
    {
        UpscaleLocalEngineStatusText.Text = "Runtime uninstall failed";
        SetUpscaleRemovalStatus(message);
    }

    private void SetUpscaleRuntimeCancellationStatus(string message)
    {
        UpscaleLocalEngineStatusText.Text = "Runtime uninstall canceled";
        SetUpscaleRemovalStatus(message);
    }

    private void ResetUpscaleModelRemovalGuardAfterCooldown()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LocalEngineProjectOpenCooldownMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _upscaleModelRemovalInProgress = false;
            UpdateUpscaleLocalEngineUi();
        };
        timer.Start();
    }

    private void UpscaleCopyErrorBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalUpscaleEngine();
        if (!TryGetUpscaleJobError(executionProvider, engine, out var error))
            return;

        try
        {
            ClipboardService.CopyTextToClipboard(error);
            ToastWindow.Show("Copied", "Upscale details copied to clipboard.");
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the upscale details. Check Settings -> Upscale and try again.\n{ex.Message}");
        }
    }
}
