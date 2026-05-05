using Xunit;

namespace OddSnap.Tests;

public sealed class AppUploadPolishTests
{
    [Fact]
    public void UploadSuccessClipboardFailureDoesNotBecomeUploadFailure()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");

        var successIndex = uploadBlock.IndexOf("if (result.Success)", StringComparison.Ordinal);
        Assert.True(successIndex >= 0, "Could not find upload success branch.");

        var uploadErrorClearedIndex = uploadBlock.IndexOf("entry.UploadError = null;", successIndex, StringComparison.Ordinal);
        Assert.True(uploadErrorClearedIndex > successIndex, "Upload success should clear prior upload errors before post-upload feedback.");

        var providerIndex = uploadBlock.IndexOf("entry.UploadProvider = providerName;", successIndex, StringComparison.Ordinal);
        Assert.True(providerIndex > successIndex && providerIndex < uploadErrorClearedIndex,
            "Upload success should refresh the provider before clearing stale upload errors.");

        var copyIndex = uploadBlock.IndexOf("ClipboardService.CopyTextToClipboard(result.Url);", uploadErrorClearedIndex, StringComparison.Ordinal);
        Assert.True(copyIndex > uploadErrorClearedIndex, "Upload URL copy should happen after the successful upload state is saved.");

        var copyFailureIndex = uploadBlock.IndexOf("ToastWindow.ShowError(\"Copy failed\", BuildUploadCopyFailureToastBody(ex.Message), filePath);", copyIndex, StringComparison.Ordinal);
        Assert.True(copyFailureIndex > copyIndex, "Post-upload clipboard failures should show copy-specific feedback.");
        Assert.DoesNotContain("ToastWindow.ShowError(\"Copy failed\", $\"Upload succeeded, but the link was not copied.\\n{ex.Message}\");", uploadBlock);
        Assert.Contains("Open History and copy the upload link manually.", source);

        var copyFailureBodyBlock = GetMethodBlock(source, "private static string BuildUploadCopyFailureToastBody(string details)");
        Assert.Contains("Upload succeeded, but OddSnap could not copy the link. Open History and copy the upload link manually.", copyFailureBodyBlock);
        Assert.Contains("string.IsNullOrWhiteSpace(details) ? recovery : $\"{recovery}\\n{details}\"", copyFailureBodyBlock);

        var uploadErrorIndex = uploadBlock.IndexOf("AppDiagnostics.LogError(\"upload.toast-error\"", copyFailureIndex, StringComparison.Ordinal);
        Assert.True(uploadErrorIndex > copyFailureIndex, "Copy failure handling should stay separate from the outer upload-error catch.");

        var uploadFailureBranchIndex = uploadBlock.IndexOf("AppDiagnostics.LogWarning(\"upload.toast-failed\"", copyFailureIndex, StringComparison.Ordinal);
        Assert.True(uploadFailureBranchIndex > copyFailureIndex, "Regular upload failure handling should remain after the success branch.");

