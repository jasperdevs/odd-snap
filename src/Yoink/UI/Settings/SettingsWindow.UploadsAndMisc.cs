using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yoink.Helpers;
using Yoink.Models;
using Yoink.Services;

namespace Yoink.UI;

public partial class SettingsWindow
{
    private Services.UploadDestination GetSelectedUploadDest()
    {
        if (UploadDestCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var val))
            return (Services.UploadDestination)val;
        return Services.UploadDestination.None;
    }

    private void UploadDestCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.ImageUploadDestination = GetSelectedUploadDest();
        if (ActiveUploadSettings.AiChatUploadDestinationSynced)
            ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(GetSelectedUploadDest());
        _settingsService.Save();
        UpdateUploadSettingsVisibility();
        UpdateAiRedirectPanelVisibility();
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
        TestUploadCard.Visibility = dest != Services.UploadDestination.None ? Visibility.Visible : Visibility.Collapsed;
        AutoUploadHeader.Visibility = Visibility.Visible;
        AutoUploadCard.Visibility = Visibility.Visible;
    }

    private void AutoUploadScreenshotsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadScreenshots = AutoUploadScreenshotsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void AutoUploadGifsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadGifs = AutoUploadGifsCheck.IsChecked == true;
        _settingsService.Save();
    }

    private void AutoUploadVideosCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AutoUploadVideos = AutoUploadVideosCheck.IsChecked == true;
        _settingsService.Save();
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
        return Services.AiChatProvider.ChatGpt;
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
    }

    private void AiRedirectProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AiChatProvider = GetSelectedAiRedirectPanelProvider();
        UpdateAiRedirectPanelVisibility();
        _settingsService.Save();
    }

    private void AiRedirectLensUploadDestPanelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (AiRedirectLensUploadSyncCheck.IsChecked == true)
            return;
        ActiveUploadSettings.AiChatUploadDestination = GetSelectedAiRedirectPanelUploadDest();
        _settingsService.Save();
    }

    private void AiRedirectLensUploadSyncCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AiChatUploadDestinationSynced = AiRedirectLensUploadSyncCheck.IsChecked == true;
        if (ActiveUploadSettings.AiChatUploadDestinationSynced)
            ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(GetSelectedUploadDest());
        else
            ActiveUploadSettings.AiChatUploadDestination = GetSelectedAiRedirectPanelUploadDest();
        UpdateAiRedirectPanelVisibility();
        _settingsService.Save();
    }

    private void AiRedirectPanelHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AiRedirectPanelHotkeyBox.Text = "Press keys...";
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
        _settingsService.Settings.AiRedirectHotkeyModifiers = 0;
        _settingsService.Settings.AiRedirectHotkeyKey = 0;
        _settingsService.Save();
        AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
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
        if (vk == 0)
            return;

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

            ClearAiRedirectConflict(modifiers, vk);
        }

        _settingsService.Settings.AiRedirectHotkeyModifiers = modifiers;
        _settingsService.Settings.AiRedirectHotkeyKey = vk;
        _settingsService.Save();
        AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(modifiers, vk);
        Keyboard.ClearFocus();
        HotkeyChanged?.Invoke();
    }

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape;

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

    private void ClearAiRedirectConflict(uint modifiers, uint key)
    {
        var settings = _settingsService.Settings;
        foreach (var tool in ToolDef.AllTools.Where(t => t.Group == 0))
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(tool.Id);
            if (existingModifiers == modifiers && existingKey == key)
                settings.SetToolHotkey(tool.Id, 0, 0);
        }

        foreach (var (id, _, _) in ExtraTools)
        {
            var (existingModifiers, existingKey) = settings.GetToolHotkey(id);
            if (existingModifiers == modifiers && existingKey == key)
                settings.SetToolHotkey(id, 0, 0);
        }

        _settingsService.Save();
    }

    private void ImgurClientIdBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgurClientId = ImgurClientIdBox.Text;
        _settingsService.Save();
    }

    private void ImgurTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgurAccessToken = ImgurTokenBox.Text;
        _settingsService.Save();
    }

    private void ImgBBKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgBBApiKey = ImgBBKeyBox.Text;
        _settingsService.Save();
    }

    private void ImgPileTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImgPileApiToken = ImgPileTokenBox.Text;
        _settingsService.Save();
    }

    private void GyazoTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GyazoAccessToken = GyazoTokenBox.Text;
        _settingsService.Save();
    }

    private void DropboxTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.DropboxAccessToken = DropboxTokenBox.Text;
        _settingsService.Save();
    }

    private void DropboxPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.DropboxPathPrefix = DropboxPathBox.Text;
        _settingsService.Save();
    }

    private void GoogleDriveTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GoogleDriveAccessToken = GoogleDriveTokenBox.Text;
        _settingsService.Save();
    }

    private void GoogleDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GoogleDriveFolderId = GoogleDriveFolderBox.Text;
        _settingsService.Save();
    }

    private void OneDriveTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.OneDriveAccessToken = OneDriveTokenBox.Text;
        _settingsService.Save();
    }

    private void OneDriveFolderBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.OneDriveFolder = OneDriveFolderBox.Text;
        _settingsService.Save();
    }

    private void AzureBlobSasBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AzureBlobSasUrl = AzureBlobSasBox.Text;
        _settingsService.Save();
    }

    private void GitHubTokenBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubToken = GitHubTokenBox.Text;
        _settingsService.Save();
    }

    private void GitHubRepoBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubRepo = GitHubRepoBox.Text;
        _settingsService.Save();
    }

    private void GitHubBranchBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.GitHubBranch = GitHubBranchBox.Text;
        _settingsService.Save();
    }

    private void ImmichUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImmichBaseUrl = ImmichUrlBox.Text;
        _settingsService.Save();
    }

    private void ImmichApiKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.ImmichApiKey = ImmichApiKeyBox.Text;
        _settingsService.Save();
    }

    private void FtpUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpUrl = FtpUrlBox.Text; _settingsService.Save(); }
    private void FtpUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpUsername = FtpUsernameBox.Text; _settingsService.Save(); }
    private void FtpPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpPassword = FtpPasswordBox.Text; _settingsService.Save(); }
    private void FtpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.FtpPublicUrl = FtpPublicUrlBox.Text; _settingsService.Save(); }
    private void SftpHostBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpHost = SftpHostBox.Text; _settingsService.Save(); }
    private void SftpPortBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; if (int.TryParse(SftpPortBox.Text, out var port)) ActiveUploadSettings.SftpPort = port; _settingsService.Save(); }
    private void SftpUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpUsername = SftpUsernameBox.Text; _settingsService.Save(); }
    private void SftpPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpPassword = SftpPasswordBox.Text; _settingsService.Save(); }
    private void SftpRemotePathBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpRemotePath = SftpRemotePathBox.Text; _settingsService.Save(); }
    private void SftpPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpPublicUrl = SftpPublicUrlBox.Text; _settingsService.Save(); }
    private void SftpHostKeyFingerprintBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.SftpHostKeyFingerprint = SftpHostKeyFingerprintBox.Text; _settingsService.Save(); }
    private void WebDavUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavUrl = WebDavUrlBox.Text; _settingsService.Save(); }
    private void WebDavUsernameBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavUsername = WebDavUsernameBox.Text; _settingsService.Save(); }
    private void WebDavPasswordBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavPassword = WebDavPasswordBox.Text; _settingsService.Save(); }
    private void WebDavPublicUrlBox_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; ActiveUploadSettings.WebDavPublicUrl = WebDavPublicUrlBox.Text; _settingsService.Save(); }

    private void S3EndpointBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Endpoint = S3EndpointBox.Text;
        _settingsService.Save();
    }

    private void S3BucketBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Bucket = S3BucketBox.Text;
        _settingsService.Save();
    }

    private void S3RegionBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3Region = S3RegionBox.Text;
        _settingsService.Save();
    }

    private void S3AccessKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3AccessKey = S3AccessKeyBox.Text;
        _settingsService.Save();
    }

    private void S3SecretKeyBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3SecretKey = S3SecretKeyBox.Text;
        _settingsService.Save();
    }

    private void S3PublicUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.S3PublicUrl = S3PublicUrlBox.Text;
        _settingsService.Save();
    }

    private void CustomUrlBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomUploadUrl = CustomUrlBox.Text;
        _settingsService.Save();
    }

    private void CustomFieldBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomFileFormName = CustomFieldBox.Text;
        _settingsService.Save();
    }

    private void CustomJsonPathBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomResponseUrlPath = CustomJsonPathBox.Text;
        _settingsService.Save();
    }

    private void CustomHeadersBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.CustomHeaders = CustomHeadersBox.Text;
        _settingsService.Save();
    }

    private async void TestUpload_Click(object sender, RoutedEventArgs e)
    {
        TestUploadBtn.Content = "Uploading...";
        TestUploadBtn.IsEnabled = false;

        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yoink_test.png");
        try
        {
            using (var bmp = new Bitmap(1, 1))
                bmp.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

            if (_settingsService.Settings.ImageUploadDestination == Services.UploadDestination.AiChat)
            {
                if (ActiveUploadSettings.AiChatProvider == Services.AiChatProvider.GoogleLens)
                {
                    var hostDest = Services.UploadService.NormalizeAiChatUploadDestination(ActiveUploadSettings.AiChatUploadDestination);
                    var uploadResult = await Services.UploadService.UploadAsync(tempPath, hostDest, ActiveUploadSettings);
                    if (uploadResult.Success)
                    {
                        var lensUrl = Services.UploadService.BuildGoogleLensUrl(uploadResult.Url);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = lensUrl,
                            UseShellExecute = true
                        });
                        ToastWindow.Show("Google Lens works", uploadResult.Url);
                    }
                    else
                    {
                        ToastWindow.ShowError("Google Lens upload failed", uploadResult.Error);
                    }
                }
                else
                {
                    var startUrl = Services.UploadService.BuildAiChatStartUrl(ActiveUploadSettings.AiChatProvider);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = startUrl,
                        UseShellExecute = true
                    });
                    ToastWindow.Show("AI redirect works", Services.UploadService.GetAiChatProviderName(ActiveUploadSettings.AiChatProvider));
                }
            }
            else
            {
                var result = await Services.UploadService.UploadAsync(
                    tempPath,
                    _settingsService.Settings.ImageUploadDestination,
                    ActiveUploadSettings);

                if (result.Success)
                    ToastWindow.Show("Upload works", result.Url);
                else
                    ToastWindow.ShowError("Upload failed", result.Error);
            }
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError("Upload error", ex.Message);
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
            TestUploadBtn.Content = "Test Upload";
            TestUploadBtn.IsEnabled = true;
        }
    }

    private readonly List<ComboBoxItem> _uploadDestItems = new();
    private bool _uploadDestItemsCached;

    private void CacheUploadDestItems()
    {
        if (_uploadDestItemsCached) return;
        _uploadDestItemsCached = true;
        _uploadDestItems.Clear();
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

        foreach (var source in _uploadDestItems)
        {
            if (source.Tag is not string tag || !int.TryParse(tag, out var raw))
                continue;

            var destination = (Services.UploadDestination)raw;
            if (destination is Services.UploadDestination.None or Services.UploadDestination.AiChat or Services.UploadDestination.TransferSh)
                continue;

            AiRedirectLensUploadDestPanelCombo.Items.Add(new ComboBoxItem
            {
                Content = source.Content,
                Tag = source.Tag
            });
        }
    }

    private void UploadDestCombo_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        CacheUploadDestItems();
        UploadDestCombo.IsDropDownOpen = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var editText = UploadDestCombo.Text?.Trim() ?? "";
            UploadDestCombo.Items.Clear();

            if (string.IsNullOrEmpty(editText))
            {
                foreach (var item in _uploadDestItems)
                    UploadDestCombo.Items.Add(item);
            }
            else
            {
                var lower = editText.ToLowerInvariant();
                foreach (var item in _uploadDestItems)
                {
                    var content = (item.Content as string ?? "").ToLowerInvariant();
                    if (content.Contains(lower))
                        UploadDestCombo.Items.Add(item);
                }
            }

            UploadDestCombo.IsDropDownOpen = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UploadDestCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
        {
            CacheUploadDestItems();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var editText = UploadDestCombo.Text?.Trim() ?? "";
                UploadDestCombo.Items.Clear();

                if (string.IsNullOrEmpty(editText))
                {
                    foreach (var item in _uploadDestItems)
                        UploadDestCombo.Items.Add(item);
                }
                else
                {
                    var lower = editText.ToLowerInvariant();
                    foreach (var item in _uploadDestItems)
                    {
                        var content = (item.Content as string ?? "").ToLowerInvariant();
                        if (content.Contains(lower))
                            UploadDestCombo.Items.Add(item);
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
