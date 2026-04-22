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
                        ToastWindow.ShowError("Capture error", task.Exception?.GetBaseException().Message ?? "Capture failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    if (ShouldCopyAfterCapture(action))
                        ClipboardService.CopyToClipboard(persisted.Output);
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
                        ToastWindow.ShowError("Sticker error", task.Exception?.GetBaseException().Message ?? "Sticker processing failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    if (ShouldCopyAfterCapture(action))
                        ClipboardService.CopyToClipboard(persisted.Output);
                    ResetCapturing();

                    if (ShouldPreviewAfterCapture(action))
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        persisted.Output.Dispose();
                        ToastWindow.Show(ShouldCopyAfterCapture(action) ? "Sticker copied" : "Sticker ready");
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
                        ToastWindow.ShowError("Upscale error", task.Exception?.GetBaseException().Message ?? "Upscale processing failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    var action = NormalizeAfterCaptureAction(settings.AfterCapture);
                    if (ShouldCopyAfterCapture(action))
                        ClipboardService.CopyToClipboard(persisted.Output);
                    ResetCapturing();

                    if (ShouldPreviewAfterCapture(action))
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        persisted.Output.Dispose();
                        ToastWindow.Show(ShouldCopyAfterCapture(action) ? "Upscale copied" : "Upscale ready");
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

                    // Open OCR result window instead of copying directly
                    var window = new OcrResultWindow(text, _settingsService);
                    window.Show();
                }
                else
                {
                    ToastWindow.Show("OCR", "No text found");
                }
            }
            catch (Exception ex)
            {
                ToastWindow.ShowError("OCR error", ex.Message);
            }
            finally { result.Dispose(); }
            ScheduleIdleMemoryTrim();
        });
    }
}
