using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Models;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private void HandleCaptureResult(Bitmap result, bool useAiRedirect = false)
    {
        var settings = _settingsService!.Settings;
        var sourceApp = _captureSourceApp;
        var ext = CaptureOutputService.GetExtension(settings.CaptureImageFormat);
        if (!TryResolveCaptureOutputPath(
                () => $"{FormatCaptureFileName(settings, result.Width, result.Height, sourceApp)}.{ext}",
                settings.CaptureImageFormat,
                "Capture error",
                "OddSnap could not prepare the capture save path. Choose another save folder in Settings and try again.",
                out var requestedPath))
        {
            result.Dispose();
            return;
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: false, providerName: null, sourceApp: sourceApp)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var error = task.Exception?.GetBaseException();
                    if (!TryPostToAppDispatcher(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Capture error",
                            "OddSnap could not finish the capture result. Try again, or choose another save folder in Settings.",
                            error?.Message ?? "Capture failed");
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.result-failed-post"))
                    {
                        if (error is not null)
                            AppDiagnostics.LogError("capture.result-failed", error);
                        ResetCapturingWithoutUiRestore();
                    }
                    return;
                }

                var persisted = task.Result;
                var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                var copyRequested = ShouldCopyAfterCapture(action);
                var clipboardPayload = PrepareCaptureClipboardPayload(persisted.Output, persisted.FilePath, copyRequested, out var clipboardPrepareError);
                if (!TryPostToAppDispatcher(() =>
                {
                    if (copyRequested)
                        TryCopyCaptureOutputToClipboard(persisted.Output, persisted.FilePath, clipboardPayload, clipboardPrepareError);
                    ResetCapturing();

                    bool willAiRedirect = useAiRedirect && persisted.FilePath != null;
                    bool willUpload = !willAiRedirect && UploadService.ShouldUploadScreenshot(
                        settings,
                        hasFilePath: persisted.FilePath != null,
                        useAiRedirect: useAiRedirect);

                    if (willAiRedirect)
                    {
                        persisted.Output.Dispose();
                        _ = StartAiRedirectAsync(persisted.FilePath!, persisted.HistoryEntry);
                    }
                    else if (willUpload)
                    {
                        // Don't show preview toast yet — upload handler will show result
                        persisted.Output.Dispose();
                        _ = UploadFileAsync(persisted.FilePath!, "Screenshot", persisted.HistoryEntry);
                    }
                    else
                    {
                        if (ShouldPreviewAfterCapture(action))
                        {
                            ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                        }
                        else
                        {
                            persisted.Output.Dispose();
                            ToastWindow.Show("Screenshot ready", "", persisted.FilePath);
                        }
                    }

                    ScheduleIdleMemoryTrim();
                }, DispatcherPriority.Normal, "capture.result-complete-post"))
                {
                    persisted.Output.Dispose();
                    ResetCapturingWithoutUiRestore();
                }
            }, TaskScheduler.Default);
    }

    private void HandleStickerResult(Bitmap result, string providerName)
    {
        var settings = _settingsService!.Settings;
        var sourceApp = _captureSourceApp;
        if (!TryResolveCaptureOutputPath(
                () => $"{FormatCaptureFileName(settings, result.Width, result.Height, sourceApp)}_sticker.png",
                CaptureImageFormat.Png,
                "Sticker error",
                "OddSnap could not prepare the sticker save path. Choose another save folder in Settings and try again.",
                out var requestedPath))
        {
            result.Dispose();
            return;
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: true, providerName: providerName, sourceApp: sourceApp)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var error = task.Exception?.GetBaseException();
                    if (!TryPostToAppDispatcher(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Sticker error",
                            "OddSnap could not finish the sticker result. Try again, or check Settings -> Stickers.",
                            error?.Message ?? "Sticker processing failed");
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.sticker-result-failed-post"))
                    {
                        if (error is not null)
                            AppDiagnostics.LogError("capture.sticker-result-failed", error);
                        ResetCapturingWithoutUiRestore();
                    }
                    return;
                }

                var persisted = task.Result;
                var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                var copyRequested = ShouldCopyAfterCapture(action);
                var clipboardPayload = PrepareCaptureClipboardPayload(persisted.Output, persisted.FilePath, copyRequested, out var clipboardPrepareError);
                if (!TryPostToAppDispatcher(() =>
                {
                    var copySucceeded = copyRequested && TryCopyCaptureOutputToClipboard(persisted.Output, persisted.FilePath, clipboardPayload, clipboardPrepareError);
                    ResetCapturing();

                    if (ShouldPreviewAfterCapture(action))
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        persisted.Output.Dispose();
                        ToastWindow.Show(copySucceeded ? "Sticker copied" : "Sticker ready");
                    }

                    if (persisted.FilePath != null && settings.AutoUploadScreenshots
                        && settings.ImageUploadDestination != UploadDestination.None)
                    {
                        _ = UploadFileAsync(persisted.FilePath, "Sticker", persisted.HistoryEntry);
                    }

                    ScheduleIdleMemoryTrim();
                }, DispatcherPriority.Normal, "capture.sticker-result-complete-post"))
                {
                    persisted.Output.Dispose();
                    ResetCapturingWithoutUiRestore();
                }
            }, TaskScheduler.Default);
    }

    private void HandleUpscaleResult(Bitmap result, string providerName)
    {
        var settings = _settingsService!.Settings;
        var sourceApp = _captureSourceApp;
        if (!TryResolveCaptureOutputPath(
                () => $"{FormatCaptureFileName(settings, result.Width, result.Height, sourceApp)}_upscale.png",
                CaptureImageFormat.Png,
                "Upscale error",
                "OddSnap could not prepare the upscale save path. Choose another save folder in Settings and try again.",
                out var requestedPath))
        {
            result.Dispose();
            return;
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: false, providerName: providerName, sourceApp: sourceApp)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var error = task.Exception?.GetBaseException();
                    if (!TryPostToAppDispatcher(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Upscale error",
                            "OddSnap could not finish the upscale result. Try again, or check Settings -> Upscale.",
                            error?.Message ?? "Upscale processing failed");
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.upscale-result-failed-post"))
                    {
                        if (error is not null)
                            AppDiagnostics.LogError("capture.upscale-result-failed", error);
                        ResetCapturingWithoutUiRestore();
                    }
                    return;
                }

                var persisted = task.Result;
                var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                var copyRequested = ShouldCopyAfterCapture(action);
                var clipboardPayload = PrepareCaptureClipboardPayload(persisted.Output, persisted.FilePath, copyRequested, out var clipboardPrepareError);
                if (!TryPostToAppDispatcher(() =>
                {
                    var copySucceeded = copyRequested && TryCopyCaptureOutputToClipboard(persisted.Output, persisted.FilePath, clipboardPayload, clipboardPrepareError);
                    ResetCapturing();

                    if (ShouldPreviewAfterCapture(action))
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        persisted.Output.Dispose();
                        ToastWindow.Show(copySucceeded ? "Upscale copied" : "Upscale ready");
                    }

                    if (persisted.FilePath != null && settings.AutoUploadScreenshots
                        && settings.ImageUploadDestination != UploadDestination.None)
                    {
                        _ = UploadFileAsync(persisted.FilePath, "Upscale", persisted.HistoryEntry);
                    }

                    ScheduleIdleMemoryTrim();
                }, DispatcherPriority.Normal, "capture.upscale-result-complete-post"))
                {
                    persisted.Output.Dispose();
                    ResetCapturingWithoutUiRestore();
                }
            }, TaskScheduler.Default);
    }

    private bool TryResolveCaptureOutputPath(
        Func<string> fileNameFactory,
        CaptureImageFormat format,
        string errorTitle,
        string errorRecovery,
        out string? requestedPath)
    {
        requestedPath = null;
        var settings = _settingsService!.Settings;
        if (!settings.SaveToFile)
            return true;

        try
        {
            var defaultPath = Helpers.CaptureSavePath.BuildAvailablePath(
                settings.SaveDirectory,
                fileNameFactory(),
                settings.SaveInMonthlyFolders);

            if (settings.AskForFileNameOnSave)
            {
                // SaveFileDialog must run on the WPF dispatcher thread.
                string? resolved = null;
                if (Dispatcher.CheckAccess())
                    resolved = ResolveSavePath(defaultPath, format);
                else
                    Dispatcher.Invoke(() => resolved = ResolveSavePath(defaultPath, format));
                requestedPath = resolved;
            }
            else
            {
                requestedPath = defaultPath;
            }

            if (requestedPath is null)
            {
                ResetCapturing();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!TryPostToAppDispatcher(() =>
            {
                ResetCapturing();
                ShowCaptureProcessingFailed(errorTitle, errorRecovery, ex.Message);
                ScheduleIdleMemoryTrim();
            }, DispatcherPriority.Normal, "capture.resolve-save-path-failed-post"))
            {
                AppDiagnostics.LogError("capture.resolve-save-path", ex);
                ResetCapturingWithoutUiRestore();
            }
            return false;
        }
    }

    private static string FormatCaptureFileName(AppSettings settings, int width, int height, string? sourceApp) =>
        Helpers.FileNameTemplate.Format(
            settings.FileNameTemplate,
            width,
            height,
            sourceApp,
            settings.IncludeSourceAppInFileName);

    private Task<PersistedCaptureResult> PersistCaptureAsync(
        Bitmap source,
        string? requestedPath,
        bool saveHistory,
        bool isSticker,
        string? providerName,
        string? sourceApp = null)
    {
        var settings = _settingsService!.Settings;
        int maxLongEdge = settings.CaptureMaxLongEdge;
        var captureFormat = settings.CaptureImageFormat;
        int jpegQuality = settings.JpegQuality;

        return Task.Run(() =>
        {
            Bitmap? ownedSource = source;
            Bitmap? output = null;
            try
            {
                if (maxLongEdge > 0 && Math.Max(ownedSource.Width, ownedSource.Height) > maxLongEdge)
                {
                    output = CaptureOutputService.PrepareBitmap(ownedSource, maxLongEdge);
                }
                else
                {
                    output = ownedSource;
                    ownedSource = null;
                }

                string? filePath = requestedPath;
                Services.HistoryEntry? historyEntry = null;
                var historyService = saveHistory ? EnsureHistoryService() : null;

                if (requestedPath != null)
                {
                    var directory = Path.GetDirectoryName(requestedPath);
                    if (string.IsNullOrWhiteSpace(directory))
                        throw new InvalidOperationException("Save path must include a directory.");

                    Directory.CreateDirectory(directory);
                    var savedFormat = isSticker ? CaptureImageFormat.Png : captureFormat;
                    CaptureOutputService.SaveBitmap(output, requestedPath, savedFormat, jpegQuality);
                    Helpers.ImageMetadataWriter.TryWrite(
                        requestedPath,
                        savedFormat,
                        Helpers.ImageMetadataWriter.BuildCaptureMetadata(sourceApp, DateTime.Now));

                    filePath = requestedPath;
                }

                if (historyService != null)
                {
                    if (filePath != null && !isSticker)
                    {
                        historyEntry = historyService.TrackExistingCapture(
                            filePath,
                            output.Width,
                            output.Height,
                            isSticker ? HistoryKind.Sticker : HistoryKind.Image,
                            providerName,
                            sourceApp);
                    }
                    else
                    {
                        historyEntry = isSticker
                            ? historyService.SaveStickerEntry(output, providerName)
                            : historyService.SaveCapture(output, sourceApp);
                        filePath = historyEntry.FilePath;
                    }
                }

                if (historyEntry is not null)
                {
                    try
                    {
                        SettingsWindow.WarmRecentHistoryThumbs(new[] { historyEntry }, maxCount: 1);
                    }
                    catch (Exception ex)
                    {
                        AppDiagnostics.LogWarning("capture.persist.warm-thumbnail", ex.Message, ex);
                    }
                }

                var result = new PersistedCaptureResult
                {
                    Output = output,
                    FilePath = filePath,
                    HistoryEntry = historyEntry
                };
                output = null;
                return result;
            }
            catch
            {
                output?.Dispose();
                throw;
            }
            finally
            {
                ownedSource?.Dispose();
            }
        });
    }


    private static AfterCaptureAction NormalizeAfterCaptureAction(AfterCaptureAction action) =>
        Enum.IsDefined(typeof(AfterCaptureAction), action)
            ? action
            : AfterCaptureAction.PreviewAndCopy;

    private static bool ShouldCopyAfterCapture(AfterCaptureAction action) =>
        action is AfterCaptureAction.CopyToClipboard or AfterCaptureAction.PreviewAndCopy;

    private static bool ShouldPreviewAfterCapture(AfterCaptureAction action) =>
        action is AfterCaptureAction.PreviewAndCopy or AfterCaptureAction.PreviewOnly;

    private static ClipboardService.ImageClipboardPayload? PrepareCaptureClipboardPayload(
        Bitmap output,
        string? filePath,
        bool copyRequested,
        out Exception? prepareError)
    {
        prepareError = null;
        if (!copyRequested)
            return null;

        try
        {
            return ClipboardService.PrepareImageClipboardPayload(output, filePath);
        }
        catch (Exception ex)
        {
            prepareError = ex;
            AppDiagnostics.LogWarning("capture.clipboard.prepare", $"Failed to prepare capture clipboard data: {ex.Message}", ex);
            return null;
        }
    }

    private static bool TryCopyCaptureOutputToClipboard(
        Bitmap output,
        string? filePath,
        ClipboardService.ImageClipboardPayload? preparedPayload = null,
        Exception? prepareError = null)
    {
        try
        {
            if (prepareError is not null)
            {
                throw new InvalidOperationException(
                    "OddSnap could not prepare the image data for the clipboard. Try saving or dragging the preview instead.",
                    prepareError);
            }

            if (preparedPayload is not null)
                ClipboardService.CopyPreparedImageToClipboard(output, preparedPayload);
            else
                ClipboardService.CopyToClipboard(output, filePath);

            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy the capture. The result flow will continue.\n{ex.Message}");
            return false;
        }
    }

    private static void ShowCaptureProcessingFailed(string title, string recoveryMessage, string details)
    {
        ToastWindow.ShowError(title, $"{recoveryMessage}\n{details}");
    }

    private void HandleOcrResult(Bitmap result)
    {
        if (!TryPostToAppDispatcherAsync(async () =>
        {
            try
            {
                var langTag = _settingsService?.Settings.OcrLanguageTag;
                string text = await OcrService.RecognizeAsync(result, langTag);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SoundService.PlayTextSound();

                    if (_settingsService!.Settings.SaveHistory)
                        EnsureHistoryService().SaveOcrEntry(text);

                    if (_settingsService.Settings.OcrAutoCopyToClipboard)
                    {
                        var copied = TryCopyCaptureTextToClipboard(text);
                        ToastWindow.Show(copied
                            ? ToastSpec.Standard("OCR copied", ToastSpec.CompactTextPreview(text)) with { SuppressSound = true }
                            : ToastSpec.Standard("OCR ready", "Clipboard copy failed."));
                        if (!copied)
                        {
                            var window = new OcrResultWindow(text, _settingsService);
                            window.Show();
                        }
                    }
                    else
                    {
                        var window = new OcrResultWindow(text, _settingsService);
                        window.Show();
                    }
                }
                else
                {
                    ToastWindow.Show("OCR", "No text found");
                }
            }
            catch (Exception ex)
            {
                ShowCaptureProcessingFailed(
                    "OCR error",
                    "OddSnap could not read text from this capture. Try a clearer region, or check Settings -> OCR.",
                    ex.Message);
            }
            finally { result.Dispose(); }
            ScheduleIdleMemoryTrim();
        }, DispatcherPriority.Normal, "capture.ocr-post"))
        {
            result.Dispose();
        }
    }

}
