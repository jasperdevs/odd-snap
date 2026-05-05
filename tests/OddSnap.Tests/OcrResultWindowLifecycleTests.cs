using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public class OcrResultWindowLifecycleTests
{
    [Fact]
    public void ShouldCloseOnDeactivate_OnlyWhenLoadedAndOpen()
    {
        var lifecycle = new OcrResultWindowLifecycle();

        Assert.True(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: false));
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: false, isMinimized: false));
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: true));
    }

    [Fact]
    public void TryBeginClose_IsIdempotent()
    {
        var lifecycle = new OcrResultWindowLifecycle();

        Assert.True(lifecycle.TryBeginClose());
        Assert.True(lifecycle.IsCloseRequested);
        Assert.False(lifecycle.TryBeginClose());
        Assert.False(lifecycle.ShouldCloseOnDeactivate(isLoaded: true, isMinimized: false));
    }

    [Fact]
    public void CopyButtonsReportClipboardFailures()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var copyBlock = GetMethodBlock(source, "private void CopyBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(text))", copyBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"Nothing to copy\", \"OCR text is empty.\") with { SuppressSound = true });", copyBlock);
        Assert.Contains("return;", copyBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(text);", copyBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"Copied\", FormatCopyToastPreview(text)) with { SuppressSound = true });", copyBlock);
        Assert.Contains("catch (Exception ex)", copyBlock);
        Assert.Contains("OddSnap could not copy the OCR text. Keep the result window open and try again.", copyBlock);
        Assert.DoesNotContain("if (!string.IsNullOrWhiteSpace(text))", copyBlock);

        var translationBlock = GetMethodBlock(source, "private void CopyTranslationBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(text))", translationBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"No translation to copy\", \"Translate text first.\") with { SuppressSound = true });", translationBlock);
        Assert.Contains("return;", translationBlock);
        Assert.Contains("ClipboardService.CopyTextToClipboard(text);", translationBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"Copied translation\", FormatCopyToastPreview(text)) with { SuppressSound = true });", translationBlock);
        Assert.Contains("catch (Exception ex)", translationBlock);
        Assert.Contains("OddSnap could not copy the translated text. Keep the result window open and try again.", translationBlock);
        Assert.DoesNotContain("if (!string.IsNullOrWhiteSpace(text))", translationBlock);
    }

    [Fact]
    public void CopyToastPreviewCollapsesWhitespaceAndTruncatesLongText()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var previewBlock = GetMethodBlock(source, "private static string FormatCopyToastPreview(string text)");
        Assert.Contains("string.Join(\" \", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));", previewBlock);
        Assert.Contains("preview.Length > 80 ? preview[..80] + \"...\" : preview;", previewBlock);
    }

    [Fact]
    public void TranslationStatusWrapsInsideResultPanel()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml"));

        var statusIndex = xaml.IndexOf("x:Name=\"TranslateStatus\"", StringComparison.Ordinal);
        Assert.True(statusIndex >= 0, "Could not find TranslateStatus.");

        var statusBlock = xaml[statusIndex..xaml.IndexOf("/>", statusIndex, StringComparison.Ordinal)];
        Assert.Contains("HorizontalAlignment=\"Stretch\"", statusBlock);
        Assert.Contains("Margin=\"16,0\"", statusBlock);
        Assert.Contains("TextAlignment=\"Center\"", statusBlock);
        Assert.Contains("TextWrapping=\"Wrap\"", statusBlock);
    }

    [Fact]
    public void TranslationControlsAndFooterActionsWrapWithSpacing()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml"));

        Assert.Contains("<WrapPanel VerticalAlignment=\"Center\">", xaml);
        Assert.Contains("x:Name=\"FromLanguageCombo\" FontSize=\"11\" Padding=\"6,4\" MinWidth=\"110\"", xaml);
        Assert.Contains("Margin=\"0,0,6,6\"", xaml);
        Assert.Contains("Text=\"→\" FontSize=\"14\" Margin=\"0,0,6,6\"", xaml);
        Assert.Contains("x:Name=\"ToLanguageCombo\" FontSize=\"11\" Padding=\"6,4\" MinWidth=\"110\"", xaml);
        Assert.Contains("Width=\"1\" Height=\"18\" Background=\"{DynamicResource ThemeInputBorderBrush}\" Margin=\"0,0,8,6\"", xaml);
        Assert.Contains("x:Name=\"ModelCombo\" FontSize=\"10\" Padding=\"5,4\" MinWidth=\"115\"", xaml);
        Assert.Contains("Margin=\"0,0,8,6\"", xaml);
        Assert.Contains("FontSize=\"12\" Padding=\"16,6\" Margin=\"0,0,0,6\"", xaml);
        Assert.Contains("<WrapPanel DockPanel.Dock=\"Right\" HorizontalAlignment=\"Right\">", xaml);
        Assert.Contains("x:Name=\"CopyTranslationBtn\" Content=\"Copy result\" FontSize=\"11\" Padding=\"10,6\" Margin=\"0,0,6,4\"", xaml);
        Assert.Contains("x:Name=\"CopyBtn\" Content=\"Copy text\" FontSize=\"11\" Padding=\"10,6\" Margin=\"0,0,0,4\"", xaml);
        Assert.DoesNotContain("<StackPanel Orientation=\"Horizontal\" DockPanel.Dock=\"Right\" HorizontalAlignment=\"Right\">", xaml);
    }

    [Fact]
    public void FooterCharacterCountYieldsToCopyActions()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml"));

        var charCountIndex = xaml.IndexOf("x:Name=\"CharCountText\"", StringComparison.Ordinal);
        Assert.True(charCountIndex >= 0, "Could not find CharCountText.");

        var charCountBlock = xaml[charCountIndex..xaml.IndexOf("/>", charCountIndex, StringComparison.Ordinal)];
        Assert.Contains("Margin=\"0,0,12,4\"", charCountBlock);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", charCountBlock);
        Assert.Contains("TextWrapping=\"NoWrap\"", charCountBlock);
    }

    [Fact]
    public void TranslationLoadingOverlayFitsCompactResultPanel()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml"));

        var overlayIndex = xaml.IndexOf("x:Name=\"TranslationLoadingOverlay\"", StringComparison.Ordinal);
        Assert.True(overlayIndex >= 0, "Could not find TranslationLoadingOverlay.");

        var statusIndex = xaml.IndexOf("x:Name=\"TranslateStatus\"", overlayIndex, StringComparison.Ordinal);
        Assert.True(statusIndex > overlayIndex, "Could not find TranslateStatus after overlay.");

        var overlayBlock = xaml[overlayIndex..statusIndex];
        Assert.Contains("ClipToBounds=\"True\"", overlayBlock);
        Assert.Contains("<StackPanel Margin=\"12,8,12,8\" VerticalAlignment=\"Center\">", overlayBlock);
        Assert.Contains("<Border Height=\"10\" Width=\"150\" CornerRadius=\"5\"", overlayBlock);
        Assert.Contains("<Border Height=\"8\" Margin=\"0,8,0,0\" CornerRadius=\"4\"", overlayBlock);
        Assert.Contains("<Border Height=\"8\" Margin=\"0,6,42,0\" CornerRadius=\"4\"", overlayBlock);
        Assert.Contains("Margin=\"0,10,0,0\"", overlayBlock);
        Assert.DoesNotContain("Margin=\"0,8,86,0\"", overlayBlock);
        Assert.DoesNotContain("Margin=\"0,14,0,0\"", overlayBlock);
    }

    [Fact]
    public void BackdropFailuresAreLoggedWithoutBlockingWindow()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var backdropBlock = GetMethodBlock(source, "private void ApplyMicaBackdrop()");
        Assert.Contains("Native.Dwm.DisableBackdrop(hwnd);", backdropBlock);
        Assert.Contains("catch (Exception ex)", backdropBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"ocr-result.backdrop\", ex.Message, ex);", backdropBlock);
        Assert.DoesNotContain("catch { }", backdropBlock);
    }

    [Fact]
    public void TranslationPreferenceChangesRollBackAndLeaveInlineStatus()
    {
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml"));
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        Assert.Contains("x:Name=\"TranslationPreferenceStatusText\"", xaml);
        Assert.Contains("private bool _suppressTranslationPreferenceChange;", source);

        var fromBlock = GetMethodBlock(source, "private void FromLanguageCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressTranslationPreferenceChange) return;", fromBlock);
        Assert.Contains("var previous = _settingsService.Settings.OcrDefaultTranslateFrom;", fromBlock);
        Assert.Contains("var selected = TranslationService.ResolveSourceLanguage(item.Tag as string);", fromBlock);
        Assert.Contains("string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase)", fromBlock);
        Assert.Contains("\"ocr-result.translation-source-language\"", fromBlock);
        Assert.Contains("value => _settingsService.Settings.OcrDefaultTranslateFrom = value", fromBlock);
        Assert.Contains("value => SelectComboByTag(FromLanguageCombo, value)", fromBlock);
        Assert.Contains("ResetTranslationForTranslationOptionChange();", fromBlock);
        Assert.True(
            fromBlock.IndexOf("ResetTranslationForTranslationOptionChange();", StringComparison.Ordinal) >
            fromBlock.IndexOf("if (UpdateTranslationPreference(", StringComparison.Ordinal),
            "Source language changes should clear stale translation UI only after the preference save succeeds.");

        var toBlock = GetMethodBlock(source, "private void ToLanguageCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressTranslationPreferenceChange) return;", toBlock);
        Assert.Contains("var previous = _settingsService.Settings.OcrDefaultTranslateTo;", toBlock);
        Assert.Contains("var selected = item.Tag as string ?? \"auto\";", toBlock);
        Assert.Contains("string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase)", toBlock);
        Assert.Contains("\"ocr-result.translation-target-language\"", toBlock);
        Assert.Contains("value => _settingsService.Settings.OcrDefaultTranslateTo = value", toBlock);
        Assert.Contains("value => SelectComboByTag(ToLanguageCombo, value)", toBlock);
        Assert.Contains("ResetTranslationForTranslationOptionChange();", toBlock);
        Assert.True(
            toBlock.IndexOf("ResetTranslationForTranslationOptionChange();", StringComparison.Ordinal) >
            toBlock.IndexOf("if (UpdateTranslationPreference(", StringComparison.Ordinal),
            "Target language changes should clear stale translation UI only after the preference save succeeds.");

        var modelBlock = GetMethodBlock(source, "private void ModelCombo_Changed(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (!IsLoaded || _suppressTranslationPreferenceChange) return;", modelBlock);
        Assert.Contains("var previous = _settingsService.Settings.TranslationModel;", modelBlock);
        Assert.Contains("var selected = (int)GetSelectedModel();", modelBlock);
        Assert.Contains("if (previous == selected)", modelBlock);
        Assert.Contains("\"ocr-result.translation-model\"", modelBlock);
        Assert.Contains("value => _settingsService.Settings.TranslationModel = value", modelBlock);
        Assert.Contains("SelectTranslationModelCombo", modelBlock);
        Assert.Contains("ResetTranslationForTranslationOptionChange();", modelBlock);
        Assert.True(
            modelBlock.IndexOf("ResetTranslationForTranslationOptionChange();", StringComparison.Ordinal) >
            modelBlock.IndexOf("if (UpdateTranslationPreference(", StringComparison.Ordinal),
            "Model changes should clear stale translation UI only after the preference save succeeds.");

        var helperBlock = GetMethodBlock(source, "private bool UpdateTranslationPreference<T>(");
        Assert.Contains("setValue(current);", helperBlock);
        Assert.Contains("_settingsService.Save();", helperBlock);
        Assert.Contains("SetTranslationPreferenceStatus(string.Empty);", helperBlock);
        Assert.Contains("return true;", helperBlock);
        Assert.Contains("AppDiagnostics.LogError(diagnosticKey, ex);", helperBlock);
        Assert.Contains("setValue(previous);", helperBlock);
        Assert.Contains("AppDiagnostics.LogError($\"{diagnosticKey}-rollback\", rollbackEx);", helperBlock);
        Assert.Contains("_suppressTranslationPreferenceChange = true;", helperBlock);
        Assert.Contains("restoreUi(previous);", helperBlock);
        Assert.Contains("_suppressTranslationPreferenceChange = false;", helperBlock);
        Assert.Contains("SetTranslationPreferenceStatus($\"{label} failed. Previous option restored.\");", helperBlock);
        Assert.Contains("The previous translation preference was restored. Keep the result window open and try again.", helperBlock);
        Assert.Contains("return false;", helperBlock);

        var resetPreferenceBlock = GetMethodBlock(source, "private void ResetTranslationForTranslationOptionChange()");
        Assert.Contains("ResetTranslationForTranslationInputChange();", resetPreferenceBlock);

        var statusBlock = GetMethodBlock(source, "private void SetTranslationPreferenceStatus(string message)");
        Assert.Contains("TranslationPreferenceStatusText.Text = message;", statusBlock);
        Assert.Contains("Visibility.Collapsed", statusBlock);
        Assert.Contains("Visibility.Visible", statusBlock);
    }

    [Fact]
    public void TranslationRequestCancellationCleansUpLoadingStateAndTokenSource()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var requestCts = _translateCts;", translateBlock);
        Assert.Contains("var token = requestCts.Token;", translateBlock);
        Assert.Contains("StopTranslationConfigurationCheck();", translateBlock);
        Assert.Contains("if (_lifecycle.IsCloseRequested)", translateBlock);
        Assert.Contains("if (!IsActiveTranslationRequest(requestCts))", translateBlock);
        Assert.Contains("if (token.IsCancellationRequested)", translateBlock);
        Assert.Contains("StopTranslationLoading(keepStatusVisible: false);", translateBlock);
        Assert.Contains("finally", translateBlock);
        Assert.Contains("if (IsActiveTranslationRequest(requestCts))", translateBlock);
        Assert.Contains("_translateCts = null;", translateBlock);
        Assert.Contains("requestCts.Dispose();", translateBlock);

        var helperBlock = GetMethodBlock(source, "private bool IsActiveTranslationRequest(CancellationTokenSource requestCts)");
        Assert.Contains("ReferenceEquals(_translateCts, requestCts)", helperBlock);

        var ensureReadyIndex = translateBlock.IndexOf("await TranslationService.EnsureReadyAsync(fromCode, model, token);", StringComparison.Ordinal);
        var firstCloseCheckIndex = translateBlock.IndexOf("if (_lifecycle.IsCloseRequested)", ensureReadyIndex, StringComparison.Ordinal);
        var firstCancelCheckIndex = translateBlock.IndexOf("if (token.IsCancellationRequested)", firstCloseCheckIndex, StringComparison.Ordinal);
        var translateIndex = translateBlock.IndexOf("var result = await TranslationService.TranslateAsync", StringComparison.Ordinal);
        Assert.True(firstCancelCheckIndex > firstCloseCheckIndex && firstCancelCheckIndex < translateIndex,
            "The first post-ready cancellation check should reset loading before translation starts.");

        var secondCloseCheckIndex = translateBlock.IndexOf("if (_lifecycle.IsCloseRequested)", translateIndex, StringComparison.Ordinal);
        var secondCancelCheckIndex = translateBlock.IndexOf("if (token.IsCancellationRequested)", secondCloseCheckIndex, StringComparison.Ordinal);
        var successStopIndex = translateBlock.IndexOf("StopTranslationLoading(keepStatusVisible: false);", secondCancelCheckIndex, StringComparison.Ordinal);
        Assert.True(secondCancelCheckIndex > secondCloseCheckIndex && successStopIndex > secondCancelCheckIndex,
            "The post-translate cancellation check should reset loading before returning.");
    }

    [Fact]
    public void StaleTranslationRequestsDoNotOverwriteCurrentRequestUi()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        var firstStaleCheckIndex = translateBlock.IndexOf("if (!IsActiveTranslationRequest(requestCts))", StringComparison.Ordinal);
        var firstCanceledIndex = translateBlock.IndexOf("if (token.IsCancellationRequested)", firstStaleCheckIndex, StringComparison.Ordinal);
        var firstStopIndex = translateBlock.IndexOf("StopTranslationConfigurationCheck();", firstCanceledIndex, StringComparison.Ordinal);
        Assert.True(firstStaleCheckIndex >= 0 && firstStaleCheckIndex < firstCanceledIndex && firstCanceledIndex < firstStopIndex,
            "Setup-check cancellation should ignore stale requests before restoring setup-check UI.");

        var firstLoadingCancelIndex = translateBlock.IndexOf("if (token.IsCancellationRequested)", firstStopIndex, StringComparison.Ordinal);
        var loadingStopIndex = translateBlock.IndexOf("StopTranslationLoading(keepStatusVisible: false);", firstLoadingCancelIndex, StringComparison.Ordinal);
        var staleBeforeLoadingStopIndex = translateBlock.LastIndexOf("if (!IsActiveTranslationRequest(requestCts))", loadingStopIndex, StringComparison.Ordinal);
        Assert.True(staleBeforeLoadingStopIndex > firstStopIndex && staleBeforeLoadingStopIndex < firstLoadingCancelIndex,
            "Translation-loading cancellation should ignore stale requests before restoring loading UI.");

        var catchIndex = translateBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var catchStaleIndex = translateBlock.IndexOf("if (!IsActiveTranslationRequest(requestCts))", catchIndex, StringComparison.Ordinal);
        var showErrorIndex = translateBlock.IndexOf("ShowTranslateError(ex.Message);", catchStaleIndex, StringComparison.Ordinal);
        Assert.True(catchStaleIndex > catchIndex && catchStaleIndex < showErrorIndex,
            "Stale request failures should not overwrite the active request status.");
    }

    [Fact]
    public void ClosePathCancelsAndDisposesActiveTranslationRequest()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var closeBlock = GetMethodBlock(source, "private void CloseWindow()");
        Assert.Contains("if (!_lifecycle.TryBeginClose())", closeBlock);
        Assert.Contains("_translateCts?.Cancel();", closeBlock);
        Assert.Contains("StopTranslateTimer();", closeBlock);
        Assert.Contains("Close();", closeBlock);
        Assert.DoesNotContain("_translateCts?.Dispose();", closeBlock);

        var closedBlock = GetMethodBlock(source, "protected override void OnClosed(EventArgs e)");
        Assert.Contains("_translateCts?.Cancel();", closedBlock);
        Assert.Contains("_translateCts?.Dispose();", closedBlock);
        Assert.Contains("_translateCts = null;", closedBlock);
        Assert.Contains("StopTranslateTimer();", closedBlock);
        Assert.Contains("base.OnClosed(e);", closedBlock);

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        var finallyIndex = translateBlock.IndexOf("finally", StringComparison.Ordinal);
        var activeClearIndex = translateBlock.IndexOf("if (IsActiveTranslationRequest(requestCts))", finallyIndex, StringComparison.Ordinal);
        var fieldClearIndex = translateBlock.IndexOf("_translateCts = null;", activeClearIndex, StringComparison.Ordinal);
        var disposeIndex = translateBlock.IndexOf("requestCts.Dispose();", fieldClearIndex, StringComparison.Ordinal);
        Assert.True(activeClearIndex > finallyIndex && fieldClearIndex > activeClearIndex && disposeIndex > fieldClearIndex,
            "Async cleanup should clear the active field before disposing the request source.");
    }

    [Fact]
    public void StartingNewTranslationDoesNotDisposePreviousRequestBeforeItUnwinds()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        var handoffIndex = translateBlock.IndexOf("_translateCts?.Cancel();", StringComparison.Ordinal);
        var newSourceIndex = translateBlock.IndexOf("_translateCts = new CancellationTokenSource();", handoffIndex, StringComparison.Ordinal);
        var finallyIndex = translateBlock.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(handoffIndex >= 0 && newSourceIndex > handoffIndex && finallyIndex > newSourceIndex,
            "Translation request handoff should cancel before assigning a new source.");

        var handoffBlock = translateBlock[handoffIndex..newSourceIndex];
        Assert.DoesNotContain("_translateCts?.Dispose();", handoffBlock);

        var disposeIndex = translateBlock.IndexOf("requestCts.Dispose();", finallyIndex, StringComparison.Ordinal);
        Assert.True(disposeIndex > finallyIndex, "Each request should dispose its own cancellation source in finally.");
    }

    [Fact]
    public void SourceTextEditsClearStaleTranslationResult()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        Assert.Contains("OcrTextBox.TextChanged += OcrTextBox_TextChanged;", source);
        Assert.DoesNotContain("OcrTextBox.TextChanged += (_, _) => UpdateCharCount();", source);

        var changedBlock = GetMethodBlock(source, "private void OcrTextBox_TextChanged(object sender, TextChangedEventArgs e)");
        Assert.Contains("UpdateCharCount();", changedBlock);
        Assert.Contains("if (!IsLoaded)", changedBlock);
        Assert.Contains("ResetTranslationForSourceEdit();", changedBlock);

        var resetBlock = GetMethodBlock(source, "private void ResetTranslationForSourceEdit()");
        Assert.Contains("ResetTranslationForTranslationInputChange();", resetBlock);

        var inputResetBlock = GetMethodBlock(source, "private void ResetTranslationForTranslationInputChange()");
        Assert.Contains("if (_translateCts is not null)", inputResetBlock);
        Assert.Contains("_translateCts.Cancel();", inputResetBlock);
        Assert.Contains("_translateCts = null;", inputResetBlock);
        Assert.Contains("StopTranslationConfigurationCheck();", inputResetBlock);
        Assert.Contains("StopTranslationLoading(keepStatusVisible: false);", inputResetBlock);
        Assert.Contains("TranslatedTextBox.Text = string.Empty;", inputResetBlock);
        Assert.Contains("CopyTranslationBtn.Visibility = Visibility.Collapsed;", inputResetBlock);
        Assert.DoesNotContain("_translateCts.Dispose();", inputResetBlock);

        var cancelBlockIndex = inputResetBlock.IndexOf("if (_translateCts is not null)", StringComparison.Ordinal);
        var stopConfigurationIndex = inputResetBlock.IndexOf("StopTranslationConfigurationCheck();", StringComparison.Ordinal);
        var stopLoadingIndex = inputResetBlock.IndexOf("StopTranslationLoading(keepStatusVisible: false);", StringComparison.Ordinal);
        var clearTextIndex = inputResetBlock.IndexOf("TranslatedTextBox.Text = string.Empty;", StringComparison.Ordinal);
        Assert.True(cancelBlockIndex >= 0 && stopConfigurationIndex > cancelBlockIndex,
            "Source edits should clear setup status even when no translation request is active.");
        Assert.True(stopLoadingIndex > stopConfigurationIndex && clearTextIndex > stopLoadingIndex,
            "Source edits should clear stale translation status before clearing translated text.");
    }

    [Fact]
    public void TranslationOptionChangesClearStaleTranslationResult()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var resetBlock = GetMethodBlock(source, "private void ResetTranslationForTranslationOptionChange()");
        Assert.Contains("ResetTranslationForTranslationInputChange();", resetBlock);

        var inputResetBlock = GetMethodBlock(source, "private void ResetTranslationForTranslationInputChange()");
        Assert.Contains("if (_translateCts is not null)", inputResetBlock);
        Assert.Contains("_translateCts.Cancel();", inputResetBlock);
        Assert.Contains("_translateCts = null;", inputResetBlock);
        Assert.Contains("StopTranslationConfigurationCheck();", inputResetBlock);
        Assert.Contains("StopTranslationLoading(keepStatusVisible: false);", inputResetBlock);
        Assert.Contains("TranslatedTextBox.Text = string.Empty;", inputResetBlock);
        Assert.Contains("CopyTranslationBtn.Visibility = Visibility.Collapsed;", inputResetBlock);
        Assert.DoesNotContain("_translateCts.Dispose();", inputResetBlock);

        var cancelBlockIndex = inputResetBlock.IndexOf("if (_translateCts is not null)", StringComparison.Ordinal);
        var stopConfigurationIndex = inputResetBlock.IndexOf("StopTranslationConfigurationCheck();", StringComparison.Ordinal);
        var stopLoadingIndex = inputResetBlock.IndexOf("StopTranslationLoading(keepStatusVisible: false);", StringComparison.Ordinal);
        var clearTextIndex = inputResetBlock.IndexOf("TranslatedTextBox.Text = string.Empty;", StringComparison.Ordinal);
        Assert.True(cancelBlockIndex >= 0 && stopConfigurationIndex > cancelBlockIndex,
            "Translation option changes should clear setup status even when no translation request is active.");
        Assert.True(stopLoadingIndex > stopConfigurationIndex && clearTextIndex > stopLoadingIndex,
            "Translation option changes should clear stale translation status before clearing translated text.");
    }

    [Fact]
    public void TranslationConfigurationErrorsAreShownBeforeLoadingOverlayStarts()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("StartTranslationConfigurationCheck();", translateBlock);
        Assert.Contains("var configurationError = await TranslationService.GetConfigurationErrorAsync(fromCode, model, token);", translateBlock);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(configurationError))", translateBlock);
        Assert.Contains("ShowTranslateError(configurationError);", translateBlock);
        Assert.Contains("StopTranslationConfigurationCheck();", translateBlock);
        Assert.Contains("StartTranslationLoading(model);", translateBlock);
        Assert.Contains("ShowTranslateError(ex.Message);", translateBlock);

        var configCheckIndex = translateBlock.IndexOf("StartTranslationConfigurationCheck();", StringComparison.Ordinal);
        var configurationErrorIndex = translateBlock.IndexOf("var configurationError = await TranslationService.GetConfigurationErrorAsync", StringComparison.Ordinal);
        var setupCancelIndex = translateBlock.IndexOf("if (token.IsCancellationRequested)", configurationErrorIndex, StringComparison.Ordinal);
        var setupCancelStopIndex = translateBlock.IndexOf("StopTranslationConfigurationCheck();", setupCancelIndex, StringComparison.Ordinal);
        var setupCancelHideIndex = translateBlock.IndexOf("TranslateStatus.Visibility = Visibility.Collapsed;", setupCancelStopIndex, StringComparison.Ordinal);
        var setupCancelReturnIndex = translateBlock.IndexOf("return;", setupCancelHideIndex, StringComparison.Ordinal);
        var configurationFailureIndex = translateBlock.IndexOf("ShowTranslateError(configurationError);", StringComparison.Ordinal);
        var loadingIndex = translateBlock.IndexOf("StartTranslationLoading(model);", StringComparison.Ordinal);
        Assert.True(configCheckIndex < configurationErrorIndex, "The setup status should appear before probing configuration.");
        Assert.True(setupCancelIndex > configurationErrorIndex, "Setup cancellation should be checked after probing configuration.");
        Assert.True(setupCancelStopIndex > setupCancelIndex && setupCancelHideIndex > setupCancelStopIndex && setupCancelReturnIndex > setupCancelHideIndex,
            "Setup cancellation should restore the button and hide setup status before returning.");
        Assert.True(configurationErrorIndex < configurationFailureIndex, "Configuration errors should use the preflight result.");
        Assert.True(configurationFailureIndex < loadingIndex, "Known configuration errors should be handled before the full loading overlay starts.");

        var startCheckBlock = GetMethodBlock(source, "private void StartTranslationConfigurationCheck()");
        Assert.Contains("TranslatedTextBox.Text = string.Empty;", startCheckBlock);
        Assert.Contains("TranslateStatus.Visibility = Visibility.Visible;", startCheckBlock);
        Assert.Contains("TranslateStatus.Text = \"Checking translation setup...\";", startCheckBlock);
        Assert.Contains("CopyTranslationBtn.Visibility = Visibility.Collapsed;", startCheckBlock);
        Assert.Contains("TranslateBtn.IsEnabled = false;", startCheckBlock);
        Assert.Contains("TranslateBtn.Content = \"Checking...\";", startCheckBlock);
        Assert.True(
            startCheckBlock.IndexOf("TranslatedTextBox.Text = string.Empty;", StringComparison.Ordinal) <
            startCheckBlock.IndexOf("TranslateStatus.Text = \"Checking translation setup...\";", StringComparison.Ordinal),
            "Starting a setup check should clear stale translated output before showing retry status.");

        var stopCheckBlock = GetMethodBlock(source, "private void StopTranslationConfigurationCheck()");
        Assert.Contains("TranslateBtn.IsEnabled = true;", stopCheckBlock);
        Assert.Contains("TranslateBtn.Content = \"Translate\";", stopCheckBlock);

        var errorBlock = GetMethodBlock(source, "private void ShowTranslateError(string message)");
        Assert.Contains("StopTranslationLoading(keepStatusVisible: true);", errorBlock);
        Assert.Contains("TranslateStatus.Text = $\"Error: {message}\";", errorBlock);
    }

    [Fact]
    public void TranslationNoOpInputsShowVisibleStatus()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var idleStatusBlock = GetMethodBlock(source, "private void SetTranslationIdleStatus(string message)");
        Assert.Contains("StopTranslateTimer();", idleStatusBlock);
        Assert.Contains("TranslationLoadingOverlay.Visibility = Visibility.Collapsed;", idleStatusBlock);
        Assert.Contains("TranslateProgressBar.IsIndeterminate = false;", idleStatusBlock);
        Assert.Contains("TranslateStatus.Visibility = Visibility.Visible;", idleStatusBlock);
        Assert.Contains("TranslateStatus.Text = message;", idleStatusBlock);

        var translateBlock = GetMethodBlock(source, "private async void TranslateBtn_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("SetTranslationIdleStatus(\"No text to translate.\");", translateBlock);
        Assert.Contains("SetTranslationIdleStatus(\"Choose translation languages first.\");", translateBlock);
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(text)) return;", translateBlock);
        Assert.DoesNotContain("if (fromItem == null || toItem == null) return;", translateBlock);
    }

    [Fact]
    public void LanguageComboFilteringPreservesSelectionAndReportsEmptyFilters()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "OcrResultWindow.xaml.cs"));

        var filterBlock = GetMethodBlock(source, "private void FilterComboItems(ComboBox combo)");
        Assert.Contains("var currentTag = GetFilteredComboSelectionTag(combo);", filterBlock);
        Assert.Contains("var matchCount = 0;", filterBlock);
        Assert.Contains("var wasSuppressingPreferenceChange = _suppressTranslationPreferenceChange;", filterBlock);
        Assert.Contains("_suppressTranslationPreferenceChange = true;", filterBlock);
        Assert.Contains("matchCount++;", filterBlock);
        Assert.Contains("RestoreFilteredComboSelection(combo, currentTag);", filterBlock);
        Assert.Contains("finally", filterBlock);
        Assert.Contains("_suppressTranslationPreferenceChange = wasSuppressingPreferenceChange;", filterBlock);
        Assert.Contains("if (matchCount == 0)", filterBlock);
        Assert.Contains("SetTranslationPreferenceStatus(\"No languages match that filter.\");", filterBlock);
        Assert.Contains("else if (TranslationPreferenceStatusText.Text == \"No languages match that filter.\")", filterBlock);
        Assert.Contains("SetTranslationPreferenceStatus(string.Empty);", filterBlock);
        Assert.True(
            filterBlock.IndexOf("_suppressTranslationPreferenceChange = true;", StringComparison.Ordinal) <
            filterBlock.IndexOf("combo.Items.Clear();", StringComparison.Ordinal),
            "Language filtering should suppress preference handlers before rebuilding combo items.");
        Assert.True(
            filterBlock.IndexOf("RestoreFilteredComboSelection(combo, currentTag);", StringComparison.Ordinal) <
            filterBlock.IndexOf("_suppressTranslationPreferenceChange = wasSuppressingPreferenceChange;", StringComparison.Ordinal),
            "Restoring the filtered selection should remain suppressed until the selected item is stable.");
        Assert.True(
            filterBlock.IndexOf("_suppressTranslationPreferenceChange = wasSuppressingPreferenceChange;", StringComparison.Ordinal) <
            filterBlock.IndexOf("if (matchCount == 0)", StringComparison.Ordinal),
            "Filter status feedback should update after preference-handler suppression is restored.");

        var restoreBlock = GetMethodBlock(source, "private static void RestoreFilteredComboSelection(ComboBox combo, string? selectedTag)");
        Assert.Contains("if (string.IsNullOrWhiteSpace(selectedTag))", restoreBlock);
        Assert.Contains("foreach (var item in combo.Items.OfType<ComboBoxItem>())", restoreBlock);
        Assert.Contains("string.Equals(item.Tag as string, selectedTag, StringComparison.OrdinalIgnoreCase)", restoreBlock);
        Assert.Contains("combo.SelectedItem = item;", restoreBlock);

        var selectionTagBlock = GetMethodBlock(source, "private string? GetFilteredComboSelectionTag(ComboBox combo)");
        Assert.Contains("(combo.SelectedItem as ComboBoxItem)?.Tag is string selectedTag", selectionTagBlock);
        Assert.Contains("return selectedTag;", selectionTagBlock);
        Assert.Contains("_settingsService.Settings.OcrDefaultTranslateFrom", selectionTagBlock);
        Assert.Contains("_settingsService.Settings.OcrDefaultTranslateTo", selectionTagBlock);
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
