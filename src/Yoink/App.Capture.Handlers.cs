using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
using Yoink.Models;
using Yoink.Services;
using Yoink.UI;

namespace Yoink;

public partial class App
{
    private void HandleCaptureResult(Bitmap result)
    {
        SoundService.PlayCaptureSound();

        var settings = _settingsService!.Settings;
        var ext = CaptureOutputService.GetExtension(settings.CaptureImageFormat);
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultPath = Path.Combine(settings.SaveDirectory, $"{Helpers.FileNameTemplate.Format(settings.FileNameTemplate)}.{ext}");
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
                _isCapturing = false;
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
                        _isCapturing = false;
                        ToastWindow.ShowError("Capture error", task.Exception?.GetBaseException().Message ?? "Capture failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    ClipboardService.CopyToClipboard(persisted.Output);
                    _isCapturing = false;

                    bool willUpload = persisted.FilePath != null
                        && settings.AutoUploadScreenshots
                        && settings.ImageUploadDestination != UploadDestination.None;

                    if (willUpload)
                    {
                        // Don't show preview toast yet — upload handler will show result
                        persisted.Output.Dispose();
                        _ = UploadFileAsync(persisted.FilePath!, "Screenshot", persisted.HistoryEntry);
                    }
                    else
                    {
                        var action = settings.AfterCapture;
                        if (action == AfterCaptureAction.ShowPreview)
                        {
                            ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                        }
                        else
                        {
                            var dims = $"{persisted.Output.Width}x{persisted.Output.Height}";
                            persisted.Output.Dispose();
                            ToastWindow.Show("Copied to clipboard", dims, persisted.FilePath);
                        }
                    }

                    ScheduleIdleMemoryTrim();
                });
            }, TaskScheduler.Default);
    }

    private void HandleStickerResult(Bitmap result, string providerName)
    {
        SoundService.PlayCaptureSound();

        var settings = _settingsService!.Settings;
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultStickerPath = Path.Combine(settings.SaveDirectory, $"{Helpers.FileNameTemplate.Format(settings.FileNameTemplate)}_sticker.png");
            requestedPath = settings.AskForFileNameOnSave
                ? ResolveSavePath(defaultStickerPath, CaptureImageFormat.Png)
                : defaultStickerPath;
            if (requestedPath is null)
            {
                result.Dispose();
                _isCapturing = false;
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
                        _isCapturing = false;
                        ToastWindow.ShowError("Sticker error", task.Exception?.GetBaseException().Message ?? "Sticker processing failed");
                        ScheduleIdleMemoryTrim();
                    });
                    return;
                }

                var persisted = task.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    ClipboardService.CopyToClipboard(persisted.Output);
                    _isCapturing = false;

                    var action = settings.AfterCapture;
                    if (action == AfterCaptureAction.ShowPreview)
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        persisted.Output.Dispose();
                        ToastWindow.Show("Sticker copied");
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
                var output = CaptureOutputService.PrepareBitmap(source, maxLongEdge);
                string? filePath = requestedPath;
                Services.HistoryEntry? historyEntry = null;

                if (saveHistory)
                {
                    lock (_historyGate)
                    {
                        historyEntry = isSticker
                            ? EnsureHistoryService().SaveStickerEntry(output, providerName)
                            : EnsureHistoryService().SaveCapture(output);
                    }
                    filePath ??= historyEntry.FilePath;
                }

                if (requestedPath != null)
                {
                    var directory = Path.GetDirectoryName(filePath!);
                    if (string.IsNullOrWhiteSpace(directory))
                        throw new InvalidOperationException("Save path must include a directory.");

                    Directory.CreateDirectory(directory);
                    if (isSticker)
                        output.Save(filePath!, ImageFormat.Png);
                    else
                        CaptureOutputService.SaveBitmap(output, filePath!, captureFormat, jpegQuality);
                }

                return new PersistedCaptureResult
                {
                    Output = output,
                    FilePath = filePath,
                    HistoryEntry = historyEntry
                };
            }
        });
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
