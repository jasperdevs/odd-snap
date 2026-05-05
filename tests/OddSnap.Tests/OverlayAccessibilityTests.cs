using System.Reflection;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class OverlayAccessibilityTests
{
    [Fact]
    public void PreviewOverlayButtonsExposeNamesTooltipsAndKeyboardActivation()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml"));

        AssertOverlayButton(xaml, "CloseBtn", "Close preview", "Close preview", requiresKeyboardHandler: true, "Close this preview.");
        AssertOverlayButton(xaml, "PinBtn", "Pin preview", "Pin preview", requiresKeyboardHandler: true, "Keep this preview open.");
        AssertOverlayButton(xaml, "SaveBtn", "Save preview", "Save preview", requiresKeyboardHandler: true, "Save this preview image.");
    }

    [Fact]
    public void PreviewContentAndOverlayButtonsRefreshAccessibilityMetadata()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml"));
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));
        var previewActionsCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        Assert.Contains("using System.Windows.Automation;", previewCode);
        AssertImageHasAccessibilityMetadata(xaml, "ThumbnailImage", "Screenshot preview", "Screenshot preview", "Screenshot preview.");

        var initBlock = GetMethodBlock(previewCode, "private void InitCommon()");
        Assert.Contains("RefreshPreviewAccessibility();", initBlock);

        var uploadLinkBlock = GetMethodBlock(previewCode, "private void SetUploadedLink(string url, string provider)");
        Assert.Contains("RefreshPreviewAccessibility();", uploadLinkBlock);

        var themeBlock = GetMethodBlock(previewCode, "private void ApplyTheme()");
        Assert.Contains("RefreshPreviewAccessibility();", themeBlock);

        var previewAccessibilityBlock = GetMethodBlock(previewCode, "private void RefreshPreviewAccessibility()");
        Assert.Contains("RefreshPreviewWindowTooltip();", previewAccessibilityBlock);
        Assert.Contains("SetPreviewElementAccessibility(ThumbnailImage, previewName, BuildPreviewImageHelpText());", previewAccessibilityBlock);
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(CloseBtn, \"Close preview\", \"Close this preview.\");", previewAccessibilityBlock);
        Assert.Contains("\"Unpin preview\"", previewAccessibilityBlock);
        Assert.Contains("\"Allow this preview to dismiss automatically.\"", previewAccessibilityBlock);
        Assert.Contains("\"Pin preview\"", previewAccessibilityBlock);
        Assert.Contains("\"Keep this preview open.\"", previewAccessibilityBlock);
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(SaveBtn, \"Save preview\", \"Save this preview image.\");", previewAccessibilityBlock);

        var imageHelpBlock = GetMethodBlock(previewCode, "private string BuildPreviewImageHelpText()");
        Assert.Contains("Preview for {fileName} with {provider} upload link.", imageHelpBlock);
        Assert.Contains("Screenshot preview for {fileName}.", imageHelpBlock);
        Assert.Contains("GIF preview for {fileName}.", imageHelpBlock);

        var windowTooltipBlock = GetMethodBlock(previewCode, "private void RefreshPreviewWindowTooltip()");
        Assert.Contains("SetPreviewWindowStatusTooltip(BuildPreviewWindowTooltip());", windowTooltipBlock);

        var windowTooltipTextBlock = GetMethodBlock(previewCode, "private string BuildPreviewWindowTooltip()");
        Assert.Contains("return $\"Open {provider} link\";", windowTooltipTextBlock);
        Assert.Contains("return BuildPreviewImageHelpText();", windowTooltipTextBlock);

        var setMetadataBlock = GetMethodBlock(previewCode, "private static void SetPreviewElementAccessibility(FrameworkElement element, string name, string helpText)");
        Assert.Contains("element.ToolTip = helpText;", setMetadataBlock);
        Assert.Contains("AutomationProperties.SetName(element, name);", setMetadataBlock);
        Assert.Contains("AutomationProperties.SetHelpText(element, helpText);", setMetadataBlock);

        var setWindowMetadataBlock = GetMethodBlock(previewCode, "private void SetPreviewWindowStatusTooltip(string helpText)");
        Assert.Contains("ToolTip = helpText;", setWindowMetadataBlock);
        Assert.Contains("AutomationProperties.SetName(this, \"Preview window\");", setWindowMetadataBlock);
        Assert.Contains("AutomationProperties.SetHelpText(this, helpText);", setWindowMetadataBlock);

        var togglePinnedBlock = GetMethodBlock(previewActionsCode, "private void TogglePinned()");
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(PinBtn, \"Unpin preview\", \"Allow this preview to dismiss automatically.\");", togglePinnedBlock);
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(PinBtn, \"Pin preview\", \"Keep this preview open.\");", togglePinnedBlock);
    }

    [Fact]
    public void ToastOverlayButtonsExposeNamesTooltipsAndKeyboardActivation()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml"));

        AssertOverlayButton(xaml, "CloseBtn", "Close preview", "Close preview", requiresKeyboardHandler: true, "Close this preview.");
        AssertOverlayButton(xaml, "PinBtn", "Pin preview", "Pin preview", requiresKeyboardHandler: true, "Keep this preview open.");
        AssertOverlayButton(xaml, "SaveBtn", "Save preview", "Save preview", requiresKeyboardHandler: true, "Save this preview image.");
        AssertOverlayButton(xaml, "OfficeBtn", "Open with or send to Office", "Open with or send to Office", requiresKeyboardHandler: true, "Open this preview with another app or send it to Office.");
        AssertOverlayButton(xaml, "AiRedirectBtn", "Open in AI", "Open in AI", requiresKeyboardHandler: true, "Open this preview in the configured AI destination.");
        AssertOverlayButton(xaml, "DeleteBtn", "Delete file", "Delete file", requiresKeyboardHandler: true, "Delete the saved file for this preview.");
        AssertOverlayButton(xaml, "TextCloseBtn", "Close notification", "Close notification", requiresKeyboardHandler: true, "Close this notification.");

        Assert.DoesNotContain("ToolTipService.IsEnabled=\"False\"", xaml);
    }

    [Fact]
    public void ToastContentAndOverlayButtonsRefreshAccessibilityMetadata()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("using System.Windows.Automation;", toastCode);

        var applySpecBlock = GetMethodBlock(toastCode, "private void ApplySpec(ToastSpec spec)");
        Assert.Contains("RefreshToastContentAccessibility(spec);", applySpecBlock);
        Assert.Contains("RefreshOverlayButtonLayout();", applySpecBlock);

        AssertDynamicToastTextBlock(xaml: File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml")), "TitleText", "Toast title");
        AssertDynamicToastTextBlock(xaml: File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml")), "BodyText", "Toast message");

        var contentBlock = GetMethodBlock(toastCode, "private void RefreshToastContentAccessibility(ToastSpec spec)");
        Assert.Contains("AutomationProperties.SetName(TitleText, \"Toast title\");", contentBlock);
        Assert.Contains("AutomationProperties.SetHelpText(TitleText, title);", contentBlock);
        Assert.Contains("AutomationProperties.SetName(BodyText, \"Toast message\");", contentBlock);
        Assert.Contains("AutomationProperties.SetHelpText(BodyText, body);", contentBlock);
        Assert.Contains("SetToastElementAccessibility(ColorSwatch, \"Toast color swatch\"", contentBlock);
        Assert.Contains("SetToastElementAccessibility(InlinePreviewHost, \"Inline toast preview\"", contentBlock);
        Assert.Contains("SetToastElementAccessibility(PreviewImage, \"Toast preview image\", previewHelp);", contentBlock);

        var layoutBlock = GetMethodBlock(toastCode, "internal void RefreshOverlayButtonLayout()");
        Assert.Contains("SetToastElementAccessibility(TextCloseBtn, \"Close notification\", \"Close this notification.\");", layoutBlock);

        var overlayBlock = GetMethodBlock(toastCode, "private void ApplyOverlayButton(System.Windows.Controls.Border button, Helpers.ToastButtonKind kind)");
        Assert.Contains("RefreshOverlayButtonAccessibility(button, kind);", overlayBlock);

        var overlayAccessibilityBlock = GetMethodBlock(toastCode, "private void RefreshOverlayButtonAccessibility(System.Windows.Controls.Border button, Helpers.ToastButtonKind kind)");
        Assert.Contains("(\"Unpin preview\", \"Allow this preview to dismiss automatically.\")", overlayAccessibilityBlock);
        Assert.Contains("(\"Pin preview\", \"Keep this preview open.\")", overlayAccessibilityBlock);
        Assert.Contains("(\"Saving preview\", \"Save is already running.\")", overlayAccessibilityBlock);
        Assert.Contains("(\"Office action running\", \"Open with or Office export is already running.\")", overlayAccessibilityBlock);
        Assert.Contains("(\"Opening in AI\", \"AI Redirect is already running.\")", overlayAccessibilityBlock);
        Assert.Contains("(\"Deleting file\", \"Delete is already running.\")", overlayAccessibilityBlock);
        Assert.Contains("SetToastElementAccessibility(button, name, helpText);", overlayAccessibilityBlock);

        var pinnedBlock = GetMethodBlock(toastCode, "private void ApplyPinnedState(bool pinned)");
        Assert.Contains("RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);", pinnedBlock);

        var setMetadataBlock = GetMethodBlock(toastCode, "private static void SetToastElementAccessibility(FrameworkElement element, string name, string helpText)");
        Assert.Contains("element.ToolTip = helpText;", setMetadataBlock);
        Assert.Contains("AutomationProperties.SetName(element, name);", setMetadataBlock);
        Assert.Contains("AutomationProperties.SetHelpText(element, helpText);", setMetadataBlock);
    }

    [Fact]
    public void PreviewSaveActionsGuardAgainstRepeatedActivation()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));
        var previewStateCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private bool _isSavingPreview;", previewStateCode);
        Assert.Contains("if (_isSavingPreview || _isFading)", previewCode);
        Assert.Contains("_isSavingPreview = true;", previewCode);
        Assert.Contains("SaveBtn.IsEnabled = false;", previewCode);
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(SaveBtn, \"Saving preview\", \"Save is already running.\");", previewCode);
        Assert.Contains("_isSavingPreview = false;", previewCode);
        Assert.Contains("SaveBtn.IsEnabled = true;", previewCode);
        Assert.Contains("RefreshPreviewOverlayButtonAccessibility(SaveBtn, \"Save preview\", \"Save this preview image.\");", previewCode);

        var previewKeyHelperBlock = GetMethodBlock(previewCode, "private static bool CanActivateKeyboardControl(object sender, WpfKeyEventArgs e)");
        Assert.Contains("IsKeyboardActivateKey(e)", previewKeyHelperBlock);
        Assert.Contains("sender is not UIElement { IsEnabled: false }", previewKeyHelperBlock);

        var previewMouseHelperBlock = GetMethodBlock(previewCode, "private static bool CanActivateMouseControl(object sender)");
        Assert.Contains("sender is not UIElement { IsEnabled: false }", previewMouseHelperBlock);

        var previewSaveKeyBlock = GetMethodBlock(previewCode, "private void SaveBtn_KeyDown(object sender, WpfKeyEventArgs e)");
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", previewSaveKeyBlock);
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(previewCode, "private void CloseBtn_KeyDown(object sender, WpfKeyEventArgs e)"));
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(previewCode, "private void PinBtn_KeyDown(object sender, WpfKeyEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(previewCode, "private void SaveClick(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(previewCode, "private void CloseClick(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(previewCode, "private void PinClick(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("var saved = false;", previewCode);
        Assert.Contains("saved = saveResult.Saved;", previewCode);
        Assert.Contains("if (saved)", previewCode);
        Assert.Contains("ShowPreviewSaveError(saveResult.ErrorMessage);", previewCode);
        var saveBlock = GetMethodBlock(previewCode, "private void SavePreview()");
        Assert.Contains("RefreshPreviewWindowTooltip();", saveBlock);
        Assert.Contains("OddSnap could not save the preview. Choose another folder or check write permissions.", previewCode);
        Assert.Contains("ToastWindow.ShowError(\"Save failed\", body, GetExistingPreviewFilePathOrNull());", previewCode);
        Assert.Contains("var remainingAutoDismissSeconds = PausePreviewAutoDismiss();", previewCode);
        Assert.Contains("ResumePreviewAutoDismiss(remainingAutoDismissSeconds);", previewCode);
        Assert.Contains("RunPreviewSaveOperation(() => dlg.ShowDialog(this)", previewCode);
        Assert.Contains("PreviewSaveOperationResult.Failed(\"No preview image is available to save.\")", previewCode);
        Assert.DoesNotContain("? PreviewSaveOperationResult.Canceled", previewCode);

        Assert.Contains("private bool _isSavingPreview;", toastCode);
        Assert.Contains("if (_previewBitmap is null || _isSavingPreview)", toastCode);
        Assert.Contains("_isSavingPreview = true;", toastCode);
        Assert.Contains("SaveBtn.IsEnabled = false;", toastCode);
        Assert.Contains("RefreshOverlayButtonAccessibility(SaveBtn, Helpers.ToastButtonKind.Save);", toastCode);
        Assert.Contains("var wasPinnedBeforeSave = _isPinned;", toastCode);
        Assert.Contains("var remainingAutoDismissSeconds = PauseToastAutoDismiss();", toastCode);
        Assert.Contains("if (!wasPinnedBeforeSave)", toastCode);
        Assert.Contains("ResumeToastAutoDismiss(remainingAutoDismissSeconds);", toastCode);
        Assert.Contains("_isSavingPreview = false;", toastCode);
        Assert.Contains("SaveBtn.IsEnabled = true;", toastCode);

        var toastKeyHelperBlock = GetMethodBlock(toastCode, "private static bool CanActivateKeyboardControl(object sender, System.Windows.Input.KeyEventArgs e)");
        Assert.Contains("IsKeyboardActivateKey(e)", toastKeyHelperBlock);
        Assert.Contains("sender is not UIElement { IsEnabled: false }", toastKeyHelperBlock);

        var toastMouseHelperBlock = GetMethodBlock(toastCode, "private static bool CanActivateMouseControl(object sender)");
        Assert.Contains("sender is not UIElement { IsEnabled: false }", toastMouseHelperBlock);

        var toastSaveKeyBlock = GetMethodBlock(toastCode, "private void SaveBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)");
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", toastSaveKeyBlock);
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(toastCode, "private void CloseBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)"));
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(toastCode, "private void PinBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)"));
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(toastCode, "private void OfficeBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)"));
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(toastCode, "private async void AiRedirectBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)"));
        Assert.Contains("if (!CanActivateKeyboardControl(sender, e))", GetMethodBlock(toastCode, "private void DeleteBtn_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private void SaveBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private void CloseBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private void PinBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private void OfficeBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private async void AiRedirectBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("if (!CanActivateMouseControl(sender))", GetMethodBlock(toastCode, "private void DeleteBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)"));
        Assert.Contains("BuildToastActionFailureBody(\"OddSnap could not save the preview. Choose another folder or check write permissions.\", ex.Message)", toastCode);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", toastCode);
        Assert.DoesNotContain("Show(ToastSpec.Error(\"Save failed\", ex.Message));", toastCode);

        var toastResumeBlock = GetMethodBlock(toastCode, "private void ResumeToastAutoDismiss(double remainingSeconds)");
        Assert.Contains("_isPinned = false;", toastResumeBlock);
        Assert.Contains("RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);", toastResumeBlock);
        Assert.Contains("if (_isHovered)", toastResumeBlock);
        Assert.Contains("_timer.Interval = TimeSpan.FromSeconds(remainingSeconds);", toastResumeBlock);
        Assert.Contains("_timer.Start();", toastResumeBlock);

        var savePinSnapshotIndex = toastCode.IndexOf("var wasPinnedBeforeSave = _isPinned;", StringComparison.Ordinal);
        var saveCancelRestoreIndex = toastCode.IndexOf("ResumeToastAutoDismiss(remainingAutoDismissSeconds);", savePinSnapshotIndex, StringComparison.Ordinal);
        Assert.True(saveCancelRestoreIndex > savePinSnapshotIndex, "Save cancel should restore auto-dismiss after capturing the previous pin state.");
    }

    [Theory]
    [InlineData(1.0, 5.5, 5.5)]
    [InlineData(0.5, 6.0, 3.0)]
    [InlineData(0.0, 6.0, 0.1)]
    [InlineData(2.0, 6.0, 6.0)]
    [InlineData(-1.0, 6.0, 0.1)]
    public void PreviewAutoDismissResumeUsesRemainingTime(double progressScale, double durationSeconds, double expectedSeconds)
    {
        var method = typeof(PreviewWindow).GetMethod(
            "GetPreviewAutoDismissRemainingSeconds",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = Assert.IsType<double>(method.Invoke(null, new object[] { progressScale, durationSeconds }));
        Assert.Equal(expectedSeconds, actual, precision: 3);
    }

    [Theory]
    [InlineData(1.0, 5.5, 5.5)]
    [InlineData(0.5, 6.0, 3.0)]
    [InlineData(0.0, 6.0, 0.1)]
    [InlineData(2.0, 6.0, 6.0)]
    [InlineData(-1.0, 6.0, 0.1)]
    public void ToastAutoDismissResumeUsesRemainingTime(double progressScale, double durationSeconds, double expectedSeconds)
    {
        var method = typeof(ToastWindow).GetMethod(
            "GetToastAutoDismissRemainingSeconds",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = Assert.IsType<double>(method.Invoke(null, new object[] { progressScale, durationSeconds }));
        Assert.Equal(expectedSeconds, actual, precision: 3);
    }

    [Fact]
    public void PreviewSaveOperationReportsCancelSuccessAndFailure()
    {
        var method = typeof(PreviewWindow).GetMethod(
            "RunPreviewSaveOperation",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var saved = false;
        var canceled = InvokePreviewSaveOperation(method, () => false, () => saved = true);
        Assert.False(GetPreviewSaveResultSaved(canceled));
        Assert.Null(GetPreviewSaveResultError(canceled));
        Assert.False(saved);

        var succeeded = InvokePreviewSaveOperation(method, () => true, () => saved = true);
        Assert.True(GetPreviewSaveResultSaved(succeeded));
        Assert.Null(GetPreviewSaveResultError(succeeded));
        Assert.True(saved);

        var failed = InvokePreviewSaveOperation(method, () => true, () => throw new InvalidOperationException("disk full"));
        Assert.False(GetPreviewSaveResultSaved(failed));
        Assert.Equal("disk full", GetPreviewSaveResultError(failed));
    }

    [Fact]
    public void PreviewGifSaveMissingSourceFailsBeforeOpeningDialog()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        Assert.Contains("private bool HasSavedPreviewFileOnDisk()", previewCode);
        Assert.Contains("=> !string.IsNullOrWhiteSpace(_savedFilePath) && File.Exists(_savedFilePath);", previewCode);

        var saveBlock = GetMethodBlock(previewCode, "private void SavePreview()");
        Assert.Contains("if (!HasSavedPreviewFileOnDisk())", saveBlock);
        Assert.Contains("saveResult = PreviewSaveOperationResult.Failed(\"The saved file is no longer on disk.\");", saveBlock);
        Assert.Contains("FileName = Path.GetFileName(_savedFilePath)", saveBlock);
        Assert.Contains("File.Copy(_savedFilePath!, dlg.FileName!, true)", saveBlock);
        Assert.DoesNotContain("_savedFilePath is null", saveBlock);

        var gifBranchIndex = saveBlock.IndexOf("if (_isGif)", StringComparison.Ordinal);
        var missingFileIndex = saveBlock.IndexOf("if (!HasSavedPreviewFileOnDisk())", gifBranchIndex, StringComparison.Ordinal);
        var dialogIndex = saveBlock.IndexOf("new SaveFileDialog", missingFileIndex, StringComparison.Ordinal);

        Assert.True(missingFileIndex > gifBranchIndex, "GIF saves should validate the backing file before preparing save UI.");
        Assert.True(dialogIndex > missingFileIndex, "GIF saves should only create the Save dialog after the backing file exists.");
    }

    [Fact]
    public void PreviewGifClipboardFailuresAreVisible()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));

        var constructorBlock = GetMethodBlock(previewCode, "public PreviewWindow(string gifFilePath)");
        Assert.Contains("TryCopyGifFileToClipboard(gifFilePath);", constructorBlock);
        Assert.Contains("InitializeComponent();", constructorBlock);

        var helperBlock = GetMethodBlock(previewCode, "private static bool TryCopyGifFileToClipboard(string gifFilePath)");
        Assert.Contains("System.Windows.Clipboard.SetFileDropList(files);", helperBlock);
        Assert.Contains("OddSnap could not copy the GIF file. The preview will still open; save or drag the GIF manually.", helperBlock);
        Assert.Contains("gifFilePath);", helperBlock);
        Assert.Contains("return false;", helperBlock);

        var copyIndex = constructorBlock.IndexOf("TryCopyGifFileToClipboard(gifFilePath);", StringComparison.Ordinal);
        var initIndex = constructorBlock.IndexOf("InitializeComponent();", copyIndex, StringComparison.Ordinal);
        var commonIndex = constructorBlock.IndexOf("InitCommon();", initIndex, StringComparison.Ordinal);
        var currentIndex = constructorBlock.IndexOf("_current = this;", commonIndex, StringComparison.Ordinal);
        Assert.True(initIndex > copyIndex, "GIF preview should still initialize after reporting clipboard failures.");
        Assert.True(currentIndex > commonIndex, "GIF preview should only become current after controls and common state initialize.");

        var thumbnailBlock = GetMethodBlock(previewCode, "private void SetGifThumbnail(string gifPath)");
        Assert.Contains("ThumbnailImage.Source = bitmapImage;", thumbnailBlock);
        Assert.Contains("catch (Exception ex)", thumbnailBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"preview.gif-thumbnail\"", thumbnailBlock);
        Assert.DoesNotContain("catch { }", thumbnailBlock);
    }

    [Fact]
    public void PreviewConstructorsPublishCurrentAfterInitialization()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));

        var bitmapConstructor = GetMethodBlock(previewCode, "public PreviewWindow(Bitmap screenshot, string? savedFilePath = null)");
        var gifConstructor = GetMethodBlock(previewCode, "public PreviewWindow(string gifFilePath)");

        AssertPreviewConstructorPublishesCurrentAfterInit(bitmapConstructor);
        AssertPreviewConstructorPublishesCurrentAfterInit(gifConstructor);

        var replacementBlock = GetMethodBlock(previewCode, "private static void CloseCurrentForReplacement()");
        Assert.Contains("var current = _current;", replacementBlock);
        Assert.Contains("if (current.Dispatcher.CheckAccess())", replacementBlock);
        Assert.Contains("current.ForceClose();", replacementBlock);
        Assert.Contains("current.Dispatcher.BeginInvoke(current.ForceClose);", replacementBlock);

        var dismissBlock = GetMethodBlock(previewCode, "public static void DismissCurrent()");
        Assert.Contains("var current = _current;", dismissBlock);
        Assert.Contains("if (current.Dispatcher.CheckAccess())", dismissBlock);
        Assert.Contains("current.ForceCloseIfStillCurrent();", dismissBlock);
        Assert.Contains("current.Dispatcher.BeginInvoke(current.ForceCloseIfStillCurrent);", dismissBlock);

        var attachBlock = GetMethodBlock(previewCode, "public static void AttachUploadedLink(string localPath, string url, string provider)");
        Assert.Contains("var current = _current;", attachBlock);
        Assert.DoesNotContain("current._savedFilePath", attachBlock);
        Assert.DoesNotContain("_current._savedFilePath", attachBlock);
        Assert.Contains("current.AttachUploadedLinkOnOwnerDispatcher(localPath, url, provider);", attachBlock);
        Assert.Contains("current.Dispatcher.BeginInvoke(() => current.AttachUploadedLinkOnOwnerDispatcher(localPath, url, provider));", attachBlock);

        var attachOwnerBlock = GetMethodBlock(previewCode, "private void AttachUploadedLinkOnOwnerDispatcher(string localPath, string url, string provider)");
        Assert.Contains("if (_current != this) return;", attachOwnerBlock);
        Assert.Contains("if (_savedFilePath is null) return;", attachOwnerBlock);
        Assert.Contains("string.Equals(_savedFilePath, localPath, StringComparison.OrdinalIgnoreCase)", attachOwnerBlock);
        Assert.Contains("SetUploadedLink(url, provider);", attachOwnerBlock);

        var forceCloseCurrentBlock = GetMethodBlock(previewCode, "private void ForceCloseIfStillCurrent()");
        Assert.Contains("if (_current != this) return;", forceCloseCurrentBlock);
        Assert.Contains("ForceClose();", forceCloseCurrentBlock);
    }

    [Fact]
    public void PreviewOpenTargetGuardsAgainstRepeatedActivation()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));
        var previewStateCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));

        Assert.Contains("private const int PreviewTargetOpenCooldownMs = 900;", previewStateCode);
        Assert.Contains("private bool _isOpeningPreviewTarget;", previewStateCode);
        Assert.Contains("private DispatcherTimer? _previewTargetOpenCooldownTimer;", previewStateCode);
        Assert.Contains("OpenPreviewTarget();", previewCode);
        Assert.Contains("if (_isOpeningPreviewTarget || _isFading)", previewCode);
        Assert.Contains("_isOpeningPreviewTarget = true;", previewCode);
        Assert.Contains("var opened = false;", previewCode);
        Assert.Contains("opened = true;", previewCode);
        Assert.Contains("ResetPreviewTargetOpenGuardAfterCooldown();", previewCode);
        Assert.Contains("private void ResetPreviewTargetOpenGuardAfterCooldown()", previewCode);
        var cooldownBlock = GetMethodBlock(previewCode, "private void ResetPreviewTargetOpenGuardAfterCooldown()");
        Assert.Contains("_previewTargetOpenCooldownTimer?.Stop();", cooldownBlock);
        Assert.Contains("_previewTargetOpenCooldownTimer = new DispatcherTimer", cooldownBlock);
        Assert.Contains("_previewTargetOpenCooldownTimer = null;", cooldownBlock);
        Assert.Contains("RefreshPreviewWindowTooltip();", cooldownBlock);
        Assert.Contains("private void CancelActivePreviewState()", previewCode);
        var cancelBlock = GetMethodBlock(previewCode, "private void CancelActivePreviewState()");
        Assert.Contains("_previewTargetOpenCooldownTimer?.Stop();", cancelBlock);
        Assert.Contains("_previewTargetOpenCooldownTimer = null;", cancelBlock);
        Assert.Contains("_isOpeningPreviewTarget = false;", cancelBlock);
        Assert.Contains("_isSavingPreview = false;", cancelBlock);
        Assert.Contains("_mouseIsDown = false;", cancelBlock);
        Assert.Contains("SaveBtn.IsEnabled = true;", cancelBlock);
        Assert.Contains("if (opened)", previewCode);
        Assert.Contains("_isOpeningPreviewTarget = false;", previewCode);
        Assert.Contains("else if (!string.IsNullOrWhiteSpace(_savedFilePath))", previewCode);
        Assert.Contains("ShowPreviewOpenError(\"The saved file is no longer on disk.\");", previewCode);
        Assert.Contains("ShowPreviewUploadFallback(_savedFilePath);", previewCode);
        Assert.Contains("private static void ShowPreviewUploadFallback(string filePath)", previewCode);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"Upload link unavailable\", \"Opened local file.\", filePath) with { SuppressSound = true });", previewCode);
        Assert.Contains("ShowPreviewUploadUnavailableMissingFile();", previewCode);
        Assert.Contains("private void ShowPreviewUploadUnavailableMissingFile()", previewCode);
        Assert.Contains("SetPreviewWindowStatusTooltip(\"Upload link unavailable - saved file missing\");", previewCode);
        Assert.Contains("ToastWindow.ShowError(\"Upload link unavailable\", \"The upload link could not be opened, and the saved file is no longer on disk.\");", previewCode);
        Assert.Contains("ShowPreviewUploadUnavailableNoLocalFile();", previewCode);
        Assert.Contains("private void ShowPreviewUploadUnavailableNoLocalFile()", previewCode);
        Assert.Contains("SetPreviewWindowStatusTooltip(\"Upload link unavailable - no local file\");", previewCode);
        Assert.Contains("ToastWindow.ShowError(\"Upload link unavailable\", \"The upload link could not be opened, and no local file is available.\");", previewCode);
        Assert.Contains("ShowPreviewNoOpenTarget();", previewCode);
        Assert.Contains("private void ShowPreviewNoOpenTarget()", previewCode);
        Assert.Contains("SetPreviewWindowStatusTooltip(\"Preview only - no saved file to open\");", previewCode);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"Preview only\", \"No saved file to open.\") with { SuppressSound = true });", previewCode);
        Assert.Contains("ShowPreviewOpenError(ex.Message);", previewCode);
        Assert.Contains("private void ShowPreviewOpenError(string message)", previewCode);
        Assert.Contains("OddSnap could not open the saved file location. The file is still saved; open it from History or try again.", previewCode);
        Assert.Contains("OddSnap could not open the preview target. Capture again or check History for another copy.", previewCode);
        Assert.Contains("BuildPreviewFailureBody(", previewCode);
        Assert.Contains("GetExistingPreviewFilePathOrNull()", previewCode);
        Assert.Contains("SetPreviewWindowStatusTooltip($\"Open failed - {message}\");", previewCode);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", body, GetExistingPreviewFilePathOrNull());", previewCode);

        var openBlock = GetMethodBlock(previewCode, "private void OpenPreviewTarget()");
        Assert.Contains("RefreshPreviewWindowTooltip();", openBlock);
        var uploadCatchIndex = openBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var uploadLogIndex = openBlock.IndexOf("AppDiagnostics.LogWarning(\"preview.upload-link-open\"", uploadCatchIndex, StringComparison.Ordinal);
        var deadIndex = openBlock.IndexOf("_uploadDead = true;", uploadCatchIndex, StringComparison.Ordinal);
        var missingIndex = openBlock.IndexOf("ShowPreviewUploadUnavailableMissingFile();", deadIndex, StringComparison.Ordinal);
        var noLocalIndex = openBlock.IndexOf("ShowPreviewUploadUnavailableNoLocalFile();", missingIndex, StringComparison.Ordinal);
        var rawErrorIndex = openBlock.IndexOf("ShowPreviewOpenError(ex.Message);", noLocalIndex, StringComparison.Ordinal);
        Assert.True(uploadLogIndex > uploadCatchIndex && uploadLogIndex < deadIndex, "Dead upload link failures should be logged before fallback handling.");
        Assert.True(deadIndex > uploadCatchIndex, "Dead upload links should be marked before fallback handling.");
        Assert.True(missingIndex > deadIndex, "Dead upload links with missing local files should show targeted missing-file feedback.");
        Assert.True(noLocalIndex > missingIndex, "Dead upload links without a local file should show targeted no-local-file feedback.");
        Assert.True(rawErrorIndex > noLocalIndex, "Raw browser errors should only be used outside upload-link fallback handling.");
    }

    [Fact]
    public void PreviewDragMissingFileShowsVisibleError()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        Assert.Contains("ShowPreviewDragError(\"The saved file is no longer on disk.\");", previewCode);
        Assert.Contains("ShowPreviewDragError(\"No preview file is available to drag.\");", previewCode);
        Assert.Contains("private void ShowPreviewDragError(string message)", previewCode);
        Assert.Contains("OddSnap could not start the drag. The file is still saved; open it from History or try again.", previewCode);
        Assert.Contains("OddSnap could not start the drag. The preview is still open; use Save or try the capture again.", previewCode);
        Assert.Contains("SetPreviewWindowStatusTooltip($\"Drag failed - {message}\");", previewCode);
        Assert.Contains("ToastWindow.ShowError(\"Drag failed\", body, GetExistingPreviewFilePathOrNull());", previewCode);
        Assert.Contains("private bool IsPreviewOverlayButtonSource(DependencyObject? source)", previewCode);
        Assert.Contains("IsChildOf(source, CloseBtn)", previewCode);
        Assert.Contains("IsChildOf(source, PinBtn)", previewCode);
        Assert.Contains("IsChildOf(source, SaveBtn)", previewCode);
        Assert.Contains("catch (Exception ex)", previewCode);
        Assert.Contains("private void ResetPreviewDragFeedback()", previewCode);
        Assert.Contains("DragScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(1, 140, Motion.SmoothOut));", previewCode);
        Assert.Contains("BeginAnimation(OpacityProperty, Motion.To(1, 140, Motion.SoftOut));", previewCode);
        Assert.Contains("SlideX.BeginAnimation(TranslateTransform.XProperty, Motion.To(0, 140, Motion.SmoothOut));", previewCode);
        Assert.Contains("ShowPreviewDragError(ex.Message);", previewCode);

        var dragBlock = GetMethodBlock(previewCode, "protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)");
        var mouseDownBlock = GetMethodBlock(previewCode, "protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)");
        var mouseUpBlock = GetMethodBlock(previewCode, "protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)");
        Assert.Contains("if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))", mouseDownBlock);
        Assert.Contains("CancelPreviewRootInteractionFromOverlaySource(e);", mouseDownBlock);
        Assert.Contains("if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))", dragBlock);
        Assert.Contains("CancelPreviewRootInteractionFromOverlaySource(e);", dragBlock);
        Assert.Contains("if (IsPreviewOverlayButtonSource(e.OriginalSource as DependencyObject))", mouseUpBlock);
        Assert.Contains("CancelPreviewRootInteractionFromOverlaySource(e);", mouseUpBlock);
        var cancelPreviewBlock = GetMethodBlock(previewCode, "private void CancelPreviewRootInteractionFromOverlaySource(System.Windows.Input.MouseEventArgs e)");
        Assert.Contains("_mouseIsDown = false;", cancelPreviewBlock);
        Assert.Contains("e.Handled = true;", cancelPreviewBlock);
        var missingFileIndex = dragBlock.IndexOf("if (!string.IsNullOrWhiteSpace(_savedFilePath))", StringComparison.Ordinal);
        var showErrorIndex = dragBlock.IndexOf("ShowPreviewDragError(\"The saved file is no longer on disk.\");", StringComparison.Ordinal);
        var noPreviewErrorIndex = dragBlock.IndexOf("ShowPreviewDragError(\"No preview file is available to drag.\");", StringComparison.Ordinal);
        var returnIndex = dragBlock.IndexOf("return;", showErrorIndex, StringComparison.Ordinal);
        var tryIndex = dragBlock.IndexOf("try", StringComparison.Ordinal);
        var tempSaveIndex = dragBlock.IndexOf("CaptureOutputService.SavePng(_screenshot, tmpFile);", StringComparison.Ordinal);
        var doDragDropIndex = dragBlock.IndexOf("var dragResult = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);", StringComparison.Ordinal);
        var dragCancelIndex = dragBlock.IndexOf("if (dragResult == DragDropEffects.None)", doDragDropIndex, StringComparison.Ordinal);
        var dragResetIndex = dragBlock.IndexOf("ResetPreviewDragFeedback();", dragCancelIndex, StringComparison.Ordinal);
        var dragFailureIndex = dragBlock.IndexOf("ShowPreviewDragError(ex.Message);", dragResetIndex, StringComparison.Ordinal);
        var dismissIndex = dragBlock.IndexOf("AnimateDismiss();", doDragDropIndex, StringComparison.Ordinal);

        Assert.True(showErrorIndex > missingFileIndex, "Preview drag should show a missing-file error before returning.");
        Assert.True(noPreviewErrorIndex > showErrorIndex, "Preview drag should show a no-preview error when no saved path exists.");
        Assert.True(returnIndex > showErrorIndex, "Preview drag should return after reporting the missing file.");
        Assert.True(tempSaveIndex > tryIndex, "Preview drag temp-file creation should be inside the drag failure guard.");
        Assert.True(dragCancelIndex > doDragDropIndex, "Preview drag should inspect the drag result before dismissing.");
        Assert.True(dragResetIndex > dragCancelIndex, "Preview drag should reset visual state when the drag is canceled.");
        Assert.True(dragFailureIndex > dragResetIndex, "Preview drag should report temp-file and DoDragDrop failures after visual reset.");
        Assert.True(dismissIndex > doDragDropIndex, "Preview drag should still dismiss after successful drag sessions.");
        Assert.Contains("File.Delete(tmpFile);", dragBlock);
        Assert.Contains("catch (Exception ex)", dragBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"preview.drag-temp-delete\"", dragBlock);
        Assert.DoesNotContain("try { File.Delete(tmpFile); } catch { }", dragBlock);
    }

    [Fact]
    public void ToastAiRedirectActionGuardsAgainstRepeatedActivation()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private bool _isOpeningAiRedirect;", toastCode);
        Assert.Contains("private int _toastStateVersion;", toastCode);
        Assert.Contains("if (_isOpeningAiRedirect)", toastCode);
        Assert.Contains("if (!HasSavedFileOnDisk())", toastCode);
        Assert.Contains("ShowSavedFileMissingError();", toastCode);
        Assert.Contains("_isOpeningAiRedirect = true;", toastCode);
        Assert.Contains("AiRedirectBtn.IsEnabled = false;", toastCode);
        Assert.Contains("RefreshOverlayButtonAccessibility(AiRedirectBtn, Helpers.ToastButtonKind.AiRedirect);", toastCode);
        Assert.Contains("_isOpeningAiRedirect = false;", toastCode);
        Assert.Contains("AiRedirectBtn.IsEnabled = true;", toastCode);

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        Assert.Contains("BuildToastActionFailureBody(\"OddSnap could not finish AI Redirect. Check Settings -> Uploads or open the saved file from History.\", ex.Message)", aiRedirectBlock);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", aiRedirectBlock);
        Assert.DoesNotContain("Show(ToastSpec.Error(\"AI Redirect failed\", ex.Message));", aiRedirectBlock);
    }

    [Fact]
    public void ToastDeleteActionGuardsAgainstRepeatedActivation()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private bool _isDeletingSavedFile;", toastCode);
        Assert.Contains("if (_isDeletingSavedFile)", toastCode);
        Assert.Contains("if (!HasSavedFileOnDisk())", toastCode);
        Assert.Contains("ShowSavedFileMissingError();", toastCode);
        Assert.Contains("_isDeletingSavedFile = true;", toastCode);
        Assert.Contains("DeleteBtn.IsEnabled = false;", toastCode);
        Assert.Contains("RefreshOverlayButtonAccessibility(DeleteBtn, Helpers.ToastButtonKind.Delete);", toastCode);
        Assert.Contains("_isDeletingSavedFile = false;", toastCode);
        Assert.Contains("DeleteBtn.IsEnabled = true;", toastCode);

        var deleteBlock = GetMethodBlock(toastCode, "private void DeleteSavedFile()");
        Assert.Contains("BuildToastActionFailureBody(\"OddSnap could not delete the saved file. Open it from History or delete it manually in File Explorer.\", ex.Message)", deleteBlock);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", deleteBlock);
        Assert.DoesNotContain("Show(ToastSpec.Error(\"Delete failed\", ex.Message));", deleteBlock);
    }

    [Fact]
    public void ToastSavedFileActionsRequireExistingFile()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("(kind != Helpers.ToastButtonKind.AiRedirect || CanShowAiRedirectButton())", toastCode);
        Assert.Contains("(kind != Helpers.ToastButtonKind.Delete || HasSavedFileOnDisk())", toastCode);
        Assert.Contains("private bool HasSavedFileOnDisk()", toastCode);
        Assert.Contains("=> !string.IsNullOrWhiteSpace(_savedFilePath) && File.Exists(_savedFilePath);", toastCode);
        Assert.Contains("private static bool SavedFilePathStillExists(string filePath)", toastCode);
        Assert.Contains("Show(ToastSpec.Error(\"File missing\", \"The saved file is no longer on disk.\", filePath ?? _savedFilePath));", toastCode);

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        Assert.Contains("if (!HasSavedFileOnDisk())", aiRedirectBlock);
        Assert.Contains("ShowSavedFileMissingError();", aiRedirectBlock);
        Assert.Contains("if (!SavedFilePathStillExists(savedFilePath))", aiRedirectBlock);
        Assert.Contains("ShowSavedFileMissingError(savedFilePath);", aiRedirectBlock);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(_savedFilePath) || !File.Exists(_savedFilePath)", aiRedirectBlock);

        var deleteBlock = GetMethodBlock(toastCode, "private void DeleteSavedFile()");
        Assert.Contains("if (!SavedFilePathStillExists(deletePath))", deleteBlock);
        Assert.Contains("ShowSavedFileMissingError(deletePath);", deleteBlock);
        Assert.Contains("File.Delete(deletePath);", deleteBlock);
        Assert.Contains("_isDeletingSavedFile = false;", deleteBlock);
        Assert.Contains("DeleteBtn.IsEnabled = true;", deleteBlock);
        Assert.DoesNotContain("if (File.Exists(deletePath))", deleteBlock);

        var deleteIndex = deleteBlock.IndexOf("File.Delete(deletePath);", StringComparison.Ordinal);
        var resetIndex = deleteBlock.IndexOf("_isDeletingSavedFile = false;", deleteIndex, StringComparison.Ordinal);
        var enableIndex = deleteBlock.IndexOf("DeleteBtn.IsEnabled = true;", resetIndex, StringComparison.Ordinal);
        var dismissedIndex = deleteBlock.IndexOf("DismissAnimated();", enableIndex, StringComparison.Ordinal);
        Assert.True(resetIndex > deleteIndex, "Toast delete should reset the in-flight guard after a successful delete.");
        Assert.True(enableIndex > resetIndex, "Toast delete should re-enable the button after clearing the guard.");
        Assert.True(dismissedIndex > enableIndex, "Toast delete should reset action state before dismissing.");
    }

    [Fact]
    public void ToastAiRedirectReadyDoesNotAttachMissingSavedFile()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        Assert.Contains("var savedFilePath = _savedFilePath!;", aiRedirectBlock);
        Assert.Contains("UploadService.UploadAsync(savedFilePath", aiRedirectBlock);
        Assert.Contains("TryCopyAiRedirectPreviewToClipboard(_previewBitmap, savedFilePath)", aiRedirectBlock);
        Assert.Contains("ToastSpec.Standard(\"AI Redirect Ready\", $\"Opened {providerName}.\", GetExistingSavedFilePathOrNull())", aiRedirectBlock);
        Assert.Contains("BuildGoogleLensUploadFailureBody(UploadService.GetName(hostDest), result.Error, result.IsRateLimit)", aiRedirectBlock);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", aiRedirectBlock);
        Assert.DoesNotContain("ToastSpec.Standard(\"AI Redirect Ready\", $\"Opened {providerName}.\", _savedFilePath)", aiRedirectBlock);
        Assert.DoesNotContain("Show(ToastSpec.Error(\"Google Lens upload failed\", result.Error));", aiRedirectBlock);

        var failureBodyBlock = GetMethodBlock(toastCode, "private static string BuildGoogleLensUploadFailureBody(string providerName, string? error, bool isRateLimit)");
        Assert.Contains("Upload returned no link.", failureBodyBlock);
        Assert.Contains("Try another upload destination or wait before retrying Google Lens.", failureBodyBlock);
        Assert.Contains("Check {providerLabel} settings or try another upload destination for Google Lens.", failureBodyBlock);
    }

    [Fact]
    public void ToastAiRedirectCopyFailureStillOpensTarget()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var copyHelper = GetMethodBlock(toastCode, "private static bool TryCopyAiRedirectPreviewToClipboard(Bitmap previewBitmap, string savedFilePath)");
        Assert.Contains("ClipboardService.CopyToClipboard(previewBitmap, savedFilePath);", copyHelper);
        Assert.Contains("return true;", copyHelper);
        Assert.Contains("return false;", copyHelper);

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        var copyIndex = aiRedirectBlock.IndexOf("var copySucceeded = _previewBitmap is null || TryCopyAiRedirectPreviewToClipboard(_previewBitmap, savedFilePath);", StringComparison.Ordinal);
        var openIndex = aiRedirectBlock.IndexOf("if (!OpenExternalUrl(startUrl, GetExistingSavedFilePathOrNull()))", copyIndex, StringComparison.Ordinal);
        var pinIndex = aiRedirectBlock.IndexOf("ApplyPinnedState(true);", openIndex, StringComparison.Ordinal);
        var fallbackTooltipIndex = aiRedirectBlock.IndexOf("Clipboard copy failed; drag the image from this toast.", pinIndex, StringComparison.Ordinal);

        Assert.True(copyIndex >= 0, "Toast AI Redirect should guard preview image copy.");
        Assert.True(openIndex > copyIndex, "Toast AI Redirect should still open the target after image-copy failures.");
        Assert.True(pinIndex > openIndex, "Toast AI Redirect should keep the image toast pinned after opening the target.");
        Assert.True(fallbackTooltipIndex > pinIndex, "Toast AI Redirect should leave a drag fallback hint after image-copy failures.");
    }

    [Fact]
    public void ToastAiRedirectOpenFailureDoesNotShowReadyState()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var openBlock = GetMethodBlock(toastCode, "private static bool OpenExternalUrl(string url, string? filePath = null)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", openBlock);
        Assert.Contains("Show(ToastSpec.Error(\"Open failed\", \"No link is available.\", filePath));", openBlock);
        Assert.Contains("System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo", openBlock);
        Assert.Contains("UseShellExecute = true", openBlock);
        Assert.Contains("catch (Exception ex)", openBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.external-url-open\"", openBlock);
        Assert.Contains("OddSnap could not open the link. Try again from the toast, or open the link manually if it is still visible.", openBlock);
        Assert.Contains("filePath));", openBlock);
        Assert.DoesNotContain("Show(ToastSpec.Error(\"Open failed\", ex.Message));", openBlock);
        Assert.Contains("return false;", openBlock);

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        var lensOpenIndex = aiRedirectBlock.IndexOf("if (!OpenExternalUrl(UploadService.BuildGoogleLensUrl(result.Url), GetExistingSavedFilePathOrNull()))", StringComparison.Ordinal);
        var lensReturnIndex = aiRedirectBlock.IndexOf("return;", lensOpenIndex, StringComparison.Ordinal);
        var lensReadyIndex = aiRedirectBlock.IndexOf("ToastSpec.Standard(\"AI Redirect Ready\"", lensReturnIndex, StringComparison.Ordinal);
        Assert.True(lensReturnIndex > lensOpenIndex, "Google Lens redirect should return when the browser open fails.");
        Assert.True(lensReadyIndex > lensReturnIndex, "Google Lens ready feedback should only run after a successful browser open.");

        var chatOpenIndex = aiRedirectBlock.IndexOf("if (!OpenExternalUrl(startUrl, GetExistingSavedFilePathOrNull()))", StringComparison.Ordinal);
        var chatReturnIndex = aiRedirectBlock.IndexOf("return;", chatOpenIndex, StringComparison.Ordinal);
        var pinIndex = aiRedirectBlock.IndexOf("_spec = _spec with { ClickActionUrl = startUrl, ClickActionLabel = providerName };", chatReturnIndex, StringComparison.Ordinal);
        Assert.True(chatReturnIndex > chatOpenIndex, "AI chat redirect should return when the browser open fails.");
        Assert.True(pinIndex > chatReturnIndex, "AI chat ready state should only attach after a successful browser open.");
    }

    [Fact]
    public void ToastAiRedirectAsyncCompletionIgnoresReplacedToast()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private bool IsCurrentToastState(int stateVersion)", toastCode);
        Assert.Contains("=> _current == this && _toastStateVersion == stateVersion;", toastCode);

        var aiRedirectBlock = GetMethodBlock(toastCode, "private async Task OpenAiRedirectAsync()");
        Assert.Contains("var actionStateVersion = _toastStateVersion;", aiRedirectBlock);
        Assert.Contains("if (!IsCurrentToastState(actionStateVersion))", aiRedirectBlock);
        Assert.Contains("if (IsCurrentToastState(actionStateVersion))", aiRedirectBlock);

        var uploadIndex = aiRedirectBlock.IndexOf("await UploadService.UploadAsync(savedFilePath", StringComparison.Ordinal);
        var staleGuardIndex = aiRedirectBlock.IndexOf("if (!IsCurrentToastState(actionStateVersion))", uploadIndex, StringComparison.Ordinal);
        var successToastIndex = aiRedirectBlock.IndexOf("ToastSpec.Standard(\"AI Redirect Ready\"", StringComparison.Ordinal);
        Assert.True(staleGuardIndex > uploadIndex, "AI Redirect should check toast state after the async upload returns.");
        Assert.True(successToastIndex > staleGuardIndex, "AI Redirect should not show ready feedback before the stale-state guard.");

        var resetBlock = GetMethodBlock(toastCode, "private void CancelActiveToastState()");
        Assert.Contains("_toastStateVersion++;", resetBlock);
    }

    [Fact]
    public void ToastClickAndDragReportMissingSavedFile()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));
        var toastStaticCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.Static.cs"));

        Assert.Contains("private void ShowToastOpenError(string message)", toastCode);
        Assert.Contains("Show(ToastSpec.Error(\"Open failed\", message, _savedFilePath));", toastCode);
        Assert.Contains("private void ShowToastDragError(string message)", toastCode);
        Assert.Contains("Show(ToastSpec.Error(\"Drag failed\", message, _savedFilePath));", toastCode);

        var openLocationBlock = GetMethodBlock(toastStaticCode, "private static bool OpenFileLocation(string? filePath)");
        Assert.Contains("System.Diagnostics.Process.Start(\"explorer.exe\", $\"/select,\\\"{filePath}\\\"\");", openLocationBlock);
        Assert.Contains("catch (Exception ex)", openLocationBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.open-file-location\"", openLocationBlock);
        Assert.Contains("OddSnap could not open the saved file location. Try again from the toast, or open the folder manually.", openLocationBlock);
        Assert.Contains("filePath);", openLocationBlock);
        Assert.DoesNotContain("ShowError(\"Open failed\", ex.Message, filePath);", openLocationBlock);
        Assert.Contains("return false;", openLocationBlock);
        var fileLocationCatchIndex = openLocationBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var fileLocationLogIndex = openLocationBlock.IndexOf("AppDiagnostics.LogWarning(\"toast.open-file-location\"", fileLocationCatchIndex, StringComparison.Ordinal);
        var fileLocationErrorIndex = openLocationBlock.IndexOf("OddSnap could not open the saved file location.", fileLocationLogIndex, StringComparison.Ordinal);
        Assert.True(fileLocationLogIndex > fileLocationCatchIndex && fileLocationErrorIndex > fileLocationLogIndex,
            "Toast file-location failures should be logged before user-facing error feedback.");

        var mouseMoveBlock = GetMethodBlock(toastCode, "private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)");
        Assert.Contains("private bool IsToastOverlayButtonSource(DependencyObject? source)", toastCode);
        Assert.Contains("IsChildOf(source, TextCloseBtn)", toastCode);
        var mouseDownBlock = GetMethodBlock(toastCode, "private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)");
        Assert.Contains("if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))", mouseDownBlock);
        Assert.Contains("e.Handled = true;", mouseDownBlock);
        Assert.Contains("if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))", mouseMoveBlock);
        Assert.Contains("CancelRootInteractionFromOverlaySource(e);", mouseMoveBlock);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(_savedFilePath))", mouseMoveBlock);
        Assert.Contains("ShowSavedFileMissingError();", mouseMoveBlock);
        Assert.Contains("else", mouseMoveBlock);
        Assert.Contains("ShowToastDragError(\"No preview file is available to drag.\");", mouseMoveBlock);
        Assert.Contains("System.Windows.GiveFeedbackEventHandler? feedback = null;", mouseMoveBlock);
        Assert.Contains("var result = System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);", mouseMoveBlock);
        Assert.Contains("if (result == System.Windows.DragDropEffects.None)", mouseMoveBlock);
        Assert.Contains("catch (Exception ex)", mouseMoveBlock);
        Assert.Contains("EndDragFeedback(cancelled: true);", mouseMoveBlock);
        Assert.Contains("ShowToastDragError(ex.Message);", mouseMoveBlock);
        Assert.Contains("if (feedback is not null)", mouseMoveBlock);
        Assert.Contains("GiveFeedback -= feedback;", mouseMoveBlock);
        Assert.Contains("if (_savedFilePath is null && !string.IsNullOrWhiteSpace(dragFile) && File.Exists(dragFile))", mouseMoveBlock);
        Assert.Contains("File.Delete(dragFile);", mouseMoveBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.drag-temp-delete\"", mouseMoveBlock);
        Assert.DoesNotContain("try { File.Delete(dragFile); } catch { }", mouseMoveBlock);

        var feedbackDeclIndex = mouseMoveBlock.IndexOf("System.Windows.GiveFeedbackEventHandler? feedback = null;", StringComparison.Ordinal);
        var tryIndex = mouseMoveBlock.IndexOf("try", feedbackDeclIndex, StringComparison.Ordinal);
        var dragPathIndex = mouseMoveBlock.IndexOf("dragFile = GetDragFilePath();", StringComparison.Ordinal);
        var noDragFileIndex = mouseMoveBlock.IndexOf("if (dragFile is null)", dragPathIndex, StringComparison.Ordinal);
        var missingSavedIndex = mouseMoveBlock.IndexOf("ShowSavedFileMissingError();", noDragFileIndex, StringComparison.Ordinal);
        var noPreviewIndex = mouseMoveBlock.IndexOf("ShowToastDragError(\"No preview file is available to drag.\");", missingSavedIndex, StringComparison.Ordinal);
        var noDragReturnIndex = mouseMoveBlock.IndexOf("return;", noPreviewIndex, StringComparison.Ordinal);
        var dragDropIndex = mouseMoveBlock.IndexOf("var result = System.Windows.DragDrop.DoDragDrop", dragPathIndex, StringComparison.Ordinal);
        var cancelIndex = mouseMoveBlock.IndexOf("if (result == System.Windows.DragDropEffects.None)", dragDropIndex, StringComparison.Ordinal);
        var cancelResetIndex = mouseMoveBlock.IndexOf("EndDragFeedback(cancelled: true);", cancelIndex, StringComparison.Ordinal);
        var cancelReturnIndex = mouseMoveBlock.IndexOf("return;", cancelResetIndex, StringComparison.Ordinal);
        var successDismissIndex = mouseMoveBlock.IndexOf("DismissAnimated();", cancelReturnIndex, StringComparison.Ordinal);
        var toastFailureIndex = mouseMoveBlock.IndexOf("ShowToastDragError(ex.Message);", dragPathIndex, StringComparison.Ordinal);
        Assert.True(dragPathIndex > tryIndex, "Toast drag temp-file creation should be inside the drag failure guard.");
        Assert.True(noDragFileIndex > dragPathIndex && missingSavedIndex > noDragFileIndex && noPreviewIndex > missingSavedIndex && noDragReturnIndex > noPreviewIndex,
            "Toast drags without a saved file or preview bitmap should show a visible no-preview error before returning.");
        Assert.True(dragDropIndex > dragPathIndex, "Toast drag should inspect the drag/drop result.");
        Assert.True(cancelIndex > dragDropIndex && cancelResetIndex > cancelIndex && cancelReturnIndex > cancelResetIndex,
            "Canceled toast drags should restore feedback and keep the toast open.");
        Assert.True(successDismissIndex > cancelReturnIndex, "Successful toast drags should still dismiss after canceled drags return.");
        Assert.True(toastFailureIndex > dragPathIndex, "Toast drag should report temp-file and DoDragDrop failures.");

        var dragPathBlock = GetMethodBlock(toastCode, "private string? GetDragFilePath()");
        Assert.Contains("if (HasSavedFileOnDisk())", dragPathBlock);
        Assert.Contains("return _savedFilePath;", dragPathBlock);

        var mouseUpBlock = GetMethodBlock(toastCode, "private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)");
        Assert.Contains("if (IsToastOverlayButtonSource(e.OriginalSource as DependencyObject))", mouseUpBlock);
        Assert.Contains("CancelRootInteractionFromOverlaySource(e);", mouseUpBlock);
        var cancelOverlayBlock = GetMethodBlock(toastCode, "private void CancelRootInteractionFromOverlaySource(System.Windows.Input.MouseEventArgs e)");
        Assert.Contains("if (_isDragging)", cancelOverlayBlock);
        Assert.Contains("EndDragFeedback(cancelled: true);", cancelOverlayBlock);
        Assert.Contains("ResumeDismissAfterAbortedInteractionIfNeeded();", cancelOverlayBlock);
        Assert.Contains("if (IsMouseCaptured) ReleaseMouseCapture();", cancelOverlayBlock);
        Assert.Contains("e.Handled = true;", cancelOverlayBlock);
        var endDragFeedbackBlock = GetMethodBlock(toastCode, "private void EndDragFeedback(bool cancelled)");
        Assert.Contains("if (cancelled)", endDragFeedbackBlock);
        Assert.Contains("ResumeDismissAfterAbortedInteractionIfNeeded();", endDragFeedbackBlock);
        var resumeAbortedBlock = GetMethodBlock(toastCode, "private void ResumeDismissAfterAbortedInteractionIfNeeded()");
        Assert.Contains("if (!_resumeDismissOnMouseLeave || _isPinned)", resumeAbortedBlock);
        Assert.Contains("_isHovered = IsCursorOverToast();", resumeAbortedBlock);
        Assert.Contains("_resumeDismissOnMouseLeave = false;", resumeAbortedBlock);
        Assert.Contains("DismissAnimated();", resumeAbortedBlock);
        var cursorBlock = GetMethodBlock(toastCode, "private bool IsCursorOverToast()");
        Assert.Contains("GetCursorPos(out var cursor)", cursorBlock);
        Assert.Contains("return IsMouseOver;", cursorBlock);
        Assert.Contains("return IsScreenPointOver(OuterShell, cursor);", cursorBlock);
        Assert.Contains("if (HasSavedFileOnDisk())", mouseUpBlock);
        Assert.Contains("catch (Exception ex)", mouseUpBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.click-action-open\"", mouseUpBlock);
        Assert.Contains("ShowSavedFileMissingError();", mouseUpBlock);
        Assert.Contains("ShowToastOpenError(\"Could not open the linked target.\");", mouseUpBlock);

        var clickActionCatchIndex = mouseUpBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var clickActionLogIndex = mouseUpBlock.IndexOf("AppDiagnostics.LogWarning(\"toast.click-action-open\"", clickActionCatchIndex, StringComparison.Ordinal);
        var clickActionFallbackIndex = mouseUpBlock.IndexOf("if (HasSavedFileOnDisk())", clickActionLogIndex, StringComparison.Ordinal);
        var missingFileIndex = mouseUpBlock.IndexOf("ShowSavedFileMissingError();", clickActionFallbackIndex, StringComparison.Ordinal);
        var dismissIndex = mouseUpBlock.LastIndexOf("DismissAnimated();", StringComparison.Ordinal);
        Assert.True(clickActionLogIndex > clickActionCatchIndex && clickActionFallbackIndex > clickActionLogIndex,
            "Toast click-action open failures should be logged before fallback handling.");
        Assert.True(missingFileIndex >= 0, "Toast click should report stale saved files.");
        Assert.True(dismissIndex > missingFileIndex, "Toast click should only dismiss after stale-file handling is skipped.");
    }

    [Fact]
    public void ToastOfficeActionsGuardAgainstRepeatedActivation()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private bool _isRunningOfficeAction;", toastCode);
        Assert.Contains("private bool _restoreAutoDismissAfterOfficeAction;", toastCode);
        Assert.Contains("private double _officeActionRemainingAutoDismissSeconds = 0.1;", toastCode);
        Assert.Contains("if (_previewBitmap is null || _isRunningOfficeAction)", toastCode);
        Assert.Contains("_restoreAutoDismissAfterOfficeAction = !wasPinnedBeforeMenu;", toastCode);
        Assert.Contains("_officeActionRemainingAutoDismissSeconds = PauseToastAutoDismiss();", toastCode);
        Assert.Contains("_restoreAutoDismissAfterOfficeAction = false;", toastCode);
        Assert.Contains("if (_previewBitmap is null || !TryBeginOfficeAction())", toastCode);
        Assert.Contains("private bool TryBeginOfficeAction()", toastCode);
        Assert.Contains("_isRunningOfficeAction = true;", toastCode);
        Assert.Contains("OfficeBtn.IsEnabled = false;", toastCode);
        Assert.Contains("RefreshOverlayButtonAccessibility(OfficeBtn, Helpers.ToastButtonKind.Office);", toastCode);
        Assert.Contains("var restoreAutoDismiss = _restoreAutoDismissAfterOfficeAction;", toastCode);
        Assert.Contains("var remainingAutoDismissSeconds = _officeActionRemainingAutoDismissSeconds;", toastCode);
        Assert.Contains("EndOfficeAction(restoreAutoDismiss, remainingAutoDismissSeconds);", toastCode);
        Assert.Contains("private void EndOfficeAction(bool restoreAutoDismiss, double remainingAutoDismissSeconds)", toastCode);
        Assert.Contains("_isRunningOfficeAction = false;", toastCode);
        Assert.Contains("OfficeBtn.IsEnabled = true;", toastCode);
        Assert.Contains("_restoreAutoDismissAfterOfficeAction = false;", toastCode);
        Assert.Contains("if (restoreAutoDismiss)", toastCode);
        Assert.Contains("ResumeToastAutoDismiss(remainingAutoDismissSeconds);", toastCode);

        var officeMenuBlock = GetMethodBlock(toastCode, "private void OpenOfficeMenu()");
        Assert.Contains("_officeActionRemainingAutoDismissSeconds = PauseToastAutoDismiss();", officeMenuBlock);
        Assert.Contains("ResumeToastAutoDismiss(_officeActionRemainingAutoDismissSeconds);", officeMenuBlock);
        Assert.DoesNotContain("ApplyPinnedState(false);", officeMenuBlock);

        var openWithBlock = GetMethodBlock(toastCode, "private void OpenPreviewWithWindowsPicker()");
        Assert.Contains("var restoreAutoDismiss = _restoreAutoDismissAfterOfficeAction;", openWithBlock);
        Assert.Contains("var remainingAutoDismissSeconds = _officeActionRemainingAutoDismissSeconds;", openWithBlock);
        Assert.Contains("EndOfficeAction(restoreAutoDismiss, remainingAutoDismissSeconds);", openWithBlock);

        var sendOfficeBlock = GetMethodBlock(toastCode, "private void SendPreviewToOffice(Services.OfficeExportTarget target)");
        Assert.Contains("var restoreAutoDismiss = _restoreAutoDismissAfterOfficeAction;", sendOfficeBlock);
        Assert.Contains("var remainingAutoDismissSeconds = _officeActionRemainingAutoDismissSeconds;", sendOfficeBlock);
        Assert.Contains("EndOfficeAction(restoreAutoDismiss, remainingAutoDismissSeconds);", sendOfficeBlock);

        var endOfficeBlock = GetMethodBlock(toastCode, "private void EndOfficeAction(bool restoreAutoDismiss, double remainingAutoDismissSeconds)");
        Assert.Contains("_restoreAutoDismissAfterOfficeAction = false;", endOfficeBlock);
        Assert.Contains("if (restoreAutoDismiss)", endOfficeBlock);
        Assert.Contains("ResumeToastAutoDismiss(remainingAutoDismissSeconds);", endOfficeBlock);
        Assert.DoesNotContain("ApplyPinnedState(false);", endOfficeBlock);

        var resumeBlock = GetMethodBlock(toastCode, "private void ResumeToastAutoDismiss(double remainingSeconds)");
        Assert.Contains("RefreshOverlayButtonAccessibility(PinBtn, Helpers.ToastButtonKind.Pin);", resumeBlock);
    }

    [Fact]
    public void ToastOfficeFeedbackDoesNotAttachMissingSavedFile()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        Assert.Contains("private string? GetExistingSavedFilePathOrNull()", toastCode);
        Assert.Contains("=> HasSavedFileOnDisk() ? _savedFilePath : null;", toastCode);

        var openWithBlock = GetMethodBlock(toastCode, "private void OpenPreviewWithWindowsPicker()");
        Assert.Contains("EnsureOpenableFile(_previewBitmap, _savedFilePath", openWithBlock);
        Assert.Contains("TryOpenWithConfiguredApp(openPath, out var configuredAppName)", openWithBlock);
        Assert.Contains("ToastSpec.Standard(\"Open with\", $\"Opened {configuredAppName}.\", GetExistingSavedFilePathOrNull())", openWithBlock);
        Assert.Contains("if (isTemporary)", openWithBlock);
        Assert.Contains("ScheduleTemporaryOpenWithCleanup(openPath);", openWithBlock);
        Assert.Contains("ToastSpec.Standard(\"Open with\", \"Choose an app from Windows.\", GetExistingSavedFilePathOrNull())", openWithBlock);
        Assert.Contains("BuildToastActionFailureBody(\"OddSnap could not open the image with another app. Save the capture or open it from History, then try Windows Open with.\", ex.Message)", openWithBlock);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", openWithBlock);
        Assert.Contains("File.Delete(openPath);", openWithBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.open-with-temp-delete\"", openWithBlock);
        Assert.DoesNotContain("try { File.Delete(openPath); } catch { }", openWithBlock);
        Assert.DoesNotContain("ToastSpec.Standard(\"Open with\", \"Choose an app from Windows.\", _savedFilePath)", openWithBlock);
        Assert.DoesNotContain("ToastSpec.Error(\"Open with failed\", ex.Message, _savedFilePath)", openWithBlock);
        Assert.DoesNotContain("ToastSpec.Error(\"Open with failed\", ex.Message, GetExistingSavedFilePathOrNull())", openWithBlock);

        var configuredIndex = openWithBlock.IndexOf("TryOpenWithConfiguredApp(openPath, out var configuredAppName)", StringComparison.Ordinal);
        var pickerIndex = openWithBlock.IndexOf("Services.OfficeExportService.ShowOpenWithDialog(openPath);", StringComparison.Ordinal);
        var configuredCleanupIndex = openWithBlock.IndexOf("Services.OfficeExportService.ScheduleTemporaryOpenWithCleanup(openPath);", configuredIndex, StringComparison.Ordinal);
        var configuredSuccessIndex = openWithBlock.IndexOf("ToastSpec.Standard(\"Open with\", $\"Opened {configuredAppName}.\", GetExistingSavedFilePathOrNull())", configuredCleanupIndex, StringComparison.Ordinal);
        var pickerCleanupIndex = openWithBlock.IndexOf("Services.OfficeExportService.ScheduleTemporaryOpenWithCleanup(openPath);", pickerIndex, StringComparison.Ordinal);
        var pickerSuccessIndex = openWithBlock.IndexOf("ToastSpec.Standard(\"Open with\", \"Choose an app from Windows.\", GetExistingSavedFilePathOrNull())", pickerCleanupIndex, StringComparison.Ordinal);
        Assert.True(configuredIndex >= 0 && configuredIndex < pickerIndex, "Toast Open With should prefer configured apps before the Windows picker.");
        Assert.True(configuredCleanupIndex > configuredIndex, "Toast Open With should schedule temp cleanup after configured app launch.");
        Assert.True(configuredSuccessIndex > configuredCleanupIndex, "Toast Open With configured-app success feedback should appear after cleanup is scheduled.");
        Assert.True(pickerCleanupIndex > pickerIndex, "Toast Open With temp cleanup should only be scheduled after the picker launches.");
        Assert.True(pickerSuccessIndex > pickerCleanupIndex, "Toast Open With picker success feedback should be shown after temp cleanup is scheduled.");

        var configuredAppBlock = GetMethodBlock(toastCode, "private static bool TryOpenWithConfiguredApp(string imagePath, out string appName)");
        Assert.Contains("SettingsService.LoadStatic();", configuredAppBlock);
        Assert.Contains("TryGetConfiguredApp(settings, Path.GetExtension(imagePath), out var appPath)", configuredAppBlock);
        Assert.Contains("OpenFileWithApp(imagePath, appPath);", configuredAppBlock);
        Assert.Contains("Path.GetFileNameWithoutExtension(appPath);", configuredAppBlock);

        var officeBlock = GetMethodBlock(toastCode, "private void SendPreviewToOffice(Services.OfficeExportTarget target)");
        Assert.Contains("SendBitmap(_previewBitmap, _savedFilePath, target)", officeBlock);
        Assert.Contains("ToastSpec.Standard(\"Sent to Office\", Services.OfficeExportService.GetTargetName(target), GetExistingSavedFilePathOrNull())", officeBlock);
        Assert.Contains("BuildToastActionFailureBody(\"OddSnap could not send the image to Office. Save the capture and insert it manually, or try another Office target.\", ex.Message)", officeBlock);
        Assert.Contains("GetExistingSavedFilePathOrNull()));", officeBlock);
        Assert.DoesNotContain("ToastSpec.Standard(\"Sent to Office\", Services.OfficeExportService.GetTargetName(target), _savedFilePath)", officeBlock);
        Assert.DoesNotContain("ToastSpec.Error(\"Office send failed\", ex.Message, _savedFilePath)", officeBlock);
        Assert.DoesNotContain("ToastSpec.Error(\"Office send failed\", ex.Message, GetExistingSavedFilePathOrNull())", officeBlock);
    }

    [Fact]
    public void ToastInPlaceRefreshClearsOverlayActionState()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var resetBlock = GetMethodBlock(toastCode, "private void CancelActiveToastState()");
        Assert.Contains("_isSavingPreview = false;", resetBlock);
        Assert.Contains("_isOpeningAiRedirect = false;", resetBlock);
        Assert.Contains("_isDeletingSavedFile = false;", resetBlock);
        Assert.Contains("_isRunningOfficeAction = false;", resetBlock);
        Assert.Contains("_restoreAutoDismissAfterOfficeAction = false;", resetBlock);
        Assert.Contains("_officeMenuDismissTimer.Stop();", resetBlock);
        Assert.Contains("_officeMenuMouseWasDown = false;", resetBlock);
        Assert.Contains("if (_officeMenu?.IsOpen == true)", resetBlock);
        Assert.Contains("_officeMenu.IsOpen = false;", resetBlock);
        Assert.Contains("_officeMenu = null;", resetBlock);
        Assert.Contains("if (IsMouseCaptured)", resetBlock);
        Assert.Contains("ReleaseMouseCapture();", resetBlock);
        Assert.Contains("SaveBtn.IsEnabled = true;", resetBlock);
        Assert.Contains("AiRedirectBtn.IsEnabled = true;", resetBlock);
        Assert.Contains("DeleteBtn.IsEnabled = true;", resetBlock);
        Assert.Contains("OfficeBtn.IsEnabled = true;", resetBlock);
        Assert.Contains("RefreshOverlayButtonLayout();", resetBlock);
    }

    [Fact]
    public void ToastInPlaceRefreshPreservesHoverPause()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var updateBlock = GetMethodBlock(toastCode, "internal bool TryUpdateInPlace(ToastSpec spec)");
        Assert.Contains("_isHovered = IsMouseOver;", updateBlock);
        Assert.Contains("if (_spec.ShowOverlayButtons && _isHovered)", updateBlock);
        Assert.Contains("AnimateOverlayButtons(1, _isPinned ? 1 : 1);", updateBlock);
        Assert.Contains("if (!_isPinned && !_isHovered)", updateBlock);

        var hoverRefreshIndex = updateBlock.IndexOf("_isHovered = IsMouseOver;", StringComparison.Ordinal);
        var showButtonsIndex = updateBlock.IndexOf("AnimateOverlayButtons(1, _isPinned ? 1 : 1);", StringComparison.Ordinal);
        var restartIndex = updateBlock.IndexOf("RestartVisibleTimer(_durationSeconds);", StringComparison.Ordinal);
        Assert.True(showButtonsIndex > hoverRefreshIndex, "Toast refresh should show overlay buttons after sampling hover state.");
        Assert.True(restartIndex > hoverRefreshIndex, "Toast refresh should sample hover state before restarting auto-dismiss.");
    }

    [Fact]
    public void OverlayUnpinKeepsAutoDismissPausedWhileHovered()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var previewToggleBlock = GetMethodBlock(previewCode, "private void TogglePinned()");
        Assert.Contains("if (_isHovered)", previewToggleBlock);
        Assert.Contains("return;", previewToggleBlock);

        var previewHoverCheckIndex = previewToggleBlock.IndexOf("if (_isHovered)", StringComparison.Ordinal);
        var previewTimerStartIndex = previewToggleBlock.IndexOf("_fadeTimer.Start();", StringComparison.Ordinal);
        Assert.True(previewTimerStartIndex > previewHoverCheckIndex, "Preview should not restart auto-dismiss before checking hover state.");

        var toastPinnedBlock = GetMethodBlock(toastCode, "private void ApplyPinnedState(bool pinned)");
        Assert.Contains("if (_isHovered)", toastPinnedBlock);
        Assert.Contains("return;", toastPinnedBlock);

        var toastHoverCheckIndex = toastPinnedBlock.IndexOf("if (_isHovered)", StringComparison.Ordinal);
        var toastTimerStartIndex = toastPinnedBlock.IndexOf("_timer.Start();", StringComparison.Ordinal);
        Assert.True(toastTimerStartIndex > toastHoverCheckIndex, "Toast should not restart auto-dismiss before checking hover state.");
    }

    [Fact]
    public void PreviewPinnedMouseLeaveKeepsAutoDismissStopped()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));

        var mouseLeaveBlock = GetMethodBlock(previewCode, "private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)");
        Assert.Contains("if (_isPinned)", mouseLeaveBlock);
        Assert.Contains("_fadeTimer.Stop();", mouseLeaveBlock);
        Assert.Contains("return;", mouseLeaveBlock);

        var pinnedCheckIndex = mouseLeaveBlock.IndexOf("if (_isPinned)", StringComparison.Ordinal);
        var timerStopIndex = mouseLeaveBlock.IndexOf("_fadeTimer.Stop();", pinnedCheckIndex, StringComparison.Ordinal);
        var returnIndex = mouseLeaveBlock.IndexOf("return;", timerStopIndex, StringComparison.Ordinal);
        var timerStartIndex = mouseLeaveBlock.IndexOf("_fadeTimer.Start();", StringComparison.Ordinal);

        Assert.True(timerStopIndex > pinnedCheckIndex, "Pinned Preview mouse leave should explicitly stop auto-dismiss.");
        Assert.True(returnIndex > timerStopIndex, "Pinned Preview mouse leave should exit after stopping auto-dismiss.");
        Assert.True(timerStartIndex > returnIndex, "Preview should not restart auto-dismiss before the pinned-state return.");
    }

    [Fact]
    public void PreviewPinnedLoadKeepsAutoDismissStopped()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.xaml.cs"));

        var loadedBlock = GetMethodBlock(previewCode, "private void OnLoaded(object sender, RoutedEventArgs e)");
        Assert.Contains("if (!_isPinned)", loadedBlock);
        Assert.Contains("_fadeTimer.Start();", loadedBlock);
        Assert.Contains("_fadeTimer.Stop();", loadedBlock);

        var unpinnedCheckIndex = loadedBlock.IndexOf("if (!_isPinned)", StringComparison.Ordinal);
        var timerStartIndex = loadedBlock.IndexOf("_fadeTimer.Start();", unpinnedCheckIndex, StringComparison.Ordinal);
        var timerStopIndex = loadedBlock.IndexOf("_fadeTimer.Stop();", timerStartIndex, StringComparison.Ordinal);

        Assert.True(timerStartIndex > unpinnedCheckIndex, "Preview should only start auto-dismiss inside the unpinned load path.");
        Assert.True(timerStopIndex > timerStartIndex, "Pinned Preview load should keep auto-dismiss stopped.");
    }

    [Fact]
    public void PreviewForceCloseFailuresAreLogged()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        var forceCloseBlock = GetMethodBlock(previewCode, "private void ForceClose()");
        Assert.Contains("RunOnClosedCleanup(\"preview.force-close.stop-timer\", () => _fadeTimer.Stop());", forceCloseBlock);
        Assert.Contains("Close();", forceCloseBlock);
        Assert.Contains("catch (Exception ex)", forceCloseBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"preview.force-close\", ex.Message, ex);", forceCloseBlock);
        Assert.DoesNotContain("try { Close(); } catch { }", forceCloseBlock);

        var timerStopIndex = forceCloseBlock.IndexOf("RunOnClosedCleanup(\"preview.force-close.stop-timer\"", StringComparison.Ordinal);
        var closeTryIndex = forceCloseBlock.IndexOf("try", timerStopIndex, StringComparison.Ordinal);
        Assert.True(closeTryIndex > timerStopIndex, "Preview force-close should attempt Close even if timer stop cleanup fails.");
    }

    [Fact]
    public void ToastForceCloseFailuresAreLogged()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var forceCloseBlock = GetMethodBlock(toastCode, "private bool TryForceClose(bool force = false)");
        Assert.Contains("RunOnClosedCleanup(\"toast.force-close.stop-timer\", () => _timer.Stop());", forceCloseBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.force-close.stop-dismiss-animation\", StopDismissAnimationTimer);", forceCloseBlock);
        Assert.Contains("Close();", forceCloseBlock);
        Assert.Contains("catch (Exception ex)", forceCloseBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"toast.force-close\", ex.Message, ex);", forceCloseBlock);
        Assert.DoesNotContain("try { Close(); } catch { }", forceCloseBlock);

        var timerStopIndex = forceCloseBlock.IndexOf("RunOnClosedCleanup(\"toast.force-close.stop-timer\"", StringComparison.Ordinal);
        var animationStopIndex = forceCloseBlock.IndexOf("RunOnClosedCleanup(\"toast.force-close.stop-dismiss-animation\"", timerStopIndex, StringComparison.Ordinal);
        var pinnedCheckIndex = forceCloseBlock.IndexOf("if (_isPinned && !force)", animationStopIndex, StringComparison.Ordinal);
        var closeTryIndex = forceCloseBlock.IndexOf("try", pinnedCheckIndex, StringComparison.Ordinal);
        Assert.True(animationStopIndex > timerStopIndex && pinnedCheckIndex > animationStopIndex && closeTryIndex > pinnedCheckIndex,
            "Toast force-close should guard cleanup before checking pinned state and still attempt Close afterward.");
    }

    [Fact]
    public void ToastOnClosedCleanupFailuresAreLoggedAndDoNotSkipBaseClose()
    {
        var toastCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var closedBlock = GetMethodBlock(toastCode, "protected override void OnClosed(EventArgs e)");
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.stop-timer\", () => _timer.Stop());", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.stop-office-menu-timer\", () => _officeMenuDismissTimer.Stop());", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.close-office-menu\"", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.stop-dismiss-animation\", StopDismissAnimationTimer);", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.dispose-preview\", () => _previewBitmap?.Dispose());", closedBlock);
        Assert.Contains("_previewBitmap = null;", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.clear-preview-source\", () => PreviewImage.Source = null);", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"toast.closed.clear-inline-source\", () => InlinePreviewImage.Source = null);", closedBlock);
        Assert.Contains("base.OnClosed(e);", closedBlock);

        var disposeIndex = closedBlock.IndexOf("RunOnClosedCleanup(\"toast.closed.dispose-preview\"", StringComparison.Ordinal);
        var nullIndex = closedBlock.IndexOf("_previewBitmap = null;", disposeIndex, StringComparison.Ordinal);
        var baseIndex = closedBlock.IndexOf("base.OnClosed(e);", nullIndex, StringComparison.Ordinal);
        Assert.True(nullIndex > disposeIndex && baseIndex > nullIndex,
            "Toast close should clear preview references and still call base close after guarded cleanup.");

        var cleanupBlock = GetMethodBlock(toastCode, "private static void RunOnClosedCleanup(string diagnosticKey, Action cleanup)");
        Assert.Contains("cleanup();", cleanupBlock);
        Assert.Contains("catch (Exception ex)", cleanupBlock);
        Assert.Contains("AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);", cleanupBlock);
    }

    [Fact]
    public void PreviewOnClosedCleanupFailuresAreLoggedAndDoNotSkipBaseClose()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        var closedBlock = GetMethodBlock(previewCode, "protected override void OnClosed(EventArgs e)");
        Assert.Contains("RunOnClosedCleanup(\"preview.closed.stop-timer\", () => _fadeTimer.Stop());", closedBlock);
        Assert.Contains("if (_current == this) _current = null;", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"preview.closed.dispose-screenshot\", () => _screenshot?.Dispose());", closedBlock);
        Assert.Contains("RunOnClosedCleanup(\"preview.closed.clear-thumbnail-source\", () => ThumbnailImage.Source = null);", closedBlock);
        Assert.Contains("base.OnClosed(e);", closedBlock);

        var currentIndex = closedBlock.IndexOf("if (_current == this) _current = null;", StringComparison.Ordinal);
        var disposeIndex = closedBlock.IndexOf("RunOnClosedCleanup(\"preview.closed.dispose-screenshot\"", currentIndex, StringComparison.Ordinal);
        var clearIndex = closedBlock.IndexOf("RunOnClosedCleanup(\"preview.closed.clear-thumbnail-source\"", disposeIndex, StringComparison.Ordinal);
        var baseIndex = closedBlock.IndexOf("base.OnClosed(e);", clearIndex, StringComparison.Ordinal);
        Assert.True(disposeIndex > currentIndex && clearIndex > disposeIndex && baseIndex > clearIndex,
            "Preview close should dispose and clear image references before base close.");

        var cleanupBlock = GetMethodBlock(previewCode, "private static void RunOnClosedCleanup(string diagnosticKey, Action cleanup)");
        Assert.Contains("cleanup();", cleanupBlock);
        Assert.Contains("catch (Exception ex)", cleanupBlock);
        Assert.Contains("AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);", cleanupBlock);
    }

    [Fact]
    public void PreviewDismissAnimationFailuresForceClose()
    {
        var previewCode = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "PreviewWindow.Actions.cs"));

        var dismissBlock = GetMethodBlock(previewCode, "private void AnimateDismiss()");
        Assert.Contains("_isFading = true;", dismissBlock);
        Assert.Contains("_mouseIsDown = false;", dismissBlock);
        Assert.Contains("try", dismissBlock);
        Assert.Contains("PopupWindowHelper.GetDismissPlacement", dismissBlock);
        Assert.Contains("fadeOut.Completed += (_, _) => ForceClose();", dismissBlock);
        Assert.Contains("catch (Exception ex)", dismissBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"preview.dismiss\"", dismissBlock);
        Assert.Contains("ForceClose();", dismissBlock);

        var fadingIndex = dismissBlock.IndexOf("_isFading = true;", StringComparison.Ordinal);
        var mouseResetIndex = dismissBlock.IndexOf("_mouseIsDown = false;", fadingIndex, StringComparison.Ordinal);
        var tryIndex = dismissBlock.IndexOf("try", fadingIndex, StringComparison.Ordinal);
        var catchIndex = dismissBlock.IndexOf("catch (Exception ex)", tryIndex, StringComparison.Ordinal);
        var forceCloseIndex = dismissBlock.IndexOf("ForceClose();", catchIndex, StringComparison.Ordinal);
        Assert.True(mouseResetIndex > fadingIndex && mouseResetIndex < tryIndex, "Preview dismiss should clear pending mouse-down state before animation setup.");
        Assert.True(tryIndex > fadingIndex, "Preview dismiss should guard animation setup after entering fading state.");
        Assert.True(forceCloseIndex > catchIndex, "Preview dismiss should force close when animation setup fails.");
    }

    private static void AssertOverlayButton(
        string xaml,
        string name,
        string automationName,
        string toolTip,
        bool requiresKeyboardHandler,
        string? helpText = null)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var start = xaml.LastIndexOf("<Border", nameIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {name} opening tag.");

        var end = xaml.IndexOf('>', nameIndex);
        Assert.True(end > start, $"Could not read {name} opening tag.");

        var tag = xaml[start..end];
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"ToolTip=\"{toolTip}\"", tag);
        if (helpText is not null)
            Assert.Contains($"AutomationProperties.HelpText=\"{helpText}\"", tag);

        if (requiresKeyboardHandler)
        {
            Assert.Contains("Focusable=\"True\"", tag);
            Assert.Contains("KeyDown=", tag);
        }
    }

    private static void AssertDynamicToastTextBlock(string xaml, string name, string automationName)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var start = xaml.LastIndexOf("<TextBlock", nameIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {name} opening tag.");

        var end = xaml.IndexOf("/>", nameIndex, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not read {name} opening tag.");

        var tag = xaml[start..end];
        Assert.Contains("ToolTip=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", tag);
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding Text, RelativeSource={RelativeSource Self}}\"", tag);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", tag);
    }

    private static void AssertImageHasAccessibilityMetadata(
        string xaml,
        string name,
        string automationName,
        string toolTip,
        string helpText)
    {
        var nameIndex = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find {name}.");

        var start = xaml.LastIndexOf("<Image", nameIndex, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {name} opening tag.");

        var end = xaml.IndexOf("/>", nameIndex, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not read {name} opening tag.");

        var tag = xaml[start..end];
        Assert.Contains($"AutomationProperties.Name=\"{automationName}\"", tag);
        Assert.Contains($"ToolTip=\"{toolTip}\"", tag);
        Assert.Contains($"AutomationProperties.HelpText=\"{helpText}\"", tag);
    }

    private static object InvokePreviewSaveOperation(MethodInfo method, Func<bool?> showDialog, Action saveAction)
    {
        var result = method.Invoke(null, new object[] { showDialog, saveAction });
        Assert.NotNull(result);
        return result;
    }

    private static bool GetPreviewSaveResultSaved(object result)
    {
        var property = result.GetType().GetProperty("Saved", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property.GetValue(result));
    }

    private static string? GetPreviewSaveResultError(object result)
    {
        var property = result.GetType().GetProperty("ErrorMessage", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        var value = property.GetValue(result);
        Assert.True(value is null or string, $"Expected string or null error, got {value?.GetType().Name}.");
        return (string?)value;
    }

    private static void AssertPreviewConstructorPublishesCurrentAfterInit(string constructorBlock)
    {
        var forceCloseIndex = constructorBlock.IndexOf("CloseCurrentForReplacement();", StringComparison.Ordinal);
        var initIndex = constructorBlock.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var commonIndex = constructorBlock.IndexOf("InitCommon();", initIndex, StringComparison.Ordinal);
        var currentIndex = constructorBlock.IndexOf("_current = this;", commonIndex, StringComparison.Ordinal);

        Assert.True(forceCloseIndex >= 0, "Preview constructor should request previous preview close before replacement.");
        Assert.True(initIndex > forceCloseIndex, "Preview constructor should initialize controls after closing the previous preview.");
        Assert.True(commonIndex > initIndex, "Preview constructor should run common setup after controls initialize.");
        Assert.True(currentIndex > commonIndex, "Preview constructor should publish _current only after initialization succeeds.");
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
}
