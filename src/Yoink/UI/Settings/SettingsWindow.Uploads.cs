using System.Windows;
using System.Windows.Controls;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private UploadSettings ActiveUploadSettings => _settingsService.Settings.ImageUploadSettings;
    private StickerSettings ActiveStickerSettings => _settingsService.Settings.StickerUploadSettings;
    private UpscaleSettings ActiveUpscaleSettings => _settingsService.Settings.UpscaleUploadSettings;

    private void LoadUploadSettingsIntoUi(UploadSettings s)
    {
        RebuildAiRedirectPanelUploadDestItems();
        SelectAiRedirectPanelProviderByValue((int)s.AiChatProvider);
        SelectAiRedirectPanelUploadDestByValue((int)UploadService.NormalizeAiChatUploadDestination(s.AiChatUploadDestination));
        AiRedirectLensUploadSyncCheck.IsChecked = s.AiChatUploadDestinationSynced;
        AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(_settingsService.Settings.AiRedirectHotkeyModifiers, _settingsService.Settings.AiRedirectHotkeyKey);
        ImgurClientIdBox.Text = s.ImgurClientId;
        ImgurTokenBox.Text = s.ImgurAccessToken;
        ImgBBKeyBox.Text = s.ImgBBApiKey;
        GyazoTokenBox.Text = s.GyazoAccessToken;
        DropboxTokenBox.Text = s.DropboxAccessToken;
        DropboxPathBox.Text = s.DropboxPathPrefix;
        GoogleDriveTokenBox.Text = s.GoogleDriveAccessToken;
        GoogleDriveFolderBox.Text = s.GoogleDriveFolderId;
        OneDriveTokenBox.Text = s.OneDriveAccessToken;
        OneDriveFolderBox.Text = s.OneDriveFolder;
        AzureBlobSasBox.Text = s.AzureBlobSasUrl;
        GitHubTokenBox.Text = s.GitHubToken;
        GitHubRepoBox.Text = s.GitHubRepo;
        GitHubBranchBox.Text = s.GitHubBranch;
        ImmichUrlBox.Text = s.ImmichBaseUrl;
        ImmichApiKeyBox.Text = s.ImmichApiKey;
        FtpUrlBox.Text = s.FtpUrl;
        FtpUsernameBox.Text = s.FtpUsername;
        FtpPasswordBox.Text = s.FtpPassword;
        FtpPublicUrlBox.Text = s.FtpPublicUrl;
        SftpHostBox.Text = s.SftpHost;
        SftpPortBox.Text = s.SftpPort.ToString();
        SftpUsernameBox.Text = s.SftpUsername;
        SftpPasswordBox.Text = s.SftpPassword;
        SftpRemotePathBox.Text = s.SftpRemotePath;
        SftpPublicUrlBox.Text = s.SftpPublicUrl;
        SftpHostKeyFingerprintBox.Text = s.SftpHostKeyFingerprint;
        WebDavUrlBox.Text = s.WebDavUrl;
        WebDavUsernameBox.Text = s.WebDavUsername;
        WebDavPasswordBox.Text = s.WebDavPassword;
        WebDavPublicUrlBox.Text = s.WebDavPublicUrl;
        S3EndpointBox.Text = s.S3Endpoint;
        S3BucketBox.Text = s.S3Bucket;
        S3RegionBox.Text = s.S3Region;
        S3AccessKeyBox.Text = s.S3AccessKey;
        S3SecretKeyBox.Text = s.S3SecretKey;
        S3PublicUrlBox.Text = s.S3PublicUrl;
        CustomUrlBox.Text = s.CustomUploadUrl;
        CustomFieldBox.Text = s.CustomFileFormName;
        CustomJsonPathBox.Text = s.CustomResponseUrlPath;
        CustomHeadersBox.Text = s.CustomHeaders;
    }

    private void LoadStickerSettingsIntoUi(StickerSettings s)
    {
        StickerProviderCombo.SelectedIndex = (int)s.Provider;
        StickerLocalExecutionCombo.SelectedIndex = (int)s.LocalExecutionProvider;
        SelectStickerEngine(StickerLocalCpuEngineCombo, s.LocalCpuEngine);
        SelectStickerEngine(StickerLocalGpuEngineCombo, s.LocalGpuEngine);
        StickerRemoveBgKeyBox.Text = s.RemoveBgApiKey;
        StickerPhotoroomKeyBox.Text = s.PhotoroomApiKey;
        StickerShadowCheck.IsChecked = s.AddShadow;
        StickerStrokeCheck.IsChecked = s.AddStroke;
        UpdateStickerProviderVisibility();
        UpdateStickerExecutionUi();
        UpdateLocalEngineUi();
    }

    private void LoadUpscaleSettingsIntoUi(UpscaleSettings s)
    {
        UpscaleProviderCombo.SelectedIndex = (int)s.Provider;
        UpscaleDeepAiApiKeyBox.Text = s.DeepAiApiKey;
        UpscaleLocalExecutionCombo.SelectedIndex = (int)s.LocalExecutionProvider;
        SelectUpscaleEngine(UpscaleLocalCpuEngineCombo, s.LocalCpuEngine);
        SelectUpscaleEngine(UpscaleLocalGpuEngineCombo, s.LocalGpuEngine);
        UpscaleShowPreviewWindowCheck.IsChecked = s.ShowPreviewWindow;
        UpdateUpscaleProviderVisibility();
        UpdateUpscaleExecutionUi();
        UpdateUpscaleLocalEngineUi();
        UpdateUpscaleDefaultScaleUi(s.GetActiveLocalEngine());
    }

    private void UpdateUploadTabVisibility()
    {
        ImageUploadsPanel.Visibility = UploadImagesSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AiRedirectUploadsPanel.Visibility = UploadAiRedirectsSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StickerUploadsPanel.Visibility = UploadStickersSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpscaleUploadsPanel.Visibility = UploadUpscaleSubTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStickerProviderVisibility()
    {
        var provider = (StickerProvider)StickerProviderCombo.SelectedIndex;
        StickerRemoveBgPanel.Visibility = provider == StickerProvider.RemoveBg ? Visibility.Visible : Visibility.Collapsed;
        StickerPhotoroomPanel.Visibility = provider == StickerProvider.Photoroom ? Visibility.Visible : Visibility.Collapsed;
        StickerLocalCpuPanel.Visibility = provider == StickerProvider.LocalCpu ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLocalEngineUi()
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        var engine = executionProvider == StickerExecutionProvider.Gpu
            ? GetSelectedStickerEngine(StickerLocalGpuEngineCombo)
            : GetSelectedStickerEngine(StickerLocalCpuEngineCombo);
        if (BackgroundRuntimeJobService.TryGetSnapshot(GetStickerRuntimeJobKey(executionProvider), out var runtimeJob) && runtimeJob.IsRunning)
        {
            StickerInstallDriversBtn.IsEnabled = false;
            StickerDownloadRembgBtn.IsEnabled = false;
            StickerOpenLocalEngineRepoBtn.IsEnabled = false;
            StickerRemoveAllModelsBtn.IsEnabled = false;
            StickerLocalEngineStatusText.Text = runtimeJob.Status;
            StickerLocalEngineProgress.IsIndeterminate = true;
            SetStickerDownloadUi(true, null, runtimeJob.Status);
            SetLoadingTextShimmer(StickerLocalEngineStatusText, true, 1.0, 0.35);
            SetLoadingTextShimmer(StickerLocalEngineProgressText, true, 0.9, 0.25);
            return;
        }

        if (BackgroundRuntimeJobService.TryGetSnapshot(GetStickerModelJobKey(engine), out var modelJob) && modelJob.IsRunning)
        {
            StickerInstallDriversBtn.IsEnabled = true;
            StickerLocalEngineStatusText.Text = modelJob.Status;
            StickerLocalEngineProgress.IsIndeterminate = true;
            SetStickerDownloadUi(true, null, modelJob.Status);
            SetLoadingTextShimmer(StickerLocalEngineStatusText, true, 1.0, 0.35);
            SetLoadingTextShimmer(StickerLocalEngineProgressText, true, 0.9, 0.25);
            return;
        }

        StickerLocalEngineProgress.IsIndeterminate = false;
        bool downloaded = LocalStickerEngineService.IsModelDownloaded(engine);
        var hasRuntimeFailure = BackgroundRuntimeJobService.TryGetSnapshot(GetStickerRuntimeJobKey(executionProvider), out runtimeJob) &&
                                runtimeJob is { LastSucceeded: false };
        var hasRuntimeStatus = RembgRuntimeService.TryGetCachedStatus(executionProvider, out var runtimeReady, out var runtimeStatus);

        if (hasRuntimeFailure)
        {
            StickerLocalEngineStatusText.Text = $"Failed: {runtimeJob.LastError}";
            StickerInstallDriversBtn.Content = RembgRuntimeService.GetSetupButtonText(executionProvider);
        }
        else if (hasRuntimeStatus && !runtimeReady)
        {
            StickerLocalEngineStatusText.Text = runtimeStatus;
            StickerInstallDriversBtn.Content = RembgRuntimeService.GetSetupButtonText(executionProvider);
        }
        else
        {
            StickerLocalEngineStatusText.Text = downloaded
                ? $"{LocalStickerEngineService.GetEngineLabel(engine)} is downloaded and ready to use for sticker captures."
                : $"{LocalStickerEngineService.GetQualityHint(engine)}: {LocalStickerEngineService.GetEngineDescription(engine)}";
            StickerInstallDriversBtn.Content = runtimeReady ? "Installed" : RembgRuntimeService.GetSetupButtonText(executionProvider);
        }

        StickerDownloadRembgBtn.Visibility = Visibility.Visible;
        StickerOpenLocalEngineRepoBtn.Content = "Open rembg";
        StickerDownloadRembgBtn.Content = downloaded ? "Remove model" : "Download model";
        StickerRemoveAllModelsBtn.Visibility = RembgRuntimeService.HasAnyCachedModels() ? Visibility.Visible : Visibility.Collapsed;
        StickerInstallDriversBtn.IsEnabled = true;
        SetStickerDownloadUi(false, null, null);
        SetLoadingTextShimmer(StickerLocalEngineStatusText, false, 1.0, 0.35);
        SetLoadingTextShimmer(StickerLocalEngineProgressText, false, 0.9, 0.25);

        if (!IsLoaded)
            return;

        ActiveStickerSettings.LocalExecutionProvider = executionProvider;
        ActiveStickerSettings.LocalEngine = engine;
        if (executionProvider == StickerExecutionProvider.Gpu)
            ActiveStickerSettings.LocalGpuEngine = engine;
        else
            ActiveStickerSettings.LocalCpuEngine = engine;
        _settingsService.Save();
    }

    private static LocalStickerEngine GetSelectedStickerEngine(System.Windows.Controls.ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse(tag, out LocalStickerEngine engine))
            return engine;

        return LocalStickerEngine.BriaRmbg;
    }

    private static void SelectStickerEngine(System.Windows.Controls.ComboBox combo, LocalStickerEngine engine)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && Enum.TryParse(tag, out LocalStickerEngine itemEngine) && itemEngine == engine)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void UpdateStickerExecutionUi()
    {
        var executionProvider = (StickerExecutionProvider)StickerLocalExecutionCombo.SelectedIndex;
        StickerLocalCpuEnginePanel.Visibility = executionProvider == StickerExecutionProvider.Cpu ? Visibility.Visible : Visibility.Collapsed;
        StickerLocalGpuEnginePanel.Visibility = executionProvider == StickerExecutionProvider.Gpu ? Visibility.Visible : Visibility.Collapsed;
        StickerLocalEngineStatusText.Text = RembgRuntimeService.GetRuntimeSummary(executionProvider);

        if (!IsLoaded)
            return;

        ActiveStickerSettings.LocalExecutionProvider = executionProvider;
        UpdateLocalEngineUi();
    }

    private void SetStickerDownloadUi(bool isBusy, double? percent = null, string? message = null)
    {
        StickerDownloadRembgBtn.IsEnabled = !isBusy;
        StickerOpenLocalEngineRepoBtn.IsEnabled = !isBusy;
        StickerRemoveAllModelsBtn.IsEnabled = !isBusy;

        StickerLocalEngineProgress.Visibility = isBusy || percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        StickerLocalEngineProgress.IsIndeterminate = isBusy && !percent.HasValue;
        var statusMessage = StickerLocalEngineStatusText.Text ?? string.Empty;
        var duplicateMessage = !string.IsNullOrWhiteSpace(message) &&
                               string.Equals(message.Trim(), statusMessage.Trim(), StringComparison.Ordinal);
        StickerLocalEngineProgressText.Visibility = (!duplicateMessage && !string.IsNullOrWhiteSpace(message)) || percent.HasValue ? Visibility.Visible : Visibility.Collapsed;

        if (percent.HasValue)
            StickerLocalEngineProgress.Value = Math.Clamp(percent.Value, 0, 100);
        else
            StickerLocalEngineProgress.Value = 0;

        if (!duplicateMessage && !string.IsNullOrWhiteSpace(message))
            StickerLocalEngineProgressText.Text = message;
        else if (!isBusy)
            StickerLocalEngineProgressText.Text = string.Empty;
    }

    private void UpdateUpscaleProviderVisibility()
    {
        var provider = (UpscaleProvider)UpscaleProviderCombo.SelectedIndex;
        UpscaleLocalPanel.Visibility = provider == UpscaleProvider.Local ? Visibility.Visible : Visibility.Collapsed;
        UpscaleDeepAiPanel.Visibility = provider is UpscaleProvider.DeepAiSuperResolution or UpscaleProvider.DeepAiWaifu2x ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUpscaleLocalEngineUi()
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        var engine = executionProvider == UpscaleExecutionProvider.Gpu
            ? GetSelectedUpscaleEngine(UpscaleLocalGpuEngineCombo)
            : GetSelectedUpscaleEngine(UpscaleLocalCpuEngineCombo);

        if (BackgroundRuntimeJobService.TryGetSnapshot(GetUpscaleRuntimeJobKey(executionProvider), out var runtimeJob) && runtimeJob.IsRunning)
        {
            UpscaleInstallDriversBtn.IsEnabled = false;
            UpscaleDownloadModelBtn.IsEnabled = false;
            UpscaleOpenLocalEngineRepoBtn.IsEnabled = false;
            UpscaleRemoveAllModelsBtn.IsEnabled = false;
            UpscaleLocalEngineStatusText.Text = runtimeJob.Status;
            UpscaleLocalEngineProgress.IsIndeterminate = true;
            SetUpscaleDownloadUi(true, null, runtimeJob.Status);
            SetLoadingTextShimmer(UpscaleLocalEngineStatusText, true, 1.0, 0.35);
            SetLoadingTextShimmer(UpscaleLocalEngineProgressText, true, 0.9, 0.25);
            return;
        }

        if (BackgroundRuntimeJobService.TryGetSnapshot(GetUpscaleModelJobKey(engine), out var modelJob) && modelJob.IsRunning)
        {
            UpscaleInstallDriversBtn.IsEnabled = true;
            UpscaleLocalEngineStatusText.Text = modelJob.Status;
            UpscaleLocalEngineProgress.IsIndeterminate = true;
            SetUpscaleDownloadUi(true, null, modelJob.Status);
            SetLoadingTextShimmer(UpscaleLocalEngineStatusText, true, 1.0, 0.35);
            SetLoadingTextShimmer(UpscaleLocalEngineProgressText, true, 0.9, 0.25);
            return;
        }

        UpscaleLocalEngineProgress.IsIndeterminate = false;
        bool downloaded = LocalUpscaleEngineService.IsModelDownloaded(engine);
        var hasRuntimeFailure = BackgroundRuntimeJobService.TryGetSnapshot(GetUpscaleRuntimeJobKey(executionProvider), out runtimeJob) &&
                                runtimeJob is { LastSucceeded: false };
        var hasRuntimeStatus = UpscaleRuntimeService.TryGetCachedStatus(executionProvider, out var runtimeReady, out var runtimeStatus);

        if (hasRuntimeFailure)
        {
            UpscaleLocalEngineStatusText.Text = $"Failed: {runtimeJob.LastError}";
            UpscaleInstallDriversBtn.Content = UpscaleRuntimeService.GetSetupButtonText(executionProvider);
        }
        else if (hasRuntimeStatus && !runtimeReady)
        {
            UpscaleLocalEngineStatusText.Text = runtimeStatus;
            UpscaleInstallDriversBtn.Content = UpscaleRuntimeService.GetSetupButtonText(executionProvider);
        }
        else
        {
            UpscaleLocalEngineStatusText.Text = downloaded
                ? $"{LocalUpscaleEngineService.GetEngineLabel(engine)} is downloaded and ready to use for upscale captures."
                : $"{LocalUpscaleEngineService.GetQualityHint(engine)}: {LocalUpscaleEngineService.GetEngineDescription(engine)}";
            UpscaleInstallDriversBtn.Content = runtimeReady ? "Installed" : UpscaleRuntimeService.GetSetupButtonText(executionProvider);
        }

        UpscaleDownloadModelBtn.Visibility = Visibility.Visible;
        UpscaleOpenLocalEngineRepoBtn.Content = "Open project";
        UpscaleDownloadModelBtn.Content = downloaded ? "Remove model" : "Download model";
        UpscaleRemoveAllModelsBtn.Visibility = UpscaleRuntimeService.HasAnyCachedModels() ? Visibility.Visible : Visibility.Collapsed;
        UpscaleInstallDriversBtn.IsEnabled = true;
        SetUpscaleDownloadUi(false, null, null);
        SetLoadingTextShimmer(UpscaleLocalEngineStatusText, false, 1.0, 0.35);
        SetLoadingTextShimmer(UpscaleLocalEngineProgressText, false, 0.9, 0.25);

        if (!IsLoaded)
            return;

        ActiveUpscaleSettings.LocalExecutionProvider = executionProvider;
        ActiveUpscaleSettings.LocalEngine = engine;
        if (executionProvider == UpscaleExecutionProvider.Gpu)
            ActiveUpscaleSettings.LocalGpuEngine = engine;
        else
            ActiveUpscaleSettings.LocalCpuEngine = engine;
        ClampUpscaleDefaultScaleToSelectedEngine(engine);
        UpdateUpscaleDefaultScaleUi(engine);
        _settingsService.Save();
    }

    private static LocalUpscaleEngine GetSelectedUpscaleEngine(System.Windows.Controls.ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag && Enum.TryParse(tag, out LocalUpscaleEngine engine))
            return engine;

        return LocalUpscaleEngine.SwinIrRealWorld;
    }

    private static void SelectUpscaleEngine(System.Windows.Controls.ComboBox combo, LocalUpscaleEngine engine)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && Enum.TryParse(tag, out LocalUpscaleEngine itemEngine) && itemEngine == engine)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void UpdateUpscaleExecutionUi()
    {
        var executionProvider = (UpscaleExecutionProvider)UpscaleLocalExecutionCombo.SelectedIndex;
        UpscaleLocalCpuEnginePanel.Visibility = executionProvider == UpscaleExecutionProvider.Cpu ? Visibility.Visible : Visibility.Collapsed;
        UpscaleLocalGpuEnginePanel.Visibility = executionProvider == UpscaleExecutionProvider.Gpu ? Visibility.Visible : Visibility.Collapsed;
        UpscaleLocalEngineStatusText.Text = UpscaleRuntimeService.GetRuntimeSummary(executionProvider);

        if (!IsLoaded)
            return;

        ActiveUpscaleSettings.LocalExecutionProvider = executionProvider;
        UpdateUpscaleLocalEngineUi();
    }

    private void SetUpscaleDownloadUi(bool isBusy, double? percent = null, string? message = null)
    {
        UpscaleDownloadModelBtn.IsEnabled = !isBusy;
        UpscaleOpenLocalEngineRepoBtn.IsEnabled = !isBusy;
        UpscaleRemoveAllModelsBtn.IsEnabled = !isBusy;

        UpscaleLocalEngineProgress.Visibility = isBusy || percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpscaleLocalEngineProgress.IsIndeterminate = isBusy && !percent.HasValue;
        var statusMessage = UpscaleLocalEngineStatusText.Text ?? string.Empty;
        var duplicateMessage = !string.IsNullOrWhiteSpace(message) &&
                               string.Equals(message.Trim(), statusMessage.Trim(), StringComparison.Ordinal);
        UpscaleLocalEngineProgressText.Visibility = (!duplicateMessage && !string.IsNullOrWhiteSpace(message)) || percent.HasValue ? Visibility.Visible : Visibility.Collapsed;

        if (percent.HasValue)
            UpscaleLocalEngineProgress.Value = Math.Clamp(percent.Value, 0, 100);
        else
            UpscaleLocalEngineProgress.Value = 0;

        if (!duplicateMessage && !string.IsNullOrWhiteSpace(message))
            UpscaleLocalEngineProgressText.Text = message;
        else if (!isBusy)
            UpscaleLocalEngineProgressText.Text = string.Empty;
    }

    private void UpdateUpscaleDefaultScaleUi(LocalUpscaleEngine engine)
    {
        if (UpscaleDefaultScaleCombo is null)
            return;

        int minScale = LocalUpscaleEngineService.GetMinScaleFactor(engine);
        int maxScale = LocalUpscaleEngineService.GetScaleFactor(engine);
        int selectedScale = Math.Clamp(ActiveUpscaleSettings.ScaleFactor, minScale, maxScale);

        UpscaleDefaultScaleCombo.Items.Clear();
        for (int scale = minScale; scale <= maxScale; scale++)
        {
            UpscaleDefaultScaleCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{scale}x",
                Tag = scale.ToString()
            });
        }

        foreach (var item in UpscaleDefaultScaleCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == selectedScale.ToString())
            {
                UpscaleDefaultScaleCombo.SelectedItem = item;
                break;
            }
        }

        UpscaleDefaultScaleCombo.IsEnabled = !(UpscaleShowPreviewWindowCheck.IsChecked == true);
        UpscaleDefaultScaleHelpText.Opacity = UpscaleDefaultScaleCombo.IsEnabled ? 0.32 : 0.2;
    }

    private void ClampUpscaleDefaultScaleToSelectedEngine(LocalUpscaleEngine engine)
    {
        int minScale = LocalUpscaleEngineService.GetMinScaleFactor(engine);
        int maxScale = LocalUpscaleEngineService.GetScaleFactor(engine);
        ActiveUpscaleSettings.ScaleFactor = Math.Clamp(ActiveUpscaleSettings.ScaleFactor, minScale, maxScale);
    }
}
