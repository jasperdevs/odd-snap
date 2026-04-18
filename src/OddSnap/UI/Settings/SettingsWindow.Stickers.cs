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
        if (!IsLoaded) return;
        ActiveStickerSettings.Provider = (StickerProvider)StickerProviderCombo.SelectedIndex;
        UpdateStickerProviderVisibility();
        UpdateLocalEngineUi();
        _settingsService.Save();
    }

    private void StickerLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateLocalEngineUi();
    }

    private void StickerLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
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
                return;

            bool removed = RembgRuntimeService.RemoveRuntime(executionProvider);
            if (removed)
                ToastWindow.Show("Sticker runtime", "Uninstalled rembg runtime.");
            else
                ToastWindow.ShowError("Sticker runtime error", "Couldn't uninstall the rembg runtime.");
            UpdateLocalEngineUi();
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

    private void StickerShadowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.AddShadow = StickerShadowCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void StickerStrokeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.AddStroke = StickerStrokeCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void StickerRemoveBgKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.RemoveBgApiKey = StickerRemoveBgKeyBox.Text;
        _settingsService.Save();
    }

    private void StickerPhotoroomKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveStickerSettings.PhotoroomApiKey = StickerPhotoroomKeyBox.Text;
        _settingsService.Save();
    }

    private void StickerDownloadRembgBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalStickerEngine();

        if (LocalStickerEngineService.IsModelDownloaded(engine))
        {
            bool removed = LocalStickerEngineService.RemoveDownloadedModel(engine);
            SetStickerDownloadUi(false, null, removed ? "Model removed." : "Couldn't remove the model.");
            if (removed)
                ToastWindow.Show("Sticker engine", "Removed the local sticker model.");
            else
                ToastWindow.ShowError("Sticker engine error", "Couldn't remove the local sticker model.");
            UpdateLocalEngineUi();
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
        try
        {
            var engine = GetSelectedLocalStickerEngine();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LocalStickerEngineService.GetProjectUrl(engine),
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void StickerRemoveAllModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemedConfirmDialog.Confirm(
                this,
                "Remove Models",
                "Remove all downloaded local sticker models?\n\nThey will be downloaded again the next time you use them.",
                "Remove",
                "Cancel"))
            return;

        bool removed = RembgRuntimeService.RemoveAllCachedModels();
        if (removed)
        {
            ToastWindow.Show("Sticker engine", "Removed all downloaded local sticker models.");
            UpdateLocalEngineUi();
        }
        else
        {
            ToastWindow.ShowError("Sticker engine error", "Couldn't remove the downloaded models.");
        }
    }

    private void StickerCopyErrorBtn_Click(object sender, RoutedEventArgs e)
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        var engine = GetSelectedLocalStickerEngine();
        if (!TryGetStickerJobError(executionProvider, engine, out var error))
            return;

        ClipboardService.CopyTextToClipboard(error);
        ToastWindow.Show("Copied", "Sticker error copied to clipboard.");
    }
}
