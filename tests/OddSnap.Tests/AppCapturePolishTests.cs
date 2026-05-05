using Xunit;

namespace OddSnap.Tests;

public sealed class AppCapturePolishTests
{
    [Fact]
    public void CaptureOutputCopyFailuresDoNotBreakResultFlow()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.Handlers.cs"));

        var copyHelper = GetMethodBlock(source, "private static bool TryCopyCaptureOutputToClipboard(Bitmap output)");
        Assert.Contains("ClipboardService.CopyToClipboard(output);", copyHelper);
        Assert.Contains("OddSnap could not copy the capture. The result flow will continue.", copyHelper);
        Assert.Contains("return false;", copyHelper);

        Assert.DoesNotContain("ClipboardService.CopyToClipboard(persisted.Output);", source);

        var captureBlock = GetMethodBlock(source, "private void HandleCaptureResult(Bitmap result, bool useAiRedirect = false)");
        var captureCopyIndex = captureBlock.IndexOf("TryCopyCaptureOutputToClipboard(persisted.Output);", StringComparison.Ordinal);
        var captureResetIndex = captureBlock.IndexOf("ResetCapturing();", captureCopyIndex, StringComparison.Ordinal);
        var captureUploadDecisionIndex = captureBlock.IndexOf("bool willUpload", captureResetIndex, StringComparison.Ordinal);
        Assert.True(captureCopyIndex >= 0, "Screenshot capture should use guarded clipboard copy.");
        Assert.True(captureResetIndex > captureCopyIndex, "Screenshot capture should reset even after copy failures.");
        Assert.True(captureUploadDecisionIndex > captureResetIndex, "Screenshot upload/preview decisions should continue after reset.");

        var stickerBlock = GetMethodBlock(source, "private void HandleStickerResult(Bitmap result, string providerName)");
        AssertCopySuccessControlsReadyToast(stickerBlock, "Sticker copied", "Sticker ready");

