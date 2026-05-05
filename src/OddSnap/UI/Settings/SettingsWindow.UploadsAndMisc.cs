using System.Drawing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public partial class SettingsWindow
{
    private const double SettingsComboItemWidth = 300;
    private const double SettingsComboTextWidth = 256;
    private static DataTemplate? s_settingsComboItemTemplate;

    private Services.UploadDestination GetSelectedUploadDest()
    {
        if (UploadDestCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var val))
            return (Services.UploadDestination)val;
        return Services.UploadDestination.None;
    }

    private void UploadDestCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUploadDestChange) return;

        var previousDestination = _settingsService.Settings.ImageUploadDestination;
        var previousAiChatUploadDestination = ActiveUploadSettings.AiChatUploadDestination;
        var selectedDestination = GetSelectedUploadDest();

        try
        {
            _settingsService.Settings.ImageUploadDestination = selectedDestination;
            if (UploadDestCombo.SelectedItem is ComboBoxItem selected)
                UploadDestCombo.Text = GetUploadDestinationFilterText(selected);
            if (ActiveUploadSettings.AiChatUploadDestinationSynced)
                ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(selectedDestination);
            _settingsService.Save();
            UpdateUploadSettingsVisibility();
            UpdateAiRedirectPanelVisibility();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.upload-destination", ex);
            _settingsService.Settings.ImageUploadDestination = previousDestination;
            ActiveUploadSettings.AiChatUploadDestination = previousAiChatUploadDestination;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.upload-destination-rollback", rollbackEx);
            }

            _suppressUploadDestChange = true;
            try
            {
                SelectUploadDestByTag((int)previousDestination);
            }
            finally
            {
                _suppressUploadDestChange = false;
            }

            try
            {
                UpdateUploadSettingsVisibility();
                UpdateAiRedirectPanelVisibility();
            }
            catch (Exception restoreEx)
            {
                AppDiagnostics.LogError("settings.upload-destination-restore", restoreEx);
            }

            ShowUploadDestinationSaveFailed(ex);
        }
    }

    private void ShowUploadDestinationSaveFailed(Exception ex)
    {
        SetTestUploadStatus("Upload destination change was not saved. Previous destination restored.");
        ToastWindow.ShowError(
            "Upload destination failed",
            $"The previous upload destination was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private void UpdateUploadSettingsVisibility()
    {
        var dest = GetSelectedUploadDest();
        ImgurSettings.Visibility = dest == Services.UploadDestination.Imgur ? Visibility.Visible : Visibility.Collapsed;
        ImgBBSettings.Visibility = dest == Services.UploadDestination.ImgBB ? Visibility.Visible : Visibility.Collapsed;
        ImgPileSettings.Visibility = dest == Services.UploadDestination.ImgPile ? Visibility.Visible : Visibility.Collapsed;
        CatboxSettings.Visibility = dest == Services.UploadDestination.Catbox ? Visibility.Visible : Visibility.Collapsed;
        LitterboxSettings.Visibility = dest == Services.UploadDestination.Litterbox ? Visibility.Visible : Visibility.Collapsed;
        GyazoSettings.Visibility = dest == Services.UploadDestination.Gyazo ? Visibility.Visible : Visibility.Collapsed;
        FileIoSettings.Visibility = dest == Services.UploadDestination.FileIo ? Visibility.Visible : Visibility.Collapsed;
        UguuSettings.Visibility = dest == Services.UploadDestination.Uguu ? Visibility.Visible : Visibility.Collapsed;
        TmpFilesSettings.Visibility = dest == Services.UploadDestination.TmpFiles ? Visibility.Visible : Visibility.Collapsed;
        GofileSettings.Visibility = dest == Services.UploadDestination.Gofile ? Visibility.Visible : Visibility.Collapsed;
        TransferSettings.Visibility = dest == Services.UploadDestination.TransferSh ? Visibility.Visible : Visibility.Collapsed;
        DropboxSettings.Visibility = dest == Services.UploadDestination.Dropbox ? Visibility.Visible : Visibility.Collapsed;
        GoogleDriveSettings.Visibility = dest == Services.UploadDestination.GoogleDrive ? Visibility.Visible : Visibility.Collapsed;
        OneDriveSettings.Visibility = dest == Services.UploadDestination.OneDrive ? Visibility.Visible : Visibility.Collapsed;
        AzureSettings.Visibility = dest == Services.UploadDestination.AzureBlob ? Visibility.Visible : Visibility.Collapsed;
        GitHubSettings.Visibility = dest == Services.UploadDestination.GitHub ? Visibility.Visible : Visibility.Collapsed;
        ImmichSettings.Visibility = dest == Services.UploadDestination.Immich ? Visibility.Visible : Visibility.Collapsed;
        FtpSettings.Visibility = dest == Services.UploadDestination.Ftp ? Visibility.Visible : Visibility.Collapsed;
        SftpSettings.Visibility = dest == Services.UploadDestination.Sftp ? Visibility.Visible : Visibility.Collapsed;
        WebDavSettings.Visibility = dest == Services.UploadDestination.WebDav ? Visibility.Visible : Visibility.Collapsed;
        S3Settings.Visibility = dest == Services.UploadDestination.S3Compatible ? Visibility.Visible : Visibility.Collapsed;
        CustomUploadSettings.Visibility = dest == Services.UploadDestination.CustomHttp ? Visibility.Visible : Visibility.Collapsed;
        UpdateTestUploadAvailability(dest);
        AutoUploadHeader.Visibility = Visibility.Visible;
        AutoUploadCard.Visibility = Visibility.Visible;
    }

    private void UpdateTestUploadAvailability(Services.UploadDestination? selectedDestination = null)
    {
        var dest = selectedDestination ?? GetSelectedUploadDest();
        var available = CanTestUploadDestination(dest);
        TestUploadCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        TestUploadBtn.IsEnabled = available && !_testUploadInProgress;

        if (!available)
            SetTestUploadStatus(string.Empty);
    }

    private bool CanTestUploadDestination(Services.UploadDestination dest)
    {
        return dest switch
        {
            Services.UploadDestination.None => false,
            _ when Services.UploadService.IsAiChatDestination(dest) => GetSelectedAiRedirectPanelProvider() != Services.AiChatProvider.None,
            _ => true
        };
    }

    private void AutoUploadScreenshotsCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoUploadSetting(
            AutoUploadScreenshotsCheck,
            "screenshots",
            "screenshots",
            () => _settingsService.Settings.AutoUploadScreenshots,
            value => _settingsService.Settings.AutoUploadScreenshots = value);
    }

    private void AutoUploadGifsCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoUploadSetting(
            AutoUploadGifsCheck,
            "GIFs",
            "gifs",
            () => _settingsService.Settings.AutoUploadGifs,
            value => _settingsService.Settings.AutoUploadGifs = value);
    }

    private void AutoUploadVideosCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoUploadSetting(
            AutoUploadVideosCheck,
            "videos",
            "videos",
            () => _settingsService.Settings.AutoUploadVideos,
            value => _settingsService.Settings.AutoUploadVideos = value);
    }

    private void UpdateAutoUploadSetting(System.Windows.Controls.CheckBox checkBox, string label, string diagnosticSuffix, Func<bool> getValue, Action<bool> setValue)
    {
        if (!IsLoaded || _suppressAutoUploadChange) return;

        var previous = getValue();
        var enabled = checkBox.IsChecked == true;

        try
        {
            setValue(enabled);
            _settingsService.Save();
            SetAutoUploadStatus($"Auto-upload {label} {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError($"settings.auto-upload-{diagnosticSuffix}", ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"settings.auto-upload-{diagnosticSuffix}-rollback", rollbackEx);
            }

            _suppressAutoUploadChange = true;
            try
            {
                checkBox.IsChecked = previous;
            }
            finally
            {
                _suppressAutoUploadChange = false;
            }

            ShowAutoUploadSaveFailed(label, ex);
        }
    }

    private void ShowAutoUploadSaveFailed(string label, Exception ex)
    {
        SetAutoUploadStatus($"Auto-upload {label} change was not saved. Previous setting restored.");
        ToastWindow.ShowError(
            "Auto-upload setting failed",
            $"The {label} auto-upload setting was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private void SetAutoUploadStatus(string message)
    {
        AutoUploadStatusText.Text = message;
        AutoUploadStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private Services.AiChatProvider GetSelectedAiRedirectPanelProvider()
    {
        if (AiRedirectProviderCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var value))
        {
            if (value == (int)Services.AiChatProvider.ClaudeOpus)
                return Services.AiChatProvider.Claude;
            return (Services.AiChatProvider)value;
        }
        return Services.AiChatProvider.GoogleLens;
    }

    private Services.UploadDestination GetSelectedAiRedirectPanelUploadDest()
    {
        if (AiRedirectLensUploadSyncCheck.IsChecked == true)
            return Services.UploadService.NormalizeAiChatUploadDestination(GetSelectedUploadDest());

        if (AiRedirectLensUploadDestPanelCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var value))
            return Services.UploadService.NormalizeAiChatUploadDestination((Services.UploadDestination)value);

        return Services.UploadDestination.TempHosts;
    }

    private void UpdateAiRedirectPanelVisibility()
    {
        var isLens = GetSelectedAiRedirectPanelProvider() == Services.AiChatProvider.GoogleLens;
        AiRedirectLensUploadHostPanelRow.Visibility = isLens ? Visibility.Visible : Visibility.Collapsed;
        AiRedirectLensUploadPanelHint.Visibility = isLens ? Visibility.Visible : Visibility.Collapsed;
        AiRedirectLensUploadDestPanelCombo.IsEnabled = isLens && AiRedirectLensUploadSyncCheck.IsChecked != true;
        if (isLens && AiRedirectLensUploadSyncCheck.IsChecked == true)
            SelectAiRedirectPanelUploadDestByValue((int)GetSelectedUploadDest());
        UpdateAiRedirectTestAvailability();
    }

    private void UpdateAiRedirectTestAvailability()
    {
        var available = GetSelectedAiRedirectPanelProvider() != Services.AiChatProvider.None;
        AiRedirectTestCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        AiRedirectTestBtn.IsEnabled = available && !_aiRedirectTestInProgress;

        if (!available)
            SetAiRedirectTestStatus(string.Empty);
    }

    private void AiRedirectProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressAiRedirectProviderChange) return;

        var previousProvider = ActiveUploadSettings.AiChatProvider;
        var selectedProvider = GetSelectedAiRedirectPanelProvider();

        try
        {
            ActiveUploadSettings.AiChatProvider = selectedProvider;
            UpdateAiRedirectPanelVisibility();
            UpdateTestUploadAvailability();
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-provider", ex);
            ActiveUploadSettings.AiChatProvider = previousProvider;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-provider-rollback", rollbackEx);
            }

            _suppressAiRedirectProviderChange = true;
            try
            {
                SelectAiRedirectPanelProviderByValue((int)previousProvider);
            }
            catch (Exception restoreEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-provider-restore-selection", restoreEx);
            }
            finally
            {
                _suppressAiRedirectProviderChange = false;
            }

            try
            {
                UpdateAiRedirectPanelVisibility();
                UpdateTestUploadAvailability();
            }
            catch (Exception restoreEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-provider-restore", restoreEx);
            }

            ShowAiRedirectProviderSaveFailed(ex);
        }
    }

    private void ShowAiRedirectProviderSaveFailed(Exception ex)
    {
        SetAiRedirectTestStatus("AI redirect provider change was not saved. Previous provider restored.");
        ToastWindow.ShowError(
            "AI redirect provider failed",
            $"The previous AI redirect provider was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private void AiRedirectLensUploadDestPanelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressAiRedirectLensUploadDestChange) return;
        if (AiRedirectLensUploadSyncCheck.IsChecked == true)
            return;

        var previousDestination = ActiveUploadSettings.AiChatUploadDestination;
        var selectedDestination = GetSelectedAiRedirectPanelUploadDest();

        try
        {
            ActiveUploadSettings.AiChatUploadDestination = selectedDestination;
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-lens-upload-destination", ex);
            ActiveUploadSettings.AiChatUploadDestination = previousDestination;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-lens-upload-destination-rollback", rollbackEx);
            }

            _suppressAiRedirectLensUploadDestChange = true;
            try
            {
                SelectAiRedirectPanelUploadDestByValue((int)previousDestination);
            }
            finally
            {
                _suppressAiRedirectLensUploadDestChange = false;
            }

            ShowAiRedirectLensUploadServiceSaveFailed(ex);
        }
    }

    private void ShowAiRedirectLensUploadServiceSaveFailed(Exception ex)
    {
        SetAiRedirectTestStatus("Lens upload service change was not saved. Previous upload service restored.");
        ToastWindow.ShowError(
            "Lens upload service failed",
            $"The previous Lens upload service was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private void AiRedirectLensUploadSyncCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressAiRedirectLensUploadSyncChange) return;

        var previousSynced = ActiveUploadSettings.AiChatUploadDestinationSynced;
        var previousDestination = ActiveUploadSettings.AiChatUploadDestination;
        var selectedSynced = AiRedirectLensUploadSyncCheck.IsChecked == true;

        try
        {
            ActiveUploadSettings.AiChatUploadDestinationSynced = selectedSynced;
            if (ActiveUploadSettings.AiChatUploadDestinationSynced)
                ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(GetSelectedUploadDest());
            else
                ActiveUploadSettings.AiChatUploadDestination = GetSelectedAiRedirectPanelUploadDest();
            UpdateAiRedirectPanelVisibility();
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-lens-upload-sync", ex);
            ActiveUploadSettings.AiChatUploadDestinationSynced = previousSynced;
            ActiveUploadSettings.AiChatUploadDestination = previousDestination;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-lens-upload-sync-rollback", rollbackEx);
            }

            _suppressAiRedirectLensUploadSyncChange = true;
            _suppressAiRedirectLensUploadDestChange = true;
            try
            {
                AiRedirectLensUploadSyncCheck.IsChecked = previousSynced;
                SelectAiRedirectPanelUploadDestByValue((int)previousDestination);
            }
            finally
            {
                _suppressAiRedirectLensUploadDestChange = false;
                _suppressAiRedirectLensUploadSyncChange = false;
            }

            try
            {
                UpdateAiRedirectPanelVisibility();
            }
            catch (Exception restoreEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-lens-upload-sync-restore", restoreEx);
            }

            ShowAiRedirectLensUploadSyncSaveFailed(ex);
        }
    }

    private void ShowAiRedirectLensUploadSyncSaveFailed(Exception ex)
    {
        SetAiRedirectTestStatus("Lens upload sync change was not saved. Previous sync setting restored.");
        ToastWindow.ShowError(
            "Lens upload sync failed",
            $"The previous Lens upload sync setting was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private async void AiRedirectTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRedirectTestInProgress)
            return;

        var provider = GetSelectedAiRedirectPanelProvider();
        if (provider == Services.AiChatProvider.None)
        {
            UpdateAiRedirectTestAvailability();
            SetAiRedirectTestStatus("Choose an AI tool first.");
            return;
        }

        _aiRedirectTestInProgress = true;
        AiRedirectTestBtn.Content = "Testing...";
        AiRedirectTestBtn.IsEnabled = false;
        SetAiRedirectTestStatus("Testing AI redirect...");

        string? tempPath = null;
        try
        {
            if (provider == Services.AiChatProvider.GoogleLens)
            {
                tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oddsnap_ai_redirect_test.png");
                using (var bmp = new Bitmap(1, 1))
                    CaptureOutputService.SavePng(bmp, tempPath);

                var hostDest = GetSelectedAiRedirectPanelUploadDest();
                var uploadResult = await Services.UploadService.UploadAsync(tempPath, hostDest, ActiveUploadSettings);
                if (uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.Url))
                {
                    var lensUrl = Services.UploadService.BuildGoogleLensUrl(uploadResult.Url);
                    var opened = TryOpenTestUploadExternalUrl(lensUrl);
                    SetAiRedirectTestStatus(opened
                        ? "Google Lens opened with the test upload."
                        : "Test upload succeeded, but Google Lens did not open.");
                    ToastWindow.Show(opened ? "Google Lens works" : "Google Lens upload works", uploadResult.Url);
                }
                else
                {
                    var providerName = Services.UploadService.GetName(hostDest);
                    var error = GetUploadResultError(uploadResult);
                    SetAiRedirectTestStatus($"Google Lens upload failed: {providerName}: {error}");
                    ToastWindow.ShowError("Google Lens upload failed", BuildTestUploadFailureToastBody(providerName, error, uploadResult.IsRateLimit));
                }
            }
            else
            {
                var startUrl = Services.UploadService.BuildAiChatStartUrl(provider);
                var providerName = Services.UploadService.GetAiChatProviderName(provider);
                if (TryOpenTestUploadExternalUrl(startUrl))
                {
                    SetAiRedirectTestStatus("AI redirect opened.");
                    ToastWindow.Show("AI redirect works", providerName);
                }
                else
                {
                    SetAiRedirectTestStatus($"AI redirect test could not open {providerName}. Check your default browser and try again.");
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-test", ex);
            var providerName = Services.UploadService.GetAiChatProviderName(provider);
            SetAiRedirectTestStatus($"{providerName} redirect test failed. Check Settings -> Uploads and try again.");
            ToastWindow.ShowError($"{providerName} redirect test failed", BuildAiRedirectTestFailureToastBody(providerName, ex.Message));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogWarning("settings.ai-redirect-test-temp-delete", $"Failed to delete AI redirect test file {System.IO.Path.GetFileName(tempPath)}: {ex.Message}", ex);
                }
            }

            _aiRedirectTestInProgress = false;
            AiRedirectTestBtn.Content = "Test Redirect";
            UpdateAiRedirectTestAvailability();
        }
    }

    private void SetAiRedirectTestStatus(string message)
    {
        AiRedirectTestStatusText.Text = message;
        AiRedirectTestStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AiRedirectPanelHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AiRedirectPanelHotkeyBox.Text = LocalizationService.Translate("Press keys...");
    }

    private void AiRedirectPanelHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(_settingsService.Settings.AiRedirectHotkeyModifiers, _settingsService.Settings.AiRedirectHotkeyKey);
    }

    private void AiRedirectPanelHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleAiRedirectHotkeyKeyInput(e, e.Key == Key.System ? e.SystemKey : e.Key);
    }

    private void AiRedirectPanelHotkeyBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Snapshot or Key.Pause or Key.Cancel)
            HandleAiRedirectHotkeyKeyInput(e, key);
    }

    private void AiRedirectPanelHotkeyClearBtn_Click(object sender, RoutedEventArgs e)
    {
        var previousModifiers = _settingsService.Settings.AiRedirectHotkeyModifiers;
        var previousKey = _settingsService.Settings.AiRedirectHotkeyKey;

        try
        {
            _settingsService.Settings.AiRedirectHotkeyModifiers = 0;
            _settingsService.Settings.AiRedirectHotkeyKey = 0;
            _settingsService.Save();
            AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
            SetAiRedirectTestStatus("AI redirect hotkey cleared.");
            HotkeyChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-hotkey-clear", ex);
            _settingsService.Settings.AiRedirectHotkeyModifiers = previousModifiers;
            _settingsService.Settings.AiRedirectHotkeyKey = previousKey;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-hotkey-clear-rollback", rollbackEx);
            }

            AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(previousModifiers, previousKey);
            ShowAiRedirectHotkeySaveFailed("clear", ex);
        }
    }

    private void HandleAiRedirectHotkeyKeyInput(System.Windows.Input.KeyEventArgs e, Key key)
    {
        if (!AiRedirectPanelHotkeyBox.IsKeyboardFocusWithin)
            return;

        e.Handled = true;
        if (IsModifierOnly(key))
            return;

        uint modifiers = HotkeyFormatter.GetActiveModifiers();
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || IsUnsafeModifierlessHotkey(modifiers, vk))
            return;

        var previousModifiers = _settingsService.Settings.AiRedirectHotkeyModifiers;
        var previousKey = _settingsService.Settings.AiRedirectHotkeyKey;
        List<(string ToolId, uint Modifiers, uint Key)> clearedConflicts = new();

        var conflict = FindAiRedirectConflict(modifiers, vk);
        if (conflict != null)
        {
            var combo = HotkeyFormatter.Format(modifiers, vk);
            if (!ThemedConfirmDialog.Confirm(
                    this,
                    "Hotkey conflict",
                    $"{combo} is already used by \"{conflict}\".\n\nReplace it?",
                    "Replace",
                    "Cancel",
                    danger: false))
            {
                AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(_settingsService.Settings.AiRedirectHotkeyModifiers, _settingsService.Settings.AiRedirectHotkeyKey);
                Keyboard.ClearFocus();
                return;
            }

            clearedConflicts = ClearAiRedirectConflict(modifiers, vk);
        }

        try
        {
            _settingsService.Settings.AiRedirectHotkeyModifiers = modifiers;
            _settingsService.Settings.AiRedirectHotkeyKey = vk;
            _settingsService.Save();
            AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(modifiers, vk);
            SetAiRedirectTestStatus("AI redirect hotkey saved.");
            Keyboard.ClearFocus();
            HotkeyChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.ai-redirect-hotkey", ex);
            _settingsService.Settings.AiRedirectHotkeyModifiers = previousModifiers;
            _settingsService.Settings.AiRedirectHotkeyKey = previousKey;
            foreach (var (toolId, oldModifiers, oldKey) in clearedConflicts)
                _settingsService.Settings.SetToolHotkey(toolId, oldModifiers, oldKey);

            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.ai-redirect-hotkey-rollback", rollbackEx);
            }

            AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(previousModifiers, previousKey);
            Keyboard.ClearFocus();
            ShowAiRedirectHotkeySaveFailed("change", ex);
        }
    }

    private void ShowAiRedirectHotkeySaveFailed(string action, Exception ex)
    {
        SetAiRedirectTestStatus($"AI redirect hotkey {action} was not saved. Previous hotkey restored.");
        ToastWindow.ShowError(
            "AI redirect hotkey failed",
            $"The previous AI redirect hotkey was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape;

    private static bool IsUnsafeModifierlessHotkey(uint modifiers, uint key) =>
        modifiers == 0 && key != Native.User32.VK_SNAPSHOT;

    private string? FindAiRedirectConflict(uint modifiers, uint key)
    {
        var settings = _settingsService.Settings;
        foreach (var tool in ToolDef.AllTools.Where(t => t.Group == 0))
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(tool.Id);
            if (existingModifiers == modifiers && existingKey == key)
                return tool.Label;
        }

        foreach (var (id, label, _) in ExtraTools)
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(id);
            if (existingModifiers == modifiers && existingKey == key)
                return label;
        }

        return null;
    }

    private List<(string ToolId, uint Modifiers, uint Key)> ClearAiRedirectConflict(uint modifiers, uint key)
    {
        var settings = _settingsService.Settings;
        var cleared = new List<(string ToolId, uint Modifiers, uint Key)>();
        foreach (var tool in ToolDef.AllTools.Where(t => t.Group == 0))
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(tool.Id);
            if (existingModifiers == modifiers && existingKey == key)
            {
                cleared.Add((tool.Id, existingModifiers, existingKey));
                settings.SetToolHotkey(tool.Id, 0, 0);
            }
        }

        foreach (var (id, _, _) in ExtraTools)
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(id);
            if (existingModifiers == modifiers && existingKey == key)
            {
                cleared.Add((id, existingModifiers, existingKey));
                settings.SetToolHotkey(id, 0, 0);
            }
        }

        return cleared;
    }

    private void ImgurClientIdBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            ImgurClientIdBox,
            "imgur-client-id",
            () => ActiveUploadSettings.ImgurClientId,
            value => ActiveUploadSettings.ImgurClientId = value);
    }

    private void ImgurTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            ImgurTokenBox,
            "imgur-token",
            () => ActiveUploadSettings.ImgurAccessToken,
            value => ActiveUploadSettings.ImgurAccessToken = value);
    }

    private void ImgBBKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            ImgBBKeyBox,
            "imgbb-key",
            () => ActiveUploadSettings.ImgBBApiKey,
            value => ActiveUploadSettings.ImgBBApiKey = value);
    }

    private void ImgPileTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            ImgPileTokenBox,
            "imgpile-token",
            () => ActiveUploadSettings.ImgPileApiToken,
            value => ActiveUploadSettings.ImgPileApiToken = value);
    }

    private void GyazoTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            GyazoTokenBox,
            "gyazo-token",
            () => ActiveUploadSettings.GyazoAccessToken,
            value => ActiveUploadSettings.GyazoAccessToken = value);
    }

    private void DropboxTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            DropboxTokenBox,
            "dropbox-token",
            () => ActiveUploadSettings.DropboxAccessToken,
            value => ActiveUploadSettings.DropboxAccessToken = value);
    }

    private void DropboxPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            DropboxPathBox,
            "dropbox-path",
            () => ActiveUploadSettings.DropboxPathPrefix,
            value => ActiveUploadSettings.DropboxPathPrefix = value);
    }

    private void GoogleDriveTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            GoogleDriveTokenBox,
            "google-drive-token",
            () => ActiveUploadSettings.GoogleDriveAccessToken,
            value => ActiveUploadSettings.GoogleDriveAccessToken = value);
    }

    private void GoogleDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            GoogleDriveFolderBox,
            "google-drive-folder",
            () => ActiveUploadSettings.GoogleDriveFolderId,
            value => ActiveUploadSettings.GoogleDriveFolderId = value);
    }

    private void OneDriveTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            OneDriveTokenBox,
            "onedrive-token",
            () => ActiveUploadSettings.OneDriveAccessToken,
            value => ActiveUploadSettings.OneDriveAccessToken = value);
    }

    private void OneDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            OneDriveFolderBox,
            "onedrive-folder",
            () => ActiveUploadSettings.OneDriveFolder,
            value => ActiveUploadSettings.OneDriveFolder = value);
    }

    private void AzureBlobSasBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            AzureBlobSasBox,
            "azure-sas",
            () => ActiveUploadSettings.AzureBlobSasUrl,
            value => ActiveUploadSettings.AzureBlobSasUrl = value);
    }

    private void GitHubTokenBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            GitHubTokenBox,
            "github-token",
            () => ActiveUploadSettings.GitHubToken,
            value => ActiveUploadSettings.GitHubToken = value);
    }

    private void GitHubRepoBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            GitHubRepoBox,
            "github-repo",
            () => ActiveUploadSettings.GitHubRepo,
            value => ActiveUploadSettings.GitHubRepo = value);
    }

    private void GitHubBranchBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            GitHubBranchBox,
            "github-branch",
            () => ActiveUploadSettings.GitHubBranch,
            value => ActiveUploadSettings.GitHubBranch = value);
    }

    private void ImmichUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            ImmichUrlBox,
            "immich-url",
            () => ActiveUploadSettings.ImmichBaseUrl,
            value => ActiveUploadSettings.ImmichBaseUrl = value);
    }

    private void ImmichApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            ImmichApiKeyBox,
            "immich-key",
            () => ActiveUploadSettings.ImmichApiKey,
            value => ActiveUploadSettings.ImmichApiKey = value);
    }

    private void FtpUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(FtpUrlBox, "ftp-url", () => ActiveUploadSettings.FtpUrl, value => ActiveUploadSettings.FtpUrl = value);
    private void FtpUsernameBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(FtpUsernameBox, "ftp-username", () => ActiveUploadSettings.FtpUsername, value => ActiveUploadSettings.FtpUsername = value);
    private void FtpPasswordBox_Changed(object sender, RoutedEventArgs e) => UpdateUploadPasswordSetting(FtpPasswordBox, "ftp-password", () => ActiveUploadSettings.FtpPassword, value => ActiveUploadSettings.FtpPassword = value);
    private void FtpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(FtpPublicUrlBox, "ftp-public-url", () => ActiveUploadSettings.FtpPublicUrl, value => ActiveUploadSettings.FtpPublicUrl = value);
    private void SftpHostBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(SftpHostBox, "sftp-host", () => ActiveUploadSettings.SftpHost, value => ActiveUploadSettings.SftpHost = value);
    private void SftpUsernameBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(SftpUsernameBox, "sftp-username", () => ActiveUploadSettings.SftpUsername, value => ActiveUploadSettings.SftpUsername = value);
    private void SftpPasswordBox_Changed(object sender, RoutedEventArgs e) => UpdateUploadPasswordSetting(SftpPasswordBox, "sftp-password", () => ActiveUploadSettings.SftpPassword, value => ActiveUploadSettings.SftpPassword = value);
    private void SftpRemotePathBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(SftpRemotePathBox, "sftp-remote-path", () => ActiveUploadSettings.SftpRemotePath, value => ActiveUploadSettings.SftpRemotePath = value);
    private void SftpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(SftpPublicUrlBox, "sftp-public-url", () => ActiveUploadSettings.SftpPublicUrl, value => ActiveUploadSettings.SftpPublicUrl = value);
    private void SftpHostKeyFingerprintBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(SftpHostKeyFingerprintBox, "sftp-host-key", () => ActiveUploadSettings.SftpHostKeyFingerprint, value => ActiveUploadSettings.SftpHostKeyFingerprint = value);
    private void WebDavUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(WebDavUrlBox, "webdav-url", () => ActiveUploadSettings.WebDavUrl, value => ActiveUploadSettings.WebDavUrl = value);
    private void WebDavUsernameBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(WebDavUsernameBox, "webdav-username", () => ActiveUploadSettings.WebDavUsername, value => ActiveUploadSettings.WebDavUsername = value);
    private void WebDavPasswordBox_Changed(object sender, RoutedEventArgs e) => UpdateUploadPasswordSetting(WebDavPasswordBox, "webdav-password", () => ActiveUploadSettings.WebDavPassword, value => ActiveUploadSettings.WebDavPassword = value);
    private void WebDavPublicUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(WebDavPublicUrlBox, "webdav-public-url", () => ActiveUploadSettings.WebDavPublicUrl, value => ActiveUploadSettings.WebDavPublicUrl = value);

    private void SftpPortBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressUploadFieldChange) return;
        if (!int.TryParse(SftpPortBox.Text, out var port)) return;

        var previous = ActiveUploadSettings.SftpPort;
        try
        {
            ActiveUploadSettings.SftpPort = port;
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.upload-field-sftp-port", ex);
            ActiveUploadSettings.SftpPort = previous;
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError("settings.upload-field-sftp-port-rollback", rollbackEx);
            }

            _suppressUploadFieldChange = true;
            try
            {
                SftpPortBox.Text = previous.ToString();
            }
            finally
            {
                _suppressUploadFieldChange = false;
            }

            ShowUploadSettingSaveFailed(ex);
        }
    }

    private void S3EndpointBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            S3EndpointBox,
            "s3-endpoint",
            () => ActiveUploadSettings.S3Endpoint,
            value => ActiveUploadSettings.S3Endpoint = value);
    }

    private void S3BucketBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            S3BucketBox,
            "s3-bucket",
            () => ActiveUploadSettings.S3Bucket,
            value => ActiveUploadSettings.S3Bucket = value);
    }

    private void S3RegionBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            S3RegionBox,
            "s3-region",
            () => ActiveUploadSettings.S3Region,
            value => ActiveUploadSettings.S3Region = value);
    }

    private void S3AccessKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            S3AccessKeyBox,
            "s3-access-key",
            () => ActiveUploadSettings.S3AccessKey,
            value => ActiveUploadSettings.S3AccessKey = value);
    }

    private void S3SecretKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUploadPasswordSetting(
            S3SecretKeyBox,
            "s3-secret-key",
            () => ActiveUploadSettings.S3SecretKey,
            value => ActiveUploadSettings.S3SecretKey = value);
    }

    private void S3PublicUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            S3PublicUrlBox,
            "s3-public-url",
            () => ActiveUploadSettings.S3PublicUrl,
            value => ActiveUploadSettings.S3PublicUrl = value);
    }

    private void CustomUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            CustomUrlBox,
            "custom-url",
            () => ActiveUploadSettings.CustomUploadUrl,
            value => ActiveUploadSettings.CustomUploadUrl = value);
    }

    private void CustomFieldBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            CustomFieldBox,
            "custom-field",
            () => ActiveUploadSettings.CustomFileFormName,
            value => ActiveUploadSettings.CustomFileFormName = value);
    }

    private void CustomJsonPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            CustomJsonPathBox,
            "custom-json-path",
            () => ActiveUploadSettings.CustomResponseUrlPath,
            value => ActiveUploadSettings.CustomResponseUrlPath = value);
    }

    private void CustomHeadersBox_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateUploadTextSetting(
            CustomHeadersBox,
            "custom-headers",
            () => ActiveUploadSettings.CustomHeaders,
            value => ActiveUploadSettings.CustomHeaders = value);
    }

    private void UpdateUploadTextSetting(System.Windows.Controls.TextBox textBox, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)
    {
        UpdateUploadStringSetting(
            textBox.Text,
            value => textBox.Text = value,
            diagnosticSuffix,
            getValue,
            setValue);
    }

    private void UpdateUploadPasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)
    {
        UpdateUploadStringSetting(
            passwordBox.Password,
            value => passwordBox.Password = value,
            diagnosticSuffix,
            getValue,
            setValue);
    }

    private void UpdateUploadStringSetting(string value, Action<string> restoreControlValue, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)
    {
        if (!IsLoaded || _suppressUploadFieldChange) return;

        var previous = getValue();

        try
        {
            setValue(value);
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError($"settings.upload-field-{diagnosticSuffix}", ex);
            setValue(previous);
            try
            {
                _settingsService.Save();
            }
            catch (Exception rollbackEx)
            {
                AppDiagnostics.LogError($"settings.upload-field-{diagnosticSuffix}-rollback", rollbackEx);
            }

            _suppressUploadFieldChange = true;
            try
            {
                restoreControlValue(previous);
            }
            finally
            {
                _suppressUploadFieldChange = false;
            }

            ShowUploadSettingSaveFailed(ex);
        }
    }

    private void ShowUploadSettingSaveFailed(Exception ex)
    {
        SetTestUploadStatus("Upload setting failed. Previous value restored; check Settings -> Uploads and try again.");
        ToastWindow.ShowError(
            "Upload setting failed",
            $"The setting was restored. Check Settings -> Uploads and try again.\n{ex.Message}");
    }

    private async void TestUpload_Click(object sender, RoutedEventArgs e)
    {
        if (_testUploadInProgress)
            return;

        if (!CanTestUploadDestination(GetSelectedUploadDest()))
        {
            UpdateTestUploadAvailability();
            return;
        }

        _testUploadInProgress = true;
        TestUploadBtn.Content = "Uploading...";
        TestUploadBtn.IsEnabled = false;
        SetTestUploadStatus("Uploading test image...");

        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oddsnap_test.png");
        try
        {
            using (var bmp = new Bitmap(1, 1))
                CaptureOutputService.SavePng(bmp, tempPath);

            if (_settingsService.Settings.ImageUploadDestination == Services.UploadDestination.AiChat)
            {
                if (ActiveUploadSettings.AiChatProvider == Services.AiChatProvider.GoogleLens)
                {
                    var hostDest = Services.UploadService.NormalizeAiChatUploadDestination(ActiveUploadSettings.AiChatUploadDestination);
                    var uploadResult = await Services.UploadService.UploadAsync(tempPath, hostDest, ActiveUploadSettings);
                    if (uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.Url))
                    {
                        var lensUrl = Services.UploadService.BuildGoogleLensUrl(uploadResult.Url);
                        var opened = TryOpenTestUploadExternalUrl(lensUrl);
                        SetTestUploadStatus(opened
                            ? "Google Lens opened with the test upload."
                            : "Test upload succeeded, but Google Lens did not open.");
                        ToastWindow.Show(opened ? "Google Lens works" : "Google Lens upload works", uploadResult.Url);
                    }
                    else
                    {
                        var providerName = Services.UploadService.GetName(hostDest);
                        var error = GetUploadResultError(uploadResult);
                        SetTestUploadStatus($"Google Lens upload failed: {providerName}: {error}");
                        ToastWindow.ShowError("Google Lens upload failed", BuildTestUploadFailureToastBody(providerName, error, uploadResult.IsRateLimit));
                    }
                }
                else
                {
                    var startUrl = Services.UploadService.BuildAiChatStartUrl(ActiveUploadSettings.AiChatProvider);
                    if (TryOpenTestUploadExternalUrl(startUrl))
                    {
                        SetTestUploadStatus("AI redirect opened.");
                        ToastWindow.Show("AI redirect works", Services.UploadService.GetAiChatProviderName(ActiveUploadSettings.AiChatProvider));
                    }
                    else
                    {
                        SetTestUploadStatus("AI redirect test could not open the provider. Check your default browser and try again.");
                    }
                }
            }
            else
            {
                var result = await Services.UploadService.UploadAsync(
                    tempPath,
                    _settingsService.Settings.ImageUploadDestination,
                    ActiveUploadSettings);

                if (result.Success && !string.IsNullOrWhiteSpace(result.Url))
                {
                    SetTestUploadStatus("Test upload succeeded.");
                    ToastWindow.Show("Upload works", result.Url);
                }
                else
                {
                    var providerName = string.IsNullOrWhiteSpace(result.ProviderName)
                        ? Services.UploadService.GetName(_settingsService.Settings.ImageUploadDestination)
                        : result.ProviderName;
                    var error = GetUploadResultError(result);
                    SetTestUploadStatus($"Upload failed: {providerName}: {error}");
                    ToastWindow.ShowError("Upload failed", BuildTestUploadFailureToastBody(providerName, error, result.IsRateLimit));
                }
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.test-upload", ex);
            SetTestUploadStatus("Test upload failed. Check upload settings and try another destination.");
            ToastWindow.ShowError(
                "Test upload failed",
                $"OddSnap could not complete the test upload. Check Settings -> Uploads or try another destination.\n{ex.Message}");
        }
        finally
        {
            try
            {
                System.IO.File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("settings.test-upload-temp-delete", $"Failed to delete test upload file {System.IO.Path.GetFileName(tempPath)}: {ex.Message}", ex);
            }
            _testUploadInProgress = false;
            TestUploadBtn.Content = "Test Upload";
            UpdateTestUploadAvailability();
        }
    }

    private void SetTestUploadStatus(string message)
    {
        TestUploadStatusText.Text = message;
        TestUploadStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string GetUploadResultError(Services.UploadResult result)
        => string.IsNullOrWhiteSpace(result.Error) ? "Upload returned no link." : result.Error;

    private static string BuildTestUploadFailureToastBody(string providerName, string error, bool isRateLimit)
    {
        var providerLabel = string.IsNullOrWhiteSpace(providerName) ? "Upload" : providerName;
        var recovery = isRateLimit
            ? $"{providerLabel} may be rate-limiting requests. Try another upload destination or wait before retrying."
            : $"Check Settings -> Uploads for {providerLabel}, then try another upload destination.";

        return $"{providerLabel}: {error}\n{recovery}";
    }

    private static string BuildAiRedirectTestFailureToastBody(string providerName, string details)
    {
        var providerLabel = string.IsNullOrWhiteSpace(providerName) ? "AI redirect" : providerName;
        var recovery = $"OddSnap could not complete the {providerLabel} redirect test. Check Settings -> Uploads and try again.";
        return string.IsNullOrWhiteSpace(details) ? recovery : $"{recovery}\n{details}";
    }

    private static bool TryOpenTestUploadExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastWindow.ShowError("Open failed", "No test upload link is available.");
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ToastWindow.ShowError("Open failed", "The test upload link is not a valid web link.");
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
                    "Open failed",
                    "Windows did not open the test upload link. Copy it from Settings -> Uploads and open it manually.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("settings.test-upload-external-url-open", $"Failed to open test upload URL: {ex.Message}", ex);
            ToastWindow.ShowError(
                "Open failed",
                $"OddSnap could not open the test upload link. Copy the link from Settings -> Uploads and open it manually.\n{ex.Message}");
            return false;
        }
    }

    private readonly List<ComboBoxItem> _uploadDestItems = new();
    private bool _uploadDestItemsCached;

    private void CacheUploadDestItems()
    {
        if (_uploadDestItemsCached) return;
        _uploadDestItemsCached = true;
        _uploadDestItems.Clear();
        EnsureUploadDestinationComboIcons();
        foreach (var item in UploadDestCombo.Items.OfType<ComboBoxItem>())
            _uploadDestItems.Add(item);
    }

    private void SelectUploadDestByTag(int destValue)
    {
        var tag = destValue.ToString();
        foreach (ComboBoxItem item in UploadDestCombo.Items)
        {
            if (item.Tag as string == tag)
            {
                UploadDestCombo.SelectedItem = item;
                return;
            }
        }
        if (UploadDestCombo.Items.Count > 0)
            UploadDestCombo.SelectedIndex = 0;
    }

    private void SelectAiRedirectPanelProviderByValue(int providerValue)
    {
        if (providerValue == (int)Services.AiChatProvider.ClaudeOpus)
            providerValue = (int)Services.AiChatProvider.Claude;

        foreach (ComboBoxItem item in AiRedirectProviderCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var value) && value == providerValue)
            {
                AiRedirectProviderCombo.SelectedItem = item;
                UpdateAiRedirectPanelVisibility();
                return;
            }
        }

        if (AiRedirectProviderCombo.Items.Count > 0)
            AiRedirectProviderCombo.SelectedIndex = 0;
    }

    private void SelectAiRedirectPanelUploadDestByValue(int destValue)
    {
        destValue = (int)Services.UploadService.NormalizeAiChatUploadDestination((Services.UploadDestination)destValue);
        foreach (ComboBoxItem item in AiRedirectLensUploadDestPanelCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var value) && value == destValue)
            {
                AiRedirectLensUploadDestPanelCombo.SelectedItem = item;
                return;
            }
        }

        if (AiRedirectLensUploadDestPanelCombo.Items.Count > 0)
            AiRedirectLensUploadDestPanelCombo.SelectedIndex = 0;
    }

    private void RebuildAiRedirectPanelUploadDestItems()
    {
        CacheUploadDestItems();
        AiRedirectLensUploadDestPanelCombo.Items.Clear();
        AiRedirectLensUploadDestPanelCombo.ItemTemplate = GetSettingsComboItemTemplate();

        foreach (var source in _uploadDestItems)
        {
            if (source.Tag is not string tag || !int.TryParse(tag, out var raw))
                continue;

            var destination = (Services.UploadDestination)raw;
            if (destination is Services.UploadDestination.None or Services.UploadDestination.AiChat or Services.UploadDestination.TransferSh)
                continue;

            var item = new ComboBoxItem
            {
                Content = CloneUploadDestinationContent(source.Content),
                ContentTemplate = GetSettingsComboItemTemplate(),
                Tag = source.Tag,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 5, 8, 5)
            };
            SetUploadDestinationItemMetadata(item, destination, GetUploadDestinationFilterText(item), forLensUpload: true);
            AiRedirectLensUploadDestPanelCombo.Items.Add(item);
        }
    }

    private void EnsureProviderComboIcons()
    {
        ApplyComboIcons(AiRedirectProviderCombo, raw => raw switch
        {
            "-1" => null,
            "0" => "chatgpt_sq.png",
            "1" => "claude_sq.png",
            "3" => "gemini_sq.png",
            "4" => "googlelens_sq.png",
            _ => null
        });
        ApplyComboIcons(StickerProviderCombo, raw => raw switch
        {
            "1" => "removebg_sq.png",
            "2" => "photoroom_sq.png",
            _ => null
        });
        ApplyComboIcons(UpscaleProviderCombo, raw => raw switch
        {
            "2" or "3" => "deepai_sq.png",
            _ => null
        });
        ApplyTextComboIcons(StickerLocalExecutionCombo, text => text.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? "cpu" : "gpu");
        ApplyTextComboIcons(UpscaleLocalExecutionCombo, text => text.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? "cpu" : "gpu");
        ApplyTextComboIcons(StickerLocalCpuEngineCombo, _ => "sticker");
        ApplyTextComboIcons(StickerLocalGpuEngineCombo, _ => "sticker");
        ApplyTextComboIcons(UpscaleLocalCpuEngineCombo, _ => "upscale");
        ApplyTextComboIcons(UpscaleLocalGpuEngineCombo, _ => "upscale");
    }

    private void ApplyComboIcons(System.Windows.Controls.ComboBox combo, Func<string, string?> assetSelector)
    {
        combo.ItemTemplate = GetSettingsComboItemTemplate();
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is not ComboBoxItem item || item.Content is not string text)
                continue;
            var raw = item.Tag as string ?? i.ToString();
            item.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            item.Padding = new Thickness(8, 5, 8, 5);
            item.ContentTemplate = GetSettingsComboItemTemplate();
            item.Content = BuildProviderComboItem(text, assetSelector(raw));
            SetSettingsComboItemMetadata(item, combo.Name, text, GetSettingsComboItemHelpText(combo.Name, text));
        }
    }

    private void ApplyTextComboIcons(System.Windows.Controls.ComboBox combo, Func<string, string> iconSelector)
    {
        combo.ItemTemplate = GetSettingsComboItemTemplate();
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is not ComboBoxItem item || item.Content is not string text)
                continue;
            item.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            item.Padding = new Thickness(8, 5, 8, 5);
            item.ContentTemplate = GetSettingsComboItemTemplate();
            item.Content = BuildFallbackComboItem(text, iconSelector(text));
            SetSettingsComboItemMetadata(item, combo.Name, text, GetSettingsComboItemHelpText(combo.Name, text));
        }
    }

    private static void SetSettingsComboItemMetadata(ComboBoxItem item, string comboName, string text, string helpText)
    {
        item.ToolTip = helpText;
        AutomationProperties.SetName(item, GetSettingsComboItemAutomationName(comboName, text));
        AutomationProperties.SetHelpText(item, helpText);
    }

    private static string GetSettingsComboItemAutomationName(string comboName, string text)
        => comboName switch
        {
            "AiRedirectProviderCombo" => string.Equals(text, "None", StringComparison.OrdinalIgnoreCase)
                ? "No AI redirect provider"
                : $"{text} AI redirect provider",
            "StickerProviderCombo" => string.Equals(text, "None", StringComparison.OrdinalIgnoreCase)
                ? "No sticker provider"
                : $"{text} sticker provider",
            "UpscaleProviderCombo" => string.Equals(text, "None", StringComparison.OrdinalIgnoreCase)
                ? "No upscale provider"
                : $"{text} upscale provider",
            "StickerLocalExecutionCombo" => $"{text} sticker execution mode",
            "UpscaleLocalExecutionCombo" => $"{text} upscale execution mode",
            "StickerLocalCpuEngineCombo" or "StickerLocalGpuEngineCombo" => $"{text} sticker model",
            "UpscaleLocalCpuEngineCombo" or "UpscaleLocalGpuEngineCombo" => $"{text} upscale model",
            _ => text
        };

    private static string GetSettingsComboItemHelpText(string comboName, string text)
        => comboName switch
        {
            "AiRedirectProviderCombo" => string.Equals(text, "None", StringComparison.OrdinalIgnoreCase)
                ? "Do not open an AI tool after AI Redirect captures."
                : $"Open {text} after an AI Redirect capture.",
            "StickerProviderCombo" => text switch
            {
                "None" => "Do not run background removal for sticker captures.",
                "Local (CPU/GPU)" => "Use the local sticker runtime configured below.",
                _ => $"Use {text} for cloud background removal."
            },
            "UpscaleProviderCombo" => text switch
            {
                "None" => "Do not upscale captures.",
                "Local (CPU/GPU)" => "Use the local upscale runtime configured below.",
                _ => $"Use {text} for cloud upscaling."
            },
            "StickerLocalExecutionCombo" => $"Run local sticker processing on {text}.",
            "UpscaleLocalExecutionCombo" => $"Run local upscaling on {text}.",
            "StickerLocalCpuEngineCombo" or "StickerLocalGpuEngineCombo" => $"Use {text} for local sticker background removal.",
            "UpscaleLocalCpuEngineCombo" or "UpscaleLocalGpuEngineCombo" => $"Use {text} for local upscaling.",
            _ => $"Choose {text}."
        };

    private object BuildProviderComboItem(string text, string? asset)
    {
        var source = LoadAssetIcon(asset);
        return new SettingsComboOption(
            text,
            source ?? RenderFallbackIcon(GetProviderFallbackIcon(text)),
            source is not null,
            null,
            asset);
    }

    private object BuildFallbackComboItem(string text, string iconId) =>
        new SettingsComboOption(text, RenderFallbackIcon(iconId), false, null, null);

    private static string GetProviderFallbackIcon(string text)
    {
        if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
            return "close";
        if (text.Contains("local", StringComparison.OrdinalIgnoreCase))
            return "settings";
        return "folder";
    }

    private void EnsureUploadDestinationComboIcons()
    {
        UploadDestCombo.ItemTemplate = GetSettingsComboItemTemplate();
        foreach (var item in UploadDestCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Content is not string text || item.Tag is not string tag || !int.TryParse(tag, out var raw))
                continue;
            item.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            item.Padding = new Thickness(8, 5, 8, 5);
            item.ContentTemplate = GetSettingsComboItemTemplate();
            item.Content = BuildUploadDestinationItem((Services.UploadDestination)raw, text);
            SetUploadDestinationItemMetadata(item, (Services.UploadDestination)raw, text, forLensUpload: false);
        }
    }

    private object CloneUploadDestinationContent(object content)
    {
        if (content is SettingsComboOption { Destination: { } destination } option)
            return BuildUploadDestinationItem(destination, option.Text);
        return content;
    }

    private object BuildUploadDestinationItem(Services.UploadDestination destination, string text)
    {
        var (source, isBrand) = GetUploadDestinationIcon(destination);
        return new SettingsComboOption(text, source, isBrand, destination, null);
    }

    private static void SetUploadDestinationItemMetadata(
        ComboBoxItem item,
        Services.UploadDestination destination,
        string text,
        bool forLensUpload)
    {
        var helpText = forLensUpload
            ? $"Use {text} as the hosted image service before opening Google Lens."
            : GetUploadDestinationHelpText(destination, text);
        item.ToolTip = helpText;
        var automationName = (destination, forLensUpload) switch
        {
            (Services.UploadDestination.None, true) => "No Lens upload destination",
            (Services.UploadDestination.None, false) => "No upload destination",
            (_, true) => $"{text} Lens upload destination",
            _ => $"{text} upload destination"
        };
        AutomationProperties.SetName(item, automationName);
        AutomationProperties.SetHelpText(item, helpText);
    }

    private static string GetUploadDestinationHelpText(Services.UploadDestination destination, string text)
        => destination switch
        {
            Services.UploadDestination.None => "Do not upload captures automatically.",
            Services.UploadDestination.TempHosts => "Automatically try free, no-setup public hosts until one works.",
            Services.UploadDestination.Litterbox or
            Services.UploadDestination.TmpFiles or
            Services.UploadDestination.Uguu or
            Services.UploadDestination.FileIo => $"Upload to {text}. This is a temporary public host and needs no setup.",
            Services.UploadDestination.Gofile or
            Services.UploadDestination.Catbox => $"Upload to {text}. This is a free public host and needs no setup.",
            Services.UploadDestination.Imgur or
            Services.UploadDestination.ImgBB or
            Services.UploadDestination.ImgPile or
            Services.UploadDestination.Gyazo => $"Upload to {text}. Configure the required API key, token, or client ID below.",
            Services.UploadDestination.Dropbox or
            Services.UploadDestination.GoogleDrive or
            Services.UploadDestination.OneDrive or
            Services.UploadDestination.AzureBlob or
            Services.UploadDestination.GitHub or
            Services.UploadDestination.Immich => $"Upload to {text}. Configure the account or server settings below.",
            Services.UploadDestination.Ftp or
            Services.UploadDestination.Sftp or
            Services.UploadDestination.WebDav or
            Services.UploadDestination.S3Compatible or
            Services.UploadDestination.CustomHttp => $"Upload to {text}. Configure the endpoint settings below.",
            Services.UploadDestination.AiChat => "Open the selected AI tool after capture; hosted image upload is configured in AI Redirect settings.",
            _ => $"Use {text} for uploads."
        };

    private static DataTemplate GetSettingsComboItemTemplate()
    {
        if (s_settingsComboItemTemplate is not null)
            return s_settingsComboItemTemplate;

        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
        root.SetValue(FrameworkElement.WidthProperty, SettingsComboItemWidth);
        root.SetValue(FrameworkElement.HeightProperty, 22.0);
        root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var iconFrame = new FrameworkElementFactory(typeof(Border));
        iconFrame.SetValue(FrameworkElement.WidthProperty, 20.0);
        iconFrame.SetValue(FrameworkElement.HeightProperty, 20.0);
        iconFrame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        iconFrame.SetValue(Border.ClipToBoundsProperty, true);
        iconFrame.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconFrame.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        iconFrame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(SettingsComboOption.IconBackground)));

        var icon = new FrameworkElementFactory(typeof(System.Windows.Controls.Image));
        icon.SetValue(FrameworkElement.WidthProperty, 14.0);
        icon.SetValue(FrameworkElement.HeightProperty, 14.0);
        icon.SetValue(System.Windows.Controls.Image.StretchProperty, Stretch.UniformToFill);
        icon.SetValue(FrameworkElement.ClipToBoundsProperty, true);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        icon.SetBinding(System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding(nameof(SettingsComboOption.Icon)));
        iconFrame.AppendChild(icon);
        root.AppendChild(iconFrame);

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(FrameworkElement.WidthProperty, SettingsComboTextWidth);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SettingsComboOption.Text)));
        root.AppendChild(label);

        s_settingsComboItemTemplate = new DataTemplate
        {
            VisualTree = root
        };
        return s_settingsComboItemTemplate;
    }

    private static ImageSource? LoadAssetIcon(string? asset)
    {
        if (string.IsNullOrWhiteSpace(asset))
            return null;
        try
        {
            return new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("settings.icon.load", ex);
            return null;
        }
    }

    private static ImageSource RenderFallbackIcon(string iconId)
    {
        var color = Theme.IsDark
            ? System.Drawing.Color.FromArgb(220, 255, 255, 255)
            : System.Drawing.Color.FromArgb(210, 24, 24, 24);
        return Helpers.FluentIcons.RenderWpf(iconId, color, 18)
            ?? Helpers.FluentIcons.RenderWpf("folder", color, 18)!;
    }

    private (ImageSource Source, bool IsBrand) GetUploadDestinationIcon(Services.UploadDestination destination)
    {
        string? asset = destination switch
        {
            Services.UploadDestination.Imgur => "imgur_sq.png",
            Services.UploadDestination.ImgBB => "imgbb_sq.png",
            Services.UploadDestination.ImgPile => "imgpile_sq.png",
            Services.UploadDestination.Catbox => "catbox_sq.png",
            Services.UploadDestination.Litterbox => "litterbox_sq.png",
            Services.UploadDestination.Gyazo => "gyazo_sq.png",
            Services.UploadDestination.FileIo => "fileio_sq.png",
            Services.UploadDestination.Uguu => "uguu_sq.png",
            Services.UploadDestination.TmpFiles => "tmpfiles_sq.png",
            Services.UploadDestination.Gofile => "gofile_sq.png",
            Services.UploadDestination.Dropbox => "dropbox_sq.png",
            Services.UploadDestination.GoogleDrive => "gdrive_sq.png",
            Services.UploadDestination.OneDrive => "onedrive_sq.png",
            Services.UploadDestination.AzureBlob => "azure_sq.png",
            Services.UploadDestination.GitHub => "github_sq.png",
            Services.UploadDestination.Immich => "immich_sq.png",
            Services.UploadDestination.S3Compatible => "aws_sq.png",
            _ => null
        };
        if (asset is not null && LoadAssetIcon(asset) is { } assetIcon)
            return (assetIcon, true);

        var iconId = destination switch
        {
            Services.UploadDestination.None => "close",
            Services.UploadDestination.TempHosts => "filter",
            Services.UploadDestination.CustomHttp => "settings",
            Services.UploadDestination.AiChat => "ai_redirect",
            Services.UploadDestination.Ftp => "settings",
            Services.UploadDestination.Sftp => "settings",
            Services.UploadDestination.WebDav => "settings",
            _ => "folder"
        };
        return (RenderFallbackIcon(iconId), false);
    }

    public sealed class SettingsComboOption
    {
        public SettingsComboOption(string text, ImageSource icon, bool isBrand, Services.UploadDestination? destination, string? asset)
        {
            Text = text;
            Icon = icon;
            IconBackground = isBrand ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            Destination = destination;
            Asset = asset;
        }

        public string Text { get; }
        public ImageSource Icon { get; }
        public System.Windows.Media.Brush IconBackground { get; }
        public Services.UploadDestination? Destination { get; }
        public string? Asset { get; }
        public override string ToString() => Text;
    }

    private static string GetUploadDestinationFilterText(ComboBoxItem item)
    {
        if (item.Content is SettingsComboOption option)
            return option.Text;
        return item.Content as string ?? "";
    }
}
