using System.Reflection;
using System.Text.RegularExpressions;
using OddSnap.Services;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class SettingsWindowVisualPolishTests
{
    [Fact]
    public void TopLevelSettingsPagesAllowHorizontalOverflowAtMinimumSize()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var code = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        Assert.Contains("Width=\"1080\"", xaml);
        Assert.Contains("MinHeight=\"560\" MinWidth=\"1080\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"150\"/>", xaml);
        Assert.Contains("Padding=\"10,14,8,18\"", xaml);
        Assert.Contains("Margin=\"18,10,18,0\"", xaml);
        Assert.Contains("<WrapPanel>", xaml[xaml.LastIndexOf("<Border Padding=\"0,0,0,10\">", xaml.IndexOf("x:Name=\"UploadImagesSubTab\"", StringComparison.Ordinal), StringComparison.Ordinal)..xaml.IndexOf("</WrapPanel>", xaml.IndexOf("x:Name=\"UploadUpscaleSubTab\"", StringComparison.Ordinal), StringComparison.Ordinal)]);

        AssertSettingsPageAllowsHorizontalOverflow(xaml, "HotkeysPanel");
        AssertSettingsPageDisablesHorizontalOverflow(xaml, "CapturePanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "RecordingPanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "OcrPanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "SettingsPanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "ToastPanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "AboutPanel");
        AssertSettingsPageAllowsHorizontalOverflow(xaml, "UploadsPanel");
        Assert.Contains("Padding=\"18,42,18,18\"", xaml);
        Assert.DoesNotContain("Padding=\"24,52,24,24\"", xaml);
        Assert.DoesNotContain("Padding=\"32,52,32,24\"", xaml);
        Assert.DoesNotContain("MinWidth=\"220\"", xaml);
        Assert.DoesNotContain("MinWidth=\"210\"", xaml);
        Assert.DoesNotContain("MinWidth=\"190\"", xaml);
        Assert.DoesNotContain("MinWidth=\"180\" MaxWidth=\"300\"", xaml);
        Assert.DoesNotContain("Width=\"356\"", xaml);

        var fitBlock = GetMethodBlock(code, "private void EnsureSettingsWindowFitsWorkArea()");
        Assert.Contains("SystemParameters.WorkArea", fitBlock);
        Assert.Contains("MinWidth = Math.Min(MinWidth, maxWidth);", fitBlock);
        Assert.Contains("if (Width > maxWidth)", fitBlock);
        Assert.Contains("Left = Math.Min(Math.Max(Left, minLeft), Math.Max(minLeft, maxLeft));", fitBlock);
        Assert.Contains("Loaded += (_, _) => EnsureSettingsWindowFitsWorkArea();", code);
    }

    [Fact]
    public void SettingsRowsKeepNormalTwoColumnLayoutAtMinimumSize()
    {
        var code = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        var appearanceCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Appearance.cs"));

        Assert.DoesNotContain("ScheduleResponsiveSettingRowsUpdate", code);
        Assert.DoesNotContain("ScheduleResponsiveSettingRowsUpdate", appearanceCode);
        Assert.DoesNotContain("UpdateResponsiveSettingRows", code);
        Assert.DoesNotContain("ShouldCompactSettingRows", code);
        Assert.DoesNotContain("ResponsiveSettingControlState", code);
    }

    [Fact]
    public void EmptyContentSettingsCheckBoxesHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        var checkBoxes = new (string Name, string AutomationName, string ToolTip)[]
        {
            ("ShowCursorCheck", "Show cursor in captures and recordings", "Include the pointer in saved media"),
            ("CrosshairGuidesCheck", "Show crosshair guides", "Show alignment guides while selecting"),
            ("ShowCaptureMagnifierCheck", "Show pixel magnifier while selecting", "Zoom the cursor area during selection"),
            ("OverlayAllMonitorsCheck", "Span selection overlay across all monitors", "Use one overlay across the full virtual desktop"),
            ("AnnotationStrokeShadowCheck", "Annotation stroke and shadow", "Keep annotations readable on mixed backgrounds"),
            ("SaveToFileCheck", "Save screenshots to file", "Write screenshots to the configured save folder"),
            ("AskFileNameCheck", "Ask for file name every time", "Prompt for a file name before each saved capture"),
            ("MonthlyFoldersCheck", "Create monthly subfolders", "Organize captures into year-month folders"),
            ("SaveHistoryCheck", "Save capture history", "Keep captures available in the History page"),
            ("RecordShowCursorCheck", "Show cursor in recordings", "Include pointer movement in recorded output."),
            ("RecordMicCheck", "Record microphone", "Capture audio from the selected input device."),
            ("RecordDesktopAudioCheck", "Record desktop audio", "Capture system audio from the selected output device."),
            ("MuteSoundsCheck", "Mute capture sounds", "Silence the sound played when capturing."),
            ("DisableAnimationsCheck", "Disable animations", "Reduce motion effects across the app."),
            ("ShowImageSearchBarCheck", "Show image search bar", "Display the search bar in the image browser."),
            ("AutoIndexImagesCheck", "Auto-index images", "Automatically index new images for search."),
            ("ShowImageSearchDiagnosticsCheck", "Show search diagnostics", "Show diagnostics and performance info in search."),
            ("AutoPinPreviewsCheck", "Auto-pin screenshot previews", "Keep new previews open until dismissed."),
            ("ToastFadeOutCheck", "Fade out instead of sliding away", "Use a quieter preview dismissal animation."),
            ("AutoUpdateCheck", "Automatically check for updates", "Notify when a newer OddSnap release is available."),
            ("StartWithWindowsCheck", "Launch on startup", "Start OddSnap automatically when Windows starts."),
            ("AutoUploadScreenshotsCheck", "Auto-upload screenshots", "Uploads screenshots after capture"),
            ("AutoUploadGifsCheck", "Auto-upload GIFs", "Uploads GIF captures after recording"),
            ("AutoUploadVideosCheck", "Auto-upload videos", "Uploads video captures when recording completes"),
            ("AiRedirectLensUploadSyncCheck", "Synced with Images uploads", "Use the same image host for Google Lens redirects."),
            ("StickerShadowCheck", "Add drop shadow", "Give generated stickers a subtle shadow."),
            ("StickerStrokeCheck", "Add white stroke", "Add a clean border around generated stickers."),
            ("UpscaleShowPreviewWindowCheck", "Show preview window", "Open the upscale preview after selecting an image.")
        };

        foreach (var checkBox in checkBoxes)
        {
            AssertNamedControlHasLabel(xaml, checkBox.Name, "<CheckBox", checkBox.AutomationName, checkBox.ToolTip);
        }

        foreach (Match match in Regex.Matches(xaml, @"<CheckBox\b[^>]*Content=""""[^>]*>", RegexOptions.Singleline))
        {
            Assert.Contains("AutomationProperties.Name=", match.Value);
            Assert.Contains("ToolTip=", match.Value);
        }
    }

    [Fact]
    public void SettingsComboBoxesHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        var comboBoxes = new (string Name, string AutomationName, string ToolTip)[]
        {
            ("DefaultCaptureModeCombo", "Default capture tool", "Choose which selection mode opens first."),
            ("CenterAspectRatioCombo", "Center aspect ratio", "Optional ratio lock for center selection."),
            ("WindowDetectionCombo", "Window detection", "Detect windows when hovering before selection."),
            ("CaptureDockSideCombo", "Capture dock", "Position the capture toolbar around the screen."),
            ("ScrollingCaptureModeCombo", "Scrolling capture mode", "Choose whether scrolling capture collects frames itself or only when you click capture."),
            ("AfterCaptureCombo", "After capture", "Choose what OddSnap does after saving the selection."),
            ("CaptureDelayCombo", "Capture delay", "Wait before the capture overlay opens."),
            ("CaptureFormatCombo", "Default format", "Set the file type for image captures."),
            ("JpegQualityCombo", "JPG quality", "Balance quality against file size."),
            ("CaptureSizeCombo", "Max image size", "Resize oversized captures after saving."),
            ("HistoryRetentionCombo", "Auto-clear history", "Automatically prune old capture entries."),
            ("OcrLanguageCombo", "OCR language", "Choose the language used for text recognition."),
            ("TranslateFromCombo", "Default source language", "Language to translate from by default."),
            ("TranslateToCombo", "Default target language", "Language to translate into by default."),
            ("TranslateModelCombo", "Translation engine", "Choose which translator handles OCR text."),
            ("InterfaceLanguageCombo", "Interface language", "Choose your preferred language for OddSnap."),
            ("UiScaleCombo", "UI scale", "Manually scale OddSnap windows, toasts, and capture controls."),
            ("SoundPackCombo", "Sound pack", "Choose the sound pack for notifications."),
            ("ToastPositionCombo", "Toast position", "Choose where screenshot previews appear."),
            ("ToastDurationCombo", "Toast duration", "Set how long previews stay visible."),
            ("ToastFadeDurationCombo", "Fade-out duration", "Control how quickly faded previews disappear."),
            ("UploadDestCombo", "Upload service", "Choose where screenshots and captures are uploaded."),
            ("AiRedirectProviderCombo", "AI tool", "Choose the app OddSnap opens after an AI redirect capture."),
            ("AiRedirectLensUploadDestPanelCombo", "Lens upload service", "Pick the public host used before opening Google Lens."),
            ("StickerProviderCombo", "Sticker provider", "Choose the background removal engine for sticker captures."),
            ("StickerLocalExecutionCombo", "Sticker execution mode", "Choose CPU or CUDA acceleration for local stickers."),
            ("StickerLocalCpuEngineCombo", "Sticker CPU model", "Pick the local model used without GPU acceleration."),
            ("StickerLocalGpuEngineCombo", "Sticker GPU model", "Pick the CUDA model used for local stickers."),
            ("UpscaleProviderCombo", "Upscale provider", "Choose the engine used for upscale captures."),
            ("UpscaleLocalExecutionCombo", "Upscale execution mode", "Choose CPU or CUDA acceleration for local upscaling."),
            ("UpscaleLocalCpuEngineCombo", "Upscale CPU model", "Pick the local model used without GPU acceleration."),
            ("UpscaleLocalGpuEngineCombo", "Upscale GPU model", "Pick the CUDA model used for local upscaling."),
            ("UpscaleDefaultScaleCombo", "Default multiplier", "Used when preview is off.")
        };

        foreach (var comboBox in comboBoxes)
        {
            AssertNamedControlHasLabel(xaml, comboBox.Name, "<ComboBox", comboBox.AutomationName, comboBox.ToolTip);
        }

        foreach (Match match in Regex.Matches(xaml, @"<ComboBox\b[^>]*x:Name=""[^""]+""[^>]*>", RegexOptions.Singleline))
        {
            Assert.Contains("AutomationProperties.Name=", match.Value);
            Assert.Contains("ToolTip=", match.Value);
        }
    }

    [Fact]
    public void SettingsTextInputsAndActionButtonsHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertNamedControlHasLabel(xaml, "FileNameTemplateBox", "<TextBox", "File name pattern", "Use tokens to build capture file names.");
        AssertSettingsActionButton(xaml, "OpenSourceLocalInstallBtn", "Install open-source local translator", "Install or remove the open-source local translation runtime", "OpenSourceLocalInstallBtn_Click");
        AssertSettingsActionButton(xaml, "ArgosInstallBtn", "Install Argos Translate", "Install or remove the Argos local translation runtime", "ArgosInstallBtn_Click");
        AssertSettingsActionButton(xaml, "ResetImageIndexesBtn", "Reset image search cache", "Reset the image search index cache", "ResetImageIndexesBtn_Click");
        AssertSettingsActionButton(xaml, "ResetToastButtonsBtn", "Reset toast button layout", "Restore the default toast button layout", "ResetToastButtonsBtn_Click");

        foreach (Match match in Regex.Matches(xaml, @"<TextBox\b[^>]*x:Name=""[^""]+""[^>]*>", RegexOptions.Singleline))
        {
            Assert.Contains("AutomationProperties.Name=", match.Value);
            Assert.Contains("ToolTip=", match.Value);
        }

        foreach (Match match in Regex.Matches(xaml, @"<Button\b[^>]*x:Name=""[^""]+""[^>]*>", RegexOptions.Singleline))
        {
            Assert.Contains("AutomationProperties.Name=", match.Value);
            Assert.Contains("ToolTip=", match.Value);
            Assert.Contains("Cursor=\"Hand\"", match.Value);
        }
    }

    [Fact]
    public void ToastLayoutDesignerControlsAreKeyboardAccessible()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Toast.cs"));
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertDynamicStatusTextBlock(xaml, "ToastLayoutSelectionText", "Toast layout selection", isLive: true);
        var selectionTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ToastLayoutSelectionText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", selectionTag);

        var designerBlock = GetMethodBlock(toastCode, "private void RefreshToastButtonLayoutDesigner()");
        Assert.Contains("ToastLayoutSelectionText.Text = selectionText;", designerBlock);
        Assert.Contains("ToastLayoutSelectionText.ToolTip = selectionText;", designerBlock);
        Assert.Contains("AutomationProperties.SetHelpText(ToastLayoutSelectionText, selectionText);", designerBlock);

        var buttonBlock = GetMethodBlock(toastCode, "private void UpdateToastLayoutButton(Border border, ToastButtonKind button)");
        Assert.Contains("border.Focusable = true;", buttonBlock);
        Assert.Contains("AutomationProperties.SetName(border, $\"{label} toast button\");", buttonBlock);
        Assert.Contains("AutomationProperties.SetHelpText(border, \"Press Enter or Space to move the selected button here.\");", buttonBlock);
        Assert.Contains("border.KeyDown -= ToastLayoutButton_KeyDown;", buttonBlock);
        Assert.Contains("border.KeyDown += ToastLayoutButton_KeyDown;", buttonBlock);

        var slotBlock = GetMethodBlock(toastCode, "private void UpdateToastLayoutSlot(Border slotBorder, ToastButtonSlot slot)");
        Assert.Contains("slotBorder.Focusable = true;", slotBlock);
        Assert.Contains("AutomationProperties.SetName(slotBorder, $\"{label} toast slot\");", slotBlock);
        Assert.Contains("AutomationProperties.SetHelpText(slotBorder, \"Press Enter or Space to place the selected toast button here.\");", slotBlock);
        Assert.Contains("slotBorder.KeyDown -= ToastLayoutSlot_KeyDown;", slotBlock);
        Assert.Contains("slotBorder.KeyDown += ToastLayoutSlot_KeyDown;", slotBlock);

        var shelfBlock = GetMethodBlock(toastCode, "private void RefreshToastHiddenShelf()");
        Assert.Contains("ToastHiddenShelf.Focusable = true;", shelfBlock);
        Assert.Contains("AutomationProperties.SetName(ToastHiddenShelf, \"Hidden toast button shelf\");", shelfBlock);
        Assert.Contains("AutomationProperties.SetHelpText(ToastHiddenShelf, \"Press Enter or Space to hide the selected toast button.\");", shelfBlock);
        Assert.Contains("ToastHiddenShelf.KeyDown -= ToastHiddenShelf_KeyDown;", shelfBlock);
        Assert.Contains("ToastHiddenShelf.KeyDown += ToastHiddenShelf_KeyDown;", shelfBlock);

        var hiddenChipBlock = GetMethodBlock(toastCode, "private Border CreateHiddenToastButtonChip(ToastButtonKind button)");
        Assert.Contains("Focusable = true,", hiddenChipBlock);
        Assert.Contains("ToolTip = $\"Select hidden {label} toast button\",", hiddenChipBlock);
        Assert.Contains("AutomationProperties.SetName(chip, $\"Hidden {label} toast button\");", hiddenChipBlock);
        Assert.Contains("AutomationProperties.SetHelpText(chip, \"Press Enter or Space to select this hidden button, then choose a slot.\");", hiddenChipBlock);
        Assert.Contains("chip.KeyDown += ToastHiddenButton_KeyDown;", hiddenChipBlock);

        Assert.Contains("private void ToastLayoutButton_KeyDown(object sender, KeyEventArgs e)", toastCode);
        Assert.Contains("private void ToastLayoutSlot_KeyDown(object sender, KeyEventArgs e)", toastCode);
        Assert.Contains("private void ToastHiddenShelf_KeyDown(object sender, KeyEventArgs e)", toastCode);
        Assert.Contains("private void ToastHiddenButton_KeyDown(object sender, KeyEventArgs e)", toastCode);
        Assert.Contains("=> e.Key is Key.Enter or Key.Space;", toastCode);
    }

    [Fact]
    public void SensitiveCredentialFieldsAreMasked()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        Assert.Contains("ToolTip\" Value=\"Hidden for safety. Paste a new value to replace it.\"", xaml);
        Assert.Contains("AutomationProperties.HelpText\" Value=\"Hidden for safety. Paste a new value to replace it.\"", xaml);

        AssertPasswordBox(xaml, "GoogleApiKeyBox", "Google Translate API key");
        AssertPasswordBox(xaml, "ImgurTokenBox", "Imgur access token");
        AssertPasswordBox(xaml, "ImgBBKeyBox", "ImgBB API key");
        AssertPasswordBox(xaml, "ImgPileTokenBox", "imgpile API token");
        AssertPasswordBox(xaml, "GyazoTokenBox", "Gyazo access token");
        AssertPasswordBox(xaml, "DropboxTokenBox", "Dropbox access token");
        AssertPasswordBox(xaml, "GoogleDriveTokenBox", "Google Drive access token");
        AssertPasswordBox(xaml, "OneDriveTokenBox", "OneDrive access token");
        AssertPasswordBox(xaml, "AzureBlobSasBox", "Azure Blob SAS URL");
        AssertPasswordBox(xaml, "GitHubTokenBox", "GitHub token");
        AssertPasswordBox(xaml, "ImmichApiKeyBox", "Immich API key");
        AssertPasswordBox(xaml, "FtpPasswordBox", "FTP password");
        AssertPasswordBox(xaml, "SftpPasswordBox", "SFTP password");
        AssertPasswordBox(xaml, "WebDavPasswordBox", "WebDAV password");
        AssertPasswordBox(xaml, "S3SecretKeyBox", "S3 secret key");
        AssertPasswordBox(xaml, "StickerRemoveBgKeyBox", "Remove.bg API key");
        AssertPasswordBox(xaml, "StickerPhotoroomKeyBox", "Photoroom API key");
        AssertPasswordBox(xaml, "UpscaleDeepAiApiKeyBox", "DeepAI API key");
    }

    [Fact]
    public void UploadAndLocalModelHelperTextUsesReadableStyles()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("x:Key=\"SettingsHelperText\"", xaml);
        Assert.Contains("x:Key=\"SettingsStatusText\"", xaml);
        Assert.Contains("x:Key=\"SettingsProgressText\"", xaml);

        AssertTextBlockUsesStyle(xaml, "No setup required.", "SettingsHelperText");
        AssertTextBlockUsesStyle(xaml, "Temporary host. No setup required.", "SettingsHelperText");
        AssertTextBlockUsesStyle(xaml, "No setup required. Files delete after the first download.", "SettingsHelperText");
        AssertTextBlockUsesStyle(xaml, "Optional cloud providers. Local remains the default.", "SettingsHelperText");

        AssertNamedTextBlockUsesStyle(xaml, "AiRedirectLensUploadPanelHint", "SettingsHelperText");
        AssertNamedTextBlockUsesStyle(xaml, "StickerLocalEngineStatusText", "SettingsStatusText");
        AssertNamedTextBlockUsesStyle(xaml, "StickerLocalEngineProgressText", "SettingsProgressText");
        AssertNamedTextBlockUsesStyle(xaml, "UpscaleLocalEngineStatusText", "SettingsStatusText");
        AssertNamedTextBlockUsesStyle(xaml, "UpscaleLocalEngineProgressText", "SettingsProgressText");

        AssertDynamicStatusTextBlock(xaml, "StickerLocalEngineStatusText", "Sticker local runtime status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "StickerLocalEngineProgressText", "Sticker local runtime progress", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "UpscaleLocalEngineStatusText", "Upscale local runtime status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "UpscaleLocalEngineProgressText", "Upscale local runtime progress", isLive: true);

        var providerComboBlock = GetMethodBlock(uploadCode, "private void ApplyComboIcons(System.Windows.Controls.ComboBox combo, Func<string, string?> assetSelector)");
        Assert.Contains("SetSettingsComboItemMetadata(item, combo.Name, text, GetSettingsComboItemHelpText(combo.Name, text));", providerComboBlock);

        var textComboBlock = GetMethodBlock(uploadCode, "private void ApplyTextComboIcons(System.Windows.Controls.ComboBox combo, Func<string, string> iconSelector)");
        Assert.Contains("SetSettingsComboItemMetadata(item, combo.Name, text, GetSettingsComboItemHelpText(combo.Name, text));", textComboBlock);

        var comboMetadataBlock = GetMethodBlock(uploadCode, "private static void SetSettingsComboItemMetadata(ComboBoxItem item, string comboName, string text, string helpText)");
        Assert.Contains("item.ToolTip = helpText;", comboMetadataBlock);
        Assert.Contains("AutomationProperties.SetName(item, GetSettingsComboItemAutomationName(comboName, text));", comboMetadataBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", comboMetadataBlock);

        var comboNameBlock = GetMethodBlock(uploadCode, "private static string GetSettingsComboItemAutomationName(string comboName, string text)");
        Assert.Contains("No AI redirect provider", comboNameBlock);
        Assert.Contains("{text} AI redirect provider", comboNameBlock);
        Assert.Contains("{text} sticker provider", comboNameBlock);
        Assert.Contains("{text} upscale provider", comboNameBlock);
        Assert.Contains("{text} sticker execution mode", comboNameBlock);
        Assert.Contains("{text} upscale model", comboNameBlock);

        var comboHelpBlock = GetMethodBlock(uploadCode, "private static string GetSettingsComboItemHelpText(string comboName, string text)");
        Assert.Contains("Do not open an AI tool after AI Redirect captures.", comboHelpBlock);
        Assert.Contains("Open {text} after an AI Redirect capture.", comboHelpBlock);
        Assert.Contains("Do not run background removal for sticker captures.", comboHelpBlock);
        Assert.Contains("Use the local sticker runtime configured below.", comboHelpBlock);
        Assert.Contains("Do not upscale captures.", comboHelpBlock);
        Assert.Contains("Use the local upscale runtime configured below.", comboHelpBlock);
        Assert.Contains("Run local sticker processing on {text}.", comboHelpBlock);
        Assert.Contains("Use {text} for local upscaling.", comboHelpBlock);
    }

    [Fact]
    public void UploadProviderSelectorsUseResponsiveWidth()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        AssertSettingsSelectorUsesResponsiveWidth(xaml, "UploadDestCombo", "145", "240");
        AssertSettingsSelectorUsesResponsiveWidth(xaml, "AiRedirectProviderCombo", "140", "240");
        AssertSettingsSelectorUsesResponsiveWidth(xaml, "AiRedirectLensUploadDestPanelCombo", "140", "240");
        AssertNamedControlHasLabel(xaml, "UploadDestCombo", "<ComboBox", "Upload service", "Choose where screenshots and captures are uploaded.");
        AssertNamedControlHasLabel(xaml, "AiRedirectProviderCombo", "<ComboBox", "AI tool", "Choose the app OddSnap opens after an AI redirect capture.");
        AssertNamedControlHasLabel(xaml, "AiRedirectLensUploadDestPanelCombo", "<ComboBox", "Lens upload service", "Pick the public host used before opening Google Lens.");

        var uploadDestBlock = GetMethodBlock(uploadCode, "private void EnsureUploadDestinationComboIcons()");
        Assert.Contains("SetUploadDestinationItemMetadata(item, (Services.UploadDestination)raw, text, forLensUpload: false);", uploadDestBlock);

        var lensDestBlock = GetMethodBlock(uploadCode, "private void RebuildAiRedirectPanelUploadDestItems()");
        Assert.Contains("SetUploadDestinationItemMetadata(item, destination, GetUploadDestinationFilterText(item), forLensUpload: true);", lensDestBlock);

        var itemMetadataBlock = GetMethodBlock(uploadCode, "private static void SetUploadDestinationItemMetadata(");
        Assert.Contains("item.ToolTip = helpText;", itemMetadataBlock);
        Assert.Contains("(Services.UploadDestination.None, true) => \"No Lens upload destination\"", itemMetadataBlock);
        Assert.Contains("(_, true) => $\"{text} Lens upload destination\"", itemMetadataBlock);
        Assert.Contains("_ => $\"{text} upload destination\"", itemMetadataBlock);
        Assert.Contains("AutomationProperties.SetName(item, automationName);", itemMetadataBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", itemMetadataBlock);
        Assert.Contains("Use {text} as the hosted image service before opening Google Lens.", itemMetadataBlock);

        var helpTextBlock = GetMethodBlock(uploadCode, "private static string GetUploadDestinationHelpText(Services.UploadDestination destination, string text)");
        Assert.Contains("Do not upload captures automatically.", helpTextBlock);
        Assert.Contains("Automatically try free, no-setup public hosts until one works.", helpTextBlock);
        Assert.Contains("This is a temporary public host and needs no setup.", helpTextBlock);
        Assert.Contains("This is a free public host and needs no setup.", helpTextBlock);
        Assert.Contains("Configure the required API key, token, or client ID below.", helpTextBlock);
        Assert.Contains("Configure the account or server settings below.", helpTextBlock);
        Assert.Contains("Configure the endpoint settings below.", helpTextBlock);
        Assert.Contains("Open the selected AI tool after capture; hosted image upload is configured in AI Redirect settings.", helpTextBlock);
        Assert.Contains("Use {text} for uploads.", helpTextBlock);
    }

    [Fact]
    public void UploadProviderTextFieldsUseResponsiveWidth()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3EndpointBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3BucketBox", "145", "240");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3RegionBox", "145", "240");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3AccessKeyBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3PublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "DropboxPathBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GoogleDriveFolderBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "OneDriveFolderBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GitHubRepoBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GitHubBranchBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "ImmichUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpHostBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpPortBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpRemotePathBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpHostKeyFingerprintBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomFieldBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomJsonPathBox");
        AssertTextBoxUsesResponsiveWidth(xaml, "CustomHeadersBox", "145", "360");
        AssertTextBoxWrapsLongMultilineValues(xaml, "CustomHeadersBox");
    }

    [Fact]
    public void UploadProviderPasswordFieldsUseResponsiveWidth()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "ImgurTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "ImgBBKeyBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "ImgPileTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "GyazoTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "S3SecretKeyBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "DropboxTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "GoogleDriveTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "OneDriveTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "AzureBlobSasBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "GitHubTokenBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "ImmichApiKeyBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "FtpPasswordBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "SftpPasswordBox");
        AssertUploadProviderPasswordBoxUsesResponsiveWidth(xaml, "WebDavPasswordBox");
    }

    [Fact]
    public void FileNameTemplateInputUsesResponsiveWidth()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertTextBoxUsesResponsiveWidth(xaml, "FileNameTemplateBox", "145", "300");
        AssertNamedControlHasLabel(xaml, "FileNameTemplateBox", "<TextBox", "File name pattern", "Use tokens to build capture file names.");
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"FileNameTemplateBox\"", StringComparison.Ordinal), "<TextBox"));
    }

    [Fact]
    public void FileNameTokenChipsUseWrapFriendlySpacing()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        Assert.Contains("<WrapPanel x:Name=\"FileNameTokenPanel\" Margin=\"0,8,0,-6\"/>", xaml);

        var tokenBlock = GetMethodBlock(settingsCode, "private void LoadFileNameTokenButtons()");
        Assert.Contains("MinHeight = 28", tokenBlock);
        Assert.Contains("Margin = new Thickness(0, 0, 6, 6),", tokenBlock);
        Assert.DoesNotContain("new Thickness(6, 0, 0, 0)", tokenBlock);
    }

    [Fact]
    public void FileNameTemplateTokenButtonsAreAccessibleAndEasyToClick()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var tokenButtonBlock = GetMethodBlock(settingsCode, "private void LoadFileNameTokenButtons()");

        Assert.Contains("ToolTip = $\"Insert {label} token\"", tokenButtonBlock);
        Assert.Contains("MinHeight = 28", tokenButtonBlock);
        Assert.Contains("Padding = new Thickness(9, 4, 9, 4)", tokenButtonBlock);
        Assert.Contains("Cursor = System.Windows.Input.Cursors.Hand", tokenButtonBlock);
        Assert.Contains("AutomationProperties.SetName(button, $\"Insert {label} token\");", tokenButtonBlock);
        Assert.Contains("AutomationProperties.SetHelpText(button, token);", tokenButtonBlock);
        Assert.DoesNotContain("ToolTip = label", tokenButtonBlock);
        Assert.DoesNotContain("Padding = new Thickness(8, 3, 8, 3)", tokenButtonBlock);
    }

    [Fact]
    public void SaveDirectoryControlsUseCompactReadableLayout()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        AssertTextBoxUsesResponsiveWidth(xaml, "SaveDirBox", "145", "360");

        var saveDirIndex = xaml.IndexOf("x:Name=\"SaveDirBox\"", StringComparison.Ordinal);
        var saveDirTag = GetOpeningTag(xaml, saveDirIndex, "<TextBox");
        Assert.Contains("IsReadOnly=\"True\"", saveDirTag);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", saveDirTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", saveDirTag);
        Assert.Contains("AutomationProperties.Name=\"Current save folder\"", saveDirTag);

        var appearanceCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Appearance.cs"));
        Assert.Contains("SetSaveDirectoryPath(s.SaveDirectory);", appearanceCode);

        var browseIndex = xaml.IndexOf("x:Name=\"BrowseSaveDirBtn\"", StringComparison.Ordinal);
        Assert.True(browseIndex >= 0, "Could not find BrowseSaveDirBtn.");
        var browseTag = GetOpeningTag(xaml, browseIndex, "<Button");
        Assert.Contains("ToolTip=\"Choose save folder\"", browseTag);
        Assert.Contains("AutomationProperties.Name=\"Choose save folder\"", browseTag);
        Assert.Contains("Cursor=\"Hand\"", browseTag);
        Assert.Contains("Click=\"BrowseButton_Click\"", browseTag);

    }

    [Fact]
    public void HistoryLoadFailureEmptyStateOffersRetryAction()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        Assert.Contains("x:Name=\"HistoryEmptyRetryButton\"", xaml);
        Assert.Contains("Content=\"Retry\"", xaml);
        Assert.Contains("Click=\"HistoryEmptyRetryButton_Click\"", xaml);
        AssertDynamicStatusTextBlock(xaml, "HistoryEmptyTitle", "History empty state title", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "HistoryEmptyLabel", "History empty state detail", isLive: true);
        var retryTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"HistoryEmptyRetryButton\"", StringComparison.Ordinal), "<Button");
        Assert.Contains("AutomationProperties.HelpText=\"Retry loading history\"", retryTag);
        Assert.Contains("HistoryEmptyRetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;", historyCode);
        Assert.Contains("HistoryEmptyRetryButton.Visibility = Visibility.Collapsed;", historyCode);
        var emptyStateBlock = GetMethodBlock(historyCode, "private void ShowHistoryEmptyState(string title, string detail, bool showRetry = false)");
        Assert.Contains("HistoryEmptyTitle.ToolTip = title;", emptyStateBlock);
        Assert.Contains("AutomationProperties.SetHelpText(HistoryEmptyTitle, title);", emptyStateBlock);
        Assert.Contains("HistoryEmptyLabel.ToolTip = detail;", emptyStateBlock);
        Assert.Contains("AutomationProperties.SetHelpText(HistoryEmptyLabel, detail);", emptyStateBlock);
        Assert.Contains("ShowHistoryEmptyState(\"Couldn't load captures\", \"Retry loading history. If it still fails, check the app log.\", showRetry: true);", historyCode);
    }

    [Fact]
    public void HistoryServiceChangeCallbackFailuresOfferRetryState()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        var changedBlock = GetMethodBlock(settingsCode, "private void HistoryService_Changed()");
        Assert.Contains("_ = Dispatcher.BeginInvoke(() =>", changedBlock);
        Assert.Contains("try", changedBlock);
        Assert.Contains("InvalidateHistoryCategoryCaches();", changedBlock);
        Assert.Contains("_pendingHistoryDataRefresh = true;", changedBlock);
        Assert.Contains("QueueHistoryRefresh(reloadFromDisk: false);", changedBlock);
        Assert.Contains("catch (Exception ex)", changedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.history-service-changed\", ex);", changedBlock);
        Assert.Contains("_pendingHistoryDataRefresh = false;", changedBlock);
        Assert.Contains("_pendingHistoryUiRefresh = false;", changedBlock);
        Assert.Contains("_pendingHistoryDiskRefresh = false;", changedBlock);
        Assert.Contains("_historyRefreshTimer.Stop();", changedBlock);
        Assert.Contains("ShowHistoryEmptyState(\"Couldn't refresh history\", \"Retry loading history. If it still fails, check the app log.\", showRetry: true);", changedBlock);
    }

    [Fact]
    public void SettingsImportExportActionsLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));

        Assert.Contains("x:Name=\"SettingsImportExportStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "SettingsImportExportStatusText", "SettingsHelperText");
        var statusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"SettingsImportExportStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("Visibility=\"Collapsed\"", statusTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", statusTag);
        Assert.Contains("AutomationProperties.Name=\"Settings import and export status\"", statusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", statusTag);
        AssertSettingsActionButton(xaml, "ExportSettingsBtn", "Export settings", "Export redacted settings to a JSON file", "ExportSettingsButton_Click");
        AssertSettingsActionButton(xaml, "ImportSettingsBtn", "Import settings", "Import settings from a JSON file", "ImportSettingsButton_Click");

        var exportBlock = GetMethodBlock(preferencesCode, "private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("SetSettingsImportExportStatus($\"Settings exported to {Path.GetFileName(dlg.FileName)}.\");", exportBlock);
        Assert.Contains("ShowSettingsExportFailed(ex);", exportBlock);

        var exportFailureBlock = GetMethodBlock(preferencesCode, "private void ShowSettingsExportFailed(Exception ex)");
        Assert.Contains("SetSettingsImportExportStatus(\"Export failed. Choose another folder and try again.\");", exportFailureBlock);
        Assert.Contains("OddSnap could not write the settings export. Choose another folder and try again.", exportFailureBlock);
        Assert.Contains("{ex.Message}", exportFailureBlock);

        var importBlock = GetMethodBlock(preferencesCode, "private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("SetSettingsImportExportStatus(\"Import failed: invalid settings file.\");", importBlock);
        Assert.Contains("SetSettingsImportExportStatus(\"Settings imported and applied.\");", importBlock);
        Assert.Contains("AppSettings? previous = null;", preferencesCode);
        Assert.Contains("previous = _settingsService.Settings;", importBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.import\", ex);", importBlock);
        Assert.Contains("_settingsService.Settings = previous;", importBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.import-rollback\", rollbackEx);", importBlock);
        Assert.Contains("RestoreSettingsUiAfterFailedReset();", importBlock);
        Assert.Contains("ShowSettingsImportFailed(previous is not null, ex);", importBlock);

        var importFailureBlock = GetMethodBlock(preferencesCode, "private void ShowSettingsImportFailed(bool restoredPrevious, Exception ex)");
        Assert.Contains("SetSettingsImportExportStatus(restoredPrevious", importFailureBlock);
        Assert.Contains("Import failed. Previous settings restored.", importFailureBlock);
        Assert.Contains("The imported settings were not saved. Previous settings were restored. Check the file and try again.", importFailureBlock);
        Assert.Contains("Import failed. Check the file and try again.", importFailureBlock);
        Assert.Contains("OddSnap could not import settings. Check the file and try again.", importFailureBlock);
        Assert.Contains("ToastWindow.ShowError(\"Import failed\", message);", importFailureBlock);

        var statusBlock = GetMethodBlock(preferencesCode, "private void SetSettingsImportExportStatus(string message)");
        Assert.Contains("SettingsImportExportStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void ImageIndexResetLeavesInlineStatusAndLogsFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));

        Assert.Contains("x:Name=\"ResetImageIndexesBtn\"", xaml);
        Assert.Contains("x:Name=\"ImageIndexMaintenanceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "ImageIndexMaintenanceStatusText", "SettingsStatusText");
        var statusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageIndexMaintenanceStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("Visibility=\"Collapsed\"", statusTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", statusTag);
        Assert.Contains("AutomationProperties.Name=\"Image index status\"", statusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", statusTag);
        Assert.Contains("private bool ImageIndexResetInProgress { get; set; }", settingsCode);

        var resetBlock = GetMethodBlock(preferencesCode, "private void ResetImageIndexesBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (ImageIndexResetInProgress)", resetBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image index reset is already running.\");", resetBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image index reset canceled. Existing search data was left in place.\");", resetBlock);
        Assert.Contains("ImageIndexResetInProgress = true;", resetBlock);
        Assert.Contains("ResetImageIndexesBtn.IsEnabled = false;", resetBlock);
        Assert.Contains("ResetImageIndexesBtn.Content = \"Resetting...\";", resetBlock);
        Assert.Contains("try", resetBlock);
        Assert.Contains("_imageSearchIndexService.ReindexAll(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);", resetBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image search index reset requested.\");", resetBlock);
        Assert.Contains("ToastWindow.Show(\"Image indexes reset\", \"Screenshot search will rebuild in the background.\");", resetBlock);
        Assert.Contains("catch (Exception ex)", resetBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-index-reset\", ex);", resetBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image index reset failed. Existing search data was left in place.\");", resetBlock);
        Assert.Contains("OddSnap could not reset the image search index. Existing search data was left in place. Try again from Settings.", resetBlock);
        Assert.Contains("return;", resetBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-index-reset-history-refresh\", ex);", resetBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image index reset requested, but History did not refresh.\");", resetBlock);
        Assert.Contains("The image index reset was requested, but History did not refresh. Switch tabs or use Retry in History.", resetBlock);
        Assert.Contains("finally", resetBlock);
        Assert.Contains("ImageIndexResetInProgress = false;", resetBlock);
        Assert.Contains("ResetImageIndexesBtn.Content = \"Reset cache\";", resetBlock);
        Assert.Contains("ResetImageIndexesBtn.IsEnabled = true;", resetBlock);

        var statusBlock = GetMethodBlock(preferencesCode, "private void SetImageIndexMaintenanceStatus(string message)");
        Assert.Contains("ImageIndexMaintenanceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void AutoIndexImagesSettingRollsBackAndReportsFailures()
    {
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));

        Assert.Contains("private bool _suppressAutoIndexImagesChange;", preferencesCode);

        var autoIndexBlock = GetMethodBlock(preferencesCode, "private void AutoIndexImagesCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressAutoIndexImagesChange) return;", autoIndexBlock);
        Assert.Contains("var previous = _settingsService.Settings.AutoIndexImages;", autoIndexBlock);
        Assert.Contains("_settingsService.Settings.AutoIndexImages = enabled;", autoIndexBlock);
        Assert.Contains("_settingsService.Save();", autoIndexBlock);
        Assert.Contains("_imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);", autoIndexBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(enabled", autoIndexBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.auto-index-images\", ex);", autoIndexBlock);
        Assert.Contains("_settingsService.Settings.AutoIndexImages = previous;", autoIndexBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.auto-index-images-rollback\", rollbackEx);", autoIndexBlock);
        Assert.Contains("_suppressAutoIndexImagesChange = true;", autoIndexBlock);
        Assert.Contains("AutoIndexImagesCheck.IsChecked = previous;", autoIndexBlock);
        Assert.Contains("_suppressAutoIndexImagesChange = false;", autoIndexBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Automatic image indexing failed. Previous setting restored.\");", autoIndexBlock);
        Assert.Contains("The previous image indexing setting was restored. Try again from Settings.", autoIndexBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.auto-index-images-history-refresh\", ex);", autoIndexBlock);
        Assert.Contains("SetImageIndexMaintenanceStatus(\"Image indexing saved, but History did not refresh.\");", autoIndexBlock);
        Assert.Contains("The image indexing setting was saved, but History did not refresh. Switch tabs or use Retry in History.", autoIndexBlock);
    }

    [Fact]
    public void SettingsResetAndUninstallActionsLeaveRecoverableStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));

        AssertSettingsActionButton(xaml, "ResetSettingsBtn", "Reset settings", "Reset all settings to defaults", "ResetSettingsButton_Click");
        AssertSettingsActionButton(xaml, "UninstallOddSnapBtn", "Uninstall OddSnap", "Start the OddSnap uninstall flow", "UninstallButton_Click");

        var resetBlock = GetMethodBlock(preferencesCode, "private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("SetSettingsImportExportStatus(\"Reset canceled. Existing settings kept.\");", resetBlock);
        Assert.True(
            CountOccurrences(resetBlock, "SetSettingsImportExportStatus(\"Reset canceled. Existing settings kept.\");") >= 3,
            "Each reset confirmation cancel path should leave durable inline status.");
        Assert.Contains("var previous = _settingsService.Settings;", resetBlock);
        Assert.Contains("_settingsService.Settings = new AppSettings();", resetBlock);
        Assert.Contains("_settingsService.Save();", resetBlock);
        Assert.Contains("SetSettingsImportExportStatus(\"Settings reset to defaults.\");", resetBlock);
        Assert.Contains("ToastWindow.Show(\"Settings reset\", \"Defaults have been applied.\");", resetBlock);
        Assert.Contains("catch (Exception ex)", resetBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.reset\", ex);", resetBlock);
        Assert.Contains("_settingsService.Settings = previous;", resetBlock);
        Assert.Contains("RestoreSettingsUiAfterFailedReset();", resetBlock);
        Assert.Contains("ShowSettingsResetFailed(ex);", resetBlock);

        var resetFailureBlock = GetMethodBlock(preferencesCode, "private void ShowSettingsResetFailed(Exception ex)");
        Assert.Contains("SetSettingsImportExportStatus(\"Reset failed. Previous settings restored.\");", resetFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", resetFailureBlock);
        Assert.Contains("\"Reset failed\"", resetFailureBlock);
        Assert.Contains("Defaults were not saved. Previous settings were restored. Try again after checking file permissions.", resetFailureBlock);

        var uninstallBlock = GetMethodBlock(preferencesCode, "private void UninstallButton_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var uninstall = UninstallRequested;", uninstallBlock);
        Assert.Contains("if (uninstall is null)", uninstallBlock);
        Assert.Contains("SetSettingsImportExportStatus(\"Uninstall is not available from this window.\");", uninstallBlock);
        Assert.Contains("ToastWindow.ShowError(\"Uninstall unavailable\", \"Restart OddSnap and try again.\");", uninstallBlock);
        Assert.Contains("SetSettingsImportExportStatus(\"Starting uninstall...\");", uninstallBlock);
        Assert.Contains("uninstall.Invoke();", uninstallBlock);
        Assert.Contains("catch (Exception ex)", uninstallBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.uninstall\", ex);", uninstallBlock);
        Assert.Contains("ShowSettingsUninstallFailed(ex);", uninstallBlock);

        var uninstallCanceledBlock = GetMethodBlock(preferencesCode, "public void ShowUninstallCanceledStatus()");
        Assert.Contains("SetSettingsImportExportStatus(\"Uninstall canceled. OddSnap was left installed.\");", uninstallCanceledBlock);

        var uninstallFailureBlock = GetMethodBlock(preferencesCode, "private void ShowSettingsUninstallFailed(Exception ex)");
        Assert.Contains("SetSettingsImportExportStatus(\"Uninstall failed. Restart OddSnap and try again.\");", uninstallFailureBlock);
        Assert.Contains("OddSnap could not start uninstall. Restart OddSnap and try again from Settings.", uninstallFailureBlock);
        Assert.Contains("{ex.Message}", uninstallFailureBlock);

        var restoreBlock = GetMethodBlock(preferencesCode, "private void RestoreSettingsUiAfterFailedReset()");
        Assert.Contains("LoadSettings();", restoreBlock);
        Assert.Contains("PopulateToolToggles();", restoreBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.reset.restore\", restoreEx);", restoreBlock);
    }

    [Fact]
    public void StartWithWindowsSettingRevertsAndReportsRegistryFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));

        Assert.Contains("private bool _suppressStartWithWindowsChange;", settingsCode);
        Assert.Contains("private bool _suppressUpdatePreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"StartupPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "StartupPreferenceStatusText", "SettingsStatusText");
        var startupStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"StartupPreferenceStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", startupStatusTag);
        Assert.Contains("AutomationProperties.Name=\"Startup preference status\"", startupStatusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", startupStatusTag);
        Assert.Contains("x:Name=\"AboutPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "AboutPreferenceStatusText", "SettingsStatusText");
        var updateStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"AboutPreferenceStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", updateStatusTag);
        Assert.Contains("AutomationProperties.Name=\"Update preference status\"", updateStatusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", updateStatusTag);

        var startWithWindowsBlock = GetMethodBlock(settingsCode, "private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressStartWithWindowsChange) return;", startWithWindowsBlock);
        Assert.Contains("bool previous = _settingsService.Settings.StartWithWindows;", startWithWindowsBlock);
        Assert.Contains("UninstallService.SetStartupEntry(on);", startWithWindowsBlock);
        Assert.Contains("_settingsService.Settings.StartWithWindows = on;", startWithWindowsBlock);
        Assert.Contains("_settingsService.Save();", startWithWindowsBlock);
        Assert.Contains("SetStartupPreferenceStatus(string.Empty);", startWithWindowsBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.start-with-windows\", ex);", startWithWindowsBlock);
        Assert.Contains("UninstallService.SetStartupEntry(previous);", startWithWindowsBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.start-with-windows-rollback\", rollbackEx);", startWithWindowsBlock);
        Assert.Contains("_settingsService.Settings.StartWithWindows = previous;", startWithWindowsBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.start-with-windows-save-rollback\", rollbackEx);", startWithWindowsBlock);
        Assert.Contains("StartWithWindowsCheck.IsChecked = previous;", startWithWindowsBlock);
        Assert.Contains("ShowStartupPreferenceFailed(ex);", startWithWindowsBlock);

        var startupFailureBlock = GetMethodBlock(settingsCode, "private void ShowStartupPreferenceFailed(Exception ex)");
        Assert.Contains("SetStartupPreferenceStatus(\"Startup setting change was not saved. Previous setting restored.\");", startupFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", startupFailureBlock);
        Assert.Contains("\"Startup setting failed\"", startupFailureBlock);
        Assert.Contains("The previous startup setting was restored. Check Settings -> About and try again.", startupFailureBlock);

        var registryCallIndex = startWithWindowsBlock.IndexOf("UninstallService.SetStartupEntry(on);", StringComparison.Ordinal);
        var saveIndex = startWithWindowsBlock.IndexOf("_settingsService.Save();", StringComparison.Ordinal);
        Assert.True(registryCallIndex >= 0 && saveIndex > registryCallIndex, "Startup registry change should happen before saving the setting.");

        var autoUpdateBlock = GetMethodBlock(settingsCode, "private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUpdatePreferenceChange) return;", autoUpdateBlock);
        Assert.Contains("var previous = _settingsService.Settings.AutoCheckForUpdates;", autoUpdateBlock);
        Assert.Contains("\"settings.auto-update\"", autoUpdateBlock);
        Assert.Contains("value => _settingsService.Settings.AutoCheckForUpdates = value", autoUpdateBlock);
        Assert.Contains("value => AutoUpdateCheck.IsChecked = value", autoUpdateBlock);

        var updateHelperBlock = GetMethodBlock(settingsCode, "private void UpdateUpdatePreference<T>(");
        Assert.Contains("setValue(current);", updateHelperBlock);
        Assert.Contains("_settingsService.Save();", updateHelperBlock);
        Assert.Contains("SetUpdatePreferenceStatus(string.Empty);", updateHelperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", updateHelperBlock);
        Assert.Contains("setValue(previous);", updateHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", updateHelperBlock);
        Assert.Contains("_suppressUpdatePreferenceChange = true;", updateHelperBlock);
        Assert.Contains("restoreUi(previous);", updateHelperBlock);
        Assert.Contains("ShowUpdatePreferenceFailed(label, ex);", updateHelperBlock);

        var updateFailureBlock = GetMethodBlock(settingsCode, "private void ShowUpdatePreferenceFailed(string label, Exception ex)");
        Assert.Contains("SetUpdatePreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", updateFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", updateFailureBlock);
        Assert.Contains("$\"{label} failed\"", updateFailureBlock);
        Assert.Contains("The previous update setting was restored. Check Settings -> About and try again.", updateFailureBlock);

        var updateStatusBlock = GetMethodBlock(settingsCode, "private void SetUpdatePreferenceStatus(string message)");
        Assert.Contains("AboutPreferenceStatusText.Text = message;", updateStatusBlock);
        Assert.Contains("Visibility.Collapsed", updateStatusBlock);
        Assert.Contains("Visibility.Visible", updateStatusBlock);

        var startupStatusBlock = GetMethodBlock(settingsCode, "private void SetStartupPreferenceStatus(string message)");
        Assert.Contains("StartupPreferenceStatusText.Text = message;", startupStatusBlock);
        Assert.Contains("Visibility.Collapsed", startupStatusBlock);
        Assert.Contains("Visibility.Visible", startupStatusBlock);
    }

    [Fact]
    public void OcrAndTranslationPreferencesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var ocrCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Ocr.cs"));

        Assert.Contains("private bool _suppressOcrPreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"OcrPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "OcrPreferenceStatusText", "SettingsStatusText");
        Assert.Contains("x:Name=\"TranslationPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "TranslationPreferenceStatusText", "SettingsStatusText");
        AssertDynamicStatusTextBlock(xaml, "OcrLanguageStatusText", "OCR language availability", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "OcrPreferenceStatusText", "OCR preference status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "TranslationPreferenceStatusText", "Translation preference status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "OpenSourceLocalStatusText", "Open-source local translation status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "ArgosStatusText", "Argos translation status", isLive: true);
        Assert.Contains("CreateOcrLanguageItem(", ocrCode);
        Assert.Contains("\"Auto OCR language\"", ocrCode);
        Assert.Contains("Use the Windows system language for text recognition when available.", ocrCode);
        Assert.Contains("{tag} OCR language", ocrCode);
        Assert.Contains("Use {tag} for text recognition.", ocrCode);
        Assert.Contains("CreateTranslationLanguageItem(", ocrCode);
        Assert.Contains("{name} source language", ocrCode);
        Assert.Contains("Use {name} as the default translation source.", ocrCode);
        Assert.Contains("{toName} target language", ocrCode);
        Assert.Contains("Use {toName} as the default translation target.", ocrCode);
        AssertComboBoxItemInNamedComboHasLabel(xaml, "TranslateModelCombo", "Open-source Local", "Use the fully local open-source translator.", "Open-source local translator", "Translate OCR text locally without a cloud provider.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "TranslateModelCombo", "Argos Translate", "Use the Argos local fallback translator.", "Argos Translate", "Translate OCR text with the local Argos runtime.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "TranslateModelCombo", "Google Translate", "Use Google Translate with an API key.", "Google Translate", "Translate OCR text through Google using the configured API key.");

        var dynamicOcrItemBlock = GetMethodBlock(ocrCode, "private static ComboBoxItem CreateOcrLanguageItem(string text, string tag, string automationName, string helpText)");
        Assert.Contains("ToolTip = helpText", dynamicOcrItemBlock);
        Assert.Contains("AutomationProperties.SetName(item, automationName);", dynamicOcrItemBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", dynamicOcrItemBlock);

        var dynamicTranslationItemBlock = GetMethodBlock(ocrCode, "private static ComboBoxItem CreateTranslationLanguageItem(string text, string tag, string automationName, string helpText)");
        Assert.Contains("ToolTip = helpText", dynamicTranslationItemBlock);
        Assert.Contains("AutomationProperties.SetName(item, automationName);", dynamicTranslationItemBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", dynamicTranslationItemBlock);

        var loadBlock = GetMethodBlock(ocrCode, "private void LoadOcrTab()");
        Assert.Contains("_suppressOcrPreferenceChange = true;", loadBlock);
        Assert.Contains("LoadOcrLanguageOptions();", loadBlock);
        Assert.Contains("LoadTranslateLanguageCombos();", loadBlock);
        Assert.Contains("GoogleApiKeyBox.Password = _settingsService.Settings.GoogleTranslateApiKey ?? \"\";", loadBlock);
        Assert.Contains("_suppressOcrPreferenceChange = false;", loadBlock);

        var ocrLanguageBlock = GetMethodBlock(ocrCode, "private void OcrLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressOcrPreferenceChange) return;", ocrLanguageBlock);
        Assert.Contains("var previous = _settingsService.Settings.OcrLanguageTag;", ocrLanguageBlock);
        Assert.Contains("\"settings.ocr-language\"", ocrLanguageBlock);
        Assert.Contains("value => _settingsService.Settings.OcrLanguageTag = value", ocrLanguageBlock);
        Assert.Contains("value => SelectComboByTag(OcrLanguageCombo, value)", ocrLanguageBlock);
        Assert.Contains("SetOcrPreferenceStatus", ocrLanguageBlock);

        var translateFromBlock = GetMethodBlock(ocrCode, "private void TranslateFromCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressOcrPreferenceChange) return;", translateFromBlock);
        Assert.Contains("var previous = _settingsService.Settings.OcrDefaultTranslateFrom;", translateFromBlock);
        Assert.Contains("var selected = TranslationService.ResolveSourceLanguage(item.Tag as string);", translateFromBlock);
        Assert.Contains("\"settings.translation-source-language\"", translateFromBlock);
        Assert.Contains("value => _settingsService.Settings.OcrDefaultTranslateFrom = value", translateFromBlock);
        Assert.Contains("value => SelectComboByTag(TranslateFromCombo, value)", translateFromBlock);
        Assert.Contains("SetTranslationPreferenceStatus", translateFromBlock);
        Assert.Contains("_ => UpdateTranslationModelUi()", translateFromBlock);

        var translateToBlock = GetMethodBlock(ocrCode, "private void TranslateToCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressOcrPreferenceChange) return;", translateToBlock);
        Assert.Contains("var previous = _settingsService.Settings.OcrDefaultTranslateTo;", translateToBlock);
        Assert.Contains("\"settings.translation-target-language\"", translateToBlock);
        Assert.Contains("value => _settingsService.Settings.OcrDefaultTranslateTo = value", translateToBlock);
        Assert.Contains("value => SelectComboByTag(TranslateToCombo, value)", translateToBlock);

        var modelBlock = GetMethodBlock(ocrCode, "private void TranslateModelCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressOcrPreferenceChange) return;", modelBlock);
        Assert.Contains("var previous = _settingsService.Settings.TranslationModel;", modelBlock);
        Assert.Contains("\"settings.translation-engine\"", modelBlock);
        Assert.Contains("value => _settingsService.Settings.TranslationModel = value", modelBlock);
        Assert.Contains("value => SelectTranslationModelCombo(TranslateModelCombo, value)", modelBlock);
        Assert.Contains("_ => UpdateTranslationModelUi()", modelBlock);

        var openSourceInstallBlock = GetMethodBlock(ocrCode, "private void OpenSourceLocalInstallBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("\"settings.open-source-local-translation-engine\"", openSourceInstallBlock);
        Assert.Contains("(int)TranslationModel.OpenSourceLocal", openSourceInstallBlock);
        Assert.Contains("UpdateOcrPreference(", openSourceInstallBlock);

        var argosInstallBlock = GetMethodBlock(ocrCode, "private void ArgosInstallBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("\"settings.argos-translation-engine\"", argosInstallBlock);
        Assert.Contains("(int)TranslationModel.Argos", argosInstallBlock);
        Assert.Contains("UpdateOcrPreference(", argosInstallBlock);

        var googleKeyBlock = GetMethodBlock(ocrCode, "private void GoogleApiKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressOcrPreferenceChange) return;", googleKeyBlock);
        Assert.Contains("var previous = _settingsService.Settings.GoogleTranslateApiKey;", googleKeyBlock);
        Assert.Contains("\"settings.google-translate-api-key\"", googleKeyBlock);
        Assert.Contains("value => _settingsService.Settings.GoogleTranslateApiKey = value", googleKeyBlock);
        Assert.Contains("value => GoogleApiKeyBox.Password = value ?? \"\"", googleKeyBlock);
        Assert.Contains("TranslationService.SetGoogleApiKey(value);", googleKeyBlock);
        Assert.Contains("UpdateTranslationModelUi();", googleKeyBlock);

        var helperBlock = GetMethodBlock(ocrCode, "private void UpdateOcrPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("setStatus(string.Empty);", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(current);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressOcrPreferenceChange = true;", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("_suppressOcrPreferenceChange = false;", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(previous);", helperBlock);
        Assert.Contains("setStatus($\"{label} change was not saved. Previous setting restored.\");", helperBlock);
        Assert.Contains("ToastWindow.ShowError(", helperBlock);
        Assert.Contains("The previous OCR setting was restored. Check Settings -> OCR and try again.", helperBlock);

        var ocrStatusBlock = GetMethodBlock(ocrCode, "private void SetOcrPreferenceStatus(string message)");
        Assert.Contains("OcrPreferenceStatusText.Text = message;", ocrStatusBlock);
        Assert.Contains("Visibility.Collapsed", ocrStatusBlock);
        Assert.Contains("Visibility.Visible", ocrStatusBlock);

        var translationStatusBlock = GetMethodBlock(ocrCode, "private void SetTranslationPreferenceStatus(string message)");
        Assert.Contains("TranslationPreferenceStatusText.Text = message;", translationStatusBlock);
        Assert.Contains("Visibility.Collapsed", translationStatusBlock);
        Assert.Contains("Visibility.Visible", translationStatusBlock);
    }

    [Fact]
    public void CaptureAndSavePreferencesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));
        var recordingCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Recording.cs"));

        Assert.Contains("private bool _suppressCaptureSavePreferenceChange;", settingsCode);
        Assert.Contains("private bool _suppressHistoryPreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"CaptureSavePreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "CaptureSavePreferenceStatusText", "SettingsStatusText");
        Assert.Contains("x:Name=\"HistoryPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "HistoryPreferenceStatusText", "SettingsStatusText");
        AssertNamedControlHasLabel(xaml, "ShowCursorCheck", "<CheckBox", "Show cursor in captures and recordings", "Include the pointer in saved media");
        AssertNamedControlHasLabel(xaml, "CrosshairGuidesCheck", "<CheckBox", "Show crosshair guides", "Show alignment guides while selecting");
        AssertNamedControlHasLabel(xaml, "ShowCaptureMagnifierCheck", "<CheckBox", "Show pixel magnifier while selecting", "Zoom the cursor area during selection");
        AssertNamedControlHasLabel(xaml, "OverlayAllMonitorsCheck", "<CheckBox", "Span selection overlay across all monitors", "Use one overlay across the full virtual desktop");
        AssertNamedControlHasLabel(xaml, "AnnotationStrokeShadowCheck", "<CheckBox", "Annotation stroke and shadow", "Keep annotations readable on mixed backgrounds");
        AssertNamedControlHasLabel(xaml, "SaveToFileCheck", "<CheckBox", "Save screenshots to file", "Write screenshots to the configured save folder");
        AssertNamedControlHasLabel(xaml, "AskFileNameCheck", "<CheckBox", "Ask for file name every time", "Prompt for a file name before each saved capture");
        AssertNamedControlHasLabel(xaml, "MonthlyFoldersCheck", "<CheckBox", "Create monthly subfolders", "Organize captures into year-month folders");
        AssertNamedControlHasLabel(xaml, "SaveHistoryCheck", "<CheckBox", "Save capture history", "Keep captures available in the History page");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "DefaultCaptureModeCombo", "Rectangle", "Drag from corner to corner.", "Rectangle selection", "Start captures with the standard rectangular selection tool.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "DefaultCaptureModeCombo", "Center", "Drag outward from a center point.", "Center selection", "Start captures with a center-based selection tool.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "DefaultCaptureModeCombo", "Freeform", "Draw an irregular capture outline.", "Freeform selection", "Start captures with the freeform selection tool.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "Free", "Do not lock the center selection ratio.", "Free center ratio", "Let center selection resize freely without an aspect-ratio lock.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "Square", "Lock center selection to a square.", "Square center ratio", "Keep center selection width and height equal.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "16:9", "Lock center selection to widescreen landscape.", "16:9 center ratio", "Keep center selection in a 16 to 9 landscape ratio.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "4:3", "Lock center selection to standard landscape.", "4:3 center ratio", "Keep center selection in a 4 to 3 landscape ratio.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "3:2", "Lock center selection to photo landscape.", "3:2 center ratio", "Keep center selection in a 3 to 2 landscape ratio.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CenterAspectRatioCombo", "9:16", "Lock center selection to vertical portrait.", "9:16 center ratio", "Keep center selection in a 9 to 16 portrait ratio.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "WindowDetectionCombo", "Off", "Ignore windows while selecting.", "Window detection off", "Do not highlight or snap to windows during selection.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "WindowDetectionCombo", "Windows only", "Detect windows while hovering.", "Windows-only detection", "Highlight detected windows during selection so captures can snap to them.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDockSideCombo", "Top", "Show the capture toolbar along the top edge.", "Top capture dock", "Place the capture toolbar at the top of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDockSideCombo", "Bottom", "Show the capture toolbar along the bottom edge.", "Bottom capture dock", "Place the capture toolbar at the bottom of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDockSideCombo", "Left", "Show the capture toolbar along the left edge.", "Left capture dock", "Place the capture toolbar on the left side of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDockSideCombo", "Right", "Show the capture toolbar along the right edge.", "Right capture dock", "Place the capture toolbar on the right side of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ScrollingCaptureModeCombo", "Automatic", "Let OddSnap collect scrolling frames automatically.", "Automatic scrolling capture", "Automatically collect frames while scrolling capture is active.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ScrollingCaptureModeCombo", "Manual", "Capture scrolling frames only when you click.", "Manual scrolling capture", "Only collect a scrolling-capture frame when you press the capture button.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "AfterCaptureCombo", "Copy to clipboard", "Copy the capture without opening a preview.", "Copy after capture", "Copy the saved capture to the clipboard and skip the preview window.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "AfterCaptureCombo", "Preview + Copy", "Open a preview and copy the capture.", "Preview and copy after capture", "Open the preview window and also copy the saved capture to the clipboard.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "AfterCaptureCombo", "Preview only", "Open a preview without copying.", "Preview only after capture", "Open the preview window without copying the saved capture to the clipboard.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDelayCombo", "None", "Open capture immediately.", "No capture delay", "Start the capture overlay immediately after choosing capture.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDelayCombo", "3 seconds", "Wait 3 seconds before capture.", "3 second capture delay", "Wait 3 seconds before opening the capture overlay.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDelayCombo", "5 seconds", "Wait 5 seconds before capture.", "5 second capture delay", "Wait 5 seconds before opening the capture overlay.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureDelayCombo", "10 seconds", "Wait 10 seconds before capture.", "10 second capture delay", "Wait 10 seconds before opening the capture overlay.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureFormatCombo", "PNG", "Save lossless images with transparency.", "PNG image format", "Save captures as lossless PNG files, including transparency when available.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureFormatCombo", "JPG", "Save smaller photos without transparency.", "JPG image format", "Save captures as compressed JPG files for smaller image sizes.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureFormatCombo", "BMP", "Save uncompressed bitmap images.", "BMP image format", "Save captures as uncompressed BMP files for maximum compatibility.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "JpegQualityCombo", "100 - Best", "Use maximum JPG quality.", "100 JPG quality", "Save JPG captures at maximum quality with the largest file size.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "JpegQualityCombo", "90 - High", "Use high JPG quality.", "90 JPG quality", "Save JPG captures at high quality with moderate compression.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "JpegQualityCombo", "85 - Balanced", "Balance JPG quality and file size.", "85 JPG quality", "Save JPG captures with balanced quality and file size.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "JpegQualityCombo", "75 - Smaller", "Use smaller JPG files.", "75 JPG quality", "Save JPG captures with stronger compression for smaller files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "JpegQualityCombo", "60 - Tiny", "Use the smallest JPG files.", "60 JPG quality", "Save JPG captures with heavy compression for tiny files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "Original", "Keep the original capture size.", "Original capture size", "Save captures without resizing the longest edge.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "2160p", "Limit captures to 2160p.", "2160p max image size", "Resize oversized captures so the longest edge is at most 2160 pixels.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "1440p", "Limit captures to 1440p.", "1440p max image size", "Resize oversized captures so the longest edge is at most 1440 pixels.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "1080p", "Limit captures to 1080p.", "1080p max image size", "Resize oversized captures so the longest edge is at most 1080 pixels.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "720p", "Limit captures to 720p.", "720p max image size", "Resize oversized captures so the longest edge is at most 720 pixels.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "CaptureSizeCombo", "480p", "Limit captures to 480p.", "480p max image size", "Resize oversized captures so the longest edge is at most 480 pixels.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryRetentionCombo", "Never", "Keep history until manually cleared", "Never auto-clear history", "Keep saved capture history until you clear it manually.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryRetentionCombo", "1 day", "Keep history for 1 day", "Keep history for 1 day", "Automatically remove capture history older than 1 day.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryRetentionCombo", "7 days", "Keep history for 7 days", "Keep history for 7 days", "Automatically remove capture history older than 7 days.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryRetentionCombo", "30 days", "Keep history for 30 days", "Keep history for 30 days", "Automatically remove capture history older than 30 days.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryRetentionCombo", "3 months", "Keep history for 3 months", "Keep history for 3 months", "Automatically remove capture history older than 3 months.");

        var defaultCaptureBlock = GetMethodBlock(settingsCode, "private void DefaultCaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", defaultCaptureBlock);
        Assert.Contains("var previous = _settingsService.Settings.DefaultCaptureMode;", defaultCaptureBlock);
        Assert.Contains("\"settings.default-capture-mode\"", defaultCaptureBlock);
        Assert.Contains("value => _settingsService.Settings.DefaultCaptureMode = value", defaultCaptureBlock);
        Assert.Contains("DefaultCaptureModeCombo.SelectedIndex = value switch", defaultCaptureBlock);
        Assert.Contains("notifyHotkeyChanged: true", defaultCaptureBlock);

        var afterCaptureBlock = GetMethodBlock(settingsCode, "private void AfterCaptureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.AfterCapture;", afterCaptureBlock);
        Assert.Contains("\"settings.after-capture\"", afterCaptureBlock);
        Assert.Contains("AfterCaptureCombo.SelectedIndex = value switch", afterCaptureBlock);

        var aspectBlock = GetMethodBlock(settingsCode, "private void CenterAspectRatioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.CenterSelectionAspectRatio;", aspectBlock);
        Assert.Contains("var selectedIndex = Math.Clamp(CenterAspectRatioCombo.SelectedIndex, 0, 5);", aspectBlock);
        Assert.Contains("\"settings.center-aspect-ratio\"", aspectBlock);
        Assert.Contains("() => CenterAspectRatioCombo.SelectedIndex = selectedIndex", aspectBlock);

        var saveToFileBlock = GetMethodBlock(settingsCode, "private void SaveToFileCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.SaveToFile;", saveToFileBlock);
        Assert.Contains("\"settings.save-to-file\"", saveToFileBlock);
        Assert.Contains("SaveToFileCheck.IsChecked = value;", saveToFileBlock);
        Assert.Contains("SaveDirPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;", saveToFileBlock);
        Assert.Contains("() => SaveDirPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed", saveToFileBlock);

        var askNameBlock = GetMethodBlock(settingsCode, "private void AskFileNameCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.AskForFileNameOnSave;", askNameBlock);
        Assert.Contains("\"settings.ask-file-name\"", askNameBlock);
        Assert.Contains("value => AskFileNameCheck.IsChecked = value", askNameBlock);

        var templateBlock = GetMethodBlock(settingsCode, "private void FileNameTemplateBox_TextChanged(object sender, TextChangedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.FileNameTemplate;", templateBlock);
        Assert.Contains("\"settings.file-name-template\"", templateBlock);
        Assert.Contains("FileNameTemplateBox.Text = value;", templateBlock);
        Assert.Contains("UpdateFileNameTemplatePreview(value);", templateBlock);
        Assert.Contains("() => UpdateFileNameTemplatePreview(template)", templateBlock);

        var monthlyFoldersBlock = GetMethodBlock(settingsCode, "private void MonthlyFoldersCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", monthlyFoldersBlock);
        Assert.Contains("var previous = _settingsService.Settings.SaveInMonthlyFolders;", monthlyFoldersBlock);
        Assert.Contains("\"settings.monthly-folders\"", monthlyFoldersBlock);
        Assert.Contains("value => MonthlyFoldersCheck.IsChecked = value", monthlyFoldersBlock);

        var browseBlock = GetMethodBlock(settingsCode, "private void BrowseButton_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.SaveDirectory;", browseBlock);
        Assert.Contains("var selectedPath = dlg.SelectedPath;", browseBlock);
        Assert.Contains("\"settings.save-directory\"", browseBlock);
        Assert.Contains("value => _settingsService.Settings.SaveDirectory = value", browseBlock);
        Assert.Contains("SetSaveDirectoryPath,", browseBlock);
        Assert.Contains("() => SetSaveDirectoryPath(selectedPath)", browseBlock);

        var captureFormatBlock = GetMethodBlock(settingsCode, "private void CaptureFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", captureFormatBlock);
        Assert.Contains("var previous = _settingsService.Settings.CaptureImageFormat;", captureFormatBlock);
        Assert.Contains("\"settings.capture-format\"", captureFormatBlock);
        Assert.Contains("_historyService.CaptureImageFormat = value;", captureFormatBlock);
        Assert.Contains("UpdateCaptureFormatControls();", captureFormatBlock);
        Assert.Contains("_historyService.CaptureImageFormat = selected;", captureFormatBlock);

        var jpegQualityBlock = GetMethodBlock(settingsCode, "private void JpegQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", jpegQualityBlock);
        Assert.Contains("var previous = _settingsService.Settings.JpegQuality;", jpegQualityBlock);
        Assert.Contains("var selectedIndex = JpegQualityCombo.SelectedIndex;", jpegQualityBlock);
        Assert.Contains("\"settings.jpeg-quality\"", jpegQualityBlock);
        Assert.Contains("_historyService.JpegQuality = value;", jpegQualityBlock);
        Assert.Contains("_historyService.JpegQuality = quality;", jpegQualityBlock);

        var captureSizeBlock = GetMethodBlock(settingsCode, "private void CaptureSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", captureSizeBlock);
        Assert.Contains("var previous = _settingsService.Settings.CaptureMaxLongEdge;", captureSizeBlock);
        Assert.Contains("\"settings.capture-size\"", captureSizeBlock);
        Assert.Contains("value => CaptureSizeCombo.SelectedIndex = value switch", captureSizeBlock);
        Assert.Contains("() => CaptureSizeCombo.SelectedIndex = selectedIndex", captureSizeBlock);

        var dockBlock = GetMethodBlock(preferencesCode, "private void CaptureDockSideCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", dockBlock);
        Assert.Contains("var previous = _settingsService.Settings.CaptureDockSide;", dockBlock);
        Assert.Contains("\"settings.capture-dock-side\"", dockBlock);
        Assert.Contains("value => _settingsService.Settings.CaptureDockSide = value", dockBlock);
        Assert.Contains("value => CaptureDockSideCombo.SelectedIndex = (int)value", dockBlock);

        var scrollingBlock = GetMethodBlock(preferencesCode, "private void ScrollingCaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", scrollingBlock);
        Assert.Contains("var previous = _settingsService.Settings.ScrollingCaptureMode;", scrollingBlock);
        Assert.Contains("\"settings.scrolling-capture-mode\"", scrollingBlock);
        Assert.Contains("value => _settingsService.Settings.ScrollingCaptureMode = value", scrollingBlock);
        Assert.Contains("value => ScrollingCaptureModeCombo.SelectedIndex = value == ScrollingCaptureMode.Manual ? 1 : 0", scrollingBlock);

        var windowDetectionBlock = GetMethodBlock(preferencesCode, "private void WindowDetectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", windowDetectionBlock);
        Assert.Contains("var previous = (Mode: _settingsService.Settings.WindowDetection, DetectWindows: _settingsService.Settings.DetectWindows);", windowDetectionBlock);
        Assert.Contains("\"settings.window-detection\"", windowDetectionBlock);
        Assert.Contains("_settingsService.Settings.WindowDetection = value.Mode;", windowDetectionBlock);
        Assert.Contains("_settingsService.Settings.DetectWindows = value.DetectWindows;", windowDetectionBlock);
        Assert.Contains("value => WindowDetectionCombo.SelectedIndex = (int)value.Mode", windowDetectionBlock);

        var delayBlock = GetMethodBlock(preferencesCode, "private void CaptureDelayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", delayBlock);
        Assert.Contains("var previous = _settingsService.Settings.CaptureDelaySeconds;", delayBlock);
        Assert.Contains("\"settings.capture-delay\"", delayBlock);
        Assert.Contains("value => _settingsService.Settings.CaptureDelaySeconds = value", delayBlock);
        Assert.Contains("value => CaptureDelayCombo.SelectedIndex = value switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 }", delayBlock);

        var crosshairBlock = GetMethodBlock(preferencesCode, "private void CrosshairGuidesCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", crosshairBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowCrosshairGuides;", crosshairBlock);
        Assert.Contains("\"settings.crosshair-guides\"", crosshairBlock);
        Assert.Contains("value => _settingsService.Settings.ShowCrosshairGuides = value", crosshairBlock);
        Assert.Contains("value => CrosshairGuidesCheck.IsChecked = value", crosshairBlock);

        var magnifierBlock = GetMethodBlock(preferencesCode, "private void ShowCaptureMagnifierCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", magnifierBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowCaptureMagnifier;", magnifierBlock);
        Assert.Contains("\"settings.capture-magnifier\"", magnifierBlock);
        Assert.Contains("value => _settingsService.Settings.ShowCaptureMagnifier = value", magnifierBlock);
        Assert.Contains("value => ShowCaptureMagnifierCheck.IsChecked = value", magnifierBlock);

        var overlayBlock = GetMethodBlock(preferencesCode, "private void OverlayAllMonitorsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", overlayBlock);
        Assert.Contains("var previous = _settingsService.Settings.OverlayCaptureAllMonitors;", overlayBlock);
        Assert.Contains("\"settings.overlay-all-monitors\"", overlayBlock);
        Assert.Contains("value => _settingsService.Settings.OverlayCaptureAllMonitors = value", overlayBlock);
        Assert.Contains("value => OverlayAllMonitorsCheck.IsChecked = value", overlayBlock);

        var showCursorBlock = GetMethodBlock(preferencesCode, "private void ShowCursorCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", showCursorBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowCursor;", showCursorBlock);
        Assert.Contains("\"settings.show-cursor\"", showCursorBlock);
        Assert.Contains("value => _settingsService.Settings.ShowCursor = value", showCursorBlock);
        Assert.Contains("ShowCursorCheck.IsChecked = value;", showCursorBlock);
        Assert.Contains("RecordShowCursorCheck.IsChecked = value;", showCursorBlock);
        Assert.Contains("if (RecordShowCursorCheck.IsChecked != selected)", showCursorBlock);

        var annotationContrastBlock = GetMethodBlock(preferencesCode, "private void AnnotationStrokeShadowCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", annotationContrastBlock);
        Assert.Contains("var previous = _settingsService.Settings.AnnotationStrokeShadow;", annotationContrastBlock);
        Assert.Contains("\"settings.annotation-stroke-shadow\"", annotationContrastBlock);
        Assert.Contains("value => _settingsService.Settings.AnnotationStrokeShadow = value", annotationContrastBlock);
        Assert.Contains("value => AnnotationStrokeShadowCheck.IsChecked = value", annotationContrastBlock);

        var recordShowCursorBlock = GetMethodBlock(recordingCode, "private void RecordShowCursorCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressCaptureSavePreferenceChange) return;", recordShowCursorBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowCursor;", recordShowCursorBlock);
        Assert.Contains("\"settings.record-show-cursor\"", recordShowCursorBlock);
        Assert.Contains("value => _settingsService.Settings.ShowCursor = value", recordShowCursorBlock);
        Assert.Contains("RecordShowCursorCheck.IsChecked = value;", recordShowCursorBlock);
        Assert.Contains("ShowCursorCheck.IsChecked = value;", recordShowCursorBlock);
        Assert.Contains("if (ShowCursorCheck.IsChecked != selected)", recordShowCursorBlock);

        var saveHistoryBlock = GetMethodBlock(recordingCode, "private void SaveHistoryCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressHistoryPreferenceChange) return;", saveHistoryBlock);
        Assert.Contains("var previous = _settingsService.Settings.SaveHistory;", saveHistoryBlock);
        Assert.Contains("\"settings.save-history\"", saveHistoryBlock);
        Assert.Contains("value => _settingsService.Settings.SaveHistory = value", saveHistoryBlock);
        Assert.Contains("value => SaveHistoryCheck.IsChecked = value", saveHistoryBlock);

        var retentionBlock = GetMethodBlock(recordingCode, "private void HistoryRetentionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressHistoryPreferenceChange) return;", retentionBlock);
        Assert.Contains("var previous = _settingsService.Settings.HistoryRetention;", retentionBlock);
        Assert.Contains("var selected = (HistoryRetentionPeriod)Math.Clamp(HistoryRetentionCombo.SelectedIndex, 0, 4);", retentionBlock);
        Assert.Contains("\"settings.history-retention\"", retentionBlock);
        Assert.Contains("value => _settingsService.Settings.HistoryRetention = value", retentionBlock);
        Assert.Contains("HistoryRetentionCombo.SelectedIndex = (int)value;", retentionBlock);
        Assert.Contains("_historyService.RetentionPeriod = value;", retentionBlock);
        Assert.Contains("value => _historyService.PruneByRetention(value)", retentionBlock);

        var historyHelperBlock = GetMethodBlock(recordingCode, "private void UpdateHistoryPreference<T>(");
        Assert.Contains("setValue(current);", historyHelperBlock);
        Assert.Contains("_settingsService.Save();", historyHelperBlock);
        Assert.Contains("applySuccess?.Invoke(current);", historyHelperBlock);
        Assert.Contains("SetHistoryPreferenceStatus(string.Empty);", historyHelperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", historyHelperBlock);
        Assert.Contains("setValue(previous);", historyHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", historyHelperBlock);
        Assert.Contains("_suppressHistoryPreferenceChange = true;", historyHelperBlock);
        Assert.Contains("restoreUi(previous);", historyHelperBlock);
        Assert.Contains("_suppressHistoryPreferenceChange = false;", historyHelperBlock);
        Assert.Contains("SetHistoryPreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", historyHelperBlock);
        Assert.Contains("ToastWindow.ShowError(", historyHelperBlock);
        Assert.Contains("The previous history setting was restored. Check Settings -> Recording and try again.", historyHelperBlock);

        var historyStatusBlock = GetMethodBlock(recordingCode, "private void SetHistoryPreferenceStatus(string message)");
        Assert.Contains("HistoryPreferenceStatusText.Text = message;", historyStatusBlock);
        Assert.Contains("Visibility.Collapsed", historyStatusBlock);
        Assert.Contains("Visibility.Visible", historyStatusBlock);

        var helperBlock = GetMethodBlock(settingsCode, "private void UpdateCaptureSavePreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_suppressCaptureSavePreferenceChange = true;", helperBlock);
        Assert.Contains("applyCurrentUi();", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetCaptureSavePreferenceStatus(string.Empty);", helperBlock);
        Assert.Contains("HotkeyChanged?.Invoke();", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("ShowCaptureSavePreferenceFailed(label, ex);", helperBlock);

        var failureBlock = GetMethodBlock(settingsCode, "private void ShowCaptureSavePreferenceFailed(string label, Exception ex)");
        Assert.Contains("SetCaptureSavePreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", failureBlock);
        Assert.Contains("ToastWindow.ShowError(", failureBlock);
        Assert.Contains("$\"{label} failed\"", failureBlock);
        Assert.Contains("The previous capture setting was restored. Check Settings -> Capture and try again.", failureBlock);

        var statusBlock = GetMethodBlock(settingsCode, "private void SetCaptureSavePreferenceStatus(string message)");
        Assert.Contains("CaptureSavePreferenceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void GeneralPreferencesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));
        var appearanceCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Appearance.cs"));
        var recordingCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Recording.cs"));

        Assert.Contains("private bool _suppressGeneralPreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"GeneralPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "GeneralPreferenceStatusText", "SettingsStatusText");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "80%", "Scale OddSnap UI to 80%.", "80 percent UI scale", "Make OddSnap windows, toasts, and capture controls smaller.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "90%", "Scale OddSnap UI to 90%.", "90 percent UI scale", "Make OddSnap windows, toasts, and capture controls slightly smaller.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "100%", "Use normal OddSnap UI scale.", "100 percent UI scale", "Use the default OddSnap window, toast, and capture-control size.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "110%", "Scale OddSnap UI to 110%.", "110 percent UI scale", "Make OddSnap windows, toasts, and capture controls slightly larger.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "120%", "Scale OddSnap UI to 120%.", "120 percent UI scale", "Make OddSnap windows, toasts, and capture controls larger.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "130%", "Scale OddSnap UI to 130%.", "130 percent UI scale", "Make OddSnap windows, toasts, and capture controls much larger.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "UiScaleCombo", "140%", "Scale OddSnap UI to 140%.", "140 percent UI scale", "Make OddSnap windows, toasts, and capture controls extra large.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "SoundPackCombo", "Default", "Use the default notification sounds.", "Default sound pack", "Use OddSnap's standard notification sounds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "SoundPackCombo", "Soft", "Use quieter notification sounds.", "Soft sound pack", "Use softer notification sounds for captures and previews.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "SoundPackCombo", "Retro", "Use retro notification sounds.", "Retro sound pack", "Use retro-style notification sounds for captures and previews.");
        Assert.Contains("AutomationProperties.SetName(autoLanguageItem, \"Auto interface language\");", appearanceCode);
        Assert.Contains("AutomationProperties.SetHelpText(autoLanguageItem, \"Use the Windows language when OddSnap has app translations for it.\");", appearanceCode);
        Assert.Contains("ToolTip = available", appearanceCode);
        Assert.Contains("? $\"Use {label} for the OddSnap interface.\"", appearanceCode);
        Assert.Contains("AutomationProperties.SetName(item, $\"{label} interface language\");", appearanceCode);
        Assert.Contains("? $\"Use {label} for OddSnap menus, settings, and prompts.\"", appearanceCode);

        var uiScaleBlock = GetMethodBlock(preferencesCode, "private void UiScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", uiScaleBlock);
        Assert.Contains("var previous = _settingsService.Settings.UiScale;", uiScaleBlock);
        Assert.Contains("\"settings.ui-scale\"", uiScaleBlock);
        Assert.Contains("value => _settingsService.Settings.UiScale = value", uiScaleBlock);
        Assert.Contains("SelectUiScale", uiScaleBlock);
        Assert.Contains("UiScale.Set(value);", uiScaleBlock);
        Assert.Contains("ApplyThemeColors();", uiScaleBlock);

        var badgesBlock = GetMethodBlock(appearanceCode, "private void ShowToolNumberBadgesCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", badgesBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowToolNumberBadges;", badgesBlock);
        Assert.Contains("\"settings.tool-number-badges\"", badgesBlock);
        Assert.Contains("value => _settingsService.Settings.ShowToolNumberBadges = value", badgesBlock);
        Assert.Contains("value => ShowToolNumberBadgesCheck.IsChecked = value", badgesBlock);

        var languageBlock = GetMethodBlock(appearanceCode, "private void InterfaceLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", languageBlock);
        Assert.Contains("var previous = _settingsService.Settings.InterfaceLanguage;", languageBlock);
        Assert.Contains("var normalized = LocalizationService.NormalizeLanguageSetting(languageCode);", languageBlock);
        Assert.Contains("\"settings.interface-language\"", languageBlock);
        Assert.Contains("value => _settingsService.Settings.InterfaceLanguage = value", languageBlock);
        Assert.Contains("SelectInterfaceLanguage", languageBlock);
        Assert.Contains("ApplyLocalization();", languageBlock);
        Assert.Contains("LocalizationChanged?.Invoke();", languageBlock);

        var muteBlock = GetMethodBlock(recordingCode, "private void MuteSoundsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", muteBlock);
        Assert.Contains("var previous = _settingsService.Settings.MuteSounds;", muteBlock);
        Assert.Contains("\"settings.mute-sounds\"", muteBlock);
        Assert.Contains("value => _settingsService.Settings.MuteSounds = value", muteBlock);
        Assert.Contains("value => MuteSoundsCheck.IsChecked = value", muteBlock);
        Assert.Contains("value => SoundService.Muted = value", muteBlock);

        var animationsBlock = GetMethodBlock(recordingCode, "private void DisableAnimationsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", animationsBlock);
        Assert.Contains("var previous = _settingsService.Settings.DisableAnimations;", animationsBlock);
        Assert.Contains("\"settings.disable-animations\"", animationsBlock);
        Assert.Contains("value => _settingsService.Settings.DisableAnimations = value", animationsBlock);
        Assert.Contains("value => DisableAnimationsCheck.IsChecked = value", animationsBlock);
        Assert.Contains("value => Motion.Disabled = value", animationsBlock);

        var soundPackBlock = GetMethodBlock(recordingCode, "private void SoundPackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", soundPackBlock);
        Assert.Contains("var previous = _settingsService.Settings.SoundPack;", soundPackBlock);
        Assert.Contains("var selected = (SoundPack)Math.Clamp(SoundPackCombo.SelectedIndex, 0, 2);", soundPackBlock);
        Assert.Contains("\"settings.sound-pack\"", soundPackBlock);
        Assert.Contains("value => _settingsService.Settings.SoundPack = value", soundPackBlock);
        Assert.Contains("value => SoundPackCombo.SelectedIndex = (int)value", soundPackBlock);
        Assert.Contains("SoundService.SetPack(value);", soundPackBlock);
        Assert.Contains("SoundService.PlayCaptureSound();", soundPackBlock);

        var searchBarBlock = GetMethodBlock(preferencesCode, "private void ShowImageSearchBarCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", searchBarBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowImageSearchBar;", searchBarBlock);
        Assert.Contains("\"settings.show-image-search-bar\"", searchBarBlock);
        Assert.Contains("value => _settingsService.Settings.ShowImageSearchBar = value", searchBarBlock);
        Assert.Contains("value => ShowImageSearchBarCheck.IsChecked = value", searchBarBlock);
        Assert.Contains("if (!value)", searchBarBlock);
        Assert.Contains("ImageSearchBox.Clear();", searchBarBlock);
        Assert.Contains("_imageSearchQuery = \"\";", searchBarBlock);
        Assert.Contains("LoadCurrentHistoryTab();", searchBarBlock);

        var searchDiagnosticsBlock = GetMethodBlock(preferencesCode, "private void ShowImageSearchDiagnosticsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressGeneralPreferenceChange) return;", searchDiagnosticsBlock);
        Assert.Contains("var previous = _settingsService.Settings.ShowImageSearchDiagnostics;", searchDiagnosticsBlock);
        Assert.Contains("\"settings.show-image-search-diagnostics\"", searchDiagnosticsBlock);
        Assert.Contains("value => _settingsService.Settings.ShowImageSearchDiagnostics = value", searchDiagnosticsBlock);
        Assert.Contains("value => ShowImageSearchDiagnosticsCheck.IsChecked = value", searchDiagnosticsBlock);
        Assert.Contains("LoadCurrentHistoryTab();", searchDiagnosticsBlock);

        var helperBlock = GetMethodBlock(preferencesCode, "private void UpdateGeneralPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetGeneralPreferenceStatus(string.Empty);", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(current);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressGeneralPreferenceChange = true;", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("_suppressGeneralPreferenceChange = false;", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(previous);", helperBlock);
        Assert.Contains("SetGeneralPreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", helperBlock);
        Assert.Contains("ToastWindow.ShowError(", helperBlock);
        Assert.Contains("The previous general setting was restored. Check Settings -> General and try again.", helperBlock);

        var statusBlock = GetMethodBlock(preferencesCode, "private void SetGeneralPreferenceStatus(string message)");
        Assert.Contains("GeneralPreferenceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void RecordingPreferencesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var recordingCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Recording.cs"));

        Assert.Contains("private bool _suppressRecordingPreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"RecordingPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "RecordingPreferenceStatusText", "SettingsStatusText");
        AssertNamedControlHasLabel(xaml, "RecordShowCursorCheck", "<CheckBox", "Show cursor in recordings", "Include pointer movement in recorded output.");
        AssertNamedControlHasLabel(xaml, "RecordMicCheck", "<CheckBox", "Record microphone", "Capture audio from the selected input device.");
        AssertNamedControlHasLabel(xaml, "RecordDesktopAudioCheck", "<CheckBox", "Record desktop audio", "Capture system audio from the selected output device.");
        AssertNamedControlHasLabel(xaml, "RecordingFormatCombo", "<ComboBox", "Recording format", "Choose the video container for recordings");
        AssertNamedControlHasLabel(xaml, "RecordingQualityCombo", "<ComboBox", "Recording quality", "Set maximum recording resolution");
        AssertNamedControlHasLabel(xaml, "RecordingFpsCombo", "<ComboBox", "Recording FPS", "Choose how many frames are captured each second");
        AssertNamedControlHasLabel(xaml, "MicDeviceCombo", "<ComboBox", "Microphone input device", "Choose the microphone input device");
        AssertNamedControlHasLabel(xaml, "DesktopAudioDeviceCombo", "<ComboBox", "Desktop audio output device", "Choose the desktop audio output device");
        Assert.Contains("CreateAudioDeviceItem(", recordingCode);
        Assert.Contains("Microphone device {mic.Name}", recordingCode);
        Assert.Contains("Use {mic.Name} for microphone recording.", recordingCode);
        Assert.Contains("Desktop audio device {dev.Name}", recordingCode);
        Assert.Contains("Use {dev.Name} for desktop audio recording.", recordingCode);
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFormatCombo", "GIF", "Save recordings as GIF animations", "GIF recording format", "Save recordings as animated GIF files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFormatCombo", "MP4", "Save recordings as MP4 videos", "MP4 recording format", "Save recordings as MP4 video files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFormatCombo", "WebM", "Save recordings as WebM videos", "WebM recording format", "Save recordings as WebM video files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFormatCombo", "MKV", "Save recordings as MKV videos", "MKV recording format", "Save recordings as MKV video files.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingQualityCombo", "Original", "Keep the original recording resolution", "Original recording resolution", "Record at the selected area's original resolution.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingQualityCombo", "1080p", "Limit recordings to 1080p", "1080p recording resolution", "Scale recordings down to 1080p when needed.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingQualityCombo", "720p", "Limit recordings to 720p", "720p recording resolution", "Scale recordings down to 720p when needed.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingQualityCombo", "480p", "Limit recordings to 480p", "480p recording resolution", "Scale recordings down to 480p when needed.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFpsCombo", "15", "Capture 15 frames per second", "15 FPS", "Capture smoother-than-low-power recordings at 15 frames per second.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFpsCombo", "24", "Capture 24 frames per second", "24 FPS", "Capture video-like motion at 24 frames per second.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFpsCombo", "30", "Capture 30 frames per second", "30 FPS", "Capture smooth recordings at 30 frames per second.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "RecordingFpsCombo", "60", "Capture 60 frames per second", "60 FPS", "Capture very smooth recordings at 60 frames per second.");

        var formatBlock = GetMethodBlock(recordingCode, "private void RecordingFormatCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", formatBlock);
        Assert.Contains("var previous = _settingsService.Settings.RecordingFormat;", formatBlock);
        Assert.Contains("var selected = (RecordingFormat)Math.Clamp(RecordingFormatCombo.SelectedIndex, 0, 3);", formatBlock);
        Assert.Contains("\"settings.recording-format\"", formatBlock);
        Assert.Contains("value => _settingsService.Settings.RecordingFormat = value", formatBlock);
        Assert.Contains("RecordingFormatCombo.SelectedIndex = (int)value;", formatBlock);
        Assert.Contains("SelectRecordingFps(value == RecordingFormat.GIF", formatBlock);
        Assert.Contains("UpdateRecordingFormatVisibility();", formatBlock);

        var qualityBlock = GetMethodBlock(recordingCode, "private void RecordingQualityCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", qualityBlock);
        Assert.Contains("var previous = _settingsService.Settings.RecordingQuality;", qualityBlock);
        Assert.Contains("\"settings.recording-quality\"", qualityBlock);
        Assert.Contains("value => _settingsService.Settings.RecordingQuality = value", qualityBlock);
        Assert.Contains("value => RecordingQualityCombo.SelectedIndex = (int)value", qualityBlock);

        var fpsBlock = GetMethodBlock(recordingCode, "private void RecordingFpsCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", fpsBlock);
        Assert.Contains("var isGif = _settingsService.Settings.RecordingFormat == RecordingFormat.GIF;", fpsBlock);
        Assert.Contains("var previous = isGif ? _settingsService.Settings.GifFps : _settingsService.Settings.RecordingFps;", fpsBlock);
        Assert.Contains("\"settings.recording-fps\"", fpsBlock);
        Assert.Contains("_settingsService.Settings.GifFps = value;", fpsBlock);
        Assert.Contains("_settingsService.Settings.RecordingFps = value;", fpsBlock);
        Assert.Contains("SelectRecordingFps", fpsBlock);

        var audioDeviceItemBlock = GetMethodBlock(recordingCode, "private static ComboBoxItem CreateAudioDeviceItem(string name, string id, string automationName, string helpText)");
        Assert.Contains("ToolTip = helpText", audioDeviceItemBlock);
        Assert.Contains("AutomationProperties.SetName(item, automationName);", audioDeviceItemBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", audioDeviceItemBlock);

        var micBlock = GetMethodBlock(recordingCode, "private void RecordMicCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", micBlock);
        Assert.Contains("var previous = _settingsService.Settings.RecordMicrophone;", micBlock);
        Assert.Contains("\"settings.record-microphone\"", micBlock);
        Assert.Contains("value => _settingsService.Settings.RecordMicrophone = value", micBlock);
        Assert.Contains("value => RecordMicCheck.IsChecked = value", micBlock);

        var desktopAudioBlock = GetMethodBlock(recordingCode, "private void RecordDesktopAudioCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", desktopAudioBlock);
        Assert.Contains("var previous = _settingsService.Settings.RecordDesktopAudio;", desktopAudioBlock);
        Assert.Contains("\"settings.record-desktop-audio\"", desktopAudioBlock);
        Assert.Contains("value => _settingsService.Settings.RecordDesktopAudio = value", desktopAudioBlock);
        Assert.Contains("value => RecordDesktopAudioCheck.IsChecked = value", desktopAudioBlock);

        var micDeviceBlock = GetMethodBlock(recordingCode, "private void MicDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", micDeviceBlock);
        Assert.Contains("var previous = _settingsService.Settings.MicrophoneDeviceId;", micDeviceBlock);
        Assert.Contains("var selected = item.Tag as string;", micDeviceBlock);
        Assert.Contains("\"settings.microphone-device\"", micDeviceBlock);
        Assert.Contains("value => _settingsService.Settings.MicrophoneDeviceId = value", micDeviceBlock);
        Assert.Contains("SelectMicDeviceById", micDeviceBlock);

        var desktopDeviceBlock = GetMethodBlock(recordingCode, "private void DesktopAudioDeviceCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressRecordingPreferenceChange) return;", desktopDeviceBlock);
        Assert.Contains("var previous = _settingsService.Settings.DesktopAudioDeviceId;", desktopDeviceBlock);
        Assert.Contains("var selected = item.Tag as string;", desktopDeviceBlock);
        Assert.Contains("\"settings.desktop-audio-device\"", desktopDeviceBlock);
        Assert.Contains("value => _settingsService.Settings.DesktopAudioDeviceId = value", desktopDeviceBlock);
        Assert.Contains("SelectDesktopAudioDeviceById", desktopDeviceBlock);

        var helperBlock = GetMethodBlock(recordingCode, "private void UpdateRecordingPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetRecordingPreferenceStatus(string.Empty);", helperBlock);
        Assert.Contains("if (applySuccessUi != null)", helperBlock);
        Assert.Contains("_suppressRecordingPreferenceChange = true;", helperBlock);
        Assert.Contains("applySuccessUi(current);", helperBlock);
        Assert.Contains("_suppressRecordingPreferenceChange = false;", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("SetRecordingPreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", helperBlock);
        Assert.Contains("ToastWindow.ShowError(", helperBlock);
        Assert.Contains("The previous recording setting was restored. Check Settings -> Recording and try again.", helperBlock);

        var statusBlock = GetMethodBlock(recordingCode, "private void SetRecordingPreferenceStatus(string message)");
        Assert.Contains("RecordingPreferenceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);

        var selectByTagBlock = GetMethodBlock(recordingCode, "private static void SelectComboItemByTag(System.Windows.Controls.ComboBox comboBox, string? tag)");
        Assert.Contains("comboBox.SelectedItem = item;", selectByTagBlock);
        Assert.Contains("comboBox.SelectedIndex = 0;", selectByTagBlock);
    }

    [Fact]
    public void ToastPreferencesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var preferencesCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Preferences.cs"));

        Assert.Contains("private bool _suppressToastPreferenceChange;", settingsCode);
        Assert.Contains("x:Name=\"ToastPreferenceStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "ToastPreferenceStatusText", "SettingsStatusText");
        AssertDynamicStatusTextBlock(xaml, "ToastPreferenceStatusText", "Toast preference status", isLive: true);
        var toastStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ToastPreferenceStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", toastStatusTag);
        AssertNamedControlHasLabel(xaml, "ToastSlotTopLeft", "<Border", "top-left toast slot", "Move selected toast button to top-left", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotTopInnerLeft", "<Border", "top inner-left toast slot", "Move selected toast button to top inner-left", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotTopInnerRight", "<Border", "top inner-right toast slot", "Move selected toast button to top inner-right", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotTopRight", "<Border", "top-right toast slot", "Move selected toast button to top-right", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotBottomLeft", "<Border", "bottom-left toast slot", "Move selected toast button to bottom-left", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotBottomInnerLeft", "<Border", "bottom inner-left toast slot", "Move selected toast button to bottom inner-left", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotBottomInnerRight", "<Border", "bottom inner-right toast slot", "Move selected toast button to bottom inner-right", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastSlotBottomRight", "<Border", "bottom-right toast slot", "Move selected toast button to bottom-right", "Press Enter or Space to place the selected toast button here.");
        AssertNamedControlHasLabel(xaml, "ToastLayoutCloseBtn", "<Border", "Close toast button", "Move the close toast button");
        AssertNamedControlHasLabel(xaml, "ToastLayoutPinBtn", "<Border", "Pin toast button", "Move the pin toast button");
        AssertNamedControlHasLabel(xaml, "ToastLayoutSaveBtn", "<Border", "Save toast button", "Move the save toast button");
        AssertNamedControlHasLabel(xaml, "ToastLayoutOfficeBtn", "<Border", "Office export toast button", "Move the office export toast button");
        AssertNamedControlHasLabel(xaml, "ToastLayoutAiRedirectBtn", "<Border", "AI redirect toast button", "Move the AI redirect toast button");
        AssertNamedControlHasLabel(xaml, "ToastLayoutDeleteBtn", "<Border", "Delete toast button", "Move the delete toast button");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastPositionCombo", "Right", "Show previews near the right edge.", "Right toast position", "Place screenshot previews near the right edge of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastPositionCombo", "Left", "Show previews near the left edge.", "Left toast position", "Place screenshot previews near the left edge of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastPositionCombo", "Top Left", "Show previews in the top-left corner.", "Top-left toast position", "Place screenshot previews in the top-left corner of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastPositionCombo", "Top Right", "Show previews in the top-right corner.", "Top-right toast position", "Place screenshot previews in the top-right corner of the screen.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "1.5 seconds", "Hide previews after 1.5 seconds.", "1.5 second toast duration", "Keep screenshot previews visible for 1.5 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "2 seconds", "Hide previews after 2 seconds.", "2 second toast duration", "Keep screenshot previews visible for 2 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "2.5 seconds", "Hide previews after 2.5 seconds.", "2.5 second toast duration", "Keep screenshot previews visible for 2.5 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "3 seconds", "Hide previews after 3 seconds.", "3 second toast duration", "Keep screenshot previews visible for 3 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "4 seconds", "Hide previews after 4 seconds.", "4 second toast duration", "Keep screenshot previews visible for 4 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastDurationCombo", "5 seconds", "Hide previews after 5 seconds.", "5 second toast duration", "Keep screenshot previews visible for 5 seconds.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastFadeDurationCombo", "1 second", "Fade previews out over 1 second.", "1 second fade-out duration", "Dismiss screenshot previews with a 1 second fade-out animation.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastFadeDurationCombo", "2 seconds", "Fade previews out over 2 seconds.", "2 second fade-out duration", "Dismiss screenshot previews with a 2 second fade-out animation.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastFadeDurationCombo", "3 seconds", "Fade previews out over 3 seconds.", "3 second fade-out duration", "Dismiss screenshot previews with a 3 second fade-out animation.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "ToastFadeDurationCombo", "5 seconds", "Fade previews out over 5 seconds.", "5 second fade-out duration", "Dismiss screenshot previews with a 5 second fade-out animation.");

        var positionBlock = GetMethodBlock(preferencesCode, "private void ToastPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressToastPreferenceChange) return;", positionBlock);
        Assert.Contains("var previous = _settingsService.Settings.ToastPosition;", positionBlock);
        Assert.Contains("\"settings.toast-position\"", positionBlock);
        Assert.Contains("value => _settingsService.Settings.ToastPosition = value", positionBlock);
        Assert.Contains("ToastPositionCombo.SelectedIndex = (int)value", positionBlock);
        Assert.Contains("ToastWindow.SetPosition(value);", positionBlock);
        Assert.Contains("PreviewWindow.SetPosition(value);", positionBlock);

        var durationBlock = GetMethodBlock(preferencesCode, "private void ToastDurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressToastPreferenceChange) return;", durationBlock);
        Assert.Contains("var previous = _settingsService.Settings.ToastDurationSeconds;", durationBlock);
        Assert.Contains("\"settings.toast-duration\"", durationBlock);
        Assert.Contains("value => _settingsService.Settings.ToastDurationSeconds = value", durationBlock);
        Assert.Contains("SelectToastDuration", durationBlock);
        Assert.Contains("ToastWindow.SetDuration", durationBlock);

        var fadeBlock = GetMethodBlock(preferencesCode, "private void ToastFadeOutCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressToastPreferenceChange) return;", fadeBlock);
        Assert.Contains("var previous = _settingsService.Settings.ToastFadeOutEnabled;", fadeBlock);
        Assert.Contains("\"settings.toast-fade-out\"", fadeBlock);
        Assert.Contains("ToastFadeOutCheck.IsChecked = value;", fadeBlock);
        Assert.Contains("SetToastFadeDurationVisibility(value);", fadeBlock);
        Assert.Contains("ToastWindow.SetFadeOutBehavior(value, _settingsService.Settings.ToastFadeOutSeconds)", fadeBlock);

        var fadeDurationBlock = GetMethodBlock(preferencesCode, "private void ToastFadeDurationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressToastPreferenceChange) return;", fadeDurationBlock);
        Assert.Contains("var previous = _settingsService.Settings.ToastFadeOutSeconds;", fadeDurationBlock);
        Assert.Contains("\"settings.toast-fade-duration\"", fadeDurationBlock);
        Assert.Contains("value => _settingsService.Settings.ToastFadeOutSeconds = value", fadeDurationBlock);
        Assert.Contains("SelectToastFadeDuration", fadeDurationBlock);
        Assert.Contains("ToastWindow.SetFadeOutBehavior(_settingsService.Settings.ToastFadeOutEnabled, value)", fadeDurationBlock);

        var autoPinBlock = GetMethodBlock(preferencesCode, "private void AutoPinPreviewsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressToastPreferenceChange) return;", autoPinBlock);
        Assert.Contains("var previous = _settingsService.Settings.AutoPinPreviews;", autoPinBlock);
        Assert.Contains("\"settings.auto-pin-previews\"", autoPinBlock);
        Assert.Contains("value => _settingsService.Settings.AutoPinPreviews = value", autoPinBlock);
        Assert.Contains("value => AutoPinPreviewsCheck.IsChecked = value", autoPinBlock);

        var helperBlock = GetMethodBlock(preferencesCode, "private void UpdateToastPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetToastPreferenceStatus(string.Empty);", helperBlock);
        Assert.Contains("applySuccessUi?.Invoke(current);", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(current);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressToastPreferenceChange = true;", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("_suppressToastPreferenceChange = false;", helperBlock);
        Assert.Contains("applyRuntime?.Invoke(previous);", helperBlock);
        Assert.Contains("SetToastPreferenceStatus($\"{label} change was not saved. Previous setting restored.\");", helperBlock);
        Assert.Contains("ToastWindow.ShowError(", helperBlock);
        Assert.Contains("The previous toast setting was restored. Check Settings -> Toasts and try again.", helperBlock);

        var statusBlock = GetMethodBlock(preferencesCode, "private void SetToastPreferenceStatus(string message)");
        Assert.Contains("ToastPreferenceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);

        var visibilityBlock = GetMethodBlock(preferencesCode, "private void SetToastFadeDurationVisibility(bool enabled)");
        Assert.Contains("ToastFadeDurationSeparator.Visibility = visibility;", visibilityBlock);
        Assert.Contains("ToastFadeDurationRow.Visibility = visibility;", visibilityBlock);
    }

    [Theory]
    [InlineData(false, null, null, "Upload now")]
    [InlineData(false, "https://example.com/capture.png", null, "Re-upload")]
    [InlineData(false, null, "rate limited", "Retry upload")]
    [InlineData(false, "https://example.com/capture.png", "rate limited", "Retry upload")]
    [InlineData(true, null, "rate limited", "Uploading...")]
    public void HistoryUploadMenuLabelMatchesUploadState(bool isInProgress, string? uploadUrl, string? uploadError, string expectedLabel)
    {
        var method = typeof(SettingsWindow).GetMethod("GetHistoryUploadMenuLabel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entry = new HistoryEntry
        {
            FilePath = "capture.png",
            UploadUrl = uploadUrl,
            UploadError = uploadError
        };

        var label = Assert.IsType<string>(method.Invoke(null, new object[] { entry, isInProgress }));
        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData(true, null, "rate limited", "This history item upload is already running.")]
    [InlineData(false, null, "rate limited", "Retry uploading this file with the current Uploads settings.")]
    [InlineData(false, "https://example.com/capture.png", null, "Upload this file again with the current Uploads settings.")]
    [InlineData(false, null, null, "Upload this file with the current Uploads settings.")]
    public void HistoryUploadMenuHelpTextMatchesUploadState(bool isInProgress, string? uploadUrl, string? uploadError, string expectedHelpText)
    {
        var method = typeof(SettingsWindow).GetMethod("GetHistoryUploadMenuHelpText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entry = new HistoryEntry
        {
            FilePath = "capture.png",
            UploadUrl = uploadUrl,
            UploadError = uploadError
        };

        var helpText = Assert.IsType<string>(method.Invoke(null, new object[] { entry, isInProgress }));
        Assert.Equal(expectedHelpText, helpText);
    }

    [Theory]
    [InlineData("https://example.com/capture.png", null, HistoryKind.Image, "Copy link")]
    [InlineData("https://example.com/capture.png", "rate limited", HistoryKind.Image, "Copy previous link")]
    [InlineData(null, null, HistoryKind.Image, "Copy image")]
    [InlineData(null, null, HistoryKind.Sticker, "Copy image")]
    [InlineData(null, null, HistoryKind.Gif, "Copy GIF")]
    [InlineData(null, null, HistoryKind.Video, "Copy video")]
    public void HistoryCopyMenuLabelMatchesCopyTarget(string? uploadUrl, string? uploadError, HistoryKind kind, string expectedLabel)
    {
        var method = typeof(SettingsWindow).GetMethod("GetHistoryCopyMenuLabel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entry = new HistoryEntry
        {
            FilePath = "capture.png",
            Kind = kind,
            UploadUrl = uploadUrl,
            UploadError = uploadError
        };

        var label = Assert.IsType<string>(method.Invoke(null, new object[] { entry }));
        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData(null, "Open URL")]
    [InlineData("rate limited", "Open previous link")]
    public void HistoryOpenUrlMenuLabelClarifiesFailedUploadState(string? uploadError, string expectedLabel)
    {
        var method = typeof(SettingsWindow).GetMethod("GetHistoryOpenUrlMenuLabel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entry = new HistoryEntry
        {
            FilePath = "capture.png",
            UploadUrl = "https://example.com/capture.png",
            UploadError = uploadError
        };

        var label = Assert.IsType<string>(method.Invoke(null, new object[] { entry }));
        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData(null, "Open this history item's upload URL.")]
    [InlineData("rate limited", "Open the previous upload link for this history item.")]
    public void HistoryOpenUrlMenuHelpTextClarifiesFailedUploadState(string? uploadError, string expectedHelpText)
    {
        var method = typeof(SettingsWindow).GetMethod("GetHistoryOpenUrlMenuHelpText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var entry = new HistoryEntry
        {
            FilePath = "capture.png",
            UploadUrl = "https://example.com/capture.png",
            UploadError = uploadError
        };

        var helpText = Assert.IsType<string>(method.Invoke(null, new object[] { entry }));
        Assert.Equal(expectedHelpText, helpText);
    }

    [Fact]
    public void HistoryUploadInfoShowsErrorBeforeStaleUrl()
    {
        var cardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));

        var uploadInfoBlock = GetMethodBlock(cardCode, "private static void AddUploadInfo(StackPanel panel, HistoryEntry entry)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(entry.UploadError))", uploadInfoBlock);
        Assert.Contains("Foreground = Theme.Brush(Theme.DangerHover)", uploadInfoBlock);
        Assert.Contains("AutomationProperties.SetName(errorBlock, \"Upload error\");", uploadInfoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(errorBlock, entry.UploadError);", uploadInfoBlock);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(entry.UploadUrl))", uploadInfoBlock);
        Assert.Contains("AutomationProperties.SetName(urlBlock, \"Upload URL\");", uploadInfoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(urlBlock, entry.UploadUrl);", uploadInfoBlock);

        var errorIndex = uploadInfoBlock.IndexOf("if (!string.IsNullOrWhiteSpace(entry.UploadError))", StringComparison.Ordinal);
        var urlIndex = uploadInfoBlock.IndexOf("if (!string.IsNullOrWhiteSpace(entry.UploadUrl))", StringComparison.Ordinal);
        Assert.True(urlIndex > errorIndex, "History card upload status should show the retry/error state before any stale URL metadata.");
    }

    [Fact]
    public void HistoryCardActionMenuAvoidsDuplicateCopyUrlAction()
    {
        var cardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));

        Assert.Contains("CreateCardActionMenuItem(GetHistoryCopyMenuLabel(vm.Entry)", cardCode);
        Assert.Contains("GetHistoryCopyMenuHelpText(vm.Entry, kindLabel)", cardCode);
        Assert.Contains("if (hasUploadUrl)", cardCode);
        Assert.Contains("CreateCardActionMenuItem(GetHistoryOpenUrlMenuLabel(vm.Entry)", cardCode);
        Assert.Contains("GetHistoryOpenUrlMenuHelpText(vm.Entry)", cardCode);
        Assert.DoesNotContain("CreateCardActionMenuItem(\"Copy URL\"", cardCode);

        var helpTextBlock = GetMethodBlock(cardCode, "private static string GetHistoryCopyMenuHelpText(HistoryEntry entry, string kindLabel)");
        Assert.Contains("Copy the previous upload link for this history item.", helpTextBlock);
        Assert.Contains("Copy this history item's upload link.", helpTextBlock);
        Assert.Contains("return $\"Copy this {kindLabel} history item.\";", helpTextBlock);
    }

    [Fact]
    public void HistoryUploadRetryPreventsDuplicateInFlightUploads()
    {
        var cardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        Assert.Contains("uploadItem.IsEnabled = !uploadInProgress;", cardCode);
        Assert.Contains("uploadItem.Header = \"Uploading...\";", cardCode);
        Assert.Contains("uploadItem.IsEnabled = false;", cardCode);
        Assert.Contains("_historyUploadPathsInProgress", uploadCode);
        Assert.Contains("TryBeginHistoryUpload(entry.FilePath)", uploadCode);
        Assert.Contains("Upload already running", uploadCode);
        Assert.Contains("finally", uploadCode);
        Assert.Contains("EndHistoryUpload(entry.FilePath);", uploadCode);
        Assert.Contains("var shouldReloadHistory = false;", uploadCode);
        Assert.Contains("var destination = UploadDestination.None;", uploadCode);
        Assert.Contains("destination = _settingsService.Settings.ImageUploadDestination;", uploadCode);
        Assert.Contains("if (shouldReloadHistory)", uploadCode);
        Assert.Contains("catch (Exception ex)", uploadCode);
        Assert.Contains("entry.UploadProvider = GetHistoryUploadAttemptProvider(destination);", uploadCode);
        Assert.Contains("entry.UploadError = string.IsNullOrWhiteSpace(ex.Message) ? \"Upload failed.\" : ex.Message;", uploadCode);
        Assert.Contains("_historyService.SaveEntry(entry);", uploadCode);
        Assert.Contains("BuildHistoryUploadUnexpectedErrorToastBody(entry.UploadProvider, entry.UploadError)", uploadCode);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upload error\", entry.UploadError);", uploadCode);
        Assert.Contains("ToastWindow.ShowError(\"Upload not configured\", BuildHistoryUploadConfigurationToastBody(entry.UploadError), entry.FilePath);", uploadCode);
        Assert.Contains("ToastWindow.ShowError(\"Uploaded, copy failed\", BuildHistoryUploadCopyFailureToastBody(ex.Message), entry.FilePath);", uploadCode);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Uploaded, copy failed\", $\"The upload finished, but the link was not copied.\\n{ex.Message}\");", uploadCode);
        Assert.Contains("Open History and copy the upload link manually.", uploadCode);
        Assert.Contains("BuildHistoryUploadFailureToastBody(providerName, entry.UploadError, result.IsRateLimit)", uploadCode);

        var clearIndex = uploadCode.IndexOf("EndHistoryUpload(entry.FilePath);", StringComparison.Ordinal);
        var reloadIndex = uploadCode.IndexOf("LoadCurrentHistoryTab(preserveTransientState: true);", clearIndex, StringComparison.Ordinal);
        Assert.True(reloadIndex > clearIndex, "History should reload after clearing the in-flight upload guard.");

        var uploadSuccessIndex = uploadCode.IndexOf("entry.UploadError = null;", StringComparison.Ordinal);
        var copyFailureIndex = uploadCode.IndexOf("ToastWindow.ShowError(\"Uploaded, copy failed\", BuildHistoryUploadCopyFailureToastBody(ex.Message), entry.FilePath);", uploadSuccessIndex, StringComparison.Ordinal);
        var uploadErrorIndex = uploadCode.IndexOf("BuildHistoryUploadUnexpectedErrorToastBody(entry.UploadProvider, entry.UploadError)", StringComparison.Ordinal);
        Assert.True(copyFailureIndex > uploadSuccessIndex, "Clipboard failures after a successful upload should keep the upload success state.");
        Assert.True(uploadErrorIndex > copyFailureIndex, "Unexpected upload errors should remain separate from post-upload copy failures.");

        var providerNameIndex = uploadCode.IndexOf("var providerName = UploadService.GetName(destination);", uploadSuccessIndex, StringComparison.Ordinal);
        var providerSaveIndex = uploadCode.IndexOf("entry.UploadProvider = providerName;", providerNameIndex, StringComparison.Ordinal);
        var failureBodyIndex = uploadCode.IndexOf("BuildHistoryUploadFailureToastBody(providerName, entry.UploadError, result.IsRateLimit)", providerSaveIndex, StringComparison.Ordinal);
        var failureFilePathIndex = uploadCode.IndexOf("entry.FilePath", failureBodyIndex, StringComparison.Ordinal);
        Assert.True(providerSaveIndex > providerNameIndex && failureBodyIndex > providerSaveIndex,
            "History retry failure feedback should use the same provider saved into history.");
        Assert.True(failureFilePathIndex > failureBodyIndex,
            "History retry upload failures should keep the saved file path attached to the toast.");

        var failureBodyBlock = GetMethodBlock(uploadCode, "private static string BuildHistoryUploadFailureToastBody(string providerName, string error, bool isRateLimit)");
        Assert.Contains("var providerLabel = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", failureBodyBlock);
        Assert.Contains("Try another upload destination or wait before retrying.", failureBodyBlock);
        Assert.Contains("Check {providerLabel} settings or try another upload destination.", failureBodyBlock);
        Assert.Contains("return $\"{providerLabel}: {error}\\n{recovery}\";", failureBodyBlock);

        var copyFailureBodyBlock = GetMethodBlock(uploadCode, "private static string BuildHistoryUploadCopyFailureToastBody(string details)");
        Assert.Contains("The upload finished, but OddSnap could not copy the link. Open History and copy the upload link manually.", copyFailureBodyBlock);
        Assert.Contains("string.IsNullOrWhiteSpace(details) ? recovery : $\"{recovery}\\n{details}\"", copyFailureBodyBlock);

        var configurationBodyBlock = GetMethodBlock(uploadCode, "private static string BuildHistoryUploadConfigurationToastBody(string details)");
        Assert.Contains("Check Settings -> Uploads, then retry from History.", configurationBodyBlock);
        Assert.Contains("string.IsNullOrWhiteSpace(details) ? recovery : $\"{recovery}\\n{details}\"", configurationBodyBlock);

        var missingFileBodyBlock = GetMethodBlock(uploadCode, "private static string BuildHistoryUploadMissingFileToastBody(string filePath)");
        Assert.Contains("Path.GetFileName(filePath)", missingFileBodyBlock);
        Assert.Contains("Restore the file or capture it again, then retry the upload from History.", missingFileBodyBlock);

        var unexpectedBodyBlock = GetMethodBlock(uploadCode, "private static string BuildHistoryUploadUnexpectedErrorToastBody(string providerName, string error)");
        Assert.Contains("The file is still saved. Check Settings -> Uploads, then retry from History or try another destination.", unexpectedBodyBlock);
        Assert.Contains("return $\"{providerLabel}: {detail}\\n{recovery}\";", unexpectedBodyBlock);
    }

    [Fact]
    public void HistoryUploadRetryUnsupportedDestinationRefreshesProvider()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        var retryBlock = GetMethodBlock(uploadCode, "private async Task RetryHistoryUploadAsync(HistoryItemVM vm)");
        var unsupportedIndex = retryBlock.IndexOf("if (destination == UploadDestination.None || UploadService.IsAiChatDestination(destination))", StringComparison.Ordinal);
        var providerIndex = retryBlock.IndexOf("entry.UploadProvider = GetHistoryUploadAttemptProvider(destination);", unsupportedIndex, StringComparison.Ordinal);
        var errorIndex = retryBlock.IndexOf("entry.UploadError = \"Choose an upload destination in Settings -> Uploads.\";", providerIndex, StringComparison.Ordinal);
        Assert.True(providerIndex > unsupportedIndex && errorIndex > providerIndex,
            "Unsupported history retry destinations should replace stale provider metadata before saving the failure.");

        var catchIndex = retryBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var catchProviderIndex = retryBlock.IndexOf("entry.UploadProvider = GetHistoryUploadAttemptProvider(destination);", catchIndex, StringComparison.Ordinal);
        var catchErrorIndex = retryBlock.IndexOf("entry.UploadError = string.IsNullOrWhiteSpace(ex.Message) ? \"Upload failed.\" : ex.Message;", catchProviderIndex, StringComparison.Ordinal);
        Assert.True(catchProviderIndex > catchIndex && catchErrorIndex > catchProviderIndex,
            "Unexpected history retry errors should record the attempted provider before saving the failure.");

        var providerBlock = GetMethodBlock(uploadCode, "private static string GetHistoryUploadAttemptProvider(UploadDestination destination)");
        Assert.Contains("destination == UploadDestination.None ? \"Upload\" : UploadService.GetName(destination)", providerBlock);
    }

    [Fact]
    public void HistoryUploadRetryMissingFilePersistsFailureAndReloads()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        var missingFileBlock = GetBlockStartingAt(uploadCode, "if (!File.Exists(entry.FilePath))");
        Assert.Contains("entry.UploadProvider = GetHistoryUploadAttemptProvider(UploadDestination.None);", missingFileBlock);
        Assert.Contains("entry.UploadError = \"File no longer exists.\";", missingFileBlock);
        Assert.Contains("_historyService.SaveEntry(entry);", missingFileBlock);
        Assert.Contains("ToastWindow.ShowError(\"Upload failed\", BuildHistoryUploadMissingFileToastBody(entry.FilePath), entry.FilePath);", missingFileBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upload failed\", \"File no longer exists.\");", missingFileBlock);
        Assert.Contains("LoadCurrentHistoryTab(preserveTransientState: true);", missingFileBlock);

        var providerIndex = missingFileBlock.IndexOf("entry.UploadProvider = GetHistoryUploadAttemptProvider(UploadDestination.None);", StringComparison.Ordinal);
        var errorIndex = missingFileBlock.IndexOf("entry.UploadError = \"File no longer exists.\";", providerIndex, StringComparison.Ordinal);
        var saveIndex = missingFileBlock.IndexOf("_historyService.SaveEntry(entry);", errorIndex, StringComparison.Ordinal);
        Assert.True(errorIndex > providerIndex && saveIndex > errorIndex,
            "Missing-file retry should replace stale provider metadata before saving the failure.");
    }

    [Fact]
    public void HistoryUploadedFilterExcludesFailedRetryState()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        var applyBlock = GetMethodBlock(uploadCode, "private IEnumerable<HistoryItemVM> ApplyHistoryUploadFilter(IEnumerable<HistoryItemVM> items)");
        var uploadedIndex = applyBlock.IndexOf("state == HistoryUploadStateFilter.Uploaded", StringComparison.Ordinal);
        var notUploadedIndex = applyBlock.IndexOf("state == HistoryUploadStateFilter.NotUploaded", StringComparison.Ordinal);
        Assert.True(uploadedIndex >= 0, "Could not find uploaded filter branch.");
        Assert.True(notUploadedIndex > uploadedIndex, "Could not find not-uploaded filter branch after uploaded filter branch.");

        var uploadedBranch = applyBlock[uploadedIndex..notUploadedIndex];
        Assert.Contains("string.IsNullOrWhiteSpace(entry.UploadUrl)", uploadedBranch);
        Assert.Contains("!string.IsNullOrWhiteSpace(entry.UploadError)", uploadedBranch);
        Assert.Contains("continue;", uploadedBranch);
    }

    [Fact]
    public void HistoryUploadFiltersOnlyApplyToFileBackedCategories()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        Assert.Contains("private bool SupportsHistoryUploadFilter()", uploadCode);
        Assert.Contains("=> HistoryCategoryCombo.SelectedIndex is 0 or 2 or 4;", uploadCode);

        var activeBlock = GetMethodBlock(uploadCode, "private bool IsHistoryUploadFilterActive()");
        Assert.Contains("if (!IsLoaded || !SupportsHistoryUploadFilter())", activeBlock);

        var applyBlock = GetMethodBlock(uploadCode, "private IEnumerable<HistoryItemVM> ApplyHistoryUploadFilter(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("if (!SupportsHistoryUploadFilter())", applyBlock);
        Assert.Contains("yield return item;", applyBlock);
        Assert.Contains("yield break;", applyBlock);

        var uiBlock = GetMethodBlock(uploadCode, "private void UpdateHistoryUploadFilterUi()");
        Assert.Contains("var supportsUploadFilter = SupportsHistoryUploadFilter();", uiBlock);
        Assert.Contains("HistoryUploadFilterCombo.ToolTip = stateHelp;", uiBlock);
        Assert.Contains("HistoryUploadProviderCombo.ToolTip = providerHelp;", uiBlock);
        Assert.Contains("AutomationProperties.SetName(HistoryUploadFilterCombo, $\"{categoryName} upload state filter\");", uiBlock);
        Assert.Contains("AutomationProperties.SetName(HistoryUploadProviderCombo, $\"{categoryName} upload provider filter\");", uiBlock);
        Assert.Contains("AutomationProperties.SetHelpText(HistoryUploadFilterCombo, stateHelp);", uiBlock);
        Assert.Contains("AutomationProperties.SetHelpText(HistoryUploadProviderCombo, providerHelp);", uiBlock);
        Assert.Contains("Filter {categoryLabel} by upload state.", uiBlock);
        Assert.Contains("Filter {categoryLabel} by upload provider.", uiBlock);
        Assert.Contains("Upload filters are available for screenshots, videos/GIFs, and stickers.", uiBlock);
        Assert.DoesNotContain("HistoryCategoryCombo.SelectedIndex is 0 or 2 or 4", activeBlock);
        Assert.DoesNotContain("HistoryCategoryCombo.SelectedIndex is 0 or 2 or 4", uiBlock);

        var stateChangedBlock = GetMethodBlock(uploadCode, "private void HistoryUploadFilterCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressHistoryUploadFilterEvents || !SupportsHistoryUploadFilter())", stateChangedBlock);

        var providerChangedBlock = GetMethodBlock(uploadCode, "private void HistoryUploadProviderCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressHistoryUploadFilterEvents || !SupportsHistoryUploadFilter())", providerChangedBlock);
    }

    [Fact]
    public void HistoryUploadProviderFilterNormalizesMetadataWhitespace()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        Assert.Contains("private static string NormalizeHistoryUploadProvider(string? provider)", uploadCode);
        Assert.Contains("=> string.IsNullOrWhiteSpace(provider) ? \"\" : provider.Trim();", uploadCode);

        var applyBlock = GetMethodBlock(uploadCode, "private IEnumerable<HistoryItemVM> ApplyHistoryUploadFilter(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("var entryProvider = NormalizeHistoryUploadProvider(entry.UploadProvider);", applyBlock);
        Assert.Contains("!string.Equals(entryProvider, provider, StringComparison.OrdinalIgnoreCase)", applyBlock);
        Assert.DoesNotContain("!string.Equals(entry.UploadProvider, provider, StringComparison.OrdinalIgnoreCase)", applyBlock);

        var refreshBlock = GetMethodBlock(uploadCode, "private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryEntry> entries)");
        Assert.Contains(".Select(entry => NormalizeHistoryUploadProvider(entry.UploadProvider))", refreshBlock);

        var selectedBlock = GetMethodBlock(uploadCode, "private string GetHistoryUploadProviderFilter()");
        Assert.Contains("NormalizeHistoryUploadProvider(item.Tag as string)", selectedBlock);
    }

    [Fact]
    public void HistoryUploadProviderFilterResetIsAppliedBeforeFiltering()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        Assert.Contains("private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryItemVM> items)", uploadCode);
        var refreshBlock = GetMethodBlock(uploadCode, "private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryEntry> entries)");
        Assert.Contains("return false;", refreshBlock);
        Assert.Contains("return selectedIndex == 0 && !string.IsNullOrWhiteSpace(selectedProvider);", refreshBlock);

        var imageLoadBlock = GetMethodBlock(historyCode, "private async Task LoadHistoryAsync()");
        Assert.Contains("var providerFilterReset = RefreshHistoryUploadProviderFilterItems(entries);", imageLoadBlock);
        Assert.Contains("if (providerFilterReset)", imageLoadBlock);
        Assert.Contains("EnsureAllImageHistoryItemsMaterialized();", imageLoadBlock);

        var mediaLoadBlock = GetMethodBlock(mediaHistoryCode, "private void LoadMediaHistory()");
        var mediaRefreshIndex = mediaLoadBlock.IndexOf("RefreshHistoryUploadProviderFilterItems(_allGifItems);", StringComparison.Ordinal);
        var mediaApplyIndex = mediaLoadBlock.IndexOf("_filteredGifItems = ApplyHistoryUploadFilter(_allGifItems).ToList();", StringComparison.Ordinal);
        Assert.True(mediaApplyIndex > mediaRefreshIndex, "Media history should apply upload filters after provider refresh can reset stale providers.");

        var stickerLoadBlock = GetMethodBlock(historyCode, "private void LoadStickerHistory()");
        var stickerRefreshIndex = stickerLoadBlock.IndexOf("RefreshHistoryUploadProviderFilterItems(_allStickerItems);", StringComparison.Ordinal);
        var stickerApplyIndex = stickerLoadBlock.IndexOf("_filteredStickerItems = ApplyHistoryUploadFilter(_allStickerItems).ToList();", StringComparison.Ordinal);
        Assert.True(stickerApplyIndex > stickerRefreshIndex, "Sticker history should apply upload filters after provider refresh can reset stale providers.");
    }

    [Fact]
    public void HistoryIndexedSearchCountsBytesAfterUploadFilter()
    {
        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        var filterIndex = indexedBlock.IndexOf("var uploadFilteredItems = ApplyHistoryUploadFilter(filtered).ToList();", StringComparison.Ordinal);
        var thumbnailFilterIndex = indexedBlock.IndexOf("var filteredItems = FilterSearchResultsForLoadedThumbnails(uploadFilteredItems, query);", StringComparison.Ordinal);
        var visibleBytesIndex = indexedBlock.IndexOf("long visibleBytes = 0;", StringComparison.Ordinal);
        Assert.True(filterIndex >= 0, "Could not find indexed search upload-filter application.");
        Assert.True(thumbnailFilterIndex > filterIndex, "Could not find indexed search thumbnail visibility filtering after upload filtering.");
        Assert.True(visibleBytesIndex > thumbnailFilterIndex, "Indexed search byte count should be based on filtered visible items.");

        var byteCountBlock = indexedBlock[visibleBytesIndex..indexedBlock.IndexOf("var sizeStr = FormatStorageSize(visibleBytes);", visibleBytesIndex, StringComparison.Ordinal)];
        Assert.Contains("foreach (var item in filteredItems)", byteCountBlock);
        Assert.Contains("visibleBytes += GetHistoryItemFileSize(item);", byteCountBlock);
        Assert.DoesNotContain("visibleBytes += entry.FileSizeBytes", indexedBlock[..filterIndex]);
    }

    [Fact]
    public void ImageHistoryCountsUseCurrentFileSizeFallback()
    {
        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        Assert.Contains("private static long GetHistoryItemFileSize(HistoryItemVM item)", historyCode);
        Assert.Contains("item.Entry.FileSizeBytes > 0 ? item.Entry.FileSizeBytes : TryGetFileLength(item.Entry.FilePath)", historyCode);

        var immediateBlock = GetMethodBlock(searchCode, "private void ApplyImmediateImageFilter(string query, ImageSearchSourceOptions sources, bool exactMatch)");
        Assert.Contains("visibleBytes += GetHistoryItemFileSize(item);", immediateBlock);
        Assert.DoesNotContain("visibleBytes += item.Entry.FileSizeBytes;", immediateBlock);

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        Assert.Contains("visibleBytes += GetHistoryItemFileSize(item);", indexedBlock);
        Assert.DoesNotContain("visibleBytes += item.Entry.FileSizeBytes;", indexedBlock);

        var loadedCountBlock = GetMethodBlock(historyCode, "private void UpdateLoadedImageHistoryCountText()");
        Assert.Contains("visibleBytes += GetHistoryItemFileSize(item);", loadedCountBlock);
        Assert.DoesNotContain("visibleBytes += item.Entry.FileSizeBytes;", loadedCountBlock);
    }

    [Fact]
    public void HistorySearchEmptyStateDistinguishesPendingThumbnails()
    {
        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));

        var immediateBlock = GetMethodBlock(searchCode, "private void ApplyImmediateImageFilter(string query, ImageSearchSourceOptions sources, bool exactMatch)");
        Assert.Contains("var usingSearchAndUploadFilter = usingSearch && uploadFilterActive;", immediateBlock);
        Assert.Contains("var pendingThumbnailMatches = usingSearch && rankedItems.Count > 0 && filteredItems.Count == 0;", immediateBlock);
        Assert.Contains("ShowHistoryEmptyState(\"Loading matching screenshots\", \"Thumbnail previews are loading. Results will appear shortly.\");", immediateBlock);
        Assert.Contains("ShowHistoryEmptyState(\"No screenshots match this search and filter\", \"Search and upload filters matched 0 saved screenshots.\");", immediateBlock);

        var emptyStateIndex = immediateBlock.IndexOf("if (_filteredHistoryItems.Count == 0)", StringComparison.Ordinal);
        Assert.True(emptyStateIndex >= 0, "Could not find image search empty-state branch.");
        var emptyStateBlock = immediateBlock[emptyStateIndex..];
        var pendingIndex = emptyStateBlock.IndexOf("else if (pendingThumbnailMatches)", StringComparison.Ordinal);
        var combinedFilterIndex = emptyStateBlock.IndexOf("else if (usingSearchAndUploadFilter)", StringComparison.Ordinal);
        var noMatchIndex = emptyStateBlock.IndexOf("else if (usingSearch)\r\n                ShowHistoryEmptyState", StringComparison.Ordinal);
        if (noMatchIndex < 0)
            noMatchIndex = emptyStateBlock.IndexOf("else if (usingSearch)\n                ShowHistoryEmptyState", StringComparison.Ordinal);
        Assert.True(noMatchIndex > pendingIndex, "Search should show the pending-thumbnail state before the no-match state.");
        Assert.True(combinedFilterIndex > pendingIndex, "Search should show the pending-thumbnail state before the combined search/filter empty state.");
        Assert.True(noMatchIndex > combinedFilterIndex, "Combined search/filter empty state should be more specific than the plain search empty state.");

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        Assert.Contains("var uploadFilteredItems = ApplyHistoryUploadFilter(filtered).ToList();", indexedBlock);
        Assert.Contains("var filteredItems = FilterSearchResultsForLoadedThumbnails(uploadFilteredItems, query);", indexedBlock);
        Assert.Contains("var pendingThumbnailMatches = uploadFilteredItems.Count > 0 && filteredItems.Count == 0;", indexedBlock);
        Assert.Contains("var uploadFilterActive = IsHistoryUploadFilterActive();", indexedBlock);
        Assert.Contains("if (_filteredHistoryItems.Count == 0 && pendingThumbnailMatches)", indexedBlock);
        Assert.Contains("else if (_filteredHistoryItems.Count == 0 && uploadFilterActive)", indexedBlock);
        Assert.Contains("ShowHistoryEmptyState(\"Loading matching screenshots\", \"Thumbnail previews are loading. Results will appear shortly.\");", indexedBlock);
        Assert.Contains("ShowHistoryEmptyState(\"No screenshots match this search and filter\", \"Search and upload filters matched 0 saved screenshots.\");", indexedBlock);
    }

    [Fact]
    public void HistorySearchCountTextDistinguishesPendingThumbnails()
    {
        var method = typeof(SettingsWindow).GetMethod("FormatImageSearchVisibleCountText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal("1 visible of 1 match · 10 KB", method.Invoke(null, new object[] { 1, 1, "10 KB" }));
        Assert.Equal("2 visible of 5 matches · 20 KB", method.Invoke(null, new object[] { 2, 5, "20 KB" }));

        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));

        var immediateBlock = GetMethodBlock(searchCode, "private void ApplyImmediateImageFilter(string query, ImageSearchSourceOptions sources, bool exactMatch)");
        Assert.Contains("var pendingThumbnailMatchCount = usingSearch", immediateBlock);
        Assert.Contains("Math.Max(0, rankedItems.Count - filteredItems.Count)", immediateBlock);
        Assert.Contains("if (usingSearch && pendingThumbnailMatchCount > 0)", immediateBlock);
        Assert.Contains("FormatImageSearchVisibleCountText(_filteredHistoryItems.Count, rankedItems.Count, sizeStr)", immediateBlock);

        var visibleCountIndex = immediateBlock.IndexOf("if (usingSearch && pendingThumbnailMatchCount > 0)", StringComparison.Ordinal);
        var genericSearchCountIndex = immediateBlock.IndexOf("else if (usingSearch)\r\n        {\r\n            HistoryCountText.Text = FormatImageSearchMatchCountText", StringComparison.Ordinal);
        if (genericSearchCountIndex < 0)
            genericSearchCountIndex = immediateBlock.IndexOf("else if (usingSearch)\n        {\n            HistoryCountText.Text = FormatImageSearchMatchCountText", StringComparison.Ordinal);
        Assert.True(genericSearchCountIndex > visibleCountIndex, "Pending-thumbnail search counts should be more specific than generic search/filter counts.");

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        Assert.Contains("var pendingThumbnailMatchCount = Math.Max(0, uploadFilteredItems.Count - filteredItems.Count);", indexedBlock);
        Assert.Contains("HistoryCountText.Text = pendingThumbnailMatchCount > 0", indexedBlock);
        Assert.Contains("FormatImageSearchVisibleCountText(_filteredHistoryItems.Count, uploadFilteredItems.Count, sizeStr)", indexedBlock);
    }

    [Fact]
    public void IndexedImageSearchFailuresAreLogged()
    {
        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        Assert.Contains("catch (Exception ex)", indexedBlock);
        Assert.Contains("searchFailed = true;", indexedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-search\", ex);", indexedBlock);
        Assert.Contains("SetImageSearchLoading(false, forceIndexed: true);", indexedBlock);
        Assert.Contains("HistorySearchStatusText.Text = \"Search failed\";", indexedBlock);
    }

    [Fact]
    public void ImageSearchIndexRequestFailuresAreLogged()
    {
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        var loadBlock = GetMethodBlock(historyCode, "private async Task LoadHistoryAsync()");
        Assert.Contains("_imageSearchIndexService.RequestSync(entries, _settingsService.Settings.OcrLanguageTag);", loadBlock);
        Assert.Contains("catch (Exception ex)", loadBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-search-request\", ex);", loadBlock);
        Assert.DoesNotContain("catch { }", loadBlock);
    }

    [Fact]
    public void ImageSearchSourcePreferencesRollBackAndReportFailures()
    {
        var actionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        var exactBlock = GetMethodBlock(actionsCode, "private void ImageSearchExactMatchCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressImageSearchSourceEvents)", exactBlock);
        Assert.Contains("var previous = _settingsService.Settings.ImageSearchExactMatch;", exactBlock);
        Assert.Contains("var selected = ImageSearchExactMatchCheck.IsChecked == true;", exactBlock);
        Assert.Contains("\"settings.image-search-exact-match\"", exactBlock);
        Assert.Contains("value => _settingsService.Settings.ImageSearchExactMatch = value", exactBlock);
        Assert.Contains("value => ImageSearchExactMatchCheck.IsChecked = value", exactBlock);

        var sourcesBlock = GetMethodBlock(actionsCode, "private void ImageSearchSourcesCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressImageSearchSourceEvents)", sourcesBlock);
        Assert.Contains("var previous = _settingsService.Settings.ImageSearchSources;", sourcesBlock);
        Assert.Contains("var selected = GetImageSearchSourcesFromUi();", sourcesBlock);
        Assert.Contains("\"settings.image-search-sources\"", sourcesBlock);
        Assert.Contains("value => _settingsService.Settings.ImageSearchSources = value", sourcesBlock);
        Assert.Contains("RestoreImageSearchSourceChecks", sourcesBlock);

        var helperBlock = GetMethodBlock(actionsCode, "private void UpdateImageSearchPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("UpdateImageSearchSourceSummary();", helperBlock);
        Assert.Contains("CancelImageSearchWork();", helperBlock);
        Assert.Contains("ApplyImageSearchFilter();", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressImageSearchSourceEvents = true;", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("_suppressImageSearchSourceEvents = false;", helperBlock);
        Assert.Contains("HistorySearchStatusText.Text = $\"{label} change was not saved. Previous setting restored.\";", helperBlock);
        Assert.Contains("ToastWindow.ShowError(", helperBlock);
        Assert.Contains("The previous search setting was restored. Check Settings -> History and try again.", helperBlock);

        var restoreBlock = GetMethodBlock(actionsCode, "private void RestoreImageSearchSourceChecks(ImageSearchSourceOptions sources)");
        Assert.Contains("ImageSearchFileNameCheck.IsChecked = (sources & ImageSearchSourceOptions.FileName) != 0;", restoreBlock);
        Assert.Contains("ImageSearchOcrCheck.IsChecked = (sources & ImageSearchSourceOptions.Ocr) != 0;", restoreBlock);
    }

    [Fact]
    public void ImageSearchFilterMenuAndIndexActionHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var actionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        AssertNamedControlHasLabel(xaml, "ImageSearchFileNameCheck", "<MenuItem", "Search file names", "Search screenshot file names", "Include screenshot file names in History search results.");
        AssertNamedControlHasLabel(xaml, "ImageSearchOcrCheck", "<MenuItem", "Search OCR text", "Search recognized screenshot text", "Include recognized text from indexed screenshots in History search results.");
        AssertNamedControlHasLabel(xaml, "ImageSearchExactMatchCheck", "<MenuItem", "Exact match search", "Require exact phrase/token matches", "Only show History search results that match the exact phrase or token.");

        var actionBlock = GetMethodBlock(actionsCode, "private void UpdateImageSearchActionButtons()");
        Assert.Contains("UpdateReindexAllButtonLabel(status, \"Image search indexing is already running.\");", actionBlock);
        Assert.Contains("UpdateReindexAllButtonLabel(\"Refresh image search index\", \"Refresh the image search index for all screenshot history items.\");", actionBlock);
        Assert.Contains("UpdateReindexAllButtonLabel(\"Index remaining screenshots\", $\"Index {total - indexed} screenshots for History search.\");", actionBlock);
        Assert.Contains("UpdateReindexAllButtonLabel(\"Image search index complete\", \"All visible screenshot history items are indexed.\");", actionBlock);

        var labelBlock = GetMethodBlock(actionsCode, "private void UpdateReindexAllButtonLabel(string automationName, string helpText)");
        Assert.Contains("ReindexAllBtn.ToolTip = helpText;", labelBlock);
        Assert.Contains("AutomationProperties.SetName(ReindexAllBtn, automationName);", labelBlock);
        Assert.Contains("AutomationProperties.SetHelpText(ReindexAllBtn, helpText);", labelBlock);
    }

    [Fact]
    public void HistorySearchInputMetadataTracksCategory()
    {
        var actionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        var block = GetMethodBlock(actionsCode, "private void UpdateImageSearchPlaceholderText()");
        Assert.Contains("placeholder = \"Search text captures\";", block);
        Assert.Contains("automationName = \"Text history search\";", block);
        Assert.Contains("helpText = \"Search saved OCR text captures.\";", block);
        Assert.Contains("placeholder = \"Search hex, RGB, or color names\";", block);
        Assert.Contains("automationName = \"Color history search\";", block);
        Assert.Contains("helpText = \"Search saved colors by hex value, RGB values, or color names.\";", block);
        Assert.Contains("placeholder = \"Search QR/barcode text, links, or formats\";", block);
        Assert.Contains("automationName = \"Code history search\";", block);
        Assert.Contains("helpText = \"Search saved QR and barcode text, links, or code formats.\";", block);
        Assert.Contains("placeholder = isIndexing", block);
        Assert.Contains("automationName = \"Screenshot history search\";", block);
        Assert.Contains("ImageSearchBox.ToolTip = helpText;", block);
        Assert.Contains("AutomationProperties.SetName(ImageSearchBox, automationName);", block);
        Assert.Contains("AutomationProperties.SetHelpText(ImageSearchBox, helpText);", block);
    }

    [Fact]
    public void VideoThumbnailGenerationDrainsAndLogsFfmpegFailures()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        Assert.Contains("private const int VideoThumbnailDiagnosticMaxLength = 220;", mediaHistoryCode);

        var createBlock = GetMethodBlock(mediaHistoryCode, "private static async Task<bool> TryCreateVideoThumbnailAsync(string ffmpeg, string videoPath, string thumbPath, string arguments)");
        Assert.Contains("RedirectStandardError = true", createBlock);
        Assert.Contains("var stderrTask = proc.StandardError.ReadToEndAsync();", createBlock);
        Assert.Contains("await proc.WaitForExitAsync();", createBlock);
        Assert.Contains("var stderr = await stderrTask;", createBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", createBlock);
        Assert.Contains("\"history.video-thumb.ffmpeg\"", createBlock);
        Assert.Contains("exitCode={proc.ExitCode}", createBlock);
        Assert.Contains("TrimThumbnailDiagnostic(stderr)", createBlock);
        Assert.Contains("catch (Exception ex)", createBlock);
        Assert.Contains("Failed to run ffmpeg", createBlock);
        Assert.Contains("TryDeleteVideoThumbnailFile(thumbPath, \"previous ffmpeg output\");", createBlock);

        var trimBlock = GetMethodBlock(mediaHistoryCode, "private static string TrimThumbnailDiagnostic(string? message)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(message))", trimBlock);
        Assert.Contains("message.ReplaceLineEndings(\" \").Trim();", trimBlock);
        Assert.Contains("VideoThumbnailDiagnosticMaxLength", trimBlock);
    }

    [Fact]
    public void VideoThumbnailGenerationRejectsBlankFallbacksAndLogsDeleteFailures()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var ensureBlock = GetMethodBlock(mediaHistoryCode, "private static async Task<string> EnsureVideoThumbnailAsync(string videoPath, string thumbPath)");
        Assert.Contains("TryDeleteVideoThumbnailFile(thumbPath, \"stale video thumbnail\");", ensureBlock);
        Assert.Contains("var usableThumbnail = IsUsableVideoThumbnail(thumbPath);", ensureBlock);
        Assert.Contains("if (!usableThumbnail)", ensureBlock);
        Assert.Contains("TryDeleteVideoThumbnailFile(thumbPath, \"unusable video thumbnail\");", ensureBlock);
        Assert.Contains("var result = usableThumbnail ? thumbPath : videoPath;", ensureBlock);
        Assert.Contains("RememberFailedVideoThumbnail(videoPath);", ensureBlock);
        Assert.Contains("catch (Exception ex)", ensureBlock);

        var deleteBlock = GetMethodBlock(mediaHistoryCode, "private static void TryDeleteVideoThumbnailFile(string thumbPath, string reason)");
        Assert.Contains("File.Delete(thumbPath);", deleteBlock);
        Assert.Contains("catch (Exception ex)", deleteBlock);
        Assert.Contains("\"history.video-thumb.delete\"", deleteBlock);
        Assert.Contains("Failed to delete {reason}", deleteBlock);
    }

    [Fact]
    public void VideoThumbnailGenerationRejectsUnreadableCachedThumbnails()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var mediaHelpersCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHelpers.cs"));

        var ensureBlock = GetMethodBlock(mediaHistoryCode, "private static async Task<string> EnsureVideoThumbnailAsync(string videoPath, string thumbPath)");
        Assert.Contains("if (IsUsableVideoThumbnail(thumbPath))", ensureBlock);
        Assert.DoesNotContain("File.Exists(thumbPath) && !IsLikelyBlankVideoThumbnail(thumbPath)", ensureBlock);

        var createBlock = GetMethodBlock(mediaHistoryCode, "private static async Task<bool> TryCreateVideoThumbnailAsync(string ffmpeg, string videoPath, string thumbPath, string arguments)");
        Assert.Contains("proc.ExitCode == 0 && IsUsableVideoThumbnail(thumbPath)", createBlock);

        var usableBlock = GetMethodBlock(mediaHistoryCode, "private static bool IsUsableVideoThumbnail(string thumbPath)");
        Assert.Contains("File.Exists(thumbPath) && !IsLikelyBlankVideoThumbnail(thumbPath)", usableBlock);

        var blankBlock = GetMethodBlock(mediaHistoryCode, "private static bool IsLikelyBlankVideoThumbnail(string thumbPath)");
        Assert.Contains("catch (Exception ex)", blankBlock);
        Assert.Contains("\"history.video-thumb.read\"", blankBlock);
        Assert.Contains("Rejecting unreadable video thumbnail", blankBlock);
        Assert.Contains("return true;", blankBlock);

        var cachedPathBlock = GetMethodBlock(mediaHelpersCode, "private static string? GetExistingCachedThumbnailPath(string thumbPath, string sourcePath, HistoryKind kind)");
        Assert.Contains("if (IsUsableVideoThumbnail(thumbPath))", cachedPathBlock);
        Assert.Contains("TryDeleteVideoThumbnailFile(thumbPath, \"cached unusable video thumbnail\");", cachedPathBlock);
        Assert.DoesNotContain("return File.Exists(thumbPath) ? thumbPath : null;", cachedPathBlock);
    }

    [Fact]
    public void VideoThumbnailFailureCacheInvalidatesWhenFileChanges()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        Assert.Contains("Dictionary<string, (long Length, long LastWriteTicks)> FailedVideoThumbnailPaths", mediaHistoryCode);

        var hasFailedBlock = GetMethodBlock(mediaHistoryCode, "private static bool HasFailedVideoThumbnail(string videoPath)");
        Assert.Contains("var signature = GetVideoThumbnailFailureSignature(videoPath);", hasFailedBlock);
        Assert.Contains("FailedVideoThumbnailPaths.TryGetValue(videoPath, out var failedSignature)", hasFailedBlock);
        Assert.Contains("failedSignature == signature", hasFailedBlock);

        var rememberBlock = GetMethodBlock(mediaHistoryCode, "private static void RememberFailedVideoThumbnail(string videoPath)");
        Assert.Contains("var signature = GetVideoThumbnailFailureSignature(videoPath);", rememberBlock);
        Assert.Contains("FailedVideoThumbnailPaths[videoPath] = signature;", rememberBlock);

        var signatureBlock = GetMethodBlock(mediaHistoryCode, "private static (long Length, long LastWriteTicks) GetVideoThumbnailFailureSignature(string videoPath)");
        Assert.Contains("new FileInfo(videoPath)", signatureBlock);
        Assert.Contains("info.Length", signatureBlock);
        Assert.Contains("info.LastWriteTimeUtc.Ticks", signatureBlock);
        Assert.Contains("catch (Exception ex)", signatureBlock);
        Assert.Contains("\"history.video-thumb.signature\"", signatureBlock);
        Assert.Contains("Failed to read video signature", signatureBlock);
        Assert.Contains("return (0, 0);", signatureBlock);
    }

    [Fact]
    public void MediaHistoryMetadataFailuresAreLogged()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var addInfoBlock = GetMethodBlock(mediaHistoryCode, "private static void AddMediaInfo(StackPanel panel, string fileName, string timeAgo, string filePath)");
        Assert.Contains("var sizeStr = TryGetMediaSizeText(filePath);", addInfoBlock);
        Assert.DoesNotContain("catch { }", addInfoBlock);

        var sizeTextBlock = GetMethodBlock(mediaHistoryCode, "private static string TryGetMediaSizeText(string filePath)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))", sizeTextBlock);
        Assert.Contains("return FormatStorageSize(new FileInfo(filePath).Length);", sizeTextBlock);
        Assert.Contains("catch (Exception ex)", sizeTextBlock);
        Assert.Contains("\"history.media-info.size\"", sizeTextBlock);
        Assert.Contains("Failed to read media size", sizeTextBlock);
        Assert.Contains("return \"\";", sizeTextBlock);
    }

    [Fact]
    public void ThumbnailBackgroundLoadersLogFailures()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var loadBlock = GetMethodBlock(mediaHistoryCode, "private static void LoadThumbAsync(System.Windows.Controls.Image img, HistoryItemVM vm, string path, string? sourcePath)");
        Assert.Contains("catch (Exception ex)", loadBlock);
        Assert.Contains("\"history.thumb-load\"", loadBlock);
        Assert.Contains("Failed to load thumbnail", loadBlock);
        Assert.Contains("SettingsMediaCache.EndInflight(cacheKey);", loadBlock);

        var primeBlock = GetMethodBlock(mediaHistoryCode, "private static void PrimeThumbLoad(string cacheKey, string thumbPath, HistoryKind kind, Action<BitmapSource>? onReady = null, Action? onLoaded = null)");
        Assert.Contains("catch (Exception ex)", primeBlock);
        Assert.Contains("\"history.thumb-prime\"", primeBlock);
        Assert.Contains("Failed to prime thumbnail", primeBlock);
        Assert.Contains("SettingsMediaCache.EndInflight(cacheKey);", primeBlock);
    }

    [Fact]
    public void MediaThumbnailPreloadDoesNotTreatPlaceholdersAsLoadedThumbnails()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var primeMediaBlock = GetMethodBlock(mediaHistoryCode, "private static void PrimeMediaThumbnailLoads(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("item.ThumbnailLoaded && item.ThumbnailSource != null && !IsStaleHistoryPlaceholder(item.ThumbnailSource, item.Entry.Kind)", primeMediaBlock);

        var primeVmBlock = GetMethodBlock(mediaHistoryCode, "private static void PrimeThumbLoad(HistoryItemVM vm, Action? onLoaded = null)");
        Assert.Contains("vm.ThumbnailLoaded && vm.ThumbnailSource != null && !IsStaleHistoryPlaceholder(vm.ThumbnailSource, vm.Entry.Kind)", primeVmBlock);
        Assert.Contains("ApplyThumbnailToBoundImage(vm, vm.ThumbnailSource, animate: false);", primeVmBlock);
    }

    [Fact]
    public void ThumbnailCachePlaceholdersDoNotBlockRetry()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var loadBlock = GetMethodBlock(mediaHistoryCode, "private static void LoadThumbAsync(System.Windows.Controls.Image img, HistoryItemVM vm, string path, string? sourcePath)");
        Assert.Contains("TryGetThumbFromCache(cacheKey, out var cached) && cached is not null && !IsStaleHistoryPlaceholder(cached, vm.Entry.Kind)", loadBlock);

        var primeBlock = GetMethodBlock(mediaHistoryCode, "private static void PrimeThumbLoad(string cacheKey, string thumbPath, HistoryKind kind, Action<BitmapSource>? onReady = null, Action? onLoaded = null)");
        Assert.Contains("TryGetThumbFromCache(cacheKey, out var cached) && cached is not null && !IsStaleHistoryPlaceholder(cached, kind)", primeBlock);
    }

    [Fact]
    public void VideoThumbnailWarmupLogsBackgroundFailures()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var warmupBlock = GetMethodBlock(mediaHistoryCode, "private static void QueueMissingVideoThumbnailWarmup(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("Task.Run(async () =>", warmupBlock);
        Assert.Contains("try", warmupBlock);
        Assert.Contains("Task.WhenAll(batch);", warmupBlock);
        Assert.Contains("Task.Delay(35);", warmupBlock);
        Assert.Contains("catch (Exception ex)", warmupBlock);
        Assert.Contains("\"history.video-thumb.warmup\"", warmupBlock);
        Assert.Contains("Failed to warm video thumbnails", warmupBlock);
    }

    [Fact]
    public void OrphanVideoThumbnailCleanupLogsFailures()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var queueBlock = GetMethodBlock(mediaHistoryCode, "private void QueueOrphanVideoThumbnailCleanup(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("Task.Run(() =>", queueBlock);
        Assert.Contains("try", queueBlock);
        Assert.Contains("CleanupOrphanVideoThumbnails(snapshot);", queueBlock);
        Assert.Contains("catch (Exception ex)", queueBlock);
        Assert.Contains("\"history.video-thumb.cleanup\"", queueBlock);
        Assert.Contains("Failed to clean orphan video thumbnails", queueBlock);

        var cleanupBlock = GetMethodBlock(mediaHistoryCode, "private void CleanupOrphanVideoThumbnails(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("File.Delete(thumb);", cleanupBlock);
        Assert.Contains("catch (Exception ex)", cleanupBlock);
        Assert.Contains("\"history.video-thumb.cleanup\"", cleanupBlock);
        Assert.Contains("Failed to delete orphan video thumbnail", cleanupBlock);
    }

    [Fact]
    public void ThumbnailCacheReadWriteFailuresAreLogged()
    {
        var mediaHelpersCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHelpers.cs"));

        var loadCachedBlock = GetMethodBlock(mediaHelpersCode, "private static bool TryLoadCachedThumbnailSource(string cacheKey, string thumbPath, string? sourcePath, HistoryKind kind, out BitmapSource? image)");
        Assert.Contains("\"history.thumb-cache.read\"", loadCachedBlock);
        Assert.Contains("TryDeleteThumbnailCacheFile(diskPath);", loadCachedBlock);

        var loadOrCreateBlock = GetMethodBlock(mediaHelpersCode, "private static BitmapSource? LoadOrCreateThumbnailSource(string loadPath, string sourcePath, HistoryKind kind)");
        Assert.Contains("\"history.thumb-cache.read\"", loadOrCreateBlock);
        Assert.Contains("TryDeleteThumbnailCacheFile(persistentPath);", loadOrCreateBlock);

        var pathBlock = GetMethodBlock(mediaHelpersCode, "private static string? GetPersistentThumbnailPath(string sourcePath, HistoryKind kind)");
        Assert.Contains("catch (Exception ex)", pathBlock);
        Assert.Contains("\"history.thumb-cache.path\"", pathBlock);

        var saveBlock = GetMethodBlock(mediaHelpersCode, "private static void SavePersistentThumbnail(BitmapSource bitmap, string sourcePath, HistoryKind kind)");
        Assert.Contains("catch (Exception ex)", saveBlock);
        Assert.Contains("\"history.thumb-cache.save\"", saveBlock);

        var deleteBlock = GetMethodBlock(mediaHelpersCode, "private static void TryDeleteThumbnailCacheFile(string thumbPath)");
        Assert.Contains("File.Delete(thumbPath);", deleteBlock);
        Assert.Contains("catch (Exception ex)", deleteBlock);
        Assert.Contains("\"history.thumb-cache.delete\"", deleteBlock);
    }

    [Fact]
    public void HistorySearchAndUploadFilterCountsUseSpecificWording()
    {
        var searchMethod = typeof(SettingsWindow).GetMethod("FormatImageSearchMatchCountText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(searchMethod);
        Assert.Equal("1 search match · 4 KB", searchMethod.Invoke(null, new object[] { 1, false, "4 KB" }));
        Assert.Equal("2 search matches · 8 KB", searchMethod.Invoke(null, new object[] { 2, false, "8 KB" }));
        Assert.Equal("1 search/filter match · 4 KB", searchMethod.Invoke(null, new object[] { 1, true, "4 KB" }));
        Assert.Equal("2 search/filter matches · 8 KB", searchMethod.Invoke(null, new object[] { 2, true, "8 KB" }));

        var filterMethod = typeof(SettingsWindow).GetMethod("FormatImageUploadFilterCountText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(filterMethod);
        Assert.Equal("0 of 1 capture shown by filter · 0 B", filterMethod.Invoke(null, new object[] { 0, 1, "0 B" }));
        Assert.Equal("3 of 9 captures shown by filter · 12 KB", filterMethod.Invoke(null, new object[] { 3, 9, "12 KB" }));

        var searchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));

        var immediateBlock = GetMethodBlock(searchCode, "private void ApplyImmediateImageFilter(string query, ImageSearchSourceOptions sources, bool exactMatch)");
        Assert.Contains("else if (usingSearch)", immediateBlock);
        Assert.Contains("FormatImageSearchMatchCountText(_filteredHistoryItems.Count, uploadFilterActive, sizeStr)", immediateBlock);
        Assert.Contains("else if (uploadFilterActive)", immediateBlock);
        Assert.Contains("FormatImageUploadFilterCountText(_filteredHistoryItems.Count, totalCount, sizeStr)", immediateBlock);

        var indexedBlock = GetMethodBlock(searchCode, "private async Task ApplyIndexedImageSearchAsync(int version, string query, ImageSearchSourceOptions sources, CancellationToken cancellationToken)");
        Assert.Contains("FormatImageSearchMatchCountText(_filteredHistoryItems.Count, uploadFilterActive, sizeStr)", indexedBlock);
        Assert.DoesNotContain("of {totalCount} capture", indexedBlock);
    }

    [Fact]
    public void HistoryMediaAndStickerCountsUseFilterAwareVisibleSize()
    {
        var method = typeof(SettingsWindow).GetMethod("FormatFileBackedHistoryCountText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal("2 video/GIFs · 30 KB", method.Invoke(null, new object[] { 2, 2, "video/GIF", "video/GIFs", "30 KB", false }));
        Assert.Equal("1 sticker · 8 KB", method.Invoke(null, new object[] { 1, 1, "sticker", "stickers", "8 KB", false }));
        Assert.Equal("0 of 1 sticker shown by filter · 0 B", method.Invoke(null, new object[] { 0, 1, "sticker", "stickers", "0 B", true }));
        Assert.Equal("3 of 9 video/GIFs shown by filter · 12 KB", method.Invoke(null, new object[] { 3, 9, "video/GIF", "video/GIFs", "12 KB", true }));

        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var mediaLoadBlock = GetMethodBlock(mediaHistoryCode, "private void LoadMediaHistory()");
        Assert.Contains("foreach (var e in _filteredGifItems)", mediaLoadBlock);
        Assert.Contains("FormatFileBackedHistoryCountText(", mediaLoadBlock);
        Assert.Contains("IsHistoryUploadFilterActive()", mediaLoadBlock);

        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var stickerLoadBlock = GetMethodBlock(historyCode, "private void LoadStickerHistory()");
        Assert.Contains("foreach (var item in _filteredStickerItems)", stickerLoadBlock);
        Assert.Contains("visibleBytes += item.Entry.FileSizeBytes > 0 ? item.Entry.FileSizeBytes : TryGetFileLength(item.Entry.FilePath);", stickerLoadBlock);
        Assert.Contains("FormatFileBackedHistoryCountText(", stickerLoadBlock);
        Assert.Contains("IsHistoryUploadFilterActive()", stickerLoadBlock);
        Assert.DoesNotContain("foreach (var e in entries)\r\n            totalBytes", stickerLoadBlock);
        Assert.DoesNotContain("foreach (var e in entries)\n            totalBytes", stickerLoadBlock);
    }

    [Fact]
    public void HistoryMediaAndStickerEmptyStatesNameUploadFilters()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var mediaLoadBlock = GetMethodBlock(mediaHistoryCode, "private void LoadMediaHistory()");
        Assert.Contains("ShowHistoryEmptyState(\"No videos or GIFs match the upload filter\", \"Upload filters matched 0 saved media items.\");", mediaLoadBlock);
        Assert.DoesNotContain("No videos or GIFs match this filter", mediaLoadBlock);

        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var stickerLoadBlock = GetMethodBlock(historyCode, "private void LoadStickerHistory()");
        Assert.Contains("ShowHistoryEmptyState(\"No stickers match the upload filter\", \"Upload filters matched 0 saved stickers.\");", stickerLoadBlock);
        Assert.DoesNotContain("No stickers match this filter", stickerLoadBlock);
    }

    [Fact]
    public void HistorySearchInputFailuresKeepFailureStatusVisible()
    {
        var actionCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        var textChangedBlock = GetMethodBlock(actionCode, "private void ImageSearchBox_TextChanged(object sender, TextChangedEventArgs e)");
        AssertSearchFailureStatusWrittenAfterLoadingStops(textChangedBlock);

        var keyDownBlock = GetMethodBlock(actionCode, "private void ImageSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)");
        AssertSearchFailureStatusWrittenAfterLoadingStops(keyDownBlock);
    }

    [Fact]
    public void ImageSearchDispatcherFailuresKeepFailureStatusVisible()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var indexServiceCode = File.ReadAllText(RepoPath("src", "OddSnap", "Services", "ImageSearchIndexService.Indexing.cs"));

        var indexChangedBlock = GetMethodBlock(settingsCode, "private void ImageSearchIndexService_Changed()");
        AssertSearchCallbackFailureStopsLoadingThenSetsStatus(indexChangedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-search-index-changed\", ex);", indexChangedBlock);

        var statusChangedBlock = GetMethodBlock(settingsCode, "private void ImageSearchIndexService_StatusChanged(string status)");
        AssertSearchCallbackFailureStopsLoadingThenSetsStatus(statusChangedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.image-search-status\", ex);", statusChangedBlock);

        var syncLoopBlock = GetMethodBlock(indexServiceCode, "private async Task RunSyncLoopSafelyAsync(CancellationToken cancellationToken)");
        Assert.Contains("AppDiagnostics.LogError(\"image-search.indexing\", ex);", syncLoopBlock);
        Assert.Contains("SetStatus(\"Indexing failed. Existing search data is still available.\");", syncLoopBlock);
        Assert.DoesNotContain("SetStatus($\"Indexing failed: {ex.Message}\");", syncLoopBlock);
    }

    [Fact]
    public void HistoryFileBackedCardActionsReportMissingFiles()
    {
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));

        Assert.Contains("private static void ShowHistoryFileMissingError(string? filePath = null)", historyCode);
        Assert.Contains("Path.GetFileName(filePath)", historyCode);
        Assert.Contains("Restore the file or capture it again from History.", historyCode);
        Assert.Contains("ToastWindow.ShowError(\"File missing\", $\"{detail}\\nRestore the file or capture it again from History.\", filePath);", historyCode);
        Assert.Contains("if (!File.Exists(vm.Entry.FilePath))", historyCode);
        Assert.Contains("ShowHistoryFileMissingError(vm.Entry.FilePath);", historyCode);
        Assert.Contains("Open History and copy the visible upload link manually.", historyCode);
        Assert.Contains("Try again from Settings -> History, or open the saved screenshot manually.", historyCode);
        Assert.Contains("vm.Entry.FilePath);", historyCode);

        Assert.Contains("if (!File.Exists(filePath))", mediaHistoryCode);
        Assert.Contains("ShowHistoryFileMissingError(filePath);", mediaHistoryCode);
        Assert.Contains("Try again from Settings -> History, or open the saved GIF manually.", mediaHistoryCode);
        Assert.Contains("Try again from Settings -> History, or open the saved video manually.", mediaHistoryCode);
        Assert.Contains("Open History and copy the visible upload link manually.", mediaHistoryCode);
        Assert.Contains("filePath);", mediaHistoryCode);

        Assert.Contains("private static bool HasHistoryFilePath(string? path)", mediaCardCode);
        Assert.Contains("if (HasHistoryFilePath(vm.Entry.FilePath))", mediaCardCode);
        Assert.Contains("CreateCardActionMenuItem(\"Show in folder\"", mediaCardCode);
        Assert.Contains("if (IsDraggableFile(vm.Entry.FilePath))", mediaCardCode);

        var showInFolderBlock = GetMethodBlock(mediaCardCode, "private static bool ShowFileInFolder(string filePath)");
        Assert.Contains("if (!File.Exists(filePath))", showInFolderBlock);
        Assert.Contains("ShowHistoryFileMissingError(filePath);", showInFolderBlock);
        Assert.Contains("return false;", showInFolderBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", showInFolderBlock);
        Assert.Contains("if (process is null)", showInFolderBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the file location. Try again from Settings -> History, or open the folder manually.\", filePath);", showInFolderBlock);
        Assert.Contains("return true;", showInFolderBlock);
        Assert.Contains("catch (Exception ex)", showInFolderBlock);
        Assert.Contains("OddSnap could not open the file location. Try again from Settings -> History, or open the folder manually.", showInFolderBlock);
        Assert.Contains("filePath);", showInFolderBlock);

        var openBlock = GetMethodBlock(mediaCardCode, "private static bool OpenFileWithDefaultApp(string filePath)");
        Assert.Contains("if (!File.Exists(filePath))", openBlock);
        Assert.Contains("ShowHistoryFileMissingError(filePath);", openBlock);
        Assert.Contains("return false;", openBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", openBlock);
        Assert.Contains("if (process is null)", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the saved file. Try again from Settings -> History, or open it from disk manually.\", filePath);", openBlock);
        Assert.Contains("return true;", openBlock);
    }

    [Fact]
    public void HistoryCardOpenUrlReportsFailures()
    {
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));

        Assert.Contains("OpenExternal(vm.Entry.UploadUrl!);", mediaCardCode);

        var openExternalBlock = GetMethodBlock(mediaCardCode, "private static bool OpenExternal(string target)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(target))", openExternalBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No URL is available for this history item.\");", openExternalBlock);
        Assert.Contains("return false;", openExternalBlock);
        Assert.Contains("Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri)", openExternalBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", openExternalBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"The upload URL is not a valid web link.\");", openExternalBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", openExternalBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", openExternalBlock);
        Assert.Contains("if (process is null)", openExternalBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the upload URL. Copy the link from Settings -> History and open it manually.\");", openExternalBlock);
        Assert.Contains("return true;", openExternalBlock);
        Assert.DoesNotContain("FileName = target", openExternalBlock);
        Assert.Contains("catch (Exception ex)", openExternalBlock);
        Assert.Contains("OddSnap could not open the upload URL. Copy the link from Settings -> History and open it manually.", openExternalBlock);
    }

    [Fact]
    public void HistoryCodeCopyReportsClipboardFailures()
    {
        var codeHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.CodeHistory.cs"));
        var cardBlock = GetMethodBlock(codeHistoryCode, "private Border CreateCodeHistoryCard(CodeHistoryEntry entry)");

        Assert.Contains("copyBtn.Click += (_, _) =>", cardBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(capturedText);", cardBlock);
        Assert.Contains("ToastWindow.Show(\"Copied\", \"Text copied\");", cardBlock);
        Assert.Contains("catch (Exception ex)", cardBlock);
        Assert.Contains("OddSnap could not copy this QR/barcode history item. Try again from Settings -> History, or copy the visible decoded value manually.", cardBlock);
        Assert.Contains("void CopyCodeText()", cardBlock);

        var copyButtonIndex = cardBlock.IndexOf("copyBtn.Click += (_, _) =>", StringComparison.Ordinal);
        var cardClickIndex = cardBlock.IndexOf("card.MouseLeftButtonDown += (_, e) =>", StringComparison.Ordinal);
        Assert.True(cardClickIndex > copyButtonIndex, "Code card should keep separate copy button and card click handlers.");

        Assert.Contains("copyBtn.Click += (_, _) => CopyCodeText();", cardBlock);
        Assert.Contains("CopyCodeText();", cardBlock[cardClickIndex..]);
    }

    [Fact]
    public void HistoryCodeUrlOpenValidatesExternalTargets()
    {
        var codeHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.CodeHistory.cs"));
        var cardBlock = GetMethodBlock(codeHistoryCode, "private Border CreateCodeHistoryCard(CodeHistoryEntry entry)");

        var normalizeBlock = GetMethodBlock(codeHistoryCode, "private static bool TryNormalizeUrl(string text, out string url)");
        Assert.Contains("uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps", normalizeBlock);
        Assert.Contains("Uri.TryCreate(\"https://\" + trimmed, UriKind.Absolute, out var withScheme)", normalizeBlock);

        var openBlock = GetMethodBlock(codeHistoryCode, "private static bool TryOpenExternalUrl(string url)");
        Assert.Contains("openBtn.Click += (_, _) => TryOpenExternalUrl(url);", cardBlock);
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No code URL is available.\");", openBlock);
        Assert.Contains("return false;", openBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", openBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"The code URL is not a valid web link.\");", openBlock);
        Assert.Contains("using var process = Process.Start(new ProcessStartInfo", openBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", openBlock);
        Assert.Contains("if (process is null)", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the code URL. Copy it from Settings -> History and open it manually.\");", openBlock);
        Assert.Contains("return true;", openBlock);
        Assert.DoesNotContain("FileName = url", openBlock);
        Assert.Contains("OddSnap could not open the code URL. Copy it from Settings -> History and open it manually.", openBlock);
    }

    [Fact]
    public void HistoryTextAndColorCopyReportClipboardFailures()
    {
        var textColorHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.TextColorHistory.cs"));

        var ocrBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateOcrHistoryCard(OcrHistoryEntry entry)");
        Assert.Contains("copyBtn.Click += (_, _) =>", ocrBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(capturedText);", ocrBlock);
        Assert.Contains("ToastWindow.Show(\"Copied\", \"Text copied\");", ocrBlock);
        Assert.Contains("catch (Exception ex)", ocrBlock);
        Assert.Contains("OddSnap could not copy this text history item. Try again from Settings -> History, or copy the visible text manually.", ocrBlock);

        var colorBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateColorHistoryCard(ColorHistoryEntry entry)");
        Assert.Contains("TryParseHexColor(entry.Hex, out var r, out var g, out var b)", colorBlock);
        Assert.Contains("var displayHex = FormatColorHexForDisplay(entry.Hex);", colorBlock);
        Assert.Contains("Text = displayHex", colorBlock);
        Assert.DoesNotContain("Text = $\"#{entry.Hex}\"", colorBlock);
        Assert.Contains("AppDiagnostics.LogWarning(", colorBlock);
        Assert.Contains("\"history.color.invalid\"", colorBlock);
        Assert.Contains("System.Windows.Media.Color.FromArgb(0, 0, 0, 0)", colorBlock);
        Assert.Contains("Invalid color", colorBlock);
        Assert.DoesNotContain("Convert.ToByte(entry.Hex[..2], 16)", colorBlock);
        Assert.Contains("copyBtn.Click += (_, _) =>", colorBlock);
        Assert.Contains("card.MouseLeftButtonDown += (_, e) =>", colorBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(capturedHex);", colorBlock);
        Assert.Contains("ToastWindow.Show(\"Copied\", capturedHex);", colorBlock);
        Assert.Contains("catch (Exception ex)", colorBlock);
        Assert.Contains("OddSnap could not copy this color history item. Try again from Settings -> History, or copy the visible color value manually.", colorBlock);
        Assert.Contains("void CopyColorValue()", colorBlock);

        var copyButtonIndex = colorBlock.IndexOf("copyBtn.Click += (_, _) =>", StringComparison.Ordinal);
        var cardClickIndex = colorBlock.IndexOf("card.MouseLeftButtonDown += (_, e) =>", StringComparison.Ordinal);
        Assert.True(cardClickIndex > copyButtonIndex, "Color card should keep separate copy button and card click handlers.");

        Assert.Contains("copyBtn.Click += (_, _) => CopyColorValue();", colorBlock);
        Assert.Contains("CopyColorValue();", colorBlock[cardClickIndex..]);
    }

    [Fact]
    public void HistoryCodeTextAndColorCardsSupportKeyboardActivation()
    {
        var codeHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.CodeHistory.cs"));
        var textColorHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.TextColorHistory.cs"));

        var codeBlock = GetMethodBlock(codeHistoryCode, "private Border CreateCodeHistoryCard(CodeHistoryEntry entry)");
        Assert.Contains("Focusable = true,", codeBlock);
        Assert.Contains("BorderBrush = Brushes.Transparent", codeBlock);
        Assert.Contains("BorderThickness = new Thickness(1)", codeBlock);
        Assert.Contains("AutomationProperties.SetName(card, $\"{formatLabel} history item\");", codeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this QR/barcode text. In select mode, press Enter or Space to select it.\");", codeBlock);
        Assert.Contains("AutomationProperties.SetName(openBtn, \"Open code URL\");", codeBlock);
        Assert.Contains("AutomationProperties.SetName(copyBtn, \"Copy code text\");", codeBlock);
        Assert.Contains("card.BorderBrush = HistoryCardFocusBrush;", codeBlock);
        Assert.Contains("card.GotKeyboardFocus += (_, _) =>", codeBlock);
        Assert.Contains("card.LostKeyboardFocus += (_, _) =>", codeBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", codeBlock);
        Assert.Contains("if (!IsHistoryCardActivationKey(e))", codeBlock);
        Assert.Contains("CopyCodeText();", codeBlock);
        Assert.Contains("ToggleSelection();", codeBlock);

        var ocrBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateOcrHistoryCard(OcrHistoryEntry entry)");
        Assert.Contains("private static readonly System.Windows.Media.Brush HistoryCardFocusBrush", textColorHistoryCode);
        Assert.Contains("Focusable = true,", ocrBlock);
        Assert.Contains("BorderBrush = Brushes.Transparent", ocrBlock);
        Assert.Contains("BorderThickness = new Thickness(1)", ocrBlock);
        Assert.Contains("AutomationProperties.SetName(card, \"Text history item\");", ocrBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this text item. In select mode, press Enter or Space to select it.\");", ocrBlock);
        Assert.Contains("card.BorderBrush = HistoryCardFocusBrush;", ocrBlock);
        Assert.Contains("card.GotKeyboardFocus += (_, _) =>", ocrBlock);
        Assert.Contains("card.LostKeyboardFocus += (_, _) =>", ocrBlock);
        Assert.Contains("UpdateShowMoreTextButtonLabel(showMoreBtn, expanded);", ocrBlock);
        Assert.Contains("AutomationProperties.SetName(copyBtn, \"Copy text history item\");", ocrBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", ocrBlock);
        Assert.Contains("if (!IsHistoryCardActivationKey(e))", ocrBlock);
        Assert.Contains("ToggleSelection();", ocrBlock);
        Assert.Contains("CopyTextHistoryItem();", ocrBlock);

        var showMoreLabelBlock = GetMethodBlock(textColorHistoryCode, "private static void UpdateShowMoreTextButtonLabel(Button button, bool expanded)");
        Assert.Contains("var name = expanded ? \"Show less text\" : \"Show more text\";", showMoreLabelBlock);
        Assert.Contains("button.ToolTip = helpText;", showMoreLabelBlock);
        Assert.Contains("AutomationProperties.SetName(button, name);", showMoreLabelBlock);
        Assert.Contains("AutomationProperties.SetHelpText(button, helpText);", showMoreLabelBlock);

        var colorBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateColorHistoryCard(ColorHistoryEntry entry)");
        Assert.Contains("Focusable = true,", colorBlock);
        Assert.Contains("BorderBrush = Brushes.Transparent", colorBlock);
        Assert.Contains("BorderThickness = new Thickness(1)", colorBlock);
        Assert.Contains("AutomationProperties.SetName(card, $\"Color history item {displayHex}\");", colorBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this color. In select mode, press Enter or Space to select it.\");", colorBlock);
        Assert.Contains("AutomationProperties.SetName(copyBtn, \"Copy color value\");", colorBlock);
        Assert.Contains("card.BorderBrush = HistoryCardFocusBrush;", colorBlock);
        Assert.Contains("card.GotKeyboardFocus += (_, _) =>", colorBlock);
        Assert.Contains("card.LostKeyboardFocus += (_, _) =>", colorBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", colorBlock);
        Assert.Contains("if (!IsHistoryCardActivationKey(e))", colorBlock);
        Assert.Contains("CopyColorValue();", colorBlock);
        Assert.Contains("ToggleSelection();", colorBlock);

        Assert.Contains("private static bool IsHistoryCardActivationKey(KeyEventArgs e)", textColorHistoryCode);
        Assert.Contains("=> e.Key is Key.Enter or Key.Space;", textColorHistoryCode);
    }

    [Fact]
    public void ColorHistoryDisplayAndSearchNormalizePrefixedHex()
    {
        var displayMethod = typeof(SettingsWindow).GetMethod("FormatColorHexForDisplay", BindingFlags.NonPublic | BindingFlags.Static);
        var searchMethod = typeof(SettingsWindow).GetMethod("BuildColorSearchText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(displayMethod);
        Assert.NotNull(searchMethod);

        Assert.Equal("#ffffff", displayMethod.Invoke(null, new object[] { "ffffff" }));
        Assert.Equal("#ffffff", displayMethod.Invoke(null, new object[] { "#ffffff" }));

        var searchText = Assert.IsType<string>(searchMethod.Invoke(null, new object[]
        {
            new ColorHistoryEntry { Hex = "#ffffff", CapturedAt = DateTime.UtcNow }
        }));

        Assert.Contains("#ffffff", searchText);
        Assert.Contains("white", searchText);
        Assert.DoesNotContain("##ffffff", searchText);
    }

    [Fact]
    public void SettingsExternalLinksReportOpenFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var updateCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Updates.cs"));
        var updateOpenBlock = GetMethodBlock(updateCode, "private static bool OpenExternalUrl(string url)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", updateOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No update link is available.\");", updateOpenBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", updateOpenBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", updateOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"The update link is not a valid web link.\");", updateOpenBlock);
        Assert.Contains("try", updateOpenBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", updateOpenBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", updateOpenBlock);
        Assert.Contains("if (process is null)", updateOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the update link. Copy the link from Settings -> Updates and open it manually.\");", updateOpenBlock);
        Assert.Contains("return false;", updateOpenBlock);
        Assert.Contains("return true;", updateOpenBlock);
        Assert.DoesNotContain("FileName = url", updateOpenBlock);
        Assert.Contains("UseShellExecute = true", updateOpenBlock);
        Assert.Contains("catch (Exception ex)", updateOpenBlock);
        Assert.Contains("OddSnap could not open the update link. Copy the link from Settings -> Updates and open it manually.", updateOpenBlock);
        Assert.DoesNotContain("_latestUpdate.DownloadUrl ?? _latestUpdate.ReleaseUrl", updateCode);
        Assert.Contains("var opened = OpenExternalUrl(GetUpdateFallbackUrl(_latestUpdate));", updateCode);
        Assert.Contains("UpdateStatusText.Text = opened ? \"Release opened\" : \"Open release failed\";", updateCode);
        Assert.Contains("Opened the latest setup download so you can install it manually.", updateCode);
        Assert.Contains("Automatic update failed and Windows did not open the fallback download.", updateCode);

        var fallbackBlock = GetMethodBlock(updateCode, "private static string GetUpdateFallbackUrl(UpdateCheckResult update)");
        Assert.Contains("string.IsNullOrWhiteSpace(update.DownloadUrl)", fallbackBlock);
        Assert.Contains("? update.ReleaseUrl", fallbackBlock);
        Assert.Contains(": update.DownloadUrl", fallbackBlock);

        var recordingCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Recording.cs"));
        AssertSupportActionRowKeyboardAccessible(xaml, "KoFiSupportAction", "Open Ko-fi support link", "KoFiSupport_KeyDown");
        AssertSupportActionRowKeyboardAccessible(xaml, "PayPalSupportAction", "Open PayPal support link", "PayPalSupport_KeyDown");
        Assert.Contains("private void KoFiSupport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)", recordingCode);
        Assert.Contains("private void PayPalSupport_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)", recordingCode);
        Assert.Contains("OpenSupportUrlFromKeyboard(\"https://ko-fi.com/T6T71X9ZAM\", e);", recordingCode);
        Assert.Contains("OpenSupportUrlFromKeyboard(\"https://www.paypal.com/paypalme/9KGFX\", e);", recordingCode);
        Assert.Contains("e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space)", recordingCode);
        Assert.Contains("e.Handled = true;", recordingCode);

        var supportOpenBlock = GetMethodBlock(recordingCode, "private static bool OpenSupportUrl(string url)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", supportOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No support link is available.\");", supportOpenBlock);
        Assert.Contains("return false;", supportOpenBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", supportOpenBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", supportOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"The support link is not a valid web link.\");", supportOpenBlock);
        Assert.Contains("try", supportOpenBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", supportOpenBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", supportOpenBlock);
        Assert.Contains("if (process is null)", supportOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the support link. Copy the link from Settings -> About and open it manually.\");", supportOpenBlock);
        Assert.Contains("return true;", supportOpenBlock);
        Assert.DoesNotContain("FileName = url", supportOpenBlock);
        Assert.Contains("UseShellExecute = true", supportOpenBlock);
        Assert.Contains("catch (Exception ex)", supportOpenBlock);
        Assert.Contains("OddSnap could not open the support link. Copy the link from Settings -> About and open it manually.", supportOpenBlock);
    }

    [Fact]
    public void HistoryFileBackedCopyActionsLabelUploadUrlCopies()
    {
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var imageBlock = GetMethodBlock(historyCode, "private Border CreateHistoryCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", imageBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);", imageBlock);
        Assert.Contains("ToastWindow.Show(\"Upload link copied\", vm.Entry.UploadUrl);", imageBlock);

        var gifBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateGifCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", gifBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);", gifBlock);
        Assert.Contains("ToastWindow.Show(\"Upload link copied\", vm.Entry.UploadUrl);", gifBlock);

        var videoBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateVideoCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", videoBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);", videoBlock);
        Assert.Contains("ToastWindow.Show(\"Upload link copied\", vm.Entry.UploadUrl);", videoBlock);

        var copyUrlIndex = videoBlock.IndexOf("ClipboardService.CopyTextToClipboard(vm.Entry.UploadUrl);", StringComparison.Ordinal);
        var fileClipboardIndex = videoBlock.IndexOf("System.Windows.Clipboard.SetFileDropList(files);", StringComparison.Ordinal);
        Assert.True(fileClipboardIndex > copyUrlIndex, "Video card copy should prefer the upload URL before falling back to the local file.");
    }

    [Fact]
    public void HistoryCopyActionsIgnoreWhitespaceUploadUrls()
    {
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var imageBlock = GetMethodBlock(historyCode, "private Border CreateHistoryCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", imageBlock);
        Assert.DoesNotContain("if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))", imageBlock);

        var gifBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateGifCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", gifBlock);
        Assert.DoesNotContain("if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))", gifBlock);

        var videoBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateVideoCard(HistoryItemVM vm)");
        Assert.Contains("if (!string.IsNullOrWhiteSpace(vm.Entry.UploadUrl))", videoBlock);
        Assert.DoesNotContain("if (!string.IsNullOrEmpty(vm.Entry.UploadUrl))", videoBlock);
    }

    [Fact]
    public void HistoryCardDefaultOpenReportsFailures()
    {
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));

        var openBlock = GetMethodBlock(mediaCardCode, "private static bool OpenFileWithDefaultApp(string filePath)");
        Assert.Contains("if (!File.Exists(filePath))", openBlock);
        Assert.Contains("ShowHistoryFileMissingError(filePath);", openBlock);
        Assert.Contains("return false;", openBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", openBlock);
        Assert.Contains("if (process is null)", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the saved file. Try again from Settings -> History, or open it from disk manually.\", filePath);", openBlock);
        Assert.Contains("return true;", openBlock);
        Assert.Contains("catch (Exception ex)", openBlock);
        Assert.Contains("OddSnap could not open the saved file. Try again from Settings -> History, or open it from disk manually.", openBlock);
        Assert.Contains("filePath);", openBlock);
        Assert.DoesNotContain("catch\r\n            {\r\n            }", openBlock);
        Assert.DoesNotContain("Task.Run", openBlock);
    }

    [Fact]
    public void GeneratedHistoryCardsAreKeyboardAccessible()
    {
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var historySearchCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Search.cs"));
        var codeHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.CodeHistory.cs"));
        var textColorHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.TextColorHistory.cs"));

        var mediaBlock = GetMethodBlock(mediaCardCode, "private MediaCardShell BuildMediaCardShell(HistoryItemVM vm, Action copyAction)");
        Assert.Contains("Focusable = true,", mediaBlock);
        Assert.Contains("ToolTip = $\"Open this {kindLabel} history item\"", mediaBlock);
        Assert.Contains("var cardFocusBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 255, 255, 255));", mediaBlock);
        Assert.Contains("BorderThickness = new Thickness(1)", mediaBlock);
        Assert.Contains("AutomationProperties.SetName(card, $\"{kindLabel} history item\");", mediaBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to open this history item. Press Ctrl+C to copy it or its upload link. In select mode, press Enter or Space to select it.\");", mediaBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", mediaBlock);
        Assert.Contains("e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control", mediaBlock);
        Assert.Contains("copyAction();", mediaBlock);
        Assert.Contains("card.GotKeyboardFocus += (_, _) =>", mediaBlock);
        Assert.Contains("card.LostKeyboardFocus += (_, _) =>", mediaBlock);
        Assert.Contains("card.BorderBrush = cardFocusBrush;", mediaBlock);
        Assert.Contains("if (!card.IsKeyboardFocusWithin && !actionMenu.IsOpen)", mediaBlock);
        Assert.Contains("if (!IsHistoryCardActivationKey(e))", mediaBlock);
        Assert.Contains("ActivateCard(e);", mediaBlock);
        Assert.Contains("var kindLabel = GetHistoryKindLabel(vm.Entry.Kind);", mediaBlock);

        var kindLabelBlock = GetMethodBlock(mediaCardCode, "private static string GetHistoryKindLabel(HistoryKind kind)");
        Assert.Contains("HistoryKind.Gif => \"GIF\"", kindLabelBlock);
        Assert.Contains("HistoryKind.Video => \"video\"", kindLabelBlock);
        Assert.Contains("HistoryKind.Sticker => \"sticker\"", kindLabelBlock);

        var mediaInfoBlock = GetMethodBlock(mediaHistoryCode, "private static void AddMediaInfo(StackPanel panel, string fileName, string timeAgo, string filePath)");
        Assert.Contains("ToolTip = fileName", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetName(fileNameBlock, \"Media file name\");", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(fileNameBlock, fileName);", mediaInfoBlock);
        Assert.Contains("ToolTip = $\"Media file size: {sizeStr}\"", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetName(sizeBlock, \"Media file size\");", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(sizeBlock, sizeStr);", mediaInfoBlock);
        Assert.Contains("ToolTip = $\"Captured {timeAgo}\"", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetName(capturedBlock, \"Media capture time\");", mediaInfoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(capturedBlock, timeAgo);", mediaInfoBlock);

        var videoBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateVideoCard(HistoryItemVM vm)");
        Assert.Contains("ToolTip = \"Video media type\"", videoBlock);
        Assert.Contains("AutomationProperties.SetName(playIcon, \"Video play overlay\");", videoBlock);
        Assert.Contains("AutomationProperties.SetHelpText(playIcon, \"This history item is a video. Press Enter or Space to open it.\");", videoBlock);

        var imageHistoryBlock = GetMethodBlock(historyCode, "private Border CreateHistoryCard(HistoryItemVM vm)");
        Assert.Contains("vm.FileNameTextBlock = fileNameBlock;", imageHistoryBlock);
        Assert.Contains("vm.TimeStatusTextBlock = timeStatusBlock;", imageHistoryBlock);
        Assert.Contains("vm.ImageSearchMatchTextBlock = matchBlock;", imageHistoryBlock);
        Assert.Contains("RefreshHistoryCardTextMetadata(vm);", imageHistoryBlock);

        var refreshHistoryTextBlock = GetMethodBlock(historyCode, "private void RefreshHistoryCardTextMetadata(HistoryItemVM vm)");
        Assert.Contains("vm.FileNameTextBlock.ToolTip = vm.Entry.FileName;", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetName(vm.FileNameTextBlock, \"History file name\");", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetHelpText(vm.FileNameTextBlock, vm.Entry.FileName);", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetName(vm.TimeStatusTextBlock, string.IsNullOrWhiteSpace(visibleStatus)", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetHelpText(vm.TimeStatusTextBlock, timeAndStatus);", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetName(vm.ImageSearchMatchTextBlock, \"Image search match\");", refreshHistoryTextBlock);
        Assert.Contains("AutomationProperties.SetHelpText(vm.ImageSearchMatchTextBlock, vm.ImageSearchMatchText);", refreshHistoryTextBlock);
        Assert.Contains("GetHistoryKindLabel(vm.Entry.Kind)", refreshHistoryTextBlock);

        var refreshSearchTextBlock = GetMethodBlock(historySearchCode, "private void RefreshImageSearchTexts(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("HydrateHistoryItemSearchMetadata(item);", refreshSearchTextBlock);
        Assert.Contains("RefreshHistoryCardTextMetadata(item);", refreshSearchTextBlock);

        var getOrCreateImageHistoryBlock = GetMethodBlock(historyCode, "private Border GetOrCreateHistoryCard(HistoryItemVM vm)");
        Assert.Contains("RefreshHistoryCardTextMetadata(vm);", getOrCreateImageHistoryBlock);

        var selectionBadgeBlock = GetMethodBlock(mediaCardCode, "private static Border CreateSelectionBadge(bool isSelected)");
        Assert.Contains("UpdateSelectionBadgeAccessibility(badge, isSelected);", selectionBadgeBlock);

        var selectionBadgeAccessibilityBlock = GetMethodBlock(mediaCardCode, "private static void UpdateSelectionBadgeAccessibility(FrameworkElement badge, bool isSelected)");
        Assert.Contains("badge.ToolTip = isSelected ? \"Selected history item\" : \"History item selection marker\";", selectionBadgeAccessibilityBlock);
        Assert.Contains("AutomationProperties.SetName(badge, isSelected ? \"Selected history item\" : \"History item selection marker\");", selectionBadgeAccessibilityBlock);
        Assert.Contains("\"This history item is selected.\"", selectionBadgeAccessibilityBlock);

        var codeBlock = GetMethodBlock(codeHistoryCode, "private Border CreateCodeHistoryCard(CodeHistoryEntry entry)");
        Assert.Contains("Focusable = true,", codeBlock);
        Assert.Contains("AutomationProperties.SetName(card, $\"{formatLabel} history item\");", codeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this QR/barcode text. In select mode, press Enter or Space to select it.\");", codeBlock);
        Assert.Contains("preview.ToolTip = $\"{formatLabel} preview\";", codeBlock);
        Assert.Contains("AutomationProperties.SetName(preview, $\"{formatLabel} preview\");", codeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(preview, $\"Preview image for this {formatLabel} history item.\");", codeBlock);
        Assert.Contains("primary.ToolTip = entry.Text;", codeBlock);
        Assert.Contains("AutomationProperties.SetName(primary, $\"{formatLabel} text\");", codeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(primary, entry.Text);", codeBlock);
        Assert.Contains("ToolTip = metadataText", codeBlock);
        Assert.Contains("AutomationProperties.SetName(metadata, \"Code metadata\");", codeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(metadata, metadataText);", codeBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", codeBlock);

        var ocrBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateOcrHistoryCard(OcrHistoryEntry entry)");
        Assert.Contains("Focusable = true,", ocrBlock);
        Assert.Contains("AutomationProperties.SetName(card, \"Text history item\");", ocrBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this text item. In select mode, press Enter or Space to select it.\");", ocrBlock);
        Assert.Contains("textBlock.ToolTip = capturedText;", ocrBlock);
        Assert.Contains("AutomationProperties.SetName(textBlock, \"Recognized text\");", ocrBlock);
        Assert.Contains("AutomationProperties.SetHelpText(textBlock, capturedText);", ocrBlock);
        Assert.Contains("ToolTip = $\"Captured {capturedTimeText}\"", ocrBlock);
        Assert.Contains("AutomationProperties.SetName(capturedBlock, \"Text capture time\");", ocrBlock);
        Assert.Contains("AutomationProperties.SetHelpText(capturedBlock, capturedTimeText);", ocrBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", ocrBlock);

        var colorBlock = GetMethodBlock(textColorHistoryCode, "private Border CreateColorHistoryCard(ColorHistoryEntry entry)");
        Assert.Contains("Focusable = true,", colorBlock);
        Assert.Contains("AutomationProperties.SetName(card, $\"Color history item {displayHex}\");", colorBlock);
        Assert.Contains("AutomationProperties.SetHelpText(card, \"Press Enter or Space to copy this color. In select mode, press Enter or Space to select it.\");", colorBlock);
        Assert.Contains("ToolTip = hasValidColor ? $\"Color preview {displayHex}\" : \"Invalid color preview\"", colorBlock);
        Assert.Contains("AutomationProperties.SetName(swatch, $\"Color swatch {displayHex}\");", colorBlock);
        Assert.Contains("AutomationProperties.SetHelpText(swatch, swatchHelpText);", colorBlock);
        Assert.Contains("AutomationProperties.SetName(hexBlock, \"Color hex value\");", colorBlock);
        Assert.Contains("AutomationProperties.SetHelpText(hexBlock, displayHex);", colorBlock);
        Assert.Contains("ToolTip = colorMetadataText", colorBlock);
        Assert.Contains("AutomationProperties.SetName(metadataBlock, \"Color details\");", colorBlock);
        Assert.Contains("AutomationProperties.SetHelpText(metadataBlock, colorMetadataText);", colorBlock);
        Assert.Contains("card.KeyDown += (_, e) =>", colorBlock);

        var sharedSelectionUpdateBlock = GetMethodBlock(textColorHistoryCode, "private void UpdateSelectableCardSelection(Border card, Border badge, bool selected)");
        Assert.Contains("UpdateSelectionBadgeAccessibility(badge, selected);", sharedSelectionUpdateBlock);
    }

    [Fact]
    public void HistoryCardActionMenuButtonIsKeyboardAccessible()
    {
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));
        var mediaBlock = GetMethodBlock(mediaCardCode, "private MediaCardShell BuildMediaCardShell(HistoryItemVM vm, Action copyAction)");

        Assert.Contains("ToolTip = \"Open history item actions\"", mediaBlock);
        Assert.Contains("Focusable = true,", mediaBlock);
        Assert.Contains("BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 255, 255, 255))", mediaBlock);
        Assert.Contains("BorderThickness = new Thickness(1)", mediaBlock);
        Assert.Contains("AutomationProperties.SetName(actionMenuBtn, $\"{kindLabel} actions\");", mediaBlock);
        Assert.Contains("AutomationProperties.SetHelpText(actionMenuBtn, \"Press Enter or Space to open this history item's actions.\");", mediaBlock);
        Assert.Contains("void OpenActionMenu()", mediaBlock);
        Assert.Contains("actionMenuBtn.PreviewMouseLeftButtonUp += (_, e) =>", mediaBlock);
        Assert.Contains("actionMenuBtn.KeyDown += (_, e) =>", mediaBlock);
        Assert.Contains("if (!IsHistoryCardActivationKey(e))", mediaBlock);
        Assert.Contains("actionMenuBtn.GotKeyboardFocus += (_, _) =>", mediaBlock);
        Assert.Contains("actionMenuBtn.LostKeyboardFocus += (_, _) =>", mediaBlock);
        Assert.Contains("actionMenu.Closed += (_, _) =>", mediaBlock);
    }

    [Fact]
    public void HistoryCardActionMenuItemsAreNamed()
    {
        var mediaCardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaCard.cs"));
        var mediaBlock = GetMethodBlock(mediaCardCode, "private MediaCardShell BuildMediaCardShell(HistoryItemVM vm, Action copyAction)");
        var menuItemBlock = GetMethodBlock(mediaCardCode, "private MenuItem CreateCardActionMenuItem(string label, Action action, string? helpText = null)");

        Assert.Contains("GetHistoryCopyMenuHelpText(vm.Entry, kindLabel)", mediaBlock);
        Assert.Contains("GetHistoryOpenUrlMenuHelpText(vm.Entry)", mediaBlock);
        Assert.Contains("var uploadHelpText = GetHistoryUploadMenuHelpText(vm.Entry, uploadInProgress);", mediaBlock);
        Assert.Contains("\"Show this file in File Explorer.\"", mediaBlock);
        Assert.Contains("uploadItem.Header = \"Uploading...\";", mediaBlock);
        Assert.Contains("uploadItem.ToolTip = \"This history item upload is already running.\";", mediaBlock);
        Assert.Contains("AutomationProperties.SetName(uploadItem, \"Uploading history item\");", mediaBlock);
        Assert.Contains("AutomationProperties.SetHelpText(uploadItem, \"This history item upload is already running.\");", mediaBlock);
        Assert.Contains("AutomationProperties.SetHelpText(uploadItem, uploadHelpText);", mediaBlock);
        Assert.Contains("helpText ??= \"Run this history action.\";", menuItemBlock);
        Assert.Contains("ToolTip = helpText", menuItemBlock);
        Assert.Contains("AutomationProperties.SetName(item, label);", menuItemBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", menuItemBlock);
        Assert.Contains("item.Click += (_, e) =>", menuItemBlock);
    }

    [Fact]
    public void HistoryCardBadgesExposeAccessibleLabels()
    {
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));
        var mediaHelpersCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHelpers.cs"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        var gifBlock = GetMethodBlock(mediaHistoryCode, "private Border CreateGifCard(HistoryItemVM vm)");
        Assert.Contains("ToolTip = \"GIF media type\"", gifBlock);
        Assert.Contains("AutomationProperties.SetName(gifBadge, \"GIF media type badge\");", gifBlock);
        Assert.Contains("AutomationProperties.SetHelpText(gifBadge, \"This history item is an animated GIF.\");", gifBlock);

        var providerBadgeBlock = GetMethodBlock(mediaHelpersCode, "private static FrameworkElement? CreateProviderBadge(string? providerOrPath, bool isPath = false)");
        Assert.Contains("var helpText = $\"Uploaded with {providerName}.\";", providerBadgeBlock);
        Assert.Contains("ToolTip = helpText", providerBadgeBlock);
        Assert.Contains("AutomationProperties.SetName(textBadge, $\"{providerName} upload provider badge\");", providerBadgeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(textBadge, helpText);", providerBadgeBlock);
        Assert.Contains("AutomationProperties.SetName(logoBadge, $\"{providerName} upload provider badge\");", providerBadgeBlock);
        Assert.Contains("AutomationProperties.SetHelpText(logoBadge, helpText);", providerBadgeBlock);

        var selectionUpdateBlock = GetMethodBlock(historyCode, "private void UpdateCardSelection(HistoryItemVM vm)");
        Assert.Contains("UpdateSelectionBadgeAccessibility(vm.SelectionBadge, vm.IsSelected);", selectionUpdateBlock);
    }

    [Fact]
    public void HistoryMediaDeleteReloadsOnlyAfterDeleteFlowCompletes()
    {
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));
        var mediaHistoryCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "Media", "SettingsWindow.MediaHistory.cs"));

        var deleteMediaBlock = GetMethodBlock(mediaHistoryCode, "private void DeleteMediaItems(IEnumerable<HistoryItemVM> items)");
        Assert.Contains("var entries = items.Select(item => item.Entry).ToList();", deleteMediaBlock);
        Assert.Contains("_historyService.DeleteEntries(entries);", deleteMediaBlock);
        Assert.DoesNotContain("LoadCurrentHistoryTab();", deleteMediaBlock);

        var deleteAllBlock = GetMethodBlock(historyCode, "private void DeleteAllClick(object sender, RoutedEventArgs e)");
        Assert.Contains("DeleteMediaItems(_allGifItems);", deleteAllBlock);
        Assert.Contains("LoadCurrentHistoryTab();", deleteAllBlock);

        var deleteSelectedBlock = GetMethodBlock(historyCode, "private void DeleteSelectedClick(object sender, RoutedEventArgs e)");
        Assert.Contains("DeleteMediaItems(_filteredGifItems.Where(i => i.IsSelected).ToList());", deleteSelectedBlock);
        Assert.Contains("LoadCurrentHistoryTab();", deleteSelectedBlock);
    }

    [Fact]
    public void HistoryDeleteActionsLeaveDurableStatusOnSuccessAndFailure()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        AssertSettingsActionButton(xaml, "SelectBtn", "Select history items", "Select history items", "ToggleSelectMode");
        AssertSettingsActionButton(xaml, "DeleteAllBtn", "Clear current history tab", "Delete all items in the current history tab", "DeleteAllClick");
        AssertSettingsActionButton(xaml, "DeleteSelectedBtn", "Delete selected history items", "Delete selected history items", "DeleteSelectedClick");

        var deleteAllBlock = GetMethodBlock(historyCode, "private void DeleteAllClick(object sender, RoutedEventArgs e)");
        Assert.Contains("var totalCount = GetCurrentTotalHistoryItemCount();", deleteAllBlock);
        Assert.Contains("var tab = GetCurrentHistoryCategoryLabel(totalCount);", deleteAllBlock);
        Assert.Contains("if (totalCount <= 0)", deleteAllBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"No {tab} to delete.\");", deleteAllBlock);
        Assert.Contains("UpdateHistoryActionButtons();", deleteAllBlock);
        Assert.Contains("if (!ConfirmDeleteAllStep(1, totalCount, tab)) return;", deleteAllBlock);
        Assert.Contains("if (!ConfirmDeleteAllStep(2, totalCount, tab)) return;", deleteAllBlock);
        Assert.Contains("if (!ConfirmDeleteAllStep(3, totalCount, tab)) return;", deleteAllBlock);
        Assert.True(
            deleteAllBlock.IndexOf("CancelImageSearchWork();", StringComparison.Ordinal) >
            deleteAllBlock.IndexOf("if (!ConfirmDeleteAllStep(3, totalCount, tab)) return;", StringComparison.Ordinal),
            "Image search work should only be canceled after all delete confirmations pass.");
        Assert.Contains("SetHistoryDeleteStatus($\"Deleted all {tab}.\");", deleteAllBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.history-delete-all\", ex);", deleteAllBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"Delete failed for {GetCurrentHistoryCategoryLabel(2)}. Refresh History and try again.\");", deleteAllBlock);
        Assert.Contains("OddSnap could not finish deleting {GetCurrentHistoryCategoryLabel(2)}. Refresh History and try again.", deleteAllBlock);

        var deleteSelectedBlock = GetMethodBlock(historyCode, "private void DeleteSelectedClick(object sender, RoutedEventArgs e)");
        Assert.Contains("var selectedCount = GetCurrentSelectedHistoryItemCount();", deleteSelectedBlock);
        Assert.Contains("var selectedLabel = GetCurrentHistoryCategoryLabel(selectedCount);", deleteSelectedBlock);
        Assert.Contains("if (selectedCount <= 0)", deleteSelectedBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"Select {GetCurrentHistoryCategoryLabel(2)} to delete.\");", deleteSelectedBlock);
        Assert.Contains("if (!ConfirmDeleteSelected(selectedCount, selectedLabel))", deleteSelectedBlock);
        Assert.True(
            deleteSelectedBlock.IndexOf("CancelImageSearchWork();", StringComparison.Ordinal) >
            deleteSelectedBlock.IndexOf("if (!ConfirmDeleteSelected(selectedCount, selectedLabel))", StringComparison.Ordinal),
            "Image search work should only be canceled after selected-delete confirmation passes.");
        Assert.True(
            deleteSelectedBlock.IndexOf("_selectMode = false;", StringComparison.Ordinal) >
            deleteSelectedBlock.IndexOf("if (!ConfirmDeleteSelected(selectedCount, selectedLabel))", StringComparison.Ordinal),
            "Selection mode should stay active when selected-delete confirmation is canceled.");
        Assert.Contains("SetHistoryDeleteStatus($\"Deleted {selectedCount} selected {selectedLabel}.\");", deleteSelectedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.history-delete-selected\", ex);", deleteSelectedBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"Delete failed for selected {GetCurrentHistoryCategoryLabel(2)}. Refresh History and try again.\");", deleteSelectedBlock);
        Assert.Contains("OddSnap could not finish deleting the selected {GetCurrentHistoryCategoryLabel(2)}. Refresh History and try again.", deleteSelectedBlock);

        var statusBlock = GetMethodBlock(historyCode, "private void SetHistoryDeleteStatus(string message)");
        Assert.Contains("HistorySearchStatusText.Text = message;", statusBlock);

        var confirmStepBlock = GetMethodBlock(historyCode, "private bool ConfirmDeleteAllStep(int step, int totalCount, string categoryLabel)");
        Assert.Contains("ThemedConfirmDialog.Confirm(this, BuildDeleteAllConfirmationTitle(step, totalCount, categoryLabel), BuildDeleteAllConfirmationMessage(step, totalCount, categoryLabel), \"Delete\", \"Cancel\")", confirmStepBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"Delete canceled. Kept {totalCount} {categoryLabel}.\");", confirmStepBlock);
        Assert.Contains("UpdateHistoryActionButtons();", confirmStepBlock);
        Assert.Contains("return false;", confirmStepBlock);

        var confirmSelectedBlock = GetMethodBlock(historyCode, "private bool ConfirmDeleteSelected(int selectedCount, string categoryLabel)");
        Assert.Contains("ThemedConfirmDialog.Confirm(", confirmSelectedBlock);
        Assert.Contains("$\"Delete {selectedCount} selected {categoryLabel}\"", confirmSelectedBlock);
        Assert.Contains("$\"Delete {selectedCount} selected {categoryLabel}? This cannot be undone.\"", confirmSelectedBlock);
        Assert.Contains("SetHistoryDeleteStatus($\"Delete canceled. Kept {selectedCount} selected {categoryLabel}.\");", confirmSelectedBlock);
        Assert.Contains("UpdateHistoryActionButtons();", confirmSelectedBlock);
        Assert.Contains("return false;", confirmSelectedBlock);

        Assert.Contains("private static string BuildDeleteAllConfirmationTitle(int step, int totalCount, string categoryLabel)", historyCode);
        Assert.Contains("return $\"Delete {totalCount} {categoryLabel} ({step}/3)\";", historyCode);

        var categoryLabelBlock = GetMethodBlock(historyCode, "private string GetCurrentHistoryCategoryLabel(int count)");
        Assert.Contains("0 => count == 1 ? \"screenshot\" : \"screenshots\"", categoryLabelBlock);
        Assert.Contains("1 => count == 1 ? \"text capture\" : \"text captures\"", categoryLabelBlock);
        Assert.Contains("5 => count == 1 ? \"QR/barcode scan\" : \"QR/barcode scans\"", categoryLabelBlock);

        var confirmMessageBlock = GetMethodBlock(historyCode, "private static string BuildDeleteAllConfirmationMessage(int step, int totalCount, string categoryLabel)");
        Assert.Contains("Delete all {totalCount} {categoryLabel} in this history tab?", confirmMessageBlock);
        Assert.Contains("Really delete all {totalCount} {categoryLabel}?", confirmMessageBlock);
        Assert.Contains("This cannot be undone. Delete all {totalCount} {categoryLabel}?", confirmMessageBlock);
    }

    [Fact]
    public void HistorySearchAndFilterControlsAreLabeled()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        AssertNamedControlHasLabel(xaml, "HistoryCategoryCombo", "<ComboBox", "History category", "Choose the history category");
        AssertNamedControlHasLabel(xaml, "HistoryUploadFilterCombo", "<ComboBox", "Upload state filter", "Filter history by upload state");
        AssertNamedControlHasLabel(xaml, "HistoryUploadProviderCombo", "<ComboBox", "Upload provider filter", "Filter history by upload provider");
        AssertNamedControlHasLabel(xaml, "ImageSearchBox", "<TextBox", "History search", "Search the current history category");
        AssertSettingsActionButton(xaml, "ReindexAllBtn", "Refresh image search index", "Refresh the image search index", "ReindexAllBtn_Click");
        AssertSettingsActionButton(xaml, "ImageSearchFiltersBtn", "Image search filters", "Choose image search sources and exact matching", "ImageSearchFiltersBtn_Click");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Images", "Show screenshot history", "Image history", "Show saved screenshot captures.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Text", "Show OCR text history", "Text history", "Show saved OCR text captures.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Videos/GIFs", "Show video and GIF history", "Video and GIF history", "Show saved video recordings and GIF captures.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Colors", "Show color history", "Color history", "Show saved color picks.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Stickers", "Show sticker history", "Sticker history", "Show saved sticker captures.");
        AssertComboBoxItemInNamedComboHasLabel(xaml, "HistoryCategoryCombo", "Codes", "Show QR and barcode history", "Code history", "Show saved QR and barcode scans.");
    }

    [Fact]
    public void HistorySearchFilterPopoverFocusesFirstOption()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var actionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        var filtersBtnTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageSearchFiltersBtn\"", StringComparison.Ordinal), "<Button");
        Assert.Contains("AutomationProperties.HelpText=\"Open search source and exact-match options for History.\"", filtersBtnTag);

        var filtersMenuTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageSearchFiltersMenu\"", StringComparison.Ordinal), "<ContextMenu");
        Assert.Contains("AutomationProperties.Name=\"Image search filter options\"", filtersMenuTag);

        var fileNameItemTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageSearchFileNameCheck\"", StringComparison.Ordinal), "<MenuItem");
        Assert.Contains("AutomationProperties.Name=\"Search file names\"", fileNameItemTag);
        Assert.Contains("AutomationProperties.HelpText=\"Include screenshot file names in History search results.\"", fileNameItemTag);

        var ocrItemTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageSearchOcrCheck\"", StringComparison.Ordinal), "<MenuItem");
        Assert.Contains("AutomationProperties.Name=\"Search OCR text\"", ocrItemTag);
        Assert.Contains("AutomationProperties.HelpText=\"Include recognized text from indexed screenshots in History search results.\"", ocrItemTag);

        var exactMatchItemTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"ImageSearchExactMatchCheck\"", StringComparison.Ordinal), "<MenuItem");
        Assert.Contains("AutomationProperties.Name=\"Exact match search\"", exactMatchItemTag);
        Assert.Contains("AutomationProperties.HelpText=\"Only show History search results that match the exact phrase or token.\"", exactMatchItemTag);

        var clickBlock = GetMethodBlock(actionsCode, "private void ImageSearchFiltersBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("ImageSearchFiltersMenu.PlacementTarget = ImageSearchFiltersBtn;", clickBlock);
        Assert.Contains("ImageSearchFiltersMenu.IsOpen = true;", clickBlock);
        Assert.Contains("_ = Dispatcher.BeginInvoke(() =>", clickBlock);
        Assert.Contains("ImageSearchFileNameCheck.Focus();", clickBlock);
        Assert.Contains("Keyboard.Focus(ImageSearchFileNameCheck);", clickBlock);
    }

    [Fact]
    public void HistoryCountAndStatusTextAreLiveRegions()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));

        var countTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"HistoryCountText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", countTag);
        Assert.Contains("AutomationProperties.Name=\"History item count\"", countTag);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", countTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", countTag);

        var statusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"HistorySearchStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", statusTag);
        Assert.Contains("AutomationProperties.Name=\"History status\"", statusTag);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", statusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", statusTag);
    }

    [Fact]
    public void HistoryReindexRefreshFailuresLeaveDurableStatus()
    {
        var actionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Actions.cs"));

        var reindexBlock = GetMethodBlock(actionsCode, "private void ReindexAllBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("try", reindexBlock);
        Assert.Contains("if (!ReindexAllBtn.IsEnabled)", reindexBlock);
        Assert.Contains("ReindexAllBtn.IsEnabled = false;", reindexBlock);
        Assert.Contains("ReindexAllBtn.Content = \"Starting index...\";", reindexBlock);
        Assert.Contains("ReindexAllProgressPanel.Visibility = Visibility.Visible;", reindexBlock);
        Assert.Contains("ReindexAllProgressBar.Visibility = Visibility.Visible;", reindexBlock);
        Assert.Contains("HistorySearchStatusText.Text = \"Starting image index refresh...\";", reindexBlock);
        Assert.Contains("_imageSearchIndexService.RequestSync(_historyService.ImageEntries, _settingsService.Settings.OcrLanguageTag);", reindexBlock);
        Assert.Contains("UpdateImageSearchStatus();", reindexBlock);
        Assert.Contains("QueueImageIndexRefresh();", reindexBlock);
        Assert.Contains("catch (Exception ex)", reindexBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.history-reindex-refresh\", ex);", reindexBlock);
        Assert.Contains("SetImageSearchLoading(false, forceIndexed: true);", reindexBlock);
        Assert.Contains("HistorySearchStatusText.Text = \"Index refresh failed. Existing search data is still available.\";", reindexBlock);
        Assert.Contains("UpdateImageSearchActionButtons();", reindexBlock);
        Assert.Contains("OddSnap could not refresh the image search index. Existing search data is still available; try again from History.", reindexBlock);
    }

    [Fact]
    public void HistoryToolbarActionsHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var historyCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.cs"));

        AssertSettingsActionButton(xaml, "SelectBtn", "Select history items", "Select history items", "ToggleSelectMode");
        AssertSettingsActionButton(xaml, "DeleteAllBtn", "Clear current history tab", "Delete all items in the current history tab", "DeleteAllClick");
        AssertSettingsActionButton(xaml, "DeleteSelectedBtn", "Delete selected history items", "Delete selected history items", "DeleteSelectedClick");
        AssertNamedControlHasLabel(xaml, "SelectBtn", "<Button", "Select history items", "Select history items", "Select history items");
        AssertNamedControlHasLabel(xaml, "DeleteAllBtn", "<Button", "Clear current history tab", "Delete all items in the current history tab", "Delete all items in the current history tab");
        AssertNamedControlHasLabel(xaml, "DeleteSelectedBtn", "<Button", "Delete selected history items", "Delete selected history items", "Delete selected history items");
        AssertSettingsActionButton(xaml, "ReindexAllBtn", "Refresh image search index", "Refresh the image search index", "ReindexAllBtn_Click");
        AssertSettingsActionButton(xaml, "ImageSearchFiltersBtn", "Image search filters", "Choose image search sources and exact matching", "ImageSearchFiltersBtn_Click");
        AssertSettingsActionButton(xaml, "HistoryEmptyRetryButton", "Retry loading history", "Retry loading history", "HistoryEmptyRetryButton_Click");

        var actionBlock = GetMethodBlock(historyCode, "private void UpdateHistoryActionButtons()");
        Assert.Contains("var categoryLabel = GetCurrentHistoryCategoryLabel(2);", actionBlock);
        Assert.Contains("var totalCategoryLabel = GetCurrentHistoryCategoryLabel(totalCount);", actionBlock);
        Assert.Contains("var selectedCategoryLabel = GetCurrentHistoryCategoryLabel(selectedCount);", actionBlock);
        Assert.Contains("var selectHelp = _selectMode ? $\"Finish selecting {categoryLabel}\" : $\"Select {categoryLabel}\";", actionBlock);
        Assert.Contains("var selectName = _selectMode ? $\"Finish selecting {categoryLabel}\" : $\"Select {categoryLabel}\";", actionBlock);
        Assert.Contains("var deleteAllName = totalCount > 0", actionBlock);
        Assert.Contains("var deleteSelectedName = selectedCount > 0", actionBlock);
        Assert.Contains("$\"Delete all {totalCount} {totalCategoryLabel} in the current history category\"", actionBlock);
        Assert.Contains("$\"Delete {selectedCount} selected {selectedCategoryLabel}\"", actionBlock);
        Assert.Contains("DeleteAllBtn.ToolTip = deleteAllHelp;", actionBlock);
        Assert.Contains("DeleteSelectedBtn.ToolTip = deleteSelectedHelp;", actionBlock);
        Assert.Contains("AutomationProperties.SetName(SelectBtn, selectName);", actionBlock);
        Assert.Contains("AutomationProperties.SetName(DeleteAllBtn, deleteAllName);", actionBlock);
        Assert.Contains("AutomationProperties.SetName(DeleteSelectedBtn, deleteSelectedName);", actionBlock);
        Assert.Contains("AutomationProperties.SetHelpText(SelectBtn, selectHelp);", actionBlock);
        Assert.Contains("AutomationProperties.SetHelpText(DeleteAllBtn, deleteAllHelp);", actionBlock);
        Assert.Contains("AutomationProperties.SetHelpText(DeleteSelectedBtn, deleteSelectedHelp);", actionBlock);
    }

    [Fact]
    public void HistoryUploadFilterOptionsHaveAccessibleLabels()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "History", "SettingsWindow.History.Uploads.cs"));

        AssertComboBoxItemHasLabel(xaml, "All uploads", "Show all upload states", "All uploads", "Show uploaded, not uploaded, and failed history items.");
        AssertComboBoxItemHasLabel(xaml, "Uploaded", "Show uploaded history items", "Uploaded history items", "Show items with a successful upload URL.");
        AssertComboBoxItemHasLabel(xaml, "Not uploaded", "Show history items that have not uploaded", "Not uploaded history items", "Show items without an upload URL or upload error.");
        AssertComboBoxItemHasLabel(xaml, "Failed", "Show history items with upload errors", "Failed upload history items", "Show items with an upload error.");

        var refreshBlock = GetMethodBlock(uploadCode, "private bool RefreshHistoryUploadProviderFilterItems(IEnumerable<HistoryEntry> entries)");
        Assert.Contains("HistoryUploadProviderCombo.Items.Add(CreateHistoryUploadProviderFilterItem(\"All providers\", \"\"));", refreshBlock);
        Assert.Contains("HistoryUploadProviderCombo.Items.Add(CreateHistoryUploadProviderFilterItem(provider, provider));", refreshBlock);

        var itemBlock = GetMethodBlock(uploadCode, "private static ComboBoxItem CreateHistoryUploadProviderFilterItem(string label, string provider)");
        Assert.Contains("ToolTip = helpText", itemBlock);
        Assert.Contains("AutomationProperties.SetName(item, label);", itemBlock);
        Assert.Contains("AutomationProperties.SetHelpText(item, helpText);", itemBlock);
        Assert.Contains("Show history items from every upload provider.", itemBlock);
        Assert.Contains("Show history items uploaded with {provider}.", itemBlock);
    }

    [Fact]
    public void SettingsTestUploadPreventsDuplicateInFlightRuns()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("x:Name=\"TestUploadStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "TestUploadStatusText", "SettingsStatusText");
        var testUploadStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"TestUploadStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("Visibility=\"Collapsed\"", testUploadStatusTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", testUploadStatusTag);
        Assert.Contains("AutomationProperties.Name=\"Test upload status\"", testUploadStatusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", testUploadStatusTag);
        AssertSettingsActionButton(
            xaml,
            "TestUploadBtn",
            "Test upload",
            "Sends a tiny 1x1 pixel image to test your configuration",
            "TestUpload_Click");

        Assert.Contains("private bool _testUploadInProgress;", settingsCode);
        Assert.Contains("if (_testUploadInProgress)", uploadCode);
        Assert.Contains("_testUploadInProgress = true;", uploadCode);
        Assert.Contains("TestUploadBtn.IsEnabled = false;", uploadCode);
        Assert.Contains("SetTestUploadStatus(\"Uploading test image...\");", uploadCode);
        Assert.Contains("_testUploadInProgress = false;", uploadCode);
        Assert.Contains("UpdateTestUploadAvailability();", uploadCode);
        Assert.Contains("System.IO.File.Delete(tempPath);", uploadCode);
        Assert.Contains("AppDiagnostics.LogWarning(\"settings.test-upload-temp-delete\"", uploadCode);
        Assert.DoesNotContain("try { System.IO.File.Delete(tempPath); } catch { }", uploadCode);

        var statusBlock = GetMethodBlock(uploadCode, "private void SetTestUploadStatus(string message)");
        Assert.Contains("TestUploadStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);

        var resetIndex = uploadCode.IndexOf("_testUploadInProgress = false;", StringComparison.Ordinal);
        var refreshIndex = uploadCode.IndexOf("UpdateTestUploadAvailability();", resetIndex, StringComparison.Ordinal);
        Assert.True(refreshIndex > resetIndex, "The upload test button should refresh availability after the in-flight guard is reset.");
    }

    [Fact]
    public void SettingsTestUploadAvailabilityTracksRunnableDestination()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        var visibilityBlock = GetMethodBlock(uploadCode, "private void UpdateUploadSettingsVisibility()");
        Assert.Contains("UpdateTestUploadAvailability(dest);", visibilityBlock);
        Assert.DoesNotContain("TestUploadCard.Visibility = dest != Services.UploadDestination.None", visibilityBlock);

        var availabilityBlock = GetMethodBlock(uploadCode, "private void UpdateTestUploadAvailability(Services.UploadDestination? selectedDestination = null)");
        Assert.Contains("var available = CanTestUploadDestination(dest);", availabilityBlock);
        Assert.Contains("TestUploadCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;", availabilityBlock);
        Assert.Contains("TestUploadBtn.IsEnabled = available && !_testUploadInProgress;", availabilityBlock);
        Assert.Contains("if (!available)", availabilityBlock);
        Assert.Contains("SetTestUploadStatus(string.Empty);", availabilityBlock);

        var canTestBlock = GetMethodBlock(uploadCode, "private bool CanTestUploadDestination(Services.UploadDestination dest)");
        Assert.Contains("Services.UploadDestination.None => false", canTestBlock);
        Assert.Contains("_ when Services.UploadService.IsAiChatDestination(dest) => GetSelectedAiRedirectPanelProvider() != Services.AiChatProvider.None", canTestBlock);
        Assert.Contains("_ => true", canTestBlock);

        var providerChangedBlock = GetMethodBlock(uploadCode, "private void AiRedirectProviderCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("UpdateAiRedirectPanelVisibility();", providerChangedBlock);
        Assert.Contains("UpdateTestUploadAvailability();", providerChangedBlock);

        var clickBlock = GetMethodBlock(uploadCode, "private async void TestUpload_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!CanTestUploadDestination(GetSelectedUploadDest()))", clickBlock);
        Assert.Contains("UpdateTestUploadAvailability();", clickBlock);
    }

    [Fact]
    public void AiRedirectTestActionHasFirstClassStatusAndInFlightGuard()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("x:Name=\"AiRedirectTestCard\"", xaml);
        Assert.Contains("x:Name=\"AiRedirectTestBtn\"", xaml);
        AssertSettingsActionButton(
            xaml,
            "AiRedirectTestBtn",
            "Test AI redirect",
            "Opens the selected AI tool; Google Lens also tests the hosted image upload",
            "AiRedirectTestBtn_Click");
        Assert.Contains("x:Name=\"AiRedirectTestStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "AiRedirectTestStatusText", "SettingsStatusText");
        var aiRedirectStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"AiRedirectTestStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("Visibility=\"Collapsed\"", aiRedirectStatusTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", aiRedirectStatusTag);
        Assert.Contains("AutomationProperties.Name=\"AI redirect test status\"", aiRedirectStatusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", aiRedirectStatusTag);
        Assert.Contains("private bool _aiRedirectTestInProgress;", settingsCode);

        var availabilityBlock = GetMethodBlock(uploadCode, "private void UpdateAiRedirectTestAvailability()");
        Assert.Contains("GetSelectedAiRedirectPanelProvider() != Services.AiChatProvider.None", availabilityBlock);
        Assert.Contains("AiRedirectTestCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;", availabilityBlock);
        Assert.Contains("AiRedirectTestBtn.IsEnabled = available && !_aiRedirectTestInProgress;", availabilityBlock);
        Assert.Contains("SetAiRedirectTestStatus(string.Empty);", availabilityBlock);

        var panelVisibilityBlock = GetMethodBlock(uploadCode, "private void UpdateAiRedirectPanelVisibility()");
        Assert.Contains("UpdateAiRedirectTestAvailability();", panelVisibilityBlock);

        var testBlock = GetMethodBlock(uploadCode, "private async void AiRedirectTestBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (_aiRedirectTestInProgress)", testBlock);
        Assert.Contains("var provider = GetSelectedAiRedirectPanelProvider();", testBlock);
        Assert.Contains("if (provider == Services.AiChatProvider.None)", testBlock);
        Assert.Contains("_aiRedirectTestInProgress = true;", testBlock);
        Assert.Contains("AiRedirectTestBtn.Content = \"Testing...\";", testBlock);
        Assert.Contains("AiRedirectTestBtn.IsEnabled = false;", testBlock);
        Assert.Contains("SetAiRedirectTestStatus(\"Testing AI redirect...\");", testBlock);
        Assert.Contains("provider == Services.AiChatProvider.GoogleLens", testBlock);
        Assert.Contains("CaptureOutputService.SavePng(bmp, tempPath);", testBlock);
        Assert.Contains("var hostDest = GetSelectedAiRedirectPanelUploadDest();", testBlock);
        Assert.Contains("Services.UploadService.UploadAsync(tempPath, hostDest, ActiveUploadSettings);", testBlock);
        Assert.Contains("var opened = TryOpenTestUploadExternalUrl(lensUrl);", testBlock);
        Assert.Contains("SetAiRedirectTestStatus(opened", testBlock);
        Assert.Contains("var providerName = Services.UploadService.GetName(hostDest);", testBlock);
        Assert.Contains("SetAiRedirectTestStatus($\"Google Lens upload failed: {providerName}: {error}\");", testBlock);
        Assert.Contains("ToastWindow.ShowError(\"Google Lens upload failed\", BuildTestUploadFailureToastBody(providerName, error, uploadResult.IsRateLimit));", testBlock);
        Assert.Contains("var startUrl = Services.UploadService.BuildAiChatStartUrl(provider);", testBlock);
        Assert.Contains("var providerName = Services.UploadService.GetAiChatProviderName(provider);", testBlock);
        Assert.Contains("SetAiRedirectTestStatus(\"AI redirect opened.\");", testBlock);
        Assert.Contains("SetAiRedirectTestStatus($\"AI redirect test could not open {providerName}. Check your default browser and try again.\");", testBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-test\", ex);", testBlock);
        Assert.Contains("SetAiRedirectTestStatus($\"{providerName} redirect test failed. Check Settings -> Uploads and try again.\");", testBlock);
        Assert.Contains("ToastWindow.ShowError($\"{providerName} redirect test failed\", BuildAiRedirectTestFailureToastBody(providerName, ex.Message));", testBlock);
        Assert.DoesNotContain("ToastWindow.ShowError($\"{providerName} redirect test failed\", ex.Message);", testBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"settings.ai-redirect-test-temp-delete\"", testBlock);
        Assert.Contains("_aiRedirectTestInProgress = false;", testBlock);
        Assert.Contains("AiRedirectTestBtn.Content = \"Test Redirect\";", testBlock);
        Assert.Contains("UpdateAiRedirectTestAvailability();", testBlock);

        var statusBlock = GetMethodBlock(uploadCode, "private void SetAiRedirectTestStatus(string message)");
        Assert.Contains("AiRedirectTestStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void UploadDestinationChangeRollsBackAndReportsFailures()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("private bool _suppressUploadDestChange;", settingsCode);

        var changeBlock = GetMethodBlock(uploadCode, "private void UploadDestCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUploadDestChange) return;", changeBlock);
        Assert.Contains("var previousDestination = _settingsService.Settings.ImageUploadDestination;", changeBlock);
        Assert.Contains("var previousAiChatUploadDestination = ActiveUploadSettings.AiChatUploadDestination;", changeBlock);
        Assert.Contains("var selectedDestination = GetSelectedUploadDest();", changeBlock);
        Assert.Contains("_settingsService.Settings.ImageUploadDestination = selectedDestination;", changeBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(selectedDestination);", changeBlock);
        Assert.Contains("_settingsService.Save();", changeBlock);
        Assert.Contains("UpdateUploadSettingsVisibility();", changeBlock);
        Assert.Contains("UpdateAiRedirectPanelVisibility();", changeBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upload-destination\", ex);", changeBlock);
        Assert.Contains("_settingsService.Settings.ImageUploadDestination = previousDestination;", changeBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = previousAiChatUploadDestination;", changeBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upload-destination-rollback\", rollbackEx);", changeBlock);
        Assert.Contains("_suppressUploadDestChange = true;", changeBlock);
        Assert.Contains("SelectUploadDestByTag((int)previousDestination);", changeBlock);
        Assert.Contains("_suppressUploadDestChange = false;", changeBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upload-destination-restore\", restoreEx);", changeBlock);
        Assert.Contains("ShowUploadDestinationSaveFailed(ex);", changeBlock);

        var failureBlock = GetMethodBlock(uploadCode, "private void ShowUploadDestinationSaveFailed(Exception ex)");
        Assert.Contains("SetTestUploadStatus(\"Upload destination change was not saved. Previous destination restored.\");", failureBlock);
        Assert.Contains("ToastWindow.ShowError(", failureBlock);
        Assert.Contains("\"Upload destination failed\"", failureBlock);
        Assert.Contains("The previous upload destination was restored. Check Settings -> Uploads and try again.", failureBlock);
    }

    [Fact]
    public void AiRedirectSettingChangesRollBackAndReportFailures()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("private bool _suppressAiRedirectProviderChange;", settingsCode);
        Assert.Contains("private bool _suppressAiRedirectLensUploadDestChange;", settingsCode);
        Assert.Contains("private bool _suppressAiRedirectLensUploadSyncChange;", settingsCode);

        var providerBlock = GetMethodBlock(uploadCode, "private void AiRedirectProviderCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressAiRedirectProviderChange) return;", providerBlock);
        Assert.Contains("var previousProvider = ActiveUploadSettings.AiChatProvider;", providerBlock);
        Assert.Contains("var selectedProvider = GetSelectedAiRedirectPanelProvider();", providerBlock);
        Assert.Contains("ActiveUploadSettings.AiChatProvider = selectedProvider;", providerBlock);
        Assert.Contains("UpdateAiRedirectPanelVisibility();", providerBlock);
        Assert.Contains("_settingsService.Save();", providerBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-provider\", ex);", providerBlock);
        Assert.Contains("ActiveUploadSettings.AiChatProvider = previousProvider;", providerBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-provider-rollback\", rollbackEx);", providerBlock);
        Assert.Contains("_suppressAiRedirectProviderChange = true;", providerBlock);
        Assert.Contains("SelectAiRedirectPanelProviderByValue((int)previousProvider);", providerBlock);
        Assert.Contains("_suppressAiRedirectProviderChange = false;", providerBlock);
        Assert.Contains("ShowAiRedirectProviderSaveFailed(ex);", providerBlock);

        var providerFailureBlock = GetMethodBlock(uploadCode, "private void ShowAiRedirectProviderSaveFailed(Exception ex)");
        Assert.Contains("SetAiRedirectTestStatus(\"AI redirect provider change was not saved. Previous provider restored.\");", providerFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", providerFailureBlock);
        Assert.Contains("\"AI redirect provider failed\"", providerFailureBlock);
        Assert.Contains("The previous AI redirect provider was restored. Check Settings -> Uploads and try again.", providerFailureBlock);

        var lensDestBlock = GetMethodBlock(uploadCode, "private void AiRedirectLensUploadDestPanelCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressAiRedirectLensUploadDestChange) return;", lensDestBlock);
        Assert.Contains("var previousDestination = ActiveUploadSettings.AiChatUploadDestination;", lensDestBlock);
        Assert.Contains("var selectedDestination = GetSelectedAiRedirectPanelUploadDest();", lensDestBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = selectedDestination;", lensDestBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-lens-upload-destination\", ex);", lensDestBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = previousDestination;", lensDestBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-lens-upload-destination-rollback\", rollbackEx);", lensDestBlock);
        Assert.Contains("_suppressAiRedirectLensUploadDestChange = true;", lensDestBlock);
        Assert.Contains("SelectAiRedirectPanelUploadDestByValue((int)previousDestination);", lensDestBlock);
        Assert.Contains("ShowAiRedirectLensUploadServiceSaveFailed(ex);", lensDestBlock);

        var lensDestFailureBlock = GetMethodBlock(uploadCode, "private void ShowAiRedirectLensUploadServiceSaveFailed(Exception ex)");
        Assert.Contains("SetAiRedirectTestStatus(\"Lens upload service change was not saved. Previous upload service restored.\");", lensDestFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", lensDestFailureBlock);
        Assert.Contains("\"Lens upload service failed\"", lensDestFailureBlock);
        Assert.Contains("The previous Lens upload service was restored. Check Settings -> Uploads and try again.", lensDestFailureBlock);

        var lensSyncBlock = GetMethodBlock(uploadCode, "private void AiRedirectLensUploadSyncCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressAiRedirectLensUploadSyncChange) return;", lensSyncBlock);
        Assert.Contains("var previousSynced = ActiveUploadSettings.AiChatUploadDestinationSynced;", lensSyncBlock);
        Assert.Contains("var previousDestination = ActiveUploadSettings.AiChatUploadDestination;", lensSyncBlock);
        Assert.Contains("var selectedSynced = AiRedirectLensUploadSyncCheck.IsChecked == true;", lensSyncBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestinationSynced = selectedSynced;", lensSyncBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = Services.UploadService.NormalizeAiChatUploadDestination(GetSelectedUploadDest());", lensSyncBlock);
        Assert.Contains("UpdateAiRedirectPanelVisibility();", lensSyncBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-lens-upload-sync\", ex);", lensSyncBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestinationSynced = previousSynced;", lensSyncBlock);
        Assert.Contains("ActiveUploadSettings.AiChatUploadDestination = previousDestination;", lensSyncBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-lens-upload-sync-rollback\", rollbackEx);", lensSyncBlock);
        Assert.Contains("_suppressAiRedirectLensUploadSyncChange = true;", lensSyncBlock);
        Assert.Contains("_suppressAiRedirectLensUploadDestChange = true;", lensSyncBlock);
        Assert.Contains("AiRedirectLensUploadSyncCheck.IsChecked = previousSynced;", lensSyncBlock);
        Assert.Contains("SelectAiRedirectPanelUploadDestByValue((int)previousDestination);", lensSyncBlock);
        Assert.Contains("ShowAiRedirectLensUploadSyncSaveFailed(ex);", lensSyncBlock);

        var lensSyncFailureBlock = GetMethodBlock(uploadCode, "private void ShowAiRedirectLensUploadSyncSaveFailed(Exception ex)");
        Assert.Contains("SetAiRedirectTestStatus(\"Lens upload sync change was not saved. Previous sync setting restored.\");", lensSyncFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", lensSyncFailureBlock);
        Assert.Contains("\"Lens upload sync failed\"", lensSyncFailureBlock);
        Assert.Contains("The previous Lens upload sync setting was restored. Check Settings -> Uploads and try again.", lensSyncFailureBlock);
    }

    [Fact]
    public void AiRedirectHotkeyChangesRollBackAndReportFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        AssertNamedControlHasLabel(xaml, "AiRedirectPanelHotkeyBox", "<TextBox", "AI Redirect hotkey", "Press a key combination for AI Redirect");
        AssertSettingsActionButton(xaml, "AiRedirectPanelHotkeyClearBtn", "Clear AI Redirect hotkey", "Clear AI Redirect hotkey", "AiRedirectPanelHotkeyClearBtn_Click");

        var clearBlock = GetMethodBlock(uploadCode, "private void AiRedirectPanelHotkeyClearBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var previousModifiers = _settingsService.Settings.AiRedirectHotkeyModifiers;", clearBlock);
        Assert.Contains("var previousKey = _settingsService.Settings.AiRedirectHotkeyKey;", clearBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyModifiers = 0;", clearBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyKey = 0;", clearBlock);
        Assert.Contains("SetAiRedirectTestStatus(\"AI redirect hotkey cleared.\");", clearBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-hotkey-clear\", ex);", clearBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyModifiers = previousModifiers;", clearBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyKey = previousKey;", clearBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-hotkey-clear-rollback\", rollbackEx);", clearBlock);
        Assert.Contains("AiRedirectPanelHotkeyBox.Text = HotkeyFormatter.Format(previousModifiers, previousKey);", clearBlock);
        Assert.Contains("ShowAiRedirectHotkeySaveFailed(\"clear\", ex);", clearBlock);

        var inputBlock = GetMethodBlock(uploadCode, "private void HandleAiRedirectHotkeyKeyInput(System.Windows.Input.KeyEventArgs e, Key key)");
        Assert.Contains("var previousModifiers = _settingsService.Settings.AiRedirectHotkeyModifiers;", inputBlock);
        Assert.Contains("var previousKey = _settingsService.Settings.AiRedirectHotkeyKey;", inputBlock);
        Assert.Contains("List<(string ToolId, uint Modifiers, uint Key)> clearedConflicts = new();", inputBlock);
        Assert.Contains("clearedConflicts = ClearAiRedirectConflict(modifiers, vk);", inputBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyModifiers = modifiers;", inputBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyKey = vk;", inputBlock);
        Assert.Contains("SetAiRedirectTestStatus(\"AI redirect hotkey saved.\");", inputBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-hotkey\", ex);", inputBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyModifiers = previousModifiers;", inputBlock);
        Assert.Contains("_settingsService.Settings.AiRedirectHotkeyKey = previousKey;", inputBlock);
        Assert.Contains("foreach (var (toolId, oldModifiers, oldKey) in clearedConflicts)", inputBlock);
        Assert.Contains("_settingsService.Settings.SetToolHotkey(toolId, oldModifiers, oldKey);", inputBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.ai-redirect-hotkey-rollback\", rollbackEx);", inputBlock);
        Assert.Contains("ShowAiRedirectHotkeySaveFailed(\"change\", ex);", inputBlock);

        var failureBlock = GetMethodBlock(uploadCode, "private void ShowAiRedirectHotkeySaveFailed(string action, Exception ex)");
        Assert.Contains("SetAiRedirectTestStatus($\"AI redirect hotkey {action} was not saved. Previous hotkey restored.\");", failureBlock);
        Assert.Contains("ToastWindow.ShowError(", failureBlock);
        Assert.Contains("\"AI redirect hotkey failed\"", failureBlock);
        Assert.Contains("The previous AI redirect hotkey was restored. Check Settings -> Uploads and try again.", failureBlock);

        var conflictBlock = GetMethodBlock(uploadCode, "private List<(string ToolId, uint Modifiers, uint Key)> ClearAiRedirectConflict(uint modifiers, uint key)");
        Assert.Contains("var cleared = new List<(string ToolId, uint Modifiers, uint Key)>();", conflictBlock);
        Assert.Contains("cleared.Add((tool.Id, existingModifiers, existingKey));", conflictBlock);
        Assert.Contains("cleared.Add((id, existingModifiers, existingKey));", conflictBlock);
        Assert.Contains("return cleared;", conflictBlock);
        Assert.DoesNotContain("_settingsService.Save();", conflictBlock);
    }

    [Fact]
    public void ToolListHotkeysAndTogglesRollBackAndReportSaveFailures()
    {
        var toolListCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToolListBuilder.cs"));

        Assert.Contains("private static readonly HashSet<StackPanel> RestoringEnabledToolPanels = new();", toolListCode);
        Assert.Contains("AppDiagnostics.LogError(\"settings.tool-hotkey-clear\", ex);", toolListCode);
        Assert.Contains("settingsService.Settings.SetToolHotkey(capturedId, previousMod, previousKey);", toolListCode);
        Assert.Contains("AppDiagnostics.LogError(\"settings.tool-hotkey-clear-rollback\", rollbackEx);", toolListCode);
        Assert.Contains("capturedBox.Text = HotkeyFormatter.Format(previousMod, previousKey);", toolListCode);
        Assert.Contains("ShowToolHotkeySaveFailed(\"clear\", restoredConflict: false, ex);", toolListCode);

        var saveEnabledBlock = GetMethodBlock(toolListCode, "private static void SaveEnabledTools(StackPanel panel, SettingsService svc)");
        Assert.Contains("if (RestoringEnabledToolPanels.Contains(panel))", saveEnabledBlock);
        Assert.Contains("var previous = (svc.Settings.EnabledTools ?? ToolDef.DefaultEnabledIds()).ToList();", saveEnabledBlock);
        Assert.Contains("RestoreEnabledToolChecks(panel, previous);", saveEnabledBlock);
        Assert.Contains("ToastWindow.ShowError(\"Tool required\", \"Keep at least one capture tool enabled.\");", saveEnabledBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.enabled-tools\", ex);", saveEnabledBlock);
        Assert.Contains("svc.Settings.EnabledTools = previous;", saveEnabledBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.enabled-tools-rollback\", rollbackEx);", saveEnabledBlock);
        Assert.Contains("ShowEnabledToolsSaveFailed(ex);", saveEnabledBlock);

        var enabledFailureBlock = GetMethodBlock(toolListCode, "private static void ShowEnabledToolsSaveFailed(Exception ex)");
        Assert.Contains("ToastWindow.ShowError(", enabledFailureBlock);
        Assert.Contains("\"Tool setting failed\"", enabledFailureBlock);
        Assert.Contains("The previous enabled tools were restored. Check Settings -> Tools and try again.", enabledFailureBlock);

        var restoreChecksBlock = GetMethodBlock(toolListCode, "private static void RestoreEnabledToolChecks(StackPanel panel, IReadOnlyCollection<string> enabledIds)");
        Assert.Contains("RestoringEnabledToolPanels.Add(panel);", restoreChecksBlock);
        Assert.Contains("cb.IsChecked = enabledIds.Contains(id);", restoreChecksBlock);
        Assert.Contains("RestoringEnabledToolPanels.Remove(panel);", restoreChecksBlock);

        var hotkeyBlock = GetMethodBlock(toolListCode, "private static void WireHotkeyBox(TextBox box, string toolId, SettingsService svc, FrameworkElement owner, Action? hotkeyChanged)");
        Assert.Contains("var previous = svc.Settings.GetToolHotkey(toolId);", hotkeyBlock);
        Assert.Contains("(uint Modifiers, uint Key)? clearedConflict = null;", hotkeyBlock);
        Assert.Contains("clearedConflict = ClearHotkeyConflict(svc.Settings, conflict);", hotkeyBlock);
        Assert.Contains("svc.Settings.SetToolHotkey(toolId, previous.mod, previous.key);", hotkeyBlock);
        Assert.Contains("RestoreHotkeyConflict(svc.Settings, conflict, clearedConflict);", hotkeyBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.tool-hotkey\", ex);", hotkeyBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.tool-hotkey-rollback\", rollbackEx);", hotkeyBlock);
        Assert.Contains("RestoreHotkeyText();", hotkeyBlock);
        Assert.Contains("ShowToolHotkeySaveFailed(\"change\", clearedConflict.HasValue, ex);", hotkeyBlock);

        var hotkeyFailureBlock = GetMethodBlock(toolListCode, "private static void ShowToolHotkeySaveFailed(string action, bool restoredConflict, Exception ex)");
        Assert.Contains("var conflictCopy = restoredConflict", hotkeyFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", hotkeyFailureBlock);
        Assert.Contains("\"Hotkey failed\"", hotkeyFailureBlock);
        Assert.Contains("The previous hotkey was restored after the failed {action}.", hotkeyFailureBlock);
        Assert.Contains("Any replaced hotkey was restored.", hotkeyFailureBlock);
        Assert.Contains("Check Settings -> Tools and try again.", hotkeyFailureBlock);

        var clearConflictBlock = GetMethodBlock(toolListCode, "private static (uint Modifiers, uint Key) ClearHotkeyConflict(AppSettings settings, HotkeyConflict conflict)");
        Assert.Contains("return previous;", clearConflictBlock);
        Assert.Contains("var old = settings.GetToolHotkey(conflict.ToolId);", clearConflictBlock);
        Assert.Contains("return old;", clearConflictBlock);

        var restoreConflictBlock = GetMethodBlock(toolListCode, "private static void RestoreHotkeyConflict(AppSettings settings, HotkeyConflict conflict, (uint Modifiers, uint Key)? previous)");
        Assert.Contains("if (previous is null)", restoreConflictBlock);
        Assert.Contains("settings.AiRedirectHotkeyModifiers = previous.Value.Modifiers;", restoreConflictBlock);
        Assert.Contains("settings.AiRedirectHotkeyKey = previous.Value.Key;", restoreConflictBlock);
        Assert.Contains("settings.SetToolHotkey(conflict.ToolId, previous.Value.Modifiers, previous.Value.Key);", restoreConflictBlock);
    }

    [Fact]
    public void SetupWizardHotkeysRollBackAndReportSaveFailures()
    {
        var wizardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SetupWizard.xaml.cs"));

        var hotkeyBlock = GetMethodBlock(wizardCode, "private void WireHotkey(TextBox box, string toolId)");
        Assert.Contains("var previous = _settingsService.Settings.GetToolHotkey(toolId);", hotkeyBlock);
        Assert.Contains("_settingsService.Settings.SetToolHotkey(toolId, mod, vk);", hotkeyBlock);
        Assert.Contains("_settingsService.Save();", hotkeyBlock);
        Assert.Contains("AppDiagnostics.LogError(\"setup.tool-hotkey\", ex);", hotkeyBlock);
        Assert.Contains("_settingsService.Settings.SetToolHotkey(toolId, previous.mod, previous.key);", hotkeyBlock);
        Assert.Contains("AppDiagnostics.LogError(\"setup.tool-hotkey-rollback\", rollbackEx);", hotkeyBlock);
        Assert.Contains("box.Text = HotkeyFormatter.Format(previous.mod, previous.key);", hotkeyBlock);
        Assert.Contains("ShowSetupHotkeySaveFailed(ex);", hotkeyBlock);
        Assert.DoesNotContain("Check Settings -> Tools and try again.", hotkeyBlock);
        Assert.Contains("finally", hotkeyBlock);
        Assert.Contains("recording = false;", hotkeyBlock);
        Assert.Contains("Keyboard.ClearFocus();", hotkeyBlock);

        var failureBlock = GetMethodBlock(wizardCode, "private static void ShowSetupHotkeySaveFailed(Exception ex)");
        Assert.Contains("ToastWindow.ShowError(", failureBlock);
        Assert.Contains("\"Hotkey failed\"", failureBlock);
        Assert.Contains("The previous hotkey was restored.", failureBlock);
        Assert.Contains("Try this setup step again", failureBlock);
        Assert.Contains("change it later in Settings -> Tools", failureBlock);
    }

    [Fact]
    public void SetupWizardPageSavesBlockNavigationAndCloseOnFailure()
    {
        var wizardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SetupWizard.xaml.cs"));

        var goToPageBlock = GetMethodBlock(wizardCode, "private void GoToPage(int page)");
        Assert.Contains("if (!SaveCurrentPage())", goToPageBlock);
        Assert.Contains("return;", goToPageBlock);
        Assert.Contains("_page = page;", goToPageBlock);

        var saveBlock = GetMethodBlock(wizardCode, "private bool SaveCurrentPage()");
        Assert.Contains("try", saveBlock);
        Assert.Contains("_settingsService.Save();", saveBlock);
        Assert.Contains("var previousCapture = (", saveBlock);
        Assert.Contains("s.ShowCrosshairGuides = previousCapture.ShowCrosshairGuides;", saveBlock);
        Assert.Contains("s.ShowCaptureMagnifier = previousCapture.ShowCaptureMagnifier;", saveBlock);
        Assert.Contains("s.MuteSounds = previousCapture.MuteSounds;", saveBlock);
        Assert.Contains("s.SaveToFile = previousCapture.SaveToFile;", saveBlock);
        Assert.Contains("s.CaptureImageFormat = previousCapture.CaptureImageFormat;", saveBlock);
        Assert.Contains("s.CaptureMaxLongEdge = previousCapture.CaptureMaxLongEdge;", saveBlock);
        Assert.Contains("LoadDefaults();", saveBlock);
        Assert.Contains("var previousCompleted = s.HasCompletedSetup;", saveBlock);
        Assert.Contains("s.HasCompletedSetup = previousCompleted;", saveBlock);
        Assert.Contains("return true;", saveBlock);
        Assert.Contains("catch (Exception ex)", saveBlock);
        Assert.Contains("ShowSetupSaveFailed(\"setup.save-page\", ex);", saveBlock);
        Assert.Contains("return false;", saveBlock);

        var nextBlock = GetMethodBlock(wizardCode, "private void Next_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!SaveCurrentPage())", nextBlock);
        Assert.Contains("return;", nextBlock);
        Assert.Contains("DialogResult = true;", nextBlock);

        var openSettingsBlock = GetMethodBlock(wizardCode, "private void OpenSettings_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!SaveCurrentPage() || !MarkSetupCompleted())", openSettingsBlock);
        Assert.Contains("return;", openSettingsBlock);
        Assert.Contains("Tag = \"OpenSettings\";", openSettingsBlock);

        var skipBlock = GetMethodBlock(wizardCode, "private void Skip_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!SaveCurrentPage() || !MarkSetupCompleted())", skipBlock);
        Assert.Contains("return;", skipBlock);
        Assert.Contains("DialogResult = false;", skipBlock);

        var completeBlock = GetMethodBlock(wizardCode, "private bool MarkSetupCompleted()");
        Assert.Contains("var previous = _settingsService.Settings.HasCompletedSetup;", completeBlock);
        Assert.Contains("_settingsService.Settings.HasCompletedSetup = true;", completeBlock);
        Assert.Contains("ShowSetupSaveFailed(\"setup.complete\", ex);", completeBlock);
        Assert.Contains("_settingsService.Settings.HasCompletedSetup = previous;", completeBlock);

        var failureBlock = GetMethodBlock(wizardCode, "private static void ShowSetupSaveFailed(string diagnosticKey, Exception ex)");
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", failureBlock);
        Assert.Contains("diagnosticKey switch", failureBlock);
        Assert.Contains("ToastWindow.ShowError(", failureBlock);
        Assert.Contains("\"Setup save failed\"", failureBlock);
        Assert.Contains("\"Setup completion failed\"", failureBlock);
        Assert.Contains("Your setup choices were not saved.", failureBlock);
        Assert.Contains("Previous saved settings were restored.", failureBlock);
        Assert.Contains("Stay on this step and try again", failureBlock);
        Assert.Contains("finish setup later from Settings", failureBlock);
        Assert.Contains("Setup was not marked complete.", failureBlock);
        Assert.Contains("The previous setup status was restored.", failureBlock);
    }

    [Fact]
    public void SetupWizardSaveDirectoryBrowsePersistsOrRestoresSelection()
    {
        var wizardCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SetupWizard.xaml.cs"));

        var browseBlock = GetMethodBlock(wizardCode, "private void BrowseSaveDir_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var previous = _settingsService.Settings.SaveDirectory;", browseBlock);
        Assert.Contains("_settingsService.Settings.SaveDirectory = dlg.SelectedPath;", browseBlock);
        Assert.Contains("_settingsService.Save();", browseBlock);
        Assert.Contains("WizSaveDirText.Text = dlg.SelectedPath;", browseBlock);
        Assert.Contains("AppDiagnostics.LogError(\"setup.save-directory\", ex);", browseBlock);
        Assert.Contains("_settingsService.Settings.SaveDirectory = previous;", browseBlock);
        Assert.Contains("AppDiagnostics.LogError(\"setup.save-directory-rollback\", rollbackEx);", browseBlock);
        Assert.Contains("WizSaveDirText.Text = previous;", browseBlock);
        Assert.Contains("ToastWindow.ShowError(", browseBlock);
        Assert.Contains("\"Save directory failed\"", browseBlock);
        Assert.Contains("The previous save directory was restored. Stay on this setup step and try again.", browseBlock);
    }

    [Fact]
    public void AutoUploadTogglesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("x:Name=\"AutoUploadStatusText\"", xaml);
        AssertNamedTextBlockUsesStyle(xaml, "AutoUploadStatusText", "SettingsStatusText");
        var autoUploadStatusTag = GetOpeningTag(xaml, xaml.IndexOf("x:Name=\"AutoUploadStatusText\"", StringComparison.Ordinal), "<TextBlock");
        Assert.Contains("Visibility=\"Collapsed\"", autoUploadStatusTag);
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", autoUploadStatusTag);
        Assert.Contains("AutomationProperties.Name=\"Auto-upload status\"", autoUploadStatusTag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", autoUploadStatusTag);
        AssertNamedControlHasLabel(xaml, "AutoUploadScreenshotsCheck", "<CheckBox", "Auto-upload screenshots", "Uploads screenshots after capture");
        AssertNamedControlHasLabel(xaml, "AutoUploadGifsCheck", "<CheckBox", "Auto-upload GIFs", "Uploads GIF captures after recording");
        AssertNamedControlHasLabel(xaml, "AutoUploadVideosCheck", "<CheckBox", "Auto-upload videos", "Uploads video captures when recording completes");
        Assert.Contains("private bool _suppressAutoUploadChange;", settingsCode);

        var screenshotsBlock = GetMethodBlock(uploadCode, "private void AutoUploadScreenshotsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateAutoUploadSetting(", screenshotsBlock);
        Assert.Contains("AutoUploadScreenshotsCheck", screenshotsBlock);
        Assert.Contains("\"screenshots\"", screenshotsBlock);

        var gifsBlock = GetMethodBlock(uploadCode, "private void AutoUploadGifsCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateAutoUploadSetting(", gifsBlock);
        Assert.Contains("AutoUploadGifsCheck", gifsBlock);
        Assert.Contains("\"GIFs\"", gifsBlock);
        Assert.Contains("\"gifs\"", gifsBlock);

        var videosBlock = GetMethodBlock(uploadCode, "private void AutoUploadVideosCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateAutoUploadSetting(", videosBlock);
        Assert.Contains("AutoUploadVideosCheck", videosBlock);
        Assert.Contains("\"videos\"", videosBlock);

        var helperBlock = GetMethodBlock(uploadCode, "private void UpdateAutoUploadSetting(System.Windows.Controls.CheckBox checkBox, string label, string diagnosticSuffix, Func<bool> getValue, Action<bool> setValue)");
        Assert.Contains("if (!IsLoaded || _suppressAutoUploadChange) return;", helperBlock);
        Assert.Contains("var previous = getValue();", helperBlock);
        Assert.Contains("var enabled = checkBox.IsChecked == true;", helperBlock);
        Assert.Contains("setValue(enabled);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetAutoUploadStatus(", helperBlock);
        Assert.Contains("enabled", helperBlock);
        Assert.Contains("disabled", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.auto-upload-{diagnosticSuffix}\", ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.auto-upload-{diagnosticSuffix}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressAutoUploadChange = true;", helperBlock);
        Assert.Contains("checkBox.IsChecked = previous;", helperBlock);
        Assert.Contains("_suppressAutoUploadChange = false;", helperBlock);
        Assert.Contains("ShowAutoUploadSaveFailed(label, ex);", helperBlock);

        var saveFailureBlock = GetMethodBlock(uploadCode, "private void ShowAutoUploadSaveFailed(string label, Exception ex)");
        Assert.Contains("SetAutoUploadStatus($\"Auto-upload {label} change was not saved. Previous setting restored.\");", saveFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", saveFailureBlock);
        Assert.Contains("\"Auto-upload setting failed\"", saveFailureBlock);
        Assert.Contains("The {label} auto-upload setting was restored. Check Settings -> Uploads and try again.", saveFailureBlock);

        var statusBlock = GetMethodBlock(uploadCode, "private void SetAutoUploadStatus(string message)");
        Assert.Contains("AutoUploadStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void UploadCredentialFieldsRollBackAndReportSaveFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        Assert.Contains("private bool _suppressUploadFieldChange;", settingsCode);
        AssertNamedControlHasLabel(xaml, "ImgurClientIdBox", "<TextBox", "Imgur client ID", "Optional — has built-in default for anonymous uploads");
        AssertNamedControlHasLabel(xaml, "S3EndpointBox", "<TextBox", "S3 endpoint URL", "e.g. https://s3.us-east-1.amazonaws.com or https://your-id.r2.cloudflarestorage.com");
        AssertNamedControlHasLabel(xaml, "S3BucketBox", "<TextBox", "S3 bucket", "Bucket name for S3-compatible uploads");
        AssertNamedControlHasLabel(xaml, "S3RegionBox", "<TextBox", "S3 region", "Region for S3-compatible uploads, or auto");
        AssertNamedControlHasLabel(xaml, "S3AccessKeyBox", "<TextBox", "S3 access key", "Access key ID for the selected S3-compatible service");
        AssertNamedControlHasLabel(xaml, "S3PublicUrlBox", "<TextBox", "S3 public URL", "Custom domain for public links, e.g. https://cdn.example.com");
        AssertNamedControlHasLabel(xaml, "DropboxPathBox", "<TextBox", "Dropbox path prefix", "Dropbox folder or path prefix for uploads");
        AssertNamedControlHasLabel(xaml, "GoogleDriveFolderBox", "<TextBox", "Google Drive folder ID", "Optional Google Drive folder ID for uploads");
        AssertNamedControlHasLabel(xaml, "OneDriveFolderBox", "<TextBox", "OneDrive folder", "OneDrive folder for uploads");
        AssertNamedControlHasLabel(xaml, "GitHubRepoBox", "<TextBox", "GitHub repository", "GitHub repository in owner/name format");
        AssertNamedControlHasLabel(xaml, "GitHubBranchBox", "<TextBox", "GitHub branch", "GitHub branch used for uploads");
        AssertNamedControlHasLabel(xaml, "ImmichUrlBox", "<TextBox", "Immich base URL", "Immich server base URL");
        AssertNamedControlHasLabel(xaml, "FtpUrlBox", "<TextBox", "FTP URL", "FTP upload URL");
        AssertNamedControlHasLabel(xaml, "FtpUsernameBox", "<TextBox", "FTP username", "Username for FTP uploads");
        AssertNamedControlHasLabel(xaml, "FtpPublicUrlBox", "<TextBox", "FTP public URL base", "Public base URL for FTP uploads");
        AssertNamedControlHasLabel(xaml, "SftpHostBox", "<TextBox", "SFTP host", "SFTP host name or IP address");
        AssertNamedControlHasLabel(xaml, "SftpPortBox", "<TextBox", "SFTP port", "SFTP port number");
        AssertNamedControlHasLabel(xaml, "SftpUsernameBox", "<TextBox", "SFTP username", "Username for SFTP uploads");
        AssertNamedControlHasLabel(xaml, "SftpRemotePathBox", "<TextBox", "SFTP remote path", "Remote folder path for SFTP uploads");
        AssertNamedControlHasLabel(xaml, "SftpPublicUrlBox", "<TextBox", "SFTP public URL base", "Public base URL for SFTP uploads");
        AssertNamedControlHasLabel(xaml, "SftpHostKeyFingerprintBox", "<TextBox", "SFTP host key fingerprint", "Expected SFTP server SHA256 fingerprint");
        AssertNamedControlHasLabel(xaml, "WebDavUrlBox", "<TextBox", "WebDAV URL", "WebDAV upload URL");
        AssertNamedControlHasLabel(xaml, "WebDavUsernameBox", "<TextBox", "WebDAV username", "Username for WebDAV uploads");
        AssertNamedControlHasLabel(xaml, "WebDavPublicUrlBox", "<TextBox", "WebDAV public URL base", "Public base URL for WebDAV uploads");
        AssertNamedControlHasLabel(xaml, "CustomUrlBox", "<TextBox", "Custom upload URL", "Custom HTTP endpoint for uploads");
        AssertNamedControlHasLabel(xaml, "CustomFieldBox", "<TextBox", "Custom file form field", "Multipart form field name for the uploaded file");
        AssertNamedControlHasLabel(xaml, "CustomJsonPathBox", "<TextBox", "Custom response URL JSON path", "Dot-separated path, e.g. data.url or just url");
        AssertNamedControlHasLabel(xaml, "CustomHeadersBox", "<TextBox", "Custom upload headers", "One per line, format: Name: Value");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3EndpointBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3BucketBox", "145", "240");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3RegionBox", "145", "240");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3AccessKeyBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "S3PublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "DropboxPathBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GoogleDriveFolderBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "OneDriveFolderBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GitHubRepoBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "GitHubBranchBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "ImmichUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "FtpPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpHostBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpPortBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpRemotePathBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "SftpHostKeyFingerprintBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavUsernameBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "WebDavPublicUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomUrlBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomFieldBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomJsonPathBox");
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, "CustomHeadersBox", requireHorizontalScroll: false);

        var imgurClientBlock = GetMethodBlock(uploadCode, "private void ImgurClientIdBox_Changed(object sender, TextChangedEventArgs e)");
        Assert.Contains("UpdateUploadTextSetting(", imgurClientBlock);
        Assert.Contains("ImgurClientIdBox", imgurClientBlock);
        Assert.Contains("\"imgur-client-id\"", imgurClientBlock);

        var imgbbKeyBlock = GetMethodBlock(uploadCode, "private void ImgBBKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateUploadPasswordSetting(", imgbbKeyBlock);
        Assert.Contains("ImgBBKeyBox", imgbbKeyBlock);
        Assert.Contains("\"imgbb-key\"", imgbbKeyBlock);

        var s3SecretBlock = GetMethodBlock(uploadCode, "private void S3SecretKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateUploadPasswordSetting(", s3SecretBlock);
        Assert.Contains("S3SecretKeyBox", s3SecretBlock);
        Assert.Contains("\"s3-secret-key\"", s3SecretBlock);

        var customHeadersBlock = GetMethodBlock(uploadCode, "private void CustomHeadersBox_Changed(object sender, TextChangedEventArgs e)");
        Assert.Contains("UpdateUploadTextSetting(", customHeadersBlock);
        Assert.Contains("CustomHeadersBox", customHeadersBlock);
        Assert.Contains("\"custom-headers\"", customHeadersBlock);

        Assert.Contains("FtpPasswordBox_Changed(object sender, RoutedEventArgs e) => UpdateUploadPasswordSetting(FtpPasswordBox", uploadCode);
        Assert.Contains("WebDavUrlBox_Changed(object sender, TextChangedEventArgs e) => UpdateUploadTextSetting(WebDavUrlBox", uploadCode);

        var sftpPortBlock = GetMethodBlock(uploadCode, "private void SftpPortBox_Changed(object sender, TextChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUploadFieldChange) return;", sftpPortBlock);
        Assert.Contains("if (!int.TryParse(SftpPortBox.Text, out var port)) return;", sftpPortBlock);
        Assert.Contains("var previous = ActiveUploadSettings.SftpPort;", sftpPortBlock);
        Assert.Contains("ActiveUploadSettings.SftpPort = port;", sftpPortBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upload-field-sftp-port\", ex);", sftpPortBlock);
        Assert.Contains("ActiveUploadSettings.SftpPort = previous;", sftpPortBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upload-field-sftp-port-rollback\", rollbackEx);", sftpPortBlock);
        Assert.Contains("SftpPortBox.Text = previous.ToString();", sftpPortBlock);
        Assert.Contains("ShowUploadSettingSaveFailed(ex);", sftpPortBlock);

        var textHelperBlock = GetMethodBlock(uploadCode, "private void UpdateUploadTextSetting(System.Windows.Controls.TextBox textBox, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)");
        Assert.Contains("textBox.Text", textHelperBlock);
        Assert.Contains("value => textBox.Text = value", textHelperBlock);

        var passwordHelperBlock = GetMethodBlock(uploadCode, "private void UpdateUploadPasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)");
        Assert.Contains("passwordBox.Password", passwordHelperBlock);
        Assert.Contains("value => passwordBox.Password = value", passwordHelperBlock);

        var helperBlock = GetMethodBlock(uploadCode, "private void UpdateUploadStringSetting(string value, Action<string> restoreControlValue, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)");
        Assert.Contains("if (!IsLoaded || _suppressUploadFieldChange) return;", helperBlock);
        Assert.Contains("var previous = getValue();", helperBlock);
        Assert.Contains("setValue(value);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.upload-field-{diagnosticSuffix}\", ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.upload-field-{diagnosticSuffix}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressUploadFieldChange = true;", helperBlock);
        Assert.Contains("restoreControlValue(previous);", helperBlock);
        Assert.Contains("_suppressUploadFieldChange = false;", helperBlock);
        Assert.Contains("ShowUploadSettingSaveFailed(ex);", helperBlock);

        var failureFeedbackBlock = GetMethodBlock(uploadCode, "private void ShowUploadSettingSaveFailed(Exception ex)");
        Assert.Contains("SetTestUploadStatus(\"Upload setting failed. Previous value restored; check Settings -> Uploads and try again.\");", failureFeedbackBlock);
        Assert.DoesNotContain("SetTestUploadStatus($\"Upload setting failed: change was not saved. {ex.Message}\");", failureFeedbackBlock);
        Assert.Contains("The setting was restored. Check Settings -> Uploads and try again.", failureFeedbackBlock);
        Assert.Contains("ToastWindow.ShowError(", failureFeedbackBlock);
    }

    [Fact]
    public void SettingsTestUploadExternalOpenFailuresStaySeparate()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UploadsAndMisc.cs"));

        var openBlock = GetMethodBlock(uploadCode, "private static bool TryOpenTestUploadExternalUrl(string url)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No test upload link is available.\");", openBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", openBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"The test upload link is not a valid web link.\");", openBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", openBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", openBlock);
        Assert.DoesNotContain("FileName = url", openBlock);
        Assert.Contains("UseShellExecute = true", openBlock);
        Assert.Contains("if (process is null)", openBlock);
        Assert.Contains("Windows did not open the test upload link. Copy it from Settings -> Uploads and open it manually.", openBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"settings.test-upload-external-url-open\"", openBlock);
        Assert.Contains("OddSnap could not open the test upload link. Copy the link from Settings -> Uploads and open it manually.", openBlock);
        Assert.Contains("return false;", openBlock);

        var testUploadBlock = GetMethodBlock(uploadCode, "private async void TestUpload_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.Url)", testUploadBlock);
        Assert.Contains("var opened = TryOpenTestUploadExternalUrl(lensUrl);", testUploadBlock);
        Assert.Contains("SetTestUploadStatus(opened", testUploadBlock);
        Assert.Contains("Test upload succeeded, but Google Lens did not open.", testUploadBlock);
        Assert.Contains("ToastWindow.Show(opened ? \"Google Lens works\" : \"Google Lens upload works\", uploadResult.Url);", testUploadBlock);
        Assert.Contains("var providerName = Services.UploadService.GetName(hostDest);", testUploadBlock);
        Assert.Contains("SetTestUploadStatus($\"Google Lens upload failed: {providerName}: {error}\");", testUploadBlock);
        Assert.Contains("ToastWindow.ShowError(\"Google Lens upload failed\", BuildTestUploadFailureToastBody(providerName, error, uploadResult.IsRateLimit));", testUploadBlock);
        Assert.Contains("if (TryOpenTestUploadExternalUrl(startUrl))", testUploadBlock);
        Assert.Contains("SetTestUploadStatus(\"AI redirect opened.\");", testUploadBlock);
        Assert.Contains("SetTestUploadStatus(\"AI redirect test could not open the provider. Check your default browser and try again.\");", testUploadBlock);
        Assert.Contains("result.Success && !string.IsNullOrWhiteSpace(result.Url)", testUploadBlock);
        Assert.Contains("SetTestUploadStatus(\"Test upload succeeded.\");", testUploadBlock);
        Assert.Contains("var providerName = string.IsNullOrWhiteSpace(result.ProviderName)", testUploadBlock);
        Assert.Contains("SetTestUploadStatus($\"Upload failed: {providerName}: {error}\");", testUploadBlock);
        Assert.Contains("ToastWindow.ShowError(\"Upload failed\", BuildTestUploadFailureToastBody(providerName, error, result.IsRateLimit));", testUploadBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.test-upload\", ex);", testUploadBlock);
        Assert.Contains("SetTestUploadStatus(\"Test upload failed. Check upload settings and try another destination.\");", testUploadBlock);
        Assert.Contains("OddSnap could not complete the test upload. Check Settings -> Uploads or try another destination.", testUploadBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Test upload failed\", ex.Message);", testUploadBlock);

        var errorBlock = GetMethodBlock(uploadCode, "private static string GetUploadResultError(Services.UploadResult result)");
        Assert.Contains("Upload returned no link.", errorBlock);

        var failureBodyBlock = GetMethodBlock(uploadCode, "private static string BuildTestUploadFailureToastBody(string providerName, string error, bool isRateLimit)");
        Assert.Contains("var providerLabel = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", failureBodyBlock);
        Assert.Contains("{providerLabel} may be rate-limiting requests. Try another upload destination or wait before retrying.", failureBodyBlock);
        Assert.Contains("Check Settings -> Uploads for {providerLabel}, then try another upload destination.", failureBodyBlock);
        Assert.Contains("return $\"{providerLabel}: {error}\\n{recovery}\";", failureBodyBlock);

        var aiRedirectFailureBodyBlock = GetMethodBlock(uploadCode, "private static string BuildAiRedirectTestFailureToastBody(string providerName, string details)");
        Assert.Contains("var providerLabel = string.IsNullOrWhiteSpace(providerName) ? \"AI redirect\" : providerName;", aiRedirectFailureBodyBlock);
        Assert.Contains("Check Settings -> Uploads and try again.", aiRedirectFailureBodyBlock);
        Assert.Contains("string.IsNullOrWhiteSpace(details) ? recovery : $\"{recovery}\\n{details}\"", aiRedirectFailureBodyBlock);

        var lensOpenIndex = testUploadBlock.IndexOf("var opened = TryOpenTestUploadExternalUrl(lensUrl);", StringComparison.Ordinal);
        var lensSuccessIndex = testUploadBlock.IndexOf("ToastWindow.Show(opened ? \"Google Lens works\" : \"Google Lens upload works\", uploadResult.Url);", lensOpenIndex, StringComparison.Ordinal);
        var aiOpenIndex = testUploadBlock.IndexOf("if (TryOpenTestUploadExternalUrl(startUrl))", StringComparison.Ordinal);
        var aiSuccessIndex = testUploadBlock.IndexOf("ToastWindow.Show(\"AI redirect works\"", aiOpenIndex, StringComparison.Ordinal);
        Assert.True(lensSuccessIndex > lensOpenIndex, "Google Lens upload success should stay separate from browser-open failure.");
        Assert.True(aiSuccessIndex > aiOpenIndex, "AI redirect success should only be shown after browser-open success.");
    }

    [Fact]
    public void SettingsUpdateDownloadPreventsDuplicateActivation()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var updateCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Updates.cs"));

        AssertUpdateActionButtonHasAccessibleLabel(xaml, "CheckUpdatesButton", "Check for updates", "Check for updates");
        AssertUpdateActionButtonHasAccessibleLabel(xaml, "DownloadUpdateButton", "Open or install update", "Open or install the latest release");
        AssertDynamicStatusTextBlock(xaml, "UpdateStatusText", "Update status", isLive: true);
        AssertDynamicStatusTextBlock(xaml, "UpdateDetailText", "Update details", isLive: false);

        Assert.Contains("private const int UpdateActionCooldownMs = 900;", settingsCode);
        Assert.Contains("private bool _updateActionInProgress;", settingsCode);
        Assert.Contains("if (_latestUpdate is null || _updateActionInProgress)", updateCode);
        Assert.Contains("_updateActionInProgress = true;", updateCode);
        Assert.Contains("DownloadUpdateButton.IsEnabled = false;", updateCode);
        Assert.Contains("catch (Exception ex)", updateCode);
        Assert.Contains("UpdateStatusText.Text = \"Update failed\";", updateCode);
        Assert.Contains("UpdateDetailText.Text = \"Try checking again, or open the latest release manually.\";", updateCode);
        Assert.Contains("OddSnap could not finish the update. Try checking again, or open the release manually from Settings -> Updates.", updateCode);
        Assert.Contains("OddSnap could not check GitHub Releases. Check your connection and try again.", updateCode);
        Assert.Contains("OddSnap could not install the update automatically. OddSnap will try to open the latest setup download.", updateCode);
        Assert.Contains("ResetUpdateActionGuardAfterCooldown();", updateCode);
        Assert.Contains("private void ResetUpdateActionGuardAfterCooldown()", updateCode);
        Assert.Contains("_updateActionInProgress = false;", updateCode);
        Assert.Contains("if (!_updateActionInProgress)", updateCode);
    }

    private static void AssertUpdateActionButtonHasAccessibleLabel(string xaml, string buttonName, string automationName, string toolTip)
    {
        var buttonIndex = xaml.IndexOf($"x:Name=\"{buttonName}\"", StringComparison.Ordinal);
        Assert.True(buttonIndex >= 0, $"Could not find {buttonName}.");

        var tag = GetOpeningTag(xaml, buttonIndex, "<Button");
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"ToolTip=\"{toolTip}\"", tag);
        Assert.Contains("Cursor=\"Hand\"", tag);
    }

    private static void AssertDynamicStatusTextBlock(string xaml, string textBlockName, string automationName, bool isLive)
    {
        var index = xaml.IndexOf($"x:Name=\"{textBlockName}\"", StringComparison.Ordinal);
        Assert.True(index >= 0, $"Could not find {textBlockName}.");

        var tag = GetOpeningTag(xaml, index, "<TextBlock");
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", tag);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        if (isLive)
            Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", tag);
        else
            Assert.DoesNotContain("AutomationProperties.LiveSetting=", tag);
    }

    [Fact]
    public void LocalModelDownloadsDisableRuntimeMutationActions()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Uploads.cs"));

        var stickerModelDownloadState = GetBlockStartingAt(uploadCode, "if (BackgroundRuntimeJobService.TryGetSnapshot(GetStickerModelJobKey(engine)");
        Assert.Contains("StickerInstallDriversBtn.IsEnabled = false;", stickerModelDownloadState);
        Assert.Contains("SetStickerDownloadUi(true, null, modelJob.Status);", stickerModelDownloadState);

        var upscaleModelDownloadState = GetBlockStartingAt(uploadCode, "if (BackgroundRuntimeJobService.TryGetSnapshot(GetUpscaleModelJobKey(engine)");
        Assert.Contains("UpscaleInstallDriversBtn.IsEnabled = false;", upscaleModelDownloadState);
        Assert.Contains("SetUpscaleDownloadUi(true, null, modelJob.Status);", upscaleModelDownloadState);
    }

    [Fact]
    public void LocalModelFailuresShowActionableSummaryAndKeepCopyDetails()
    {
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Uploads.cs"));

        Assert.Contains("var hasModelFailure = !downloaded && modelFailureJob is { LastSucceeded: false };", uploadCode);
        Assert.Contains("runtimeJob is { LastSucceeded: false } &&", uploadCode);
        Assert.Contains("!(hasRuntimeStatus && runtimeReady)", uploadCode);
        Assert.Contains("hasModelFailure ? modelFailureJob : null", uploadCode);
        Assert.Contains("hasRuntimeFailure ? runtimeJob : null", uploadCode);
        Assert.Contains("StickerLocalEngineStatusText.Text = hasModelFailure", uploadCode);
        Assert.Contains("Sticker model download failed. Retry the download, or copy details.", uploadCode);
        Assert.Contains("Sticker runtime setup failed. Retry setup, or copy details.", uploadCode);
        Assert.Contains("StickerCopyErrorBtn.Visibility = string.IsNullOrWhiteSpace(stickerFailure) ? Visibility.Collapsed : Visibility.Visible;", uploadCode);
        Assert.Contains("downloaded ? null : modelJob", uploadCode);
        Assert.Contains("runtimeReady ? null : runtimeJob", uploadCode);

        Assert.Contains("UpscaleLocalEngineStatusText.Text = hasModelFailure", uploadCode);
        Assert.Contains("Upscale model download failed. Retry the download, or copy details.", uploadCode);
        Assert.Contains("Upscale runtime setup failed. Retry setup, or copy details.", uploadCode);
        Assert.Contains("UpscaleCopyErrorBtn.Visibility = string.IsNullOrWhiteSpace(upscaleFailure) ? Visibility.Collapsed : Visibility.Visible;", uploadCode);
        Assert.DoesNotContain("StickerLocalEngineStatusText.Text = $\"Failed: {stickerFailure}\";", uploadCode);
        Assert.DoesNotContain("UpscaleLocalEngineStatusText.Text = $\"Failed: {upscaleFailure}\";", uploadCode);
    }

    [Fact]
    public void LocalModelSelectionSavesRollBackAndReportFailures()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));
        var uploadCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Uploads.cs"));

        Assert.Contains("private bool _suppressStickerSettingChange;", settingsCode);
        Assert.Contains("private bool _suppressUpscaleSettingChange;", settingsCode);

        var stickerProviderBlock = GetMethodBlock(stickerCode, "private void StickerProviderCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", stickerProviderBlock);
        Assert.Contains("var previousProvider = ActiveStickerSettings.Provider;", stickerProviderBlock);
        Assert.Contains("ActiveStickerSettings.Provider = selectedProvider;", stickerProviderBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.sticker-provider\", ex);", stickerProviderBlock);
        Assert.Contains("ActiveStickerSettings.Provider = previousProvider;", stickerProviderBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.sticker-provider-rollback\", rollbackEx);", stickerProviderBlock);
        Assert.Contains("_suppressStickerSettingChange = true;", stickerProviderBlock);
        Assert.Contains("StickerProviderCombo.SelectedIndex = (int)previousProvider;", stickerProviderBlock);
        Assert.Contains("ShowStickerProviderSaveFailed(ex);", stickerProviderBlock);

        var stickerProviderFailureBlock = GetMethodBlock(stickerCode, "private void ShowStickerProviderSaveFailed(Exception ex)");
        Assert.Contains("SetStickerRemovalStatus(\"Sticker provider change was not saved. Previous provider restored.\");", stickerProviderFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", stickerProviderFailureBlock);
        Assert.Contains("\"Sticker provider failed\"", stickerProviderFailureBlock);
        Assert.Contains("The previous sticker provider was restored. Check Settings -> Stickers and try again.", stickerProviderFailureBlock);

        var upscaleProviderBlock = GetMethodBlock(upscaleCode, "private void UpscaleProviderCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", upscaleProviderBlock);
        Assert.Contains("var previousProvider = ActiveUpscaleSettings.Provider;", upscaleProviderBlock);
        Assert.Contains("ActiveUpscaleSettings.Provider = selectedProvider;", upscaleProviderBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-provider\", ex);", upscaleProviderBlock);
        Assert.Contains("ActiveUpscaleSettings.Provider = previousProvider;", upscaleProviderBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-provider-rollback\", rollbackEx);", upscaleProviderBlock);
        Assert.Contains("_suppressUpscaleSettingChange = true;", upscaleProviderBlock);
        Assert.Contains("UpscaleProviderCombo.SelectedIndex = (int)previousProvider;", upscaleProviderBlock);
        Assert.Contains("ShowUpscaleProviderSaveFailed(ex);", upscaleProviderBlock);

        var upscaleProviderFailureBlock = GetMethodBlock(upscaleCode, "private void ShowUpscaleProviderSaveFailed(Exception ex)");
        Assert.Contains("SetUpscaleRemovalStatus(\"Upscale provider change was not saved. Previous provider restored.\");", upscaleProviderFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", upscaleProviderFailureBlock);
        Assert.Contains("\"Upscale provider failed\"", upscaleProviderFailureBlock);
        Assert.Contains("The previous upscale provider was restored. Check Settings -> Upscale and try again.", upscaleProviderFailureBlock);

        var stickerEngineBlock = GetMethodBlock(uploadCode, "private void UpdateLocalEngineUi()");
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange)", stickerEngineBlock);
        Assert.Contains("var previousExecutionProvider = ActiveStickerSettings.LocalExecutionProvider;", stickerEngineBlock);
        Assert.Contains("var previousCpuEngine = ActiveStickerSettings.LocalCpuEngine;", stickerEngineBlock);
        Assert.Contains("var previousGpuEngine = ActiveStickerSettings.LocalGpuEngine;", stickerEngineBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.sticker-local-engine\", ex);", stickerEngineBlock);
        Assert.Contains("ActiveStickerSettings.LocalExecutionProvider = previousExecutionProvider;", stickerEngineBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.sticker-local-engine-rollback\", rollbackEx);", stickerEngineBlock);
        Assert.Contains("StickerLocalExecutionCombo.SelectedIndex = (int)previousExecutionProvider;", stickerEngineBlock);
        Assert.Contains("SelectStickerEngine(StickerLocalCpuEngineCombo, previousCpuEngine);", stickerEngineBlock);
        Assert.Contains("SelectStickerEngine(StickerLocalGpuEngineCombo, previousGpuEngine);", stickerEngineBlock);
        Assert.Contains("SetStickerRemovalStatus(\"Sticker local engine change was not saved. Previous engine restored.\");", stickerEngineBlock);
        Assert.Contains("ToastWindow.ShowError(", stickerEngineBlock);
        Assert.Contains("\"Sticker engine failed\"", stickerEngineBlock);
        Assert.Contains("The previous sticker engine was restored. Check Settings -> Stickers and try again.", stickerEngineBlock);
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", GetMethodBlock(stickerCode, "private void StickerLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)"));
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", GetMethodBlock(stickerCode, "private void StickerLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)"));
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", GetMethodBlock(stickerCode, "private void StickerLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)"));

        var upscaleEngineBlock = GetMethodBlock(uploadCode, "private void UpdateUpscaleLocalEngineUi()");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange)", upscaleEngineBlock);
        Assert.Contains("var previousExecutionProvider = ActiveUpscaleSettings.LocalExecutionProvider;", upscaleEngineBlock);
        Assert.Contains("var previousScaleFactor = ActiveUpscaleSettings.ScaleFactor;", upscaleEngineBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-local-engine\", ex);", upscaleEngineBlock);
        Assert.Contains("ActiveUpscaleSettings.LocalExecutionProvider = previousExecutionProvider;", upscaleEngineBlock);
        Assert.Contains("ActiveUpscaleSettings.ScaleFactor = previousScaleFactor;", upscaleEngineBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-local-engine-rollback\", rollbackEx);", upscaleEngineBlock);
        Assert.Contains("UpscaleLocalExecutionCombo.SelectedIndex = (int)previousExecutionProvider;", upscaleEngineBlock);
        Assert.Contains("SelectUpscaleEngine(UpscaleLocalCpuEngineCombo, previousCpuEngine);", upscaleEngineBlock);
        Assert.Contains("SelectUpscaleEngine(UpscaleLocalGpuEngineCombo, previousGpuEngine);", upscaleEngineBlock);
        Assert.Contains("SetUpscaleRemovalStatus(\"Upscale local engine change was not saved. Previous engine restored.\");", upscaleEngineBlock);
        Assert.Contains("ToastWindow.ShowError(", upscaleEngineBlock);
        Assert.Contains("\"Upscale engine failed\"", upscaleEngineBlock);
        Assert.Contains("The previous upscale engine was restored. Check Settings -> Upscale and try again.", upscaleEngineBlock);
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", GetMethodBlock(upscaleCode, "private void UpscaleLocalCpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)"));
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", GetMethodBlock(upscaleCode, "private void UpscaleLocalGpuEngineCombo_Changed(object sender, SelectionChangedEventArgs e)"));
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", GetMethodBlock(upscaleCode, "private void UpscaleLocalExecutionCombo_Changed(object sender, SelectionChangedEventArgs e)"));

        var stickerExecutionBlock = GetMethodBlock(uploadCode, "private void UpdateStickerExecutionUi()");
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange)", stickerExecutionBlock);

        var upscaleExecutionBlock = GetMethodBlock(uploadCode, "private void UpdateUpscaleExecutionUi()");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange)", upscaleExecutionBlock);
    }

    [Fact]
    public void LocalModelApiAndStyleSettingsRollBackAndReportFailures()
    {
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));
        var upscalePreviewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.UpscalePreview.cs"));

        var stickerShadowBlock = GetMethodBlock(stickerCode, "private void StickerShadowCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateStickerBooleanSetting(", stickerShadowBlock);
        Assert.Contains("StickerShadowCheck", stickerShadowBlock);
        Assert.Contains("\"shadow\"", stickerShadowBlock);

        var stickerStrokeBlock = GetMethodBlock(stickerCode, "private void StickerStrokeCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateStickerBooleanSetting(", stickerStrokeBlock);
        Assert.Contains("StickerStrokeCheck", stickerStrokeBlock);
        Assert.Contains("\"stroke\"", stickerStrokeBlock);

        var stickerRemoveBgBlock = GetMethodBlock(stickerCode, "private void StickerRemoveBgKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateStickerPasswordSetting(", stickerRemoveBgBlock);
        Assert.Contains("StickerRemoveBgKeyBox", stickerRemoveBgBlock);
        Assert.Contains("\"removebg-key\"", stickerRemoveBgBlock);

        var stickerPhotoroomBlock = GetMethodBlock(stickerCode, "private void StickerPhotoroomKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateStickerPasswordSetting(", stickerPhotoroomBlock);
        Assert.Contains("StickerPhotoroomKeyBox", stickerPhotoroomBlock);
        Assert.Contains("\"photoroom-key\"", stickerPhotoroomBlock);

        var stickerBooleanHelperBlock = GetMethodBlock(stickerCode, "private void UpdateStickerBooleanSetting(System.Windows.Controls.CheckBox checkBox, string label, string diagnosticSuffix, Func<bool> getValue, Action<bool> setValue)");
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", stickerBooleanHelperBlock);
        Assert.Contains("var previous = getValue();", stickerBooleanHelperBlock);
        Assert.Contains("setValue(selected);", stickerBooleanHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.sticker-{diagnosticSuffix}\", ex);", stickerBooleanHelperBlock);
        Assert.Contains("setValue(previous);", stickerBooleanHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.sticker-{diagnosticSuffix}-rollback\", rollbackEx);", stickerBooleanHelperBlock);
        Assert.Contains("_suppressStickerSettingChange = true;", stickerBooleanHelperBlock);
        Assert.Contains("checkBox.IsChecked = previous;", stickerBooleanHelperBlock);
        Assert.Contains("ShowStickerSettingSaveFailed(label, ex);", stickerBooleanHelperBlock);

        var stickerPasswordHelperBlock = GetMethodBlock(stickerCode, "private void UpdateStickerPasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string label, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)");
        Assert.Contains("if (!IsLoaded || _suppressStickerSettingChange) return;", stickerPasswordHelperBlock);
        Assert.Contains("setValue(passwordBox.Password);", stickerPasswordHelperBlock);
        Assert.Contains("passwordBox.Password = previous;", stickerPasswordHelperBlock);
        Assert.Contains("ShowStickerSettingSaveFailed(label, ex);", stickerPasswordHelperBlock);

        var stickerSettingFailureBlock = GetMethodBlock(stickerCode, "private void ShowStickerSettingSaveFailed(string label, Exception ex)");
        Assert.Contains("SetStickerRemovalStatus($\"{label} change was not saved. Previous setting restored.\");", stickerSettingFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", stickerSettingFailureBlock);
        Assert.Contains("\"Sticker setting failed\"", stickerSettingFailureBlock);
        Assert.Contains("The previous sticker setting was restored. Check Settings -> Stickers and try again.", stickerSettingFailureBlock);

        var upscaleKeyBlock = GetMethodBlock(upscaleCode, "private void UpscaleDeepAiApiKeyBox_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("UpdateUpscalePasswordSetting(", upscaleKeyBlock);
        Assert.Contains("UpscaleDeepAiApiKeyBox", upscaleKeyBlock);
        Assert.Contains("\"deepai-key\"", upscaleKeyBlock);

        var upscalePasswordHelperBlock = GetMethodBlock(upscaleCode, "private void UpdateUpscalePasswordSetting(System.Windows.Controls.PasswordBox passwordBox, string label, string diagnosticSuffix, Func<string> getValue, Action<string> setValue)");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", upscalePasswordHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.upscale-{diagnosticSuffix}\", ex);", upscalePasswordHelperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"settings.upscale-{diagnosticSuffix}-rollback\", rollbackEx);", upscalePasswordHelperBlock);
        Assert.Contains("passwordBox.Password = previous;", upscalePasswordHelperBlock);
        Assert.Contains("ShowUpscaleSettingSaveFailed(label, ex);", upscalePasswordHelperBlock);

        var scaleBlock = GetMethodBlock(upscaleCode, "private void UpscaleDefaultScaleCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", scaleBlock);
        Assert.Contains("var previousScale = ActiveUpscaleSettings.ScaleFactor;", scaleBlock);
        Assert.Contains("ActiveUpscaleSettings.ScaleFactor = scale;", scaleBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-default-scale\", ex);", scaleBlock);
        Assert.Contains("ActiveUpscaleSettings.ScaleFactor = previousScale;", scaleBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-default-scale-rollback\", rollbackEx);", scaleBlock);
        Assert.Contains("_suppressUpscaleSettingChange = true;", scaleBlock);
        Assert.Contains("UpdateUpscaleDefaultScaleUi(ActiveUpscaleSettings.GetActiveLocalEngine());", scaleBlock);
        Assert.Contains("ShowUpscaleSettingSaveFailed(\"Default scale\", ex);", scaleBlock);

        var previewBlock = GetMethodBlock(upscalePreviewCode, "private void UpscaleShowPreviewWindowCheck_Changed(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressUpscaleSettingChange) return;", previewBlock);
        Assert.Contains("var previous = ActiveUpscaleSettings.ShowPreviewWindow;", previewBlock);
        Assert.Contains("ActiveUpscaleSettings.ShowPreviewWindow = UpscaleShowPreviewWindowCheck.IsChecked == true;", previewBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-preview-window\", ex);", previewBlock);
        Assert.Contains("ActiveUpscaleSettings.ShowPreviewWindow = previous;", previewBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.upscale-preview-window-rollback\", rollbackEx);", previewBlock);
        Assert.Contains("UpscaleShowPreviewWindowCheck.IsChecked = previous;", previewBlock);
        Assert.Contains("ShowUpscaleSettingSaveFailed(\"Preview window\", ex);", previewBlock);

        var upscaleSettingFailureBlock = GetMethodBlock(upscaleCode, "private void ShowUpscaleSettingSaveFailed(string label, Exception ex)");
        Assert.Contains("SetUpscaleRemovalStatus($\"{label} change was not saved. Previous setting restored.\");", upscaleSettingFailureBlock);
        Assert.Contains("ToastWindow.ShowError(", upscaleSettingFailureBlock);
        Assert.Contains("\"Upscale setting failed\"", upscaleSettingFailureBlock);
        Assert.Contains("The previous upscale setting was restored. Check Settings -> Upscale and try again.", upscaleSettingFailureBlock);
    }

    [Fact]
    public void LocalModelProjectLinksGuardAgainstRepeatedActivation()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));

        Assert.Contains("private const int LocalEngineProjectOpenCooldownMs = 900;", settingsCode);
        Assert.Contains("private bool _stickerProjectOpenInProgress;", settingsCode);
        Assert.Contains("private bool _upscaleProjectOpenInProgress;", settingsCode);

        Assert.Contains("if (_stickerProjectOpenInProgress)", stickerCode);
        Assert.Contains("_stickerProjectOpenInProgress = true;", stickerCode);
        Assert.Contains("StickerOpenLocalEngineRepoBtn.IsEnabled = false;", stickerCode);
        Assert.Contains("OpenStickerProjectUrl(LocalStickerEngineService.GetProjectUrl(engine));", stickerCode);
        Assert.Contains("OddSnap could not open the sticker project link. Check Settings -> Stickers and try again.", stickerCode);
        Assert.Contains("ResetStickerProjectOpenGuardAfterCooldown();", stickerCode);
        Assert.Contains("_stickerProjectOpenInProgress = false;", stickerCode);
        Assert.Contains("UpdateLocalEngineUi();", stickerCode);

        Assert.Contains("if (_upscaleProjectOpenInProgress)", upscaleCode);
        Assert.Contains("_upscaleProjectOpenInProgress = true;", upscaleCode);
        Assert.Contains("UpscaleOpenLocalEngineRepoBtn.IsEnabled = false;", upscaleCode);
        Assert.Contains("OpenUpscaleProjectUrl(LocalUpscaleEngineService.GetProjectUrl(engine));", upscaleCode);
        Assert.Contains("OddSnap could not open the upscale project link. Check Settings -> Upscale and try again.", upscaleCode);
        Assert.Contains("ResetUpscaleProjectOpenGuardAfterCooldown();", upscaleCode);
        Assert.Contains("_upscaleProjectOpenInProgress = false;", upscaleCode);
        Assert.Contains("UpdateUpscaleLocalEngineUi();", upscaleCode);

        var stickerOpenBlock = GetMethodBlock(stickerCode, "private static bool OpenStickerProjectUrl(string url)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", stickerOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"No sticker project link is available.\");", stickerOpenBlock);
        Assert.Contains("return false;", stickerOpenBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", stickerOpenBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", stickerOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"The sticker project link is not a valid web link.\");", stickerOpenBlock);
        Assert.Contains("try", stickerOpenBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", stickerOpenBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", stickerOpenBlock);
        Assert.Contains("if (process is null)", stickerOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"Windows did not open the sticker project link. Copy the link from Settings -> Stickers and open it manually.\");", stickerOpenBlock);
        Assert.Contains("return true;", stickerOpenBlock);
        Assert.Contains("catch (Exception ex)", stickerOpenBlock);
        Assert.Contains("Windows could not open the sticker project link. Copy the link from Settings -> Stickers and open it manually.", stickerOpenBlock);
        Assert.DoesNotContain("FileName = LocalStickerEngineService.GetProjectUrl(engine)", stickerOpenBlock);

        var upscaleOpenBlock = GetMethodBlock(upscaleCode, "private static bool OpenUpscaleProjectUrl(string url)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", upscaleOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"No upscale project link is available.\");", upscaleOpenBlock);
        Assert.Contains("return false;", upscaleOpenBlock);
        Assert.Contains("Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)", upscaleOpenBlock);
        Assert.Contains("uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps", upscaleOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"The upscale project link is not a valid web link.\");", upscaleOpenBlock);
        Assert.Contains("try", upscaleOpenBlock);
        Assert.Contains("using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", upscaleOpenBlock);
        Assert.Contains("FileName = uri.AbsoluteUri", upscaleOpenBlock);
        Assert.Contains("if (process is null)", upscaleOpenBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open project failed\", \"Windows did not open the upscale project link. Copy the link from Settings -> Upscale and open it manually.\");", upscaleOpenBlock);
        Assert.Contains("return true;", upscaleOpenBlock);
        Assert.Contains("catch (Exception ex)", upscaleOpenBlock);
        Assert.Contains("Windows could not open the upscale project link. Copy the link from Settings -> Upscale and open it manually.", upscaleOpenBlock);
        Assert.DoesNotContain("FileName = LocalUpscaleEngineService.GetProjectUrl(engine)", upscaleOpenBlock);
    }

    [Fact]
    public void LocalModelCopyErrorButtonsReportClipboardFailures()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml"));
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));

        AssertLocalModelActionRowWraps(xaml, "StickerInstallDriversBtn", "StickerCopyErrorBtn");
        AssertLocalModelActionRowWraps(xaml, "UpscaleInstallDriversBtn", "UpscaleCopyErrorBtn");
        AssertSettingsActionButton(xaml, "StickerInstallDriversBtn", "Install sticker runtime", "Install or remove the local sticker runtime", "StickerInstallDriversBtn_Click");
        AssertSettingsActionButton(xaml, "StickerDownloadRembgBtn", "Download sticker model", "Download the selected local sticker model", "StickerDownloadRembgBtn_Click");
        AssertSettingsActionButton(xaml, "StickerOpenLocalEngineRepoBtn", "Open sticker engine project", "Open the selected sticker engine project", "StickerOpenLocalEngineRepoBtn_Click");
        AssertSettingsActionButton(xaml, "StickerRemoveAllModelsBtn", "Remove sticker models", "Remove all downloaded sticker models", "StickerRemoveAllModelsBtn_Click");
        AssertSettingsActionButton(xaml, "StickerCopyErrorBtn", "Copy sticker setup details", "Copy sticker setup error details", "StickerCopyErrorBtn_Click");
        AssertSettingsActionButton(xaml, "UpscaleInstallDriversBtn", "Install upscale runtime", "Install or remove the local upscale runtime", "UpscaleInstallDriversBtn_Click");
        AssertSettingsActionButton(xaml, "UpscaleDownloadModelBtn", "Download upscale model", "Download the selected local upscale model", "UpscaleDownloadModelBtn_Click");
        AssertSettingsActionButton(xaml, "UpscaleOpenLocalEngineRepoBtn", "Open upscale engine project", "Open the selected upscale engine project", "UpscaleOpenLocalEngineRepoBtn_Click");
        AssertSettingsActionButton(xaml, "UpscaleRemoveAllModelsBtn", "Remove upscale models", "Remove all downloaded upscale models", "UpscaleRemoveAllModelsBtn_Click");
        AssertSettingsActionButton(xaml, "UpscaleCopyErrorBtn", "Copy upscale setup details", "Copy upscale setup error details", "UpscaleCopyErrorBtn_Click");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "StickerProviderCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "StickerLocalExecutionCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "StickerLocalCpuEngineCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "StickerLocalGpuEngineCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "UpscaleProviderCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "UpscaleLocalExecutionCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "UpscaleLocalCpuEngineCombo");
        AssertLocalModelSelectorUsesResponsiveWidth(xaml, "UpscaleLocalGpuEngineCombo");
        Assert.Contains("x:Name=\"StickerCopyErrorBtn\" Content=\"Copy details\"", xaml);
        Assert.Contains("x:Name=\"UpscaleCopyErrorBtn\" Content=\"Copy details\"", xaml);
        Assert.DoesNotContain("x:Name=\"StickerCopyErrorBtn\" Content=\"Copy status\"", xaml);
        Assert.DoesNotContain("x:Name=\"UpscaleCopyErrorBtn\" Content=\"Copy status\"", xaml);

        var stickerBlock = GetMethodBlock(stickerCode, "private void StickerCopyErrorBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("ClipboardService.CopyTextToClipboard(error);", stickerBlock);
        Assert.Contains("ToastWindow.Show(\"Copied\", \"Sticker details copied to clipboard.\");", stickerBlock);
        Assert.Contains("catch (Exception ex)", stickerBlock);
        Assert.Contains("OddSnap could not copy the sticker details. Check Settings -> Stickers and try again.", stickerBlock);

        var upscaleBlock = GetMethodBlock(upscaleCode, "private void UpscaleCopyErrorBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("ClipboardService.CopyTextToClipboard(error);", upscaleBlock);
        Assert.Contains("ToastWindow.Show(\"Copied\", \"Upscale details copied to clipboard.\");", upscaleBlock);
        Assert.Contains("catch (Exception ex)", upscaleBlock);
        Assert.Contains("OddSnap could not copy the upscale details. Check Settings -> Upscale and try again.", upscaleBlock);
    }

    private static void AssertLocalModelActionRowWraps(string xaml, string firstButtonName, string lastButtonName)
    {
        var firstIndex = xaml.IndexOf($"x:Name=\"{firstButtonName}\"", StringComparison.Ordinal);
        var lastIndex = xaml.IndexOf($"x:Name=\"{lastButtonName}\"", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Could not find {firstButtonName}.");
        Assert.True(lastIndex > firstIndex, $"Could not find {lastButtonName} after {firstButtonName}.");

        var wrapStart = xaml.LastIndexOf("<WrapPanel", firstIndex, StringComparison.Ordinal);
        var wrapEnd = xaml.IndexOf("</WrapPanel>", lastIndex, StringComparison.Ordinal);
        Assert.True(wrapStart >= 0, $"Could not find wrapping action row for {firstButtonName}.");
        Assert.True(wrapEnd > lastIndex, $"Could not find wrapping action row end for {lastButtonName}.");

        var row = xaml[wrapStart..wrapEnd];
        Assert.Contains($"x:Name=\"{firstButtonName}\"", row);
        Assert.Contains($"x:Name=\"{lastButtonName}\"", row);
        Assert.Contains("<WrapPanel Margin=\"0,10,0,-8\">", row);
        Assert.Contains("Margin=\"0,0,8,8\"", row);
        Assert.DoesNotContain("Margin=\"8,0,0,0\"", row);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\"", row);
    }

    private static void AssertLocalModelSelectorUsesResponsiveWidth(string xaml, string comboName)
    {
        AssertSettingsSelectorUsesResponsiveWidth(xaml, comboName, "140", "240");
    }

    private static void AssertSettingsSelectorUsesResponsiveWidth(string xaml, string comboName, string minWidth, string maxWidth)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{comboName}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {comboName}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<ComboBox");
        Assert.Contains($"MinWidth=\"{minWidth}\"", tag);
        Assert.Contains($"MaxWidth=\"{maxWidth}\"", tag);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", tag);
        Assert.DoesNotContain(" Width=\"", tag);
    }

    private static void AssertUploadProviderTextBoxUsesResponsiveWidth(string xaml, string textBoxName)
    {
        AssertUploadProviderTextBoxUsesResponsiveWidth(xaml, textBoxName, "145", "360");
    }

    private static void AssertUploadProviderTextBoxUsesResponsiveWidth(string xaml, string textBoxName, string minWidth, string maxWidth)
    {
        AssertTextBoxUsesResponsiveWidth(xaml, textBoxName, minWidth, maxWidth);

        var nameIndex = xaml.IndexOf($"x:Name=\"{textBoxName}\"", StringComparison.Ordinal);
        var tag = GetOpeningTag(xaml, nameIndex, "<TextBox");
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", tag);
    }

    private static void AssertUploadProviderPasswordBoxUsesResponsiveWidth(string xaml, string passwordBoxName)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{passwordBoxName}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {passwordBoxName}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<PasswordBox");
        Assert.Contains("MinWidth=\"145\"", tag);
        Assert.Contains("MaxWidth=\"360\"", tag);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", tag);
        Assert.Contains("AutomationProperties.HelpText=\"Hidden for safety. Paste a new value to replace it.\"", tag);
        Assert.DoesNotContain(" Width=\"", tag);
    }

    private static void AssertTextBoxWrapsLongMultilineValues(string xaml, string textBoxName)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{textBoxName}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {textBoxName}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<TextBox");
        Assert.Contains("AcceptsReturn=\"True\"", tag);
        Assert.Contains("TextWrapping=\"Wrap\"", tag);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", tag);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", tag);
    }

    private static void AssertTextBoxUsesResponsiveWidth(string xaml, string textBoxName, string minWidth, string maxWidth)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{textBoxName}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {textBoxName}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<TextBox");
        Assert.Contains($"MinWidth=\"{minWidth}\"", tag);
        Assert.Contains($"MaxWidth=\"{maxWidth}\"", tag);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", tag);
        Assert.DoesNotContain(" Width=\"", tag);
    }

    private static void AssertUploadProviderTextBoxUsesResponsiveWidth(
        string xaml,
        string textBoxName,
        string minWidth = "145",
        string maxWidth = "360",
        bool requireHorizontalScroll = true)
    {
        AssertTextBoxUsesResponsiveWidth(xaml, textBoxName, minWidth, maxWidth);

        var nameIndex = xaml.IndexOf($"x:Name=\"{textBoxName}\"", StringComparison.Ordinal);
        var tag = GetOpeningTag(xaml, nameIndex, "<TextBox");
        if (requireHorizontalScroll)
            Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", tag);
    }

    private static void AssertSupportActionRowKeyboardAccessible(string xaml, string name, string automationName, string keyDownHandler)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<Border");
        Assert.Contains("Focusable=\"True\"", tag);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"KeyDown=\"{keyDownHandler}\"", tag);
    }

    private static void AssertSettingsActionButton(string xaml, string name, string automationName, string tooltip, string clickHandler)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<Button");
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"ToolTip=\"{tooltip}\"", tag);
        Assert.Contains("Cursor=\"Hand\"", tag);
        Assert.Contains($"Click=\"{clickHandler}\"", tag);
    }

    private static void AssertNamedControlHasLabel(string xaml, string name, string tagName, string automationName, string tooltip, string? helpText = null)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, tagName);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"ToolTip=\"{tooltip}\"", tag);
        if (helpText is not null)
            Assert.Contains($"AutomationProperties.HelpText=\"{helpText}\"", tag);
    }

    private static void AssertComboBoxItemHasLabel(string xaml, string content, string tooltip, string automationName, string helpText)
    {
        var itemIndex = xaml.IndexOf($"Content=\"{content}\"", StringComparison.Ordinal);
        Assert.True(itemIndex >= 0, $"Could not find ComboBoxItem {content}.");

        var tag = GetOpeningTag(xaml, itemIndex, "<ComboBoxItem");
        Assert.Contains($"ToolTip=\"{tooltip}\"", tag);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"AutomationProperties.HelpText=\"{helpText}\"", tag);
    }

    private static void AssertComboBoxItemInNamedComboHasLabel(string xaml, string comboName, string content, string tooltip, string automationName, string helpText)
    {
        var comboIndex = xaml.IndexOf($"x:Name=\"{comboName}\"", StringComparison.Ordinal);
        Assert.True(comboIndex >= 0, $"Could not find {comboName}.");

        var comboEnd = xaml.IndexOf("</ComboBox>", comboIndex, StringComparison.Ordinal);
        Assert.True(comboEnd > comboIndex, $"Could not find {comboName} closing tag.");

        var itemIndex = xaml.IndexOf($"Content=\"{content}\"", comboIndex, comboEnd - comboIndex, StringComparison.Ordinal);
        Assert.True(itemIndex >= 0, $"Could not find ComboBoxItem {content} in {comboName}.");

        var tag = GetOpeningTag(xaml, itemIndex, "<ComboBoxItem");
        Assert.Contains($"ToolTip=\"{tooltip}\"", tag);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"AutomationProperties.HelpText=\"{helpText}\"", tag);
    }

    [Fact]
    public void LocalModelRemovalActionsGuardAgainstRepeatedActivation()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));

        Assert.Contains("private bool _stickerModelRemovalInProgress;", settingsCode);
        Assert.Contains("if (_stickerModelRemovalInProgress)", stickerCode);
        Assert.Contains("_stickerModelRemovalInProgress = true;", stickerCode);
        Assert.Contains("StickerDownloadRembgBtn.IsEnabled = false;", stickerCode);
        Assert.Contains("StickerRemoveAllModelsBtn.IsEnabled = false;", stickerCode);
        var stickerDownloadBlock = GetMethodBlock(stickerCode, "private void StickerDownloadRembgBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var engineLabel = LocalStickerEngineService.GetEngineLabel(engine);", stickerDownloadBlock);
        Assert.Contains("ThemedConfirmDialog.Confirm(", stickerDownloadBlock);
        Assert.Contains("\"Remove sticker model\"", stickerDownloadBlock);
        Assert.Contains("Remove the downloaded {engineLabel} sticker model?", stickerDownloadBlock);
        Assert.Contains("SetStickerRemovalStatus($\"Sticker model removal canceled. Kept {engineLabel}.\");", stickerDownloadBlock);
        Assert.Contains("SetStickerRemovalStatus(\"Sticker model removal canceled. Downloaded models were left in place.\");", stickerCode);
        Assert.Contains("SetStickerRemovalStatus(removed ? \"Model removed.\" : \"Sticker model was not removed. Check Settings -> Stickers and try again.\");", stickerCode);
        Assert.Contains("SetStickerRemovalStatus(removed ? \"All models removed.\" : \"Sticker models were not removed. Check Settings -> Stickers and try again.\");", stickerCode);
        Assert.Contains("OddSnap could not remove the local sticker model. Try again from Settings -> Stickers, or remove the model files manually.", stickerCode);
        Assert.Contains("OddSnap could not remove the downloaded sticker models. Try again from Settings -> Stickers, or remove the model files manually.", stickerCode);
        Assert.DoesNotContain("Couldn't remove the model.", stickerCode);
        Assert.DoesNotContain("Couldn't remove downloaded models.", stickerCode);
        Assert.Contains("catch (Exception ex)", stickerCode);
        Assert.Contains("SetStickerRemovalStatus(\"Sticker model removal failed. Check Settings -> Stickers and try again.\");", stickerCode);
        Assert.Contains("ToastWindow.ShowError(", stickerCode);
        Assert.Contains("The local sticker model files were not removed. Check Settings -> Stickers and try again.", stickerCode);
        Assert.DoesNotContain("SetStickerDownloadUi(false, null, removed ? \"Model removed.\" : \"Couldn't remove the model.\");", stickerCode);
        Assert.Contains("ResetStickerModelRemovalGuardAfterCooldown();", stickerCode);
        Assert.Contains("_stickerModelRemovalInProgress = false;", stickerCode);
        Assert.Contains("UpdateLocalEngineUi();", stickerCode);

        Assert.Contains("private bool _upscaleModelRemovalInProgress;", settingsCode);
        Assert.Contains("if (_upscaleModelRemovalInProgress)", upscaleCode);
        Assert.Contains("_upscaleModelRemovalInProgress = true;", upscaleCode);
        Assert.Contains("UpscaleDownloadModelBtn.IsEnabled = false;", upscaleCode);
        Assert.Contains("UpscaleRemoveAllModelsBtn.IsEnabled = false;", upscaleCode);
        var upscaleDownloadBlock = GetMethodBlock(upscaleCode, "private void UpscaleDownloadModelBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var engineLabel = LocalUpscaleEngineService.GetEngineLabel(engine);", upscaleDownloadBlock);
        Assert.Contains("ThemedConfirmDialog.Confirm(", upscaleDownloadBlock);
        Assert.Contains("\"Remove upscale model\"", upscaleDownloadBlock);
        Assert.Contains("Remove the downloaded {engineLabel} upscale model?", upscaleDownloadBlock);
        Assert.Contains("SetUpscaleRemovalStatus($\"Upscale model removal canceled. Kept {engineLabel}.\");", upscaleDownloadBlock);
        Assert.Contains("SetUpscaleRemovalStatus(\"Upscale model removal canceled. Downloaded models were left in place.\");", upscaleCode);
        Assert.Contains("SetUpscaleRemovalStatus(removed ? \"Model removed.\" : \"Upscale model was not removed. Check Settings -> Upscale and try again.\");", upscaleCode);
        Assert.Contains("SetUpscaleRemovalStatus(removed ? \"All models removed.\" : \"Upscale models were not removed. Check Settings -> Upscale and try again.\");", upscaleCode);
        Assert.Contains("OddSnap could not remove the local upscale model. Try again from Settings -> Upscale, or remove the model files manually.", upscaleCode);
        Assert.Contains("OddSnap could not remove the downloaded upscale models. Try again from Settings -> Upscale, or remove the model files manually.", upscaleCode);
        Assert.DoesNotContain("Couldn't remove the model.", upscaleCode);
        Assert.DoesNotContain("Couldn't remove downloaded models.", upscaleCode);
        Assert.Contains("catch (Exception ex)", upscaleCode);
        Assert.Contains("SetUpscaleRemovalStatus(\"Upscale model removal failed. Check Settings -> Upscale and try again.\");", upscaleCode);
        Assert.Contains("ToastWindow.ShowError(", upscaleCode);
        Assert.Contains("The local upscale model files were not removed. Check Settings -> Upscale and try again.", upscaleCode);
        Assert.DoesNotContain("SetUpscaleDownloadUi(false, null, removed ? \"Model removed.\" : \"Couldn't remove the model.\");", upscaleCode);
        Assert.Contains("ResetUpscaleModelRemovalGuardAfterCooldown();", upscaleCode);
        Assert.Contains("_upscaleModelRemovalInProgress = false;", upscaleCode);
        Assert.Contains("UpdateUpscaleLocalEngineUi();", upscaleCode);
    }

    [Fact]
    public void LocalRuntimeMutationActionsGuardAgainstRepeatedActivation()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var stickerCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Stickers.cs"));
        var upscaleCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Upscale.cs"));
        var stickerInstallBlock = GetMethodBlock(stickerCode, "private void StickerInstallDriversBtn_Click(object sender, RoutedEventArgs e)");
        var stickerRuntimeBlock = GetMethodBlock(stickerCode, "private bool RunStickerRuntimeMutation(Action mutation)");
        var upscaleInstallBlock = GetMethodBlock(upscaleCode, "private void UpscaleInstallDriversBtn_Click(object sender, RoutedEventArgs e)");
        var upscaleRuntimeBlock = GetMethodBlock(upscaleCode, "private bool RunUpscaleRuntimeMutation(Action mutation)");

        Assert.Contains("private bool _stickerRuntimeMutationInProgress;", settingsCode);
        Assert.Contains("if (_stickerRuntimeMutationInProgress)", stickerCode);
        Assert.Contains("var completed = RunStickerRuntimeMutation(() =>", stickerInstallBlock);
        Assert.Contains("SetStickerRuntimeCancellationStatus(\"Sticker runtime uninstall canceled. Runtime was left installed.\");", stickerInstallBlock);
        Assert.Contains("bool removed = false;", stickerCode);
        Assert.Contains("removed = RembgRuntimeService.RemoveRuntime(executionProvider);", stickerCode);
        Assert.Contains("if (completed && !removed)", stickerInstallBlock);
        Assert.Contains("SetStickerRuntimeRemovalStatus(\"Sticker runtime was not removed. Close active sticker captures and try again.\");", stickerCode);
        Assert.Contains("The rembg runtime was not removed. Close active sticker captures and try again from Settings -> Stickers.", stickerCode);
        Assert.Contains("private void SetStickerRuntimeRemovalStatus(string message)", stickerCode);
        Assert.Contains("StickerLocalEngineStatusText.Text = \"Runtime uninstall failed\";", stickerCode);
        Assert.Contains("private void SetStickerRuntimeCancellationStatus(string message)", stickerCode);
        Assert.Contains("StickerLocalEngineStatusText.Text = \"Runtime uninstall canceled\";", stickerCode);
        Assert.Contains("string? failureMessage = null;", stickerRuntimeBlock);
        Assert.Contains("failureMessage = \"Sticker runtime action failed. Check Settings -> Stickers and try again.\";", stickerRuntimeBlock);
        Assert.Contains("SetStickerRuntimeRemovalStatus(failureMessage);", stickerRuntimeBlock);
        Assert.Contains("return completed;", stickerRuntimeBlock);
        Assert.Contains("ToastWindow.ShowError(", stickerRuntimeBlock);
        Assert.Contains("\"Sticker runtime error\"", stickerRuntimeBlock);
        Assert.Contains("The sticker runtime action did not finish. Check Settings -> Stickers and try again.", stickerRuntimeBlock);
        Assert.Contains("_stickerRuntimeMutationInProgress = true;", stickerCode);
        Assert.Contains("StickerInstallDriversBtn.IsEnabled = false;", stickerCode);
        Assert.Contains("StickerDownloadRembgBtn.IsEnabled = false;", stickerCode);
        Assert.Contains("StickerRemoveAllModelsBtn.IsEnabled = false;", stickerCode);
        Assert.Contains("_stickerRuntimeMutationInProgress = false;", stickerCode);
        Assert.Contains("UpdateLocalEngineUi();", stickerCode);
        var stickerRefreshIndex = stickerRuntimeBlock.IndexOf("UpdateLocalEngineUi();", StringComparison.Ordinal);
        var stickerFailureStatusIndex = stickerRuntimeBlock.IndexOf("SetStickerRuntimeRemovalStatus(failureMessage);", StringComparison.Ordinal);
        Assert.True(stickerFailureStatusIndex > stickerRefreshIndex, "Sticker runtime exception status should be restored after the UI refresh.");

        Assert.Contains("private bool _upscaleRuntimeMutationInProgress;", settingsCode);
        Assert.Contains("if (_upscaleRuntimeMutationInProgress)", upscaleCode);
        Assert.Contains("var completed = RunUpscaleRuntimeMutation(() =>", upscaleInstallBlock);
        Assert.Contains("SetUpscaleRuntimeCancellationStatus(\"Upscale runtime uninstall canceled. Runtime was left installed.\");", upscaleInstallBlock);
        Assert.Contains("bool removed = false;", upscaleCode);
        Assert.Contains("removed = UpscaleRuntimeService.RemoveRuntime(executionProvider);", upscaleCode);
        Assert.Contains("if (completed && !removed)", upscaleInstallBlock);
        Assert.Contains("SetUpscaleRuntimeRemovalStatus(\"Upscale runtime was not removed. Close active upscale captures and try again.\");", upscaleCode);
        Assert.Contains("The upscale runtime was not removed. Close active upscale captures and try again from Settings -> Upscale.", upscaleCode);
        Assert.Contains("private void SetUpscaleRuntimeRemovalStatus(string message)", upscaleCode);
        Assert.Contains("UpscaleLocalEngineStatusText.Text = \"Runtime uninstall failed\";", upscaleCode);
        Assert.Contains("private void SetUpscaleRuntimeCancellationStatus(string message)", upscaleCode);
        Assert.Contains("UpscaleLocalEngineStatusText.Text = \"Runtime uninstall canceled\";", upscaleCode);
        Assert.Contains("string? failureMessage = null;", upscaleRuntimeBlock);
        Assert.Contains("failureMessage = \"Upscale runtime action failed. Check Settings -> Upscale and try again.\";", upscaleRuntimeBlock);
        Assert.Contains("SetUpscaleRuntimeRemovalStatus(failureMessage);", upscaleRuntimeBlock);
        Assert.Contains("return completed;", upscaleRuntimeBlock);
        Assert.Contains("ToastWindow.ShowError(", upscaleRuntimeBlock);
        Assert.Contains("\"Upscale runtime error\"", upscaleRuntimeBlock);
        Assert.Contains("The upscale runtime action did not finish. Check Settings -> Upscale and try again.", upscaleRuntimeBlock);
        Assert.Contains("_upscaleRuntimeMutationInProgress = true;", upscaleCode);
        Assert.Contains("UpscaleInstallDriversBtn.IsEnabled = false;", upscaleCode);
        Assert.Contains("UpscaleDownloadModelBtn.IsEnabled = false;", upscaleCode);
        Assert.Contains("UpscaleRemoveAllModelsBtn.IsEnabled = false;", upscaleCode);
        Assert.Contains("_upscaleRuntimeMutationInProgress = false;", upscaleCode);
        Assert.Contains("UpdateUpscaleLocalEngineUi();", upscaleCode);
        var upscaleRefreshIndex = upscaleRuntimeBlock.IndexOf("UpdateUpscaleLocalEngineUi();", StringComparison.Ordinal);
        var upscaleFailureStatusIndex = upscaleRuntimeBlock.IndexOf("SetUpscaleRuntimeRemovalStatus(failureMessage);", StringComparison.Ordinal);
        Assert.True(upscaleFailureStatusIndex > upscaleRefreshIndex, "Upscale runtime exception status should be restored after the UI refresh.");
    }

    [Fact]
    public void BackgroundRuntimeStatusRefreshesRecoverPerPanel()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var ocrCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Ocr.cs"));

        var changedBlock = GetMethodBlock(settingsCode, "private async void BackgroundRuntimeJobService_Changed(string key)");
        Assert.Contains("await CheckModelStatusAsync();", changedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.background-runtime-ocr-changed\", ex);", changedBlock);
        Assert.Contains("SetTranslationRuntimeStatusRefreshFailed(ex.Message);", changedBlock);
        Assert.Contains("UpdateLocalEngineUi();", changedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.background-runtime-sticker-changed\", ex);", changedBlock);
        Assert.Contains("SetStickerRuntimeStatusRefreshFailed(ex.Message);", changedBlock);
        Assert.Contains("UpdateUpscaleLocalEngineUi();", changedBlock);
        Assert.Contains("AppDiagnostics.LogError(\"settings.background-runtime-upscale-changed\", ex);", changedBlock);
        Assert.Contains("SetUpscaleRuntimeStatusRefreshFailed(ex.Message);", changedBlock);
        Assert.DoesNotContain("AppDiagnostics.LogError(\"settings.background-runtime-changed\", ex);", changedBlock);

        var stickerBlock = GetMethodBlock(settingsCode, "private void SetStickerRuntimeStatusRefreshFailed(string message)");
        Assert.Contains("StickerLocalEngineStatusText.Text = \"Runtime status refresh failed\";", stickerBlock);
        Assert.Contains("StickerLocalEngineProgress.Visibility = Visibility.Collapsed;", stickerBlock);
        Assert.Contains("StickerLocalEngineProgressText.Visibility = Visibility.Visible;", stickerBlock);
        Assert.Contains("StickerLocalEngineProgressText.Text = BuildRuntimeRefreshFailureDetail(\"Settings -> Stickers\", message);", stickerBlock);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(message) ? \"Check the app log for details.\" : message", stickerBlock);
        Assert.Contains("SetLoadingTextShimmer(StickerLocalEngineStatusText, false, 1.0, 0.35);", stickerBlock);

        var upscaleBlock = GetMethodBlock(settingsCode, "private void SetUpscaleRuntimeStatusRefreshFailed(string message)");
        Assert.Contains("UpscaleLocalEngineStatusText.Text = \"Runtime status refresh failed\";", upscaleBlock);
        Assert.Contains("UpscaleLocalEngineProgress.Visibility = Visibility.Collapsed;", upscaleBlock);
        Assert.Contains("UpscaleLocalEngineProgressText.Visibility = Visibility.Visible;", upscaleBlock);
        Assert.Contains("UpscaleLocalEngineProgressText.Text = BuildRuntimeRefreshFailureDetail(\"Settings -> Upscale\", message);", upscaleBlock);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(message) ? \"Check the app log for details.\" : message", upscaleBlock);
        Assert.Contains("SetLoadingTextShimmer(UpscaleLocalEngineStatusText, false, 1.0, 0.35);", upscaleBlock);

        var runtimeFailureDetailBlock = GetMethodBlock(settingsCode, "private static string BuildRuntimeRefreshFailureDetail(string settingsPanel, string message)");
        Assert.Contains("var recovery = \"Check \" + settingsPanel + \" and try again.\";", runtimeFailureDetailBlock);
        Assert.Contains("Details were logged.", runtimeFailureDetailBlock);

        var translationBlock = GetMethodBlock(ocrCode, "private void SetTranslationRuntimeStatusRefreshFailed(string message)");
        Assert.Contains("SetOpenSourceTranslationRuntimeStatusRefreshFailed(message);", translationBlock);
        Assert.Contains("SetArgosTranslationRuntimeStatusRefreshFailed(message);", translationBlock);

        var openSourceBlock = GetMethodBlock(ocrCode, "private void SetOpenSourceTranslationRuntimeStatusRefreshFailed(string message)");
        Assert.Contains("_openSourceTranslationRuntimeActionInProgress = false;", openSourceBlock);
        Assert.Contains("OpenSourceLocalStatusText.Text = FormatTranslationRuntimeRefreshFailureStatus(message);", openSourceBlock);
        Assert.Contains("OpenSourceLocalInstallBtn.IsEnabled = true;", openSourceBlock);
        Assert.Contains("OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;", openSourceBlock);
        Assert.Contains("SetLoadingTextShimmer(OpenSourceLocalStatusText, false, 0.7, 0.45);", openSourceBlock);
        Assert.DoesNotContain("OpenSourceLocalStatusText.Text = $\"Status refresh failed: {FormatRuntimeStatus(message)}\";", openSourceBlock);

        var argosBlock = GetMethodBlock(ocrCode, "private void SetArgosTranslationRuntimeStatusRefreshFailed(string message)");
        Assert.Contains("_argosTranslationRuntimeActionInProgress = false;", argosBlock);
        Assert.Contains("ArgosStatusText.Text = FormatTranslationRuntimeRefreshFailureStatus(message);", argosBlock);
        Assert.Contains("ArgosInstallBtn.IsEnabled = true;", argosBlock);
        Assert.Contains("ArgosProgressBar.Visibility = Visibility.Collapsed;", argosBlock);
        Assert.Contains("SetLoadingTextShimmer(ArgosStatusText, false, 0.7, 0.45);", argosBlock);
        Assert.DoesNotContain("ArgosStatusText.Text = $\"Status refresh failed: {FormatRuntimeStatus(message)}\";", argosBlock);

        var translationFailureStatusBlock = GetMethodBlock(ocrCode, "private static string FormatTranslationRuntimeRefreshFailureStatus(string message)");
        Assert.Contains("Status refresh failed. Check Settings -> OCR and try again.", translationFailureStatusBlock);
        Assert.Contains("details were logged", translationFailureStatusBlock);

        var actionFailureStatusBlock = GetMethodBlock(ocrCode, "private static string FormatRuntimeActionFailedStatus(string? message, string runtimeName)");
        Assert.Contains("$\"{runtimeName} action failed. Check Settings -> OCR and try again.\"", actionFailureStatusBlock);
        Assert.Contains("Details were logged.", actionFailureStatusBlock);
        Assert.DoesNotContain("FormatRuntimeStatus(message)", actionFailureStatusBlock);
    }

    [Fact]
    public void TranslationRuntimeActionsLockImmediatelyUntilStatusRefresh()
    {
        var settingsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "SettingsWindow.xaml.cs"));
        var ocrCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "Settings", "SettingsWindow.Ocr.cs"));
        var openSourceClickBlock = GetMethodBlock(ocrCode, "private void OpenSourceLocalInstallBtn_Click(object sender, RoutedEventArgs e)");
        var argosClickBlock = GetMethodBlock(ocrCode, "private void ArgosInstallBtn_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("private bool _openSourceTranslationRuntimeActionInProgress;", settingsCode);
        Assert.Contains("if (_openSourceTranslationRuntimeActionInProgress)", ocrCode);
        Assert.Contains("_openSourceTranslationRuntimeActionInProgress = true;", ocrCode);
        Assert.Contains("SetOpenSourceTranslationRuntimeBusy(startingStatus, isUninstall);", ocrCode);
        Assert.Contains("OpenSourceLocalInstallBtn.IsEnabled = false;", ocrCode);
        Assert.Contains("_openSourceTranslationRuntimeActionInProgress = false;", ocrCode);
        Assert.Contains("await RefreshOpenSourceTranslationRuntimeStatusAsync();", ocrCode);
        Assert.Contains("if (isUninstall && !ThemedConfirmDialog.Confirm(", openSourceClickBlock);
        Assert.Contains("\"Uninstall open-source local translation\"", openSourceClickBlock);
        Assert.Contains("Open-source local uninstall canceled. Runtime was left installed.", openSourceClickBlock);
        Assert.Contains("OpenSourceLocalProgressBar.Visibility = Visibility.Collapsed;", openSourceClickBlock);
        Assert.Contains("OpenSourceLocalInstallBtn.Content = \"Uninstall\";", openSourceClickBlock);
        Assert.Contains("OpenSourceLocalInstallBtn.IsEnabled = true;", openSourceClickBlock);

        Assert.Contains("private bool _argosTranslationRuntimeActionInProgress;", settingsCode);
        Assert.Contains("if (_argosTranslationRuntimeActionInProgress)", ocrCode);
        Assert.Contains("_argosTranslationRuntimeActionInProgress = true;", ocrCode);
        Assert.Contains("SetArgosTranslationRuntimeBusy(startingStatus, isUninstall);", ocrCode);
        Assert.Contains("ArgosInstallBtn.IsEnabled = false;", ocrCode);
        Assert.Contains("_argosTranslationRuntimeActionInProgress = false;", ocrCode);
        Assert.Contains("await RefreshArgosTranslationRuntimeStatusAsync();", ocrCode);
        Assert.Contains("if (isUninstall && !ThemedConfirmDialog.Confirm(", argosClickBlock);
        Assert.Contains("\"Uninstall Argos Translate\"", argosClickBlock);
        Assert.Contains("Argos uninstall canceled. Runtime was left installed.", argosClickBlock);
        Assert.Contains("ArgosProgressBar.Visibility = Visibility.Collapsed;", argosClickBlock);
        Assert.Contains("ArgosInstallBtn.Content = \"Uninstall\";", argosClickBlock);
        Assert.Contains("ArgosInstallBtn.IsEnabled = true;", argosClickBlock);
        Assert.Contains("if (!started)", openSourceClickBlock);
        Assert.Contains("ToastWindow.Show(\"Open-source local\", \"That setup is already running in the background.\");", openSourceClickBlock);
        Assert.Contains("if (!started)", argosClickBlock);
        Assert.Contains("ToastWindow.Show(\"Argos Translate\", \"That setup is already running in the background.\");", argosClickBlock);

        var openSourceStartIndex = openSourceClickBlock.IndexOf("var started = BackgroundRuntimeJobService.Start(", StringComparison.Ordinal);
        var openSourceBusyIndex = openSourceClickBlock.IndexOf("SetOpenSourceTranslationRuntimeBusy(startingStatus, isUninstall);", StringComparison.Ordinal);
        Assert.True(openSourceBusyIndex > openSourceStartIndex, "Open-source translation should only show the optimistic busy state after Start accepts the job.");

        var argosStartIndex = argosClickBlock.IndexOf("var started = BackgroundRuntimeJobService.Start(", StringComparison.Ordinal);
        var argosBusyIndex = argosClickBlock.IndexOf("SetArgosTranslationRuntimeBusy(startingStatus, isUninstall);", StringComparison.Ordinal);
        Assert.True(argosBusyIndex > argosStartIndex, "Argos should only show the optimistic busy state after Start accepts the job.");

        var openSourceRefreshStart = ocrCode.IndexOf("private async Task RefreshOpenSourceTranslationRuntimeStatusAsync()", StringComparison.Ordinal);
        var argosRefreshStart = ocrCode.IndexOf("private async Task RefreshArgosTranslationRuntimeStatusAsync()", StringComparison.Ordinal);
        var refreshFailureHelperStart = ocrCode.IndexOf("private void SetOpenSourceTranslationRuntimeStatusRefreshFailed(string message)", StringComparison.Ordinal);
        Assert.True(openSourceRefreshStart >= 0, "Could not find open-source translation runtime refresh.");
        Assert.True(argosRefreshStart > openSourceRefreshStart, "Could not find Argos translation runtime refresh after open-source refresh.");
        Assert.True(refreshFailureHelperStart > argosRefreshStart, "Could not find translation runtime refresh-failure helper after refresh blocks.");

        var openSourceRefresh = ocrCode[openSourceRefreshStart..argosRefreshStart];
        Assert.Contains("AppDiagnostics.LogError(\"settings.ocr.check-open-source-status\", ex);", openSourceRefresh);
        Assert.Contains("SetOpenSourceTranslationRuntimeStatusRefreshFailed(ex.Message);", openSourceRefresh);
        Assert.Contains("FormatRuntimeReadinessStatus(_openSourceLocalInstalled, \"Installed\", openSourceJob, \"Open-source local\");", openSourceRefresh);
        Assert.DoesNotContain("OpenSourceLocalStatusText.Text = \"Python not found\";", openSourceRefresh);
        Assert.DoesNotContain("OpenSourceLocalStatusText.Text = $\"Failed: {FormatRuntimeStatus(openSourceJob.LastError)}\";", openSourceRefresh);
        Assert.DoesNotContain("_argosTranslationRuntimeActionInProgress = false;", openSourceRefresh);

        var argosRefresh = ocrCode[argosRefreshStart..refreshFailureHelperStart];
        Assert.Contains("AppDiagnostics.LogError(\"settings.ocr.check-argos-status\", ex);", argosRefresh);
        Assert.Contains("SetArgosTranslationRuntimeStatusRefreshFailed(ex.Message);", argosRefresh);
        Assert.Contains("FormatRuntimeReadinessStatus(_argosInstalled, \"Installed\", argosJob, \"Argos Translate\");", argosRefresh);
        Assert.DoesNotContain("ArgosStatusText.Text = \"Python not found\";", argosRefresh);
        Assert.DoesNotContain("ArgosStatusText.Text = $\"Failed: {FormatRuntimeStatus(argosJob.LastError)}\";", argosRefresh);
        Assert.DoesNotContain("_openSourceTranslationRuntimeActionInProgress = false;", argosRefresh);

        Assert.Contains("FormatRuntimeActionFailedStatus(openSourceJob.LastError, \"Open-source local\");", ocrCode);
        Assert.Contains("FormatRuntimeActionFailedStatus(argosJob.LastError, \"Argos Translate\");", ocrCode);

        var readinessFormatter = GetMethodBlock(ocrCode, "private static string FormatRuntimeReadinessStatus(bool isInstalled, string installedStatus, BackgroundRuntimeJobSnapshot? lastJob, string runtimeName)");
        Assert.Contains("lastJob is { LastSucceeded: false }", readinessFormatter);
        Assert.Contains("FormatRuntimeActionFailedStatus(lastJob.LastError, runtimeName)", readinessFormatter);
        Assert.Contains("if (isInstalled)", readinessFormatter);
        Assert.Contains("return installedStatus;", readinessFormatter);
        Assert.DoesNotContain("$\"{installedStatus}; {FormatRuntimeActionFailedStatus(lastJob.LastError)}\"", readinessFormatter);
        Assert.DoesNotContain("var failure = FormatRuntimeStatus(lastJob.LastError);", readinessFormatter);
        Assert.DoesNotContain("last action failed: {failure}", readinessFormatter);
        Assert.DoesNotContain("Failed: {failure}", readinessFormatter);
        Assert.Contains("return \"Not installed\";", readinessFormatter);
    }

    private static void AssertPasswordBox(string xaml, string name, string automationName)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<PasswordBox");
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains("PasswordChanged=", tag);
        Assert.DoesNotContain("TextChanged=", tag);
    }

    private static void AssertSettingsPageAllowsHorizontalOverflow(string xaml, string name)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<ScrollViewer");
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", tag);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", tag);
    }

    private static void AssertSettingsPageDisablesHorizontalOverflow(string xaml, string name)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<ScrollViewer");
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", tag);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", tag);
    }

    private static void AssertTextBlockUsesStyle(string xaml, string text, string styleKey)
    {
        var textIndex = xaml.IndexOf($"Text=\"{text}\"", StringComparison.Ordinal);
        Assert.True(textIndex >= 0, $"Could not find helper text: {text}");

        var tag = GetOpeningTag(xaml, textIndex, "<TextBlock");
        Assert.Contains($"Style=\"{{StaticResource {styleKey}}}\"", tag);
    }

    private static void AssertNamedTextBlockUsesStyle(string xaml, string name, string styleKey)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var tag = GetOpeningTag(xaml, nameIndex, "<TextBlock");
        Assert.Contains($"Style=\"{{StaticResource {styleKey}}}\"", tag);
    }

    private static string GetOpeningTag(string xaml, int attributeIndex, string tagName)
    {
        var start = xaml.LastIndexOf(tagName, attributeIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find opening tag before index {attributeIndex}.");

        var end = xaml.IndexOf('>', attributeIndex);
        Assert.True(end > start, $"Could not read opening tag at index {attributeIndex}.");

        return xaml[start..end];
    }

    private static string GetBlockStartingAt(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find block marker: {marker}");

        var end = source.IndexOf("return;", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find return after block marker: {marker}");

        return source[start..end];
    }

    private static void AssertSearchFailureStatusWrittenAfterLoadingStops(string methodBlock)
    {
        var catchIndex = methodBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchIndex >= 0, "Could not find search failure catch block.");

        var loadingIndex = methodBlock.IndexOf("SetImageSearchLoading(false, forceIndexed: true);", catchIndex, StringComparison.Ordinal);
        var statusIndex = methodBlock.IndexOf("HistorySearchStatusText.Text = \"Search failed. Edit the query or retry from History.\";", catchIndex, StringComparison.Ordinal);
        var toastIndex = methodBlock.IndexOf("OddSnap could not update history search. Edit the query or retry from History.", catchIndex, StringComparison.Ordinal);
        Assert.True(loadingIndex > catchIndex, "Search failure should stop loading in the catch block.");
        Assert.True(statusIndex > loadingIndex, "Search failure status should be written after loading stops so status refresh does not clear it.");
        Assert.True(toastIndex > statusIndex, "Search failure toast should be shown after the visible status is set.");
    }

    private static void AssertSearchCallbackFailureStopsLoadingThenSetsStatus(string methodBlock)
    {
        var catchIndex = methodBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchIndex >= 0, "Could not find search callback failure catch block.");

        var loadingIndex = methodBlock.IndexOf("SetImageSearchLoading(false, forceIndexed: true);", catchIndex, StringComparison.Ordinal);
        var statusIndex = methodBlock.IndexOf("HistorySearchStatusText.Text = \"Search failed\";", catchIndex, StringComparison.Ordinal);
        Assert.True(loadingIndex > catchIndex, "Search callback failure should stop loading in the catch block.");
        Assert.True(statusIndex > loadingIndex, "Search callback failure status should be written after loading stops so status refresh does not clear it.");
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method body: {signature}");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }
}
