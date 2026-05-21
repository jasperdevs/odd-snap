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

        // Shottr-style flow: keep the capture in memory and show a temporary overlay.
        // No file is written and no history entry is created until the user clicks Save.
        // AI Redirect requires a saved file, so this branch is intentionally skipped when useAiRedirect is true.
        if (settings.TemporaryCaptureMode && !useAiRedirect)
        {
            HandleTemporaryCaptureResult(result, settings);
            return;
        }

        var ext = CaptureOutputService.GetExtension(settings.CaptureImageFormat);
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultPath = Helpers.CaptureSavePath.BuildAvailablePath(
                settings.SaveDirectory,
                $"{Helpers.FileNameTemplate.Format(settings.FileNameTemplate, result.Width, result.Height)}.{ext}",
                settings.SaveInMonthlyFolders);
            if (settings.AskForFileNameOnSave)
            {
                // SaveFileDialog must run on the WPF dispatcher thread
                string? resolved = null;
                Dispatcher.Invoke(() => resolved = ResolveSavePath(defaultPath, settings.CaptureImageFormat));
                requestedPath = resolved;
            }
            else
            {
                requestedPath = defaultPath;
            }
            if (requestedPath is null)
            {
                result.Dispose();
                ResetCapturing();
                return;
            }
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: false, providerName: null)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Capture error",
                            "OddSnap could not finish the capture result. Try again, or choose another save folder in Settings.",
                            task.Exception?.GetBaseException().Message ?? "Capture failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    if (ShouldCopyAfterCapture(action))
                        TryCopyCaptureOutputToClipboard(persisted.Output);
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
                });
            }, TaskScheduler.Default);
    }

    private void HandleTemporaryCaptureResult(Bitmap result, AppSettings settings)
    {
        // Critical invariant: PermanentPath stays null for this flow. The Save button
        // on the toast preview is the only path that writes to disk and creates a
        // history entry. We skip PersistCaptureAsync (and history-on-capture)
        // entirely so the user owns the persistence decision.
        Dispatcher.BeginInvoke(() =>
        {
            ResetCapturing();

            Bitmap? preview = null;
            try
            {
                preview = CaptureOutputService.PrepareBitmap(result, settings.CaptureMaxLongEdge);

                if (settings.CopyAfterCapture)
                    TryCopyCaptureOutputToClipboard(preview);

                bool autoPin = settings.AutoPinPreviews || settings.OverlayTimeoutSeconds == 0;
                if (autoPin)
                    ToastWindow.ShowImagePreview(preview, filePath: null, autoPin: true);
                else
                    ToastWindow.ShowImagePreviewWithDuration(preview, filePath: null, autoPin: false, settings.OverlayTimeoutSeconds);

                // Toast now owns the preview bitmap.
                preview = null;
            }
            catch (Exception ex)
            {
                preview?.Dispose();
                ShowCaptureProcessingFailed(
                    "Capture error",
                    "OddSnap could not finish the capture. Try again, or turn off Temporary capture mode in Settings -> General.",
                    ex.Message);
            }
            finally
            {
                result.Dispose();
            }

            ScheduleIdleMemoryTrim();
        });
    }

    private void HandleStickerResult(Bitmap result, string providerName)
    {
        var settings = _settingsService!.Settings;
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultStickerPath = Helpers.CaptureSavePath.BuildAvailablePath(
                settings.SaveDirectory,
                $"{Helpers.FileNameTemplate.Format(settings.FileNameTemplate, result.Width, result.Height)}_sticker.png",
                settings.SaveInMonthlyFolders);
            requestedPath = settings.AskForFileNameOnSave
                ? ResolveSavePath(defaultStickerPath, CaptureImageFormat.Png)
                : defaultStickerPath;
            if (requestedPath is null)
            {
                result.Dispose();
                ResetCapturing();
                return;
            }
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: true, providerName: providerName)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Sticker error",
                            "OddSnap could not finish the sticker result. Try again, or check Settings -> Stickers.",
                            task.Exception?.GetBaseException().Message ?? "Sticker processing failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    var copyRequested = ShouldCopyAfterCapture(action);
                    var copySucceeded = copyRequested && TryCopyCaptureOutputToClipboard(persisted.Output);
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
                });
            }, TaskScheduler.Default);
    }

    private void HandleUpscaleResult(Bitmap result, string providerName)
    {
        var settings = _settingsService!.Settings;
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultUpscalePath = Helpers.CaptureSavePath.BuildAvailablePath(
                settings.SaveDirectory,
                $"{Helpers.FileNameTemplate.Format(settings.FileNameTemplate, result.Width, result.Height)}_upscale.png",
                settings.SaveInMonthlyFolders);
            requestedPath = settings.AskForFileNameOnSave
                ? ResolveSavePath(defaultUpscalePath, CaptureImageFormat.Png)
                : defaultUpscalePath;
            if (requestedPath is null)
            {
                result.Dispose();
                ResetCapturing();
                return;
            }
        }

        _ = PersistCaptureAsync(result, requestedPath, saveHistory: settings.SaveHistory, isSticker: false, providerName: providerName)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Upscale error",
                            "OddSnap could not finish the upscale result. Try again, or check Settings -> Upscale.",
                            task.Exception?.GetBaseException().Message ?? "Upscale processing failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    var copyRequested = ShouldCopyAfterCapture(action);
                    var copySucceeded = copyRequested && TryCopyCaptureOutputToClipboard(persisted.Output);
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
                });
            }, TaskScheduler.Default);
    }

    private Task<PersistedCaptureResult> PersistCaptureAsync(
        Bitmap source,
        string? requestedPath,
        bool saveHistory,
        bool isSticker,
        string? providerName)
    {
        var settings = _settingsService!.Settings;
        int maxLongEdge = settings.CaptureMaxLongEdge;
        var captureFormat = settings.CaptureImageFormat;
        int jpegQuality = settings.JpegQuality;

        return Task.Run(() =>
        {
            using (source)
            {
                var prepared = CaptureOutputService.PrepareBitmap(source, maxLongEdge);
                var output = prepared;
                string? filePath = requestedPath;
                Services.HistoryEntry? historyEntry = null;
                var historyService = saveHistory ? EnsureHistoryService() : null;

                if (requestedPath != null)
                {
                    var directory = Path.GetDirectoryName(requestedPath);
                    if (string.IsNullOrWhiteSpace(directory))
                        throw new InvalidOperationException("Save path must include a directory.");

                    Directory.CreateDirectory(directory);
                    if (isSticker)
                        CaptureOutputService.SaveBitmap(output, requestedPath, CaptureImageFormat.Png, jpegQuality);
                    else
                        CaptureOutputService.SaveBitmap(output, requestedPath, captureFormat, jpegQuality);

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
                            providerName);
                    }
                    else
                    {
                        historyEntry = isSticker
                            ? historyService.SaveStickerEntry(output, providerName)
                            : historyService.SaveCapture(output);
                        filePath = historyEntry.FilePath;
                    }
                }

                if (historyEntry is not null)
                    SettingsWindow.WarmRecentHistoryThumbs(new[] { historyEntry }, maxCount: 1);

                return new PersistedCaptureResult
                {
                    Output = output,
                    FilePath = filePath,
                    HistoryEntry = historyEntry
                };
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

    private static bool TryCopyCaptureOutputToClipboard(Bitmap output)
    {
        try
        {
            ClipboardService.CopyToClipboard(output);
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
        Dispatcher.BeginInvoke(async () =>
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
                            ? ToastSpec.Standard("OCR copied", FormatOcrAutoCopyToastPreview(text)) with { SuppressSound = true }
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
        });
    }

    private static string FormatOcrAutoCopyToastPreview(string text)
    {
        var preview = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return preview.Length > 80 ? preview[..80] + "..." : preview;
    }
}
