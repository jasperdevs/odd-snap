using System.Windows;
using System.Windows.Controls;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private void UpscaleProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUpscaleSettings.Provider = (UpscaleProvider)UpscaleProviderCombo.SelectedIndex;
        UpdateUpscaleProviderVisibility();
        UpdateUpscaleLocalEngineUi();
        _settingsService.Save();
    }

    private void UpscaleDeepAiApiKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUpscaleSettings.DeepAiApiKey = UpscaleDeepAiApiKeyBox.Text;
        _settingsService.Save();
    }

    private void UpscaleLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateUpscaleLocalEngineUi();
    }

    private void UpscaleLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateUpscaleLocalEngineUi();
    }

    private void UpscaleLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateUpscaleExecutionUi();
    }

    private void UpscaleDefaultScaleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (UpscaleDefaultScaleCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var scale))
        {
            ActiveUpscaleSettings.ScaleFactor = scale;
            _settingsService.Save();
        }
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
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
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

    private void UpscaleDownloadModelBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalUpscaleEngine();

        if (LocalUpscaleEngineService.IsModelDownloaded(engine))
        {
            bool removed = LocalUpscaleEngineService.RemoveDownloadedModel(engine);
            SetUpscaleDownloadUi(false, null, removed ? "Model removed." : "Couldn't remove the model.");
            if (removed)
                ToastWindow.Show("Upscale engine", "Removed the local upscale model.");
            else
                ToastWindow.ShowError("Upscale engine error", "Couldn't remove the local upscale model.");
            UpdateUpscaleLocalEngineUi();
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
        try
        {
            var engine = GetSelectedLocalUpscaleEngine();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LocalUpscaleEngineService.GetProjectUrl(engine),
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void UpscaleRemoveAllModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Remove Models",
                "Remove all downloaded local upscale models?\n\nThey will be downloaded again the next time you use them.",
                "Remove",
                "Cancel"))
            return;

        bool removed = UpscaleRuntimeService.RemoveAllCachedModels();
        if (removed)
        {
            ToastWindow.Show("Upscale engine", "Removed all downloaded local upscale models.");
            UpdateUpscaleLocalEngineUi();
        }
        else
        {
            ToastWindow.ShowError("Upscale engine error", "Couldn't remove the downloaded models.");
        }
    }

    private void UpscaleCopyErrorBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalUpscaleEngine();
        if (!TryGetUpscaleJobError(executionProvider, engine, out var error))
            return;

        ClipboardService.CopyTextToClipboard(error);
        ToastWindow.Show("Copied", "Upscale error copied to clipboard.");
    }
}
