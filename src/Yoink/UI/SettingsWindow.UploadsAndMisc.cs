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
        _settingsService.Save();
        UpdateUploadSettingsVisibility();
    }

    private void UpdateUploadSettingsVisibility()
    {
        var dest = GetSelectedUploadDest();
        var isAiRedirects = dest == Services.UploadDestination.AiChat;
        AiChatSettings.Visibility = dest == Services.UploadDestination.AiChat ? Visibility.Visible : Visibility.Collapsed;
        UpdateAiChatProviderVisibility();
        ImgurSettings.Visibility = dest == Services.UploadDestination.Imgur ? Visibility.Visible : Visibility.Collapsed;
        ImgBBSettings.Visibility = dest == Services.UploadDestination.ImgBB ? Visibility.Visible : Visibility.Collapsed;
        CatboxSettings.Visibility = dest == Services.UploadDestination.Catbox ? Visibility.Visible : Visibility.Collapsed;
        LitterboxSettings.Visibility = dest == Services.UploadDestination.Litterbox ? Visibility.Visible : Visibility.Collapsed;
        GyazoSettings.Visibility = dest == Services.UploadDestination.Gyazo ? Visibility.Visible : Visibility.Collapsed;
        FileIoSettings.Visibility = dest == Services.UploadDestination.FileIo ? Visibility.Visible : Visibility.Collapsed;
        UguuSettings.Visibility = dest == Services.UploadDestination.Uguu ? Visibility.Visible : Visibility.Collapsed;
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
        AutoUploadHeader.Visibility = isAiRedirects ? Visibility.Collapsed : Visibility.Visible;
        AutoUploadCard.Visibility = isAiRedirects ? Visibility.Collapsed : Visibility.Visible;
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

    private Services.AiChatProvider GetSelectedAiChatProvider()
    {
        if (AiChatProviderCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var value))
            return (Services.AiChatProvider)value;
        return Services.AiChatProvider.ChatGpt;
    }

    private Services.UploadDestination GetSelectedAiChatUploadDest()
    {
        if (AiChatLensUploadDestCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var value))
        {
            return Services.UploadService.NormalizeAiChatUploadDestination((Services.UploadDestination)value);
        }

        return Services.UploadDestination.Catbox;
    }

    private void AiChatProviderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AiChatProvider = GetSelectedAiChatProvider();
        UpdateAiChatProviderVisibility();
        _settingsService.Save();
    }

    private void AiChatLensUploadDestCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ActiveUploadSettings.AiChatUploadDestination = GetSelectedAiChatUploadDest();
        _settingsService.Save();
    }

    private void UpdateAiChatProviderVisibility()
    {
        var isLens = GetSelectedAiChatProvider() == Services.AiChatProvider.GoogleLens;
        AiChatLensUploadHostRow.Visibility = isLens ? Visibility.Visible : Visibility.Collapsed;
        AiChatLensUploadHint.Visibility = isLens ? Visibility.Visible : Visibility.Collapsed;
        AiRedirectHotkeyOnlyRow.Visibility = Visibility.Visible;
        AiRedirectHotkeyRow.Visibility = AiRedirectHotkeyOnlyCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AiRedirectHotkeyOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settingsService.Settings.AiRedirectHotkeyOnly = AiRedirectHotkeyOnlyCheck.IsChecked == true;
        UpdateAiChatProviderVisibility();
        _settingsService.Save();
    }

    private void AiRedirectHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AiRedirectHotkeyBox.Text = "Press keys...";
    }

    private void AiRedirectHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        AiRedirectHotkeyBox.Text = HotkeyFormatter.Format(_settingsService.Settings.AiRedirectHotkeyModifiers, _settingsService.Settings.AiRedirectHotkeyKey);
    }

    private void AiRedirectHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleAiRedirectHotkeyKeyInput(e, e.Key == Key.System ? e.SystemKey : e.Key);
    }

    private void AiRedirectHotkeyBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Snapshot or Key.Pause or Key.Cancel)
            HandleAiRedirectHotkeyKeyInput(e, key);
    }

    private void AiRedirectHotkeyClearBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.AiRedirectHotkeyModifiers = 0;
        _settingsService.Settings.AiRedirectHotkeyKey = 0;
        _settingsService.Save();
        AiRedirectHotkeyBox.Text = HotkeyFormatter.Format(0, 0);
        HotkeyChanged?.Invoke();
    }

    private void HandleAiRedirectHotkeyKeyInput(System.Windows.Input.KeyEventArgs e, Key key)
    {
        if (!AiRedirectHotkeyBox.IsKeyboardFocusWithin)
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
            var result = MessageBox.Show(
                $"{combo} is already used by \"{conflict}\".\n\nReplace it?",
                "Hotkey conflict",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                AiRedirectHotkeyBox.Text = HotkeyFormatter.Format(_settingsService.Settings.AiRedirectHotkeyModifiers, _settingsService.Settings.AiRedirectHotkeyKey);
                Keyboard.ClearFocus();
                return;
            }

            ClearAiRedirectConflict(modifiers, vk);
        }

        _settingsService.Settings.AiRedirectHotkeyModifiers = modifiers;
        _settingsService.Settings.AiRedirectHotkeyKey = vk;
        _settingsService.Save();
        AiRedirectHotkeyBox.Text = HotkeyFormatter.Format(modifiers, vk);
        Keyboard.ClearFocus();
        HotkeyChanged?.Invoke();
    }

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

    private void SelectAiChatProviderByValue(int providerValue)
    {
        var tag = providerValue.ToString();
        foreach (ComboBoxItem item in AiChatProviderCombo.Items)
        {
            if (item.Tag as string == tag)
            {
                AiChatProviderCombo.SelectedItem = item;
                return;
            }
        }

        if (AiChatProviderCombo.Items.Count > 0)
            AiChatProviderCombo.SelectedIndex = 0;
    }

    private void SelectAiChatUploadDestByValue(int destValue)
    {
        var tag = destValue.ToString();
        foreach (ComboBoxItem item in AiChatLensUploadDestCombo.Items)
        {
            if (item.Tag as string == tag)
            {
                AiChatLensUploadDestCombo.SelectedItem = item;
                return;
            }
        }

        if (AiChatLensUploadDestCombo.Items.Count > 0)
            AiChatLensUploadDestCombo.SelectedIndex = 0;
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