        var successBranch = uploadBlock[successIndex..uploadFailureBranchIndex];
        Assert.DoesNotContain("SaveUploadFailure(", successBranch);
    }

    [Fact]
    public void UploadSuccessRefreshesProviderWithoutRenamingExistingHistory()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");

        Assert.Contains("var previousProvider = entry.UploadProvider;", uploadBlock);
        Assert.Contains("entry.UploadProvider = providerName;", uploadBlock);
        Assert.Contains("if (string.IsNullOrWhiteSpace(previousProvider))", uploadBlock);
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(entry.UploadProvider))", uploadBlock);

        var previousProviderIndex = uploadBlock.IndexOf("var previousProvider = entry.UploadProvider;", StringComparison.Ordinal);
        var providerRefreshIndex = uploadBlock.IndexOf("entry.UploadProvider = providerName;", previousProviderIndex, StringComparison.Ordinal);
        var firstUploadRenameIndex = uploadBlock.IndexOf("if (string.IsNullOrWhiteSpace(previousProvider))", providerRefreshIndex, StringComparison.Ordinal);
        var fileNameIndex = uploadBlock.IndexOf("entry.FileName =", firstUploadRenameIndex, StringComparison.Ordinal);
        Assert.True(providerRefreshIndex > previousProviderIndex,
            "Regular upload success should remember the old provider before overwriting it.");
        Assert.True(fileNameIndex > firstUploadRenameIndex,
            "Filename prefixing should stay limited to first-time upload history entries.");
    }

    [Fact]
    public void UploadExternalOpenFailureDoesNotBecomeUploadFailure()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var openBlock = GetMethodBlock(source, "private static bool OpenExternalUrl(string url)");

        Assert.Contains("if (string.IsNullOrWhiteSpace(url))", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"No browser URL was generated for the upload.\");", openBlock);
        Assert.Contains("Process.Start(new ProcessStartInfo", openBlock);
        Assert.Contains("UseShellExecute = true", openBlock);
        Assert.Contains("if (process is null)", openBlock);
        Assert.Contains("ToastWindow.ShowError(\"Open failed\", \"Windows did not open the browser.\");", openBlock);
        Assert.Contains("return true;", openBlock);
        Assert.Contains("catch (Exception ex)", openBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"upload.external-url-open\"", openBlock);
        Assert.Contains("OddSnap could not open the browser for this upload. Copy the upload link from the toast or History and open it manually.", openBlock);
        Assert.Contains("return false;", openBlock);
        Assert.DoesNotContain("SaveUploadFailure(", openBlock);
    }

    [Fact]
    public void UploadHistoryProviderUpdatesToCurrentProvider()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");

        Assert.Contains("var previousProvider = entry.UploadProvider;", uploadBlock);
        Assert.Contains("entry.UploadProvider = providerName;", uploadBlock);
        Assert.Contains("if (string.IsNullOrWhiteSpace(previousProvider))", uploadBlock);

        var providerUpdateIndex = uploadBlock.IndexOf("entry.UploadProvider = providerName;", StringComparison.Ordinal);
        var filenamePrefixIndex = uploadBlock.IndexOf("if (string.IsNullOrWhiteSpace(previousProvider))", providerUpdateIndex, StringComparison.Ordinal);
        var clearErrorIndex = uploadBlock.IndexOf("entry.UploadError = null;", filenamePrefixIndex, StringComparison.Ordinal);
        Assert.True(filenamePrefixIndex > providerUpdateIndex, "Regular upload should update provider before first-upload filename prefix handling.");
        Assert.True(clearErrorIndex > filenamePrefixIndex, "Regular upload should clear stale upload errors after provider metadata is current.");

        var failureHelper = GetMethodBlock(source, "private void SaveUploadFailure(string? filePath, Services.HistoryEntry? historyEntry, string providerName, string error)");
        Assert.Contains("entry.UploadProvider = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", failureHelper);
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(entry.UploadProvider))", failureHelper);

        var successHelper = GetMethodBlock(source, "private void SaveUploadSuccess(string? filePath, Services.HistoryEntry? historyEntry, string url, string providerName)");
        Assert.Contains("entry.UploadProvider = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", successHelper);
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(entry.UploadProvider))", successHelper);
    }

    [Fact]
    public void UploadConfigurationFailureIsSavedBeforeToast()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");

        var configIndex = uploadBlock.IndexOf("var configurationError = UploadService.GetConfigurationError(dest, settings);", StringComparison.Ordinal);
        var branchIndex = uploadBlock.IndexOf("if (!string.IsNullOrWhiteSpace(configurationError))", configIndex, StringComparison.Ordinal);
        var saveFailureIndex = uploadBlock.IndexOf("SaveUploadFailure(filePath, historyEntry, UploadService.GetName(dest), configurationError);", branchIndex, StringComparison.Ordinal);
        var toastIndex = uploadBlock.IndexOf("ToastWindow.ShowError(\"Upload not configured\"", saveFailureIndex, StringComparison.Ordinal);
        var returnIndex = uploadBlock.IndexOf("return;", toastIndex, StringComparison.Ordinal);

        Assert.True(branchIndex > configIndex, "Upload configuration should be checked before upload starts.");
        Assert.True(saveFailureIndex > branchIndex, "Upload configuration failures should save history status.");
        Assert.True(toastIndex > saveFailureIndex, "Upload configuration failure should save history before user feedback.");
        Assert.True(returnIndex > toastIndex, "Upload configuration failure should return after feedback.");
    }

    [Fact]
    public void UploadFailureToastIncludesProviderAndRecovery()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var helperBlock = GetMethodBlock(source, "private static string BuildUploadFailureToastBody(string? savedFileName, string providerName, string error, bool isRateLimit)");

        Assert.Contains("var providerLabel = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", helperBlock);
        Assert.Contains("var detail = $\"{providerLabel}: {error}\";", helperBlock);
        Assert.Contains("Try another upload destination or wait before retrying.", helperBlock);
        Assert.Contains("Check {providerLabel} settings or try another upload destination.", helperBlock);
        Assert.Contains("Saved to {savedFileName}", helperBlock);

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        var failureIndex = uploadBlock.IndexOf("else\n            {", StringComparison.Ordinal);
        var providerIndex = uploadBlock.IndexOf("var providerName = UploadService.GetName(dest);", failureIndex, StringComparison.Ordinal);
        var logIndex = uploadBlock.IndexOf("AppDiagnostics.LogWarning(\"upload.toast-failed\"", providerIndex, StringComparison.Ordinal);
        var saveFailureIndex = uploadBlock.IndexOf("SaveUploadFailure(filePath, historyEntry, providerName, errMsg);", providerIndex, StringComparison.Ordinal);
        var bodyIndex = uploadBlock.IndexOf("var body = BuildUploadFailureToastBody(saved, providerName, errMsg, result.IsRateLimit);", saveFailureIndex, StringComparison.Ordinal);
        var toastIndex = uploadBlock.IndexOf("ToastWindow.ShowError(errTitle, body, filePath);", bodyIndex, StringComparison.Ordinal);

        Assert.True(providerIndex > failureIndex, "Upload failure should resolve the provider once for history and feedback.");
        Assert.True(logIndex > providerIndex, "Upload failure diagnostics should use the resolved provider.");
        Assert.True(saveFailureIndex > providerIndex, "Upload failure should save provider-specific history status.");
        Assert.True(bodyIndex > saveFailureIndex, "Upload failure toast should include provider-specific recovery after saving history.");
        Assert.True(toastIndex > bodyIndex, "Upload failure should show the provider-specific body.");
    }

    [Fact]
    public void UploadUnexpectedErrorToastIncludesSavedFileAndRecovery()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");

        var catchIndex = uploadBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var errorIndex = uploadBlock.IndexOf("var errMsg = CleanErrorMessage(ex.Message);", catchIndex, StringComparison.Ordinal);
        var bodyIndex = uploadBlock.IndexOf("var body = BuildUploadUnexpectedErrorToastBody(saved, errMsg);", errorIndex, StringComparison.Ordinal);
        var saveIndex = uploadBlock.IndexOf("SaveUploadFailure(filePath, historyEntry, \"Upload\", errMsg);", bodyIndex, StringComparison.Ordinal);
        var toastIndex = uploadBlock.IndexOf("ToastWindow.ShowError(\"Upload error\", body, filePath);", saveIndex, StringComparison.Ordinal);

        Assert.True(bodyIndex > errorIndex, "Unexpected upload errors should build contextual recovery copy after cleaning the error.");
        Assert.True(saveIndex > bodyIndex && toastIndex > saveIndex,
            "Unexpected upload errors should save history status before showing contextual feedback.");
        Assert.DoesNotContain("var body = saved != null ? $\"Saved to {saved}\\n{errMsg}\" : errMsg;", uploadBlock);

        var helperBlock = GetMethodBlock(source, "private static string BuildUploadUnexpectedErrorToastBody(string? savedFileName, string error)");
        Assert.Contains("The file is still saved. Check Settings -> Uploads, then retry from History or try another destination.", helperBlock);
        Assert.Contains("Saved to {savedFileName}", helperBlock);
    }

    [Fact]
    public void AiRedirectMissingProviderIsSavedBeforeBrowserOpen()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectMissingProviderOrder(uploadBlock);

        var redirectBlock = GetMethodBlock(source, "private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectMissingProviderOrder(redirectBlock);
    }

    [Fact]
    public void AiRedirectReadyFeedbackRequiresBrowserOpenSuccess()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectOpenGateOrder(uploadBlock);

        var redirectBlock = GetMethodBlock(source, "private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectOpenGateOrder(redirectBlock);

        var aiRedirectFailureBodyBlock = GetMethodBlock(source, "private static string BuildAiRedirectFailureToastBody(string details)");
        Assert.Contains("OddSnap could not finish AI Redirect. Check Settings -> Uploads, then retry from the saved file.", aiRedirectFailureBodyBlock);
        Assert.Contains("string.IsNullOrWhiteSpace(details) ? recovery : $\"{recovery}\\n{details}\"", aiRedirectFailureBodyBlock);
        Assert.Contains("BuildAiRedirectFailureToastBody(CleanErrorMessage(ex.Message))", source);
        Assert.DoesNotContain("ToastWindow.ShowError(\"AI Redirect failed\", CleanErrorMessage(ex.Message), filePath);", source);
    }

    [Fact]
    public void AiRedirectImageCopyFailureKeepsPinnedFallbackToast()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var copyHelper = GetMethodBlock(source, "private static bool TryCopyAiRedirectImageToClipboard(Bitmap previewBitmap, string filePath)");

        Assert.Contains("ClipboardService.CopyToClipboard(previewBitmap, filePath);", copyHelper);
        Assert.Contains("AppDiagnostics.LogWarning(\"ai-redirect.copy-failed\"", copyHelper);
        Assert.Contains("return false;", copyHelper);

        Assert.Equal(1, CountOccurrences(source, "ClipboardService.CopyToClipboard(previewBitmap, filePath);"));
        Assert.Contains("var copySucceeded = TryCopyAiRedirectImageToClipboard(previewBitmap, filePath);", source);
        Assert.Contains("Clipboard copy failed; drag the image from this pinned toast.", source);

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectCopyFallbackOrder(uploadBlock);

        var redirectBlock = GetMethodBlock(source, "private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)");
        AssertAiRedirectCopyFallbackOrder(redirectBlock);
    }

    [Fact]
    public void AiRedirectPreviewLoadFailureFallsBackWithDiagnostics()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));
        var helperBlock = GetMethodBlock(source, "private static Bitmap? TryLoadPreviewBitmap(string filePath)");

        Assert.Contains("new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);", helperBlock);
        Assert.Contains("return new Bitmap(source);", helperBlock);
        Assert.Contains("catch (Exception ex)", helperBlock);
        Assert.Contains("AppDiagnostics.LogWarning(\"ai-redirect.preview-load\"", helperBlock);
        Assert.Contains("return null;", helperBlock);
        Assert.DoesNotContain("catch\n", helperBlock);
        Assert.DoesNotContain("catch\r\n", helperBlock);

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        Assert.Contains("var previewBitmap = TryLoadPreviewBitmap(filePath);", uploadBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"AI Redirect Ready\", $\"Opened {providerName}. Use Ctrl+V in the chat box.\", filePath) with { SuppressSound = true });", uploadBlock);

        var redirectBlock = GetMethodBlock(source, "private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)");
        Assert.Contains("var previewBitmap = TryLoadPreviewBitmap(filePath);", redirectBlock);
        Assert.Contains("ToastWindow.Show(ToastSpec.Standard(\"AI Redirect Ready\", $\"Opened {providerName}. Use Ctrl+V in the chat box.\", filePath) with { SuppressSound = true });", redirectBlock);
    }

    [Fact]
    public void GoogleLensUploadHistoryStatusTracksUploadNotBrowserOpen()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Upload.cs"));

        var successHelper = GetMethodBlock(source, "private void SaveUploadSuccess(string? filePath, Services.HistoryEntry? historyEntry, string url, string providerName)");
        Assert.Contains("entry.UploadUrl = url;", successHelper);
        Assert.Contains("entry.UploadProvider = string.IsNullOrWhiteSpace(providerName) ? \"Upload\" : providerName;", successHelper);
        Assert.Contains("entry.UploadError = null;", successHelper);
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(entry.UploadProvider))", successHelper);
        Assert.Contains("EnsureHistoryService().SaveEntry(entry);", successHelper);
        Assert.Contains("AppDiagnostics.LogError(\"upload.history-success\", ex);", successHelper);

        var uploadBlock = GetMethodBlock(source, "private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)");
        AssertGoogleLensHistoryStatusOrder(uploadBlock);

        var redirectBlock = GetMethodBlock(source, "private async Task StartAiRedirectAsync(string filePath, Services.HistoryEntry? historyEntry = null)");
        AssertGoogleLensHistoryStatusOrder(redirectBlock);
    }

    private static void AssertAiRedirectCopyFallbackOrder(string methodBlock)
    {
        var openIndex = methodBlock.IndexOf("if (!OpenExternalUrl(startUrl))", StringComparison.Ordinal);
        var copyIndex = methodBlock.IndexOf("TryCopyAiRedirectImageToClipboard(previewBitmap, filePath);", openIndex, StringComparison.Ordinal);
        var fallbackIndex = methodBlock.IndexOf("Clipboard copy failed; drag the image from this pinned toast.", copyIndex, StringComparison.Ordinal);
        var previewIndex = methodBlock.IndexOf("ToastSpec.ImagePreview", copyIndex, StringComparison.Ordinal);

        Assert.True(openIndex >= 0, "AI Redirect should open the target URL first.");
        Assert.True(copyIndex > openIndex, "AI Redirect image copy should happen after opening the target URL.");
        Assert.True(fallbackIndex > copyIndex, "AI Redirect should keep visible drag fallback copy after image-copy failure.");
        Assert.True(previewIndex > copyIndex, "AI Redirect should still show the pinned image preview after image-copy failure.");
    }

    private static void AssertAiRedirectOpenGateOrder(string methodBlock)
    {
        var lensOpenIndex = methodBlock.IndexOf("if (!OpenExternalUrl(lensUrl))", StringComparison.Ordinal);
        var lensReturnIndex = methodBlock.IndexOf("return;", lensOpenIndex, StringComparison.Ordinal);
        var lensReadyIndex = methodBlock.IndexOf("ToastWindow.Show(ToastSpec.Standard(\"Google Lens Ready\"", lensReturnIndex, StringComparison.Ordinal);
        Assert.True(lensOpenIndex >= 0, "Google Lens redirect should gate browser open failures.");
        Assert.True(lensReturnIndex > lensOpenIndex, "Google Lens redirect should return when browser open fails.");
        Assert.True(lensReadyIndex > lensReturnIndex, "Google Lens ready feedback should only run after browser open succeeds.");

        var chatOpenIndex = methodBlock.IndexOf("if (!OpenExternalUrl(startUrl))", StringComparison.Ordinal);
        var chatReturnIndex = methodBlock.IndexOf("return;", chatOpenIndex, StringComparison.Ordinal);
        var chatReadyIndex = methodBlock.IndexOf("AI Redirect Ready", chatReturnIndex, StringComparison.Ordinal);
        Assert.True(chatOpenIndex >= 0, "AI Redirect should gate browser open failures.");
        Assert.True(chatReturnIndex > chatOpenIndex, "AI Redirect should return when browser open fails.");
        Assert.True(chatReadyIndex > chatReturnIndex, "AI Redirect ready feedback should only run after browser open succeeds.");
    }

    private static void AssertAiRedirectMissingProviderOrder(string methodBlock)
    {
        var startUrlIndex = methodBlock.IndexOf("var startUrl = UploadService.BuildAiChatStartUrl(settings.AiChatProvider);", StringComparison.Ordinal);
        var blankBranchIndex = methodBlock.IndexOf("if (string.IsNullOrWhiteSpace(startUrl))", startUrlIndex, StringComparison.Ordinal);
        var errorIndex = methodBlock.IndexOf("var errMsg = \"Choose an AI Redirect provider in Settings -> Uploads.\";", blankBranchIndex, StringComparison.Ordinal);
        var saveIndex = methodBlock.IndexOf("SaveUploadFailure(filePath, historyEntry, providerName, errMsg);", errorIndex, StringComparison.Ordinal);
        var toastIndex = methodBlock.IndexOf("ToastWindow.ShowError(\"AI Redirect not configured\"", saveIndex, StringComparison.Ordinal);
        var returnIndex = methodBlock.IndexOf("return;", toastIndex, StringComparison.Ordinal);
        var openIndex = methodBlock.IndexOf("if (!OpenExternalUrl(startUrl))", returnIndex, StringComparison.Ordinal);

        Assert.True(blankBranchIndex > startUrlIndex, "AI Redirect should check for a missing provider URL before browser open.");
        Assert.True(saveIndex > errorIndex, "Missing AI Redirect provider should save history status.");
        Assert.True(toastIndex > saveIndex, "Missing AI Redirect provider should save history before feedback.");
        Assert.True(returnIndex > toastIndex, "Missing AI Redirect provider should return after feedback.");
        Assert.True(openIndex > returnIndex, "AI Redirect should only open the browser after provider configuration succeeds.");
    }

    private static void AssertGoogleLensHistoryStatusOrder(string methodBlock)
    {
        var failureIndex = methodBlock.IndexOf("if (!lensUpload.Success || string.IsNullOrWhiteSpace(lensUpload.Url))", StringComparison.Ordinal);
        var saveFailureIndex = methodBlock.IndexOf("SaveUploadFailure(filePath, historyEntry, providerName, errMsg);", failureIndex, StringComparison.Ordinal);
        var failureToastIndex = methodBlock.IndexOf("ToastWindow.ShowError(\"Google Lens upload failed\"", saveFailureIndex, StringComparison.Ordinal);
        Assert.True(saveFailureIndex > failureIndex, "Google Lens upload failure should save upload history status.");
        Assert.True(failureToastIndex > saveFailureIndex, "Google Lens upload failure should save history before showing feedback.");

        var lensUrlIndex = methodBlock.IndexOf("var lensUrl = UploadService.BuildGoogleLensUrl(lensUpload.Url);", StringComparison.Ordinal);
        var saveIndex = methodBlock.IndexOf("SaveUploadSuccess(filePath, historyEntry, lensUpload.Url, lensUpload.ProviderName);", lensUrlIndex, StringComparison.Ordinal);
        var openIndex = methodBlock.IndexOf("if (!OpenExternalUrl(lensUrl))", saveIndex, StringComparison.Ordinal);
        var returnIndex = methodBlock.IndexOf("return;", openIndex, StringComparison.Ordinal);
        var readyIndex = methodBlock.IndexOf("Google Lens Ready", returnIndex, StringComparison.Ordinal);

        Assert.True(lensUrlIndex >= 0, "Google Lens should build a Lens URL from the uploaded image URL.");
        Assert.True(saveIndex > lensUrlIndex, "Google Lens should save successful upload history before browser handoff.");
        Assert.True(openIndex > saveIndex, "Google Lens should record upload success before browser open can fail.");
        Assert.True(returnIndex > openIndex && readyIndex > returnIndex, "Ready feedback should still require browser open success.");

        var browserOpenFailureBranch = methodBlock[openIndex..readyIndex];
        Assert.DoesNotContain("SaveUploadFailure(", browserOpenFailureBranch);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