        var upscaleBlock = GetMethodBlock(source, "private void HandleUpscaleResult(Bitmap result, string providerName)");
        AssertCopySuccessControlsReadyToast(upscaleBlock, "Upscale copied", "Upscale ready");
    }

    [Fact]
    public void CaptureTextCopyFailuresKeepScanAndColorFeedback()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));

        var copyHelper = GetMethodBlock(source, "private static bool TryCopyCaptureTextToClipboard(string text)");
        Assert.Contains("ClipboardService.CopyTextToClipboard(text);", copyHelper);
        Assert.Contains("OddSnap could not copy this capture result. The result will still be shown and saved when history is enabled.", copyHelper);
        Assert.Contains("return false;", copyHelper);

        Assert.Equal(1, CountOccurrences(source, "ClipboardService.CopyTextToClipboard("));

        var scanCopyIndex = source.IndexOf("var copySucceeded = TryCopyCaptureTextToClipboard(decoded.Text);", StringComparison.Ordinal);
        var scanHistoryIndex = source.IndexOf("_historyService?.SaveCodeEntry(decoded.Text, decoded.Format.ToString());", scanCopyIndex, StringComparison.Ordinal);
        var qrFoundIndex = source.IndexOf("\"QR Code found\"", scanCopyIndex, StringComparison.Ordinal);
        var barcodeFoundIndex = source.IndexOf("\"Barcode found\"", scanCopyIndex, StringComparison.Ordinal);
        var scanPreviewIndex = source.IndexOf("ToastWindow.ShowInlinePreview(preview, title, prev, suppressSound: true);", scanCopyIndex, StringComparison.Ordinal);

        Assert.True(scanCopyIndex >= 0, "QR/barcode scan should use guarded text copy.");
        Assert.True(scanHistoryIndex > scanCopyIndex, "QR/barcode scan history should still save after copy failures.");
        Assert.True(qrFoundIndex > scanCopyIndex, "QR fallback title should avoid claiming copied.");
        Assert.True(barcodeFoundIndex > scanCopyIndex, "Barcode fallback title should avoid claiming copied.");
        Assert.True(scanPreviewIndex > scanCopyIndex, "QR/barcode scan should still show the decoded preview after copy failures.");

        var colorCopyIndex = source.IndexOf("var copySucceeded = TryCopyCaptureTextToClipboard(bare);", StringComparison.Ordinal);
        var colorPickedIndex = source.IndexOf("copySucceeded ? \"Color copied\" : \"Color picked\"", colorCopyIndex, StringComparison.Ordinal);
        var colorHistoryIndex = source.IndexOf("EnsureHistoryService().SaveColorEntry(bare);", colorCopyIndex, StringComparison.Ordinal);

        Assert.True(colorCopyIndex >= 0, "Color picker should use guarded text copy.");
        Assert.True(colorPickedIndex > colorCopyIndex, "Color picker should avoid claiming copied after copy failures.");
        Assert.True(colorHistoryIndex > colorCopyIndex, "Color history should still save after copy failures.");
    }

    [Fact]
    public void CaptureProcessingFailuresShowRecoveryCopy()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.Handlers.cs"));

        var captureBlock = GetMethodBlock(source, "private void HandleCaptureResult(Bitmap result, bool useAiRedirect = false)");
        Assert.Contains("OddSnap could not finish the capture result. Try again, or choose another save folder in Settings.", captureBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Capture error\", task.Exception?.GetBaseException().Message", captureBlock);

        var stickerBlock = GetMethodBlock(source, "private void HandleStickerResult(Bitmap result, string providerName)");
        Assert.Contains("OddSnap could not finish the sticker result. Try again, or check Settings -> Stickers.", stickerBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Sticker error\", task.Exception?.GetBaseException().Message", stickerBlock);

        var upscaleBlock = GetMethodBlock(source, "private void HandleUpscaleResult(Bitmap result, string providerName)");
        Assert.Contains("OddSnap could not finish the upscale result. Try again, or check Settings -> Upscale.", upscaleBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upscale error\", task.Exception?.GetBaseException().Message", upscaleBlock);

        var ocrBlock = GetMethodBlock(source, "private void HandleOcrResult(Bitmap result)");
        Assert.Contains("OddSnap could not read text from this capture. Try a clearer region, or check Settings -> OCR.", ocrBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"OCR error\", ex.Message);", ocrBlock);

        var helperBlock = GetMethodBlock(source, "private static void ShowCaptureProcessingFailed(string title, string recoveryMessage, string details)");
        Assert.Contains("ToastWindow.ShowError(title, $\"{recoveryMessage}\\n{details}\");", helperBlock);
    }

    [Fact]
    public void RecordingClipboardStatusIsShownInCompletionFeedback()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));

        var copyHelper = GetMethodBlock(source, "private static bool TryCopyRecordingFileToClipboard(string path)");
        Assert.Contains("System.Windows.Clipboard.SetFileDropList(files);", copyHelper);
        Assert.Contains("return true;", copyHelper);
        Assert.Contains("return false;", copyHelper);

        Assert.DoesNotContain("System.Windows.Clipboard.SetFileDropList(files);\r\n                        }\r\n                        catch { }", source);

        var recordingBlock = GetMethodBlock(source, "private void LaunchGifRecording()");
        var copyIndex = recordingBlock.IndexOf("var copiedToClipboard = TryCopyRecordingFileToClipboard(path);", StringComparison.Ordinal);
        var uploadDecisionIndex = recordingBlock.IndexOf("bool willUpload", copyIndex, StringComparison.Ordinal);
        var previewFeedbackIndex = recordingBlock.IndexOf("copiedToClipboard ? \"File copied to clipboard\" : \"Saved; clipboard copy failed\"", copyIndex, StringComparison.Ordinal);
        var textFeedbackIndex = recordingBlock.IndexOf("var copyStatus = copiedToClipboard ? \"File copied to clipboard\" : \"Saved; clipboard copy failed\";", copyIndex, StringComparison.Ordinal);

        Assert.True(copyIndex >= 0, "Recording completion should track clipboard copy status.");
        Assert.True(uploadDecisionIndex > copyIndex, "Recording upload decisions should continue after clipboard copy attempts.");
        Assert.True(previewFeedbackIndex > copyIndex, "Recording preview feedback should mention clipboard copy status.");
        Assert.True(textFeedbackIndex > copyIndex, "Recording text feedback should mention clipboard copy status.");

        Assert.DoesNotContain("catch { }", recordingBlock);
        Assert.Contains("catch (Exception ex)", recordingBlock);
        Assert.Contains("AppDiagnostics.LogError(\"capture.recording-history\"", recordingBlock);
        Assert.Contains("Failed to save recording history", recordingBlock);
    }

    [Fact]
    public void InCaptureFailuresShowModeSpecificRecoveryCopy()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "App", "App.Capture.cs"));

        var recordingBlock = GetMethodBlock(source, "private void LaunchGifRecording()");
        Assert.Contains("OddSnap could not finish the recording. Try again, or check Settings -> Recording.", recordingBlock);
        Assert.Contains("OddSnap could not start recording. Try again, or check Settings -> Recording.", recordingBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Recording error\", ex.Message);", recordingBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Recording error\", \"Recording failed\");", recordingBlock);

        var scrollingBlock = GetMethodBlock(source, "private void LaunchScrollingCapture()");
        Assert.Contains("OddSnap could not finish the scrolling capture. Try a smaller scroll area or a visible scrollable window.", scrollingBlock);
        Assert.Contains("OddSnap could not start scrolling capture. Try again with a visible scrollable window.", scrollingBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Scroll capture error\", message);", scrollingBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Scroll capture error\", \"Scrolling capture failed\");", scrollingBlock);

        var fullscreenBlock = GetMethodBlock(source, "private void CaptureFullscreenNow()");
        Assert.Contains("OddSnap could not capture the screen. Try again, or choose another capture mode.", fullscreenBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Capture error\", ex.Message);", fullscreenBlock);

        var activeWindowBlock = GetMethodBlock(source, "private void CaptureActiveWindowNow()");
        Assert.Contains("OddSnap could not capture the active window. Try again, or use region capture.", activeWindowBlock);
        Assert.Contains("Focus a visible window and try again.", activeWindowBlock);
        Assert.Contains("Use region capture or move the window onscreen.", activeWindowBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Capture error\", ex.Message);", activeWindowBlock);

        var overlayBlock = GetMethodBlock(source, "private void LaunchOverlayNow(CaptureMode initialMode, bool useAiRedirect = false)");
        Assert.Contains("OddSnap could not scan this region. Try a clearer QR/barcode region.", overlayBlock);
        Assert.Contains("OddSnap could not create the sticker. Check Settings -> Stickers and try again.", overlayBlock);
        Assert.Contains("OddSnap could not upscale this capture. Check Settings -> Upscale and try again.", overlayBlock);
        Assert.Contains("OddSnap could not start the capture overlay. Try again, or check capture settings.", overlayBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Scan failed\", ex.Message);", overlayBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Sticker failed\", ex.Message);", overlayBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Upscale failed\", ex.Message);", overlayBlock);
        Assert.DoesNotContain("ToastWindow.ShowError(\"Capture error\", ex.Message);", overlayBlock);
    }

    private static void AssertCopySuccessControlsReadyToast(string methodBlock, string copiedText, string readyText)
    {
        Assert.Contains("var copyRequested = ShouldCopyAfterCapture(action);", methodBlock);
        Assert.Contains("var copySucceeded = copyRequested && TryCopyCaptureOutputToClipboard(persisted.Output);", methodBlock);
        Assert.Contains($"ToastWindow.Show(copySucceeded ? \"{copiedText}\" : \"{readyText}\");", methodBlock);

        var copyIndex = methodBlock.IndexOf("TryCopyCaptureOutputToClipboard(persisted.Output);", StringComparison.Ordinal);
        var resetIndex = methodBlock.IndexOf("ResetCapturing();", copyIndex, StringComparison.Ordinal);
        Assert.True(resetIndex > copyIndex, $"{copiedText} flow should reset even after copy failures.");
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
