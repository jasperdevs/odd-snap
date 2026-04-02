using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
using Yoink.Models;
using Yoink.Native;
using Yoink.Services;
using Yoink.UI;

namespace Yoink;

public partial class App
{
    private sealed class PersistedCaptureResult
    {
        public required Bitmap Output { get; init; }
        public string? FilePath { get; init; }
        public Services.HistoryEntry? HistoryEntry { get; init; }
    }

    private void LaunchGifRecording()
    {
        var thread = new Thread(() =>
        {
            try
            {
                bool showCursor = _settingsService!.Settings.ShowCursor;
                var (bmp, bounds) = ScreenCapture.CaptureAllScreens(showCursor);
                var s = _settingsService!.Settings;
                var fmt = s.RecordingFormat;

                string baseDir = s.SaveDirectory;
                string ext = fmt switch { RecordingFormat.MP4 => ".mp4", RecordingFormat.WebM => ".webm", RecordingFormat.MKV => ".mkv", _ => ".gif" };
                string saveDir = fmt == RecordingFormat.GIF ? baseDir : Path.Combine(baseDir, "Videos");
                Directory.CreateDirectory(saveDir);
                string fileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                string savePath = Path.Combine(saveDir, fileName);
                int maxH = s.RecordingQuality switch { RecordingQuality.P1080 => 1080, RecordingQuality.P720 => 720, RecordingQuality.P480 => 480, _ => 0 };
                int fps = fmt == RecordingFormat.GIF ? s.GifFps : s.RecordingFps;

                bool recMic = fmt != RecordingFormat.GIF && s.RecordMicrophone;
                bool recDesktop = fmt != RecordingFormat.GIF && s.RecordDesktopAudio;
                var form = new RecordingForm(bmp, bounds, fps, savePath, fmt, maxH,
                    showCursor, recMic, s.MicrophoneDeviceId, recDesktop, s.DesktopAudioDeviceId);

                form.Shown += (_, _) =>
                {
                    Dispatcher.BeginInvoke(() => _trayIcon?.UpdateRecordingState(true));
                };

                form.RecordingCompleted += (path, firstFrame) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);

                        // Only index GIF recordings in the GIF history list (and only when history is enabled).
                        Services.HistoryEntry? historyEntry = null;
                        try
                        {
                            if (s.SaveHistory && string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
                                historyEntry = EnsureHistoryService().SaveGifEntry(path);
                        }
                        catch { }

                        try
                        {
                            var files = new System.Collections.Specialized.StringCollection { path };
                            System.Windows.Clipboard.SetFileDropList(files);
                        }
                        catch { }

                        var settings = _settingsService!.Settings;
                        bool isGif = string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);
                        bool willUpload = isGif
                            ? settings.AutoUploadGifs && settings.ImageUploadDestination != UploadDestination.None
                            : settings.AutoUploadVideos && settings.ImageUploadDestination != UploadDestination.None;

                        if (willUpload)
                        {
                            firstFrame?.Dispose();
                            _ = UploadFileAsync(path, isGif ? "GIF" : "Video", historyEntry);
                        }
                        else if (firstFrame != null)
                        {
                            ToastWindow.ShowImagePreview(firstFrame, path, false);
                        }
                        else
                        {
                            var fi = new FileInfo(path);
                            string label = fi.Extension.TrimStart('.').ToUpper();
                            string size = fi.Length > 1024 * 1024
                                ? $"{fi.Length / 1024.0 / 1024.0:F1} MB"
                                : $"{fi.Length / 1024:N0} KB";
                            ToastWindow.Show($"{label} recorded", $"{fi.Name} · {size}", path);
                        }

                        ScheduleIdleMemoryTrim();
                    });
                };

                form.RecordingFailed += ex =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        _isCapturing = false;
                        ToastWindow.ShowError("Recording error", ex.Message);
                        ScheduleIdleMemoryTrim();
                    });
                };

                form.RecordingCancelled += () =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        _isCapturing = false;
                    });
                };

                form.FormClosed += (_, _) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        _isCapturing = false;
                    });
                };

                System.Windows.Forms.Application.Run(form);
            }
            catch
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _isCapturing = false;
                    ToastWindow.ShowError("Recording error", "Recording failed");
                });
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void LaunchScrollingCapture()
    {
        var thread = new Thread(() =>
        {
            try
            {
                bool showCursor = _settingsService!.Settings.ShowCursor;
                var (bmp, bounds) = ScreenCapture.CaptureAllScreens(showCursor);
                var form = new ScrollingCaptureForm(bmp, bounds, showCursor);

                form.CaptureCompleted += result =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        HandleCaptureResult(result);
                        ScheduleIdleMemoryTrim();
                    });
                };

                form.CaptureFailed += message =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _isCapturing = false;
                        ToastWindow.ShowError("Scroll capture error", message);
                        ScheduleIdleMemoryTrim();
                    });
                };

                form.CaptureCancelled += () => Dispatcher.BeginInvoke(() => _isCapturing = false);

                form.FormClosed += (_, _) => Dispatcher.BeginInvoke(() => _isCapturing = false);

                System.Windows.Forms.Application.Run(form);
            }
            catch
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _isCapturing = false;
                    ToastWindow.ShowError("Scroll capture error", "Scrolling capture failed");
                });
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void CaptureFullscreenNow()
    {
        Bitmap? bmp = null;
        try
        {
            (bmp, _) = ScreenCapture.CaptureAllScreens(_settingsService!.Settings.ShowCursor);
            HandleCaptureResult(bmp);
            bmp = null;
        }
        catch (Exception ex)
        {
            bmp?.Dispose();
            _isCapturing = false;
            ToastWindow.ShowError("Capture error", ex.Message);
        }
    }

    private void CaptureActiveWindowNow()
    {
        Bitmap? bmp = null;
        try
        {
            (bmp, var bounds) = ScreenCapture.CaptureAllScreens(_settingsService!.Settings.ShowCursor);
            var hwnd = Native.User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !Native.User32.GetWindowRect(hwnd, out var rect))
            {
                bmp.Dispose();
                _isCapturing = false;
                ToastWindow.ShowError("Capture error", "Couldn't find the active window.");
                return;
            }

            var crop = new Rectangle(rect.Left - bounds.X, rect.Top - bounds.Y, rect.Width, rect.Height);
            crop.Intersect(new Rectangle(System.Drawing.Point.Empty, bmp.Size));
            if (crop.Width <= 1 || crop.Height <= 1)
            {
                bmp.Dispose();
                _isCapturing = false;
                ToastWindow.ShowError("Capture error", "Active window is out of bounds.");
                return;
            }

            var cropped = ScreenCapture.CropRegion(bmp, crop);
            HandleCaptureResult(cropped);
            bmp.Dispose();
        }
        catch (Exception ex)
        {
            bmp?.Dispose();
            _isCapturing = false;
            ToastWindow.ShowError("Capture error", ex.Message);
        }
    }

    private void LaunchOverlay(CaptureMode initialMode)
    {
        LaunchWithDelay(() => LaunchOverlayNow(initialMode));
    }

    private void LaunchOverlayNow(CaptureMode initialMode)
    {
        var thread = new Thread(() =>
        {
            Bitmap? screenshot = null;
            try
            {
                bool showCursor = _settingsService!.Settings.ShowCursor;
                var (bmp, bounds) = ScreenCapture.CaptureAllScreens(showCursor);
                screenshot = bmp;

                var overlay = new RegionOverlayForm(screenshot, bounds, initialMode, _settingsService!.Settings.WindowDetection)
                {
                    ShowCrosshairGuides = _settingsService!.Settings.ShowCrosshairGuides,
                    DetectWindows = _settingsService.Settings.DetectWindows
                };
                overlay.SetEnabledTools(_settingsService.Settings.EnabledTools);
                overlay.SetShowToolNumberBadges(_settingsService.Settings.ShowToolNumberBadges);

                overlay.RegionSelected += sel =>
                {
                    overlay.Hide();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var cropped = ScreenCapture.CropRegion(annotated, sel);
                    HandleCaptureResult(cropped);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.FreeformSelected += fbmp =>
                {
                    overlay.Hide();
                    HandleCaptureResult(fbmp);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.OcrRegionSelected += sel =>
                {
                    overlay.Hide();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var cropped = ScreenCapture.CropRegion(annotated, sel);
                    HandleOcrResult(cropped);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.ScanRegionSelected += sel =>
                {
                    overlay.Hide();
                    SoundService.PlayScanSound();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var scanned = ScreenCapture.CropRegion(annotated, sel);
                    Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var text = BarcodeService.Decode(scanned);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                System.Windows.Clipboard.SetText(text);
                                var prev = text.Length > 100 ? text[..100] + "..." : text;
                                ToastWindow.Show("Code copied", prev);
                            }
                            else
                            {
                                ToastWindow.Show("Scan", "No QR/barcode found");
                            }
                        }
                        catch (Exception ex)
                        {
                            ToastWindow.ShowError("Scan failed", ex.Message);
                        }
                        finally
                        {
                            scanned.Dispose();
                        }
                    });
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.StickerRegionSelected += sel =>
                {
                    overlay.Hide();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var sticker = ScreenCapture.CropRegion(annotated, sel);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();

                    Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            var processed = await StickerService.ProcessAsync(sticker, _settingsService!.Settings.StickerUploadSettings);
                            if (processed.Success && processed.Image is not null)
                            {
                                HandleStickerResult(processed.Image, processed.ProviderName);
                            }
                            else
                            {
                                ToastWindow.ShowError("Sticker error", processed.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            ToastWindow.ShowError("Sticker error", ex.Message);
                        }
                        finally
                        {
                            sticker.Dispose();
                        }
                    });
                };

                overlay.ColorPicked += hex =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        SoundService.PlayColorSound();
                        string bare = hex.TrimStart('#');
                        System.Windows.Clipboard.SetText(bare);
                        byte r = Convert.ToByte(bare[..2], 16);
                        byte g = Convert.ToByte(bare[2..4], 16);
                        byte b = Convert.ToByte(bare[4..6], 16);
                        ToastWindow.ShowWithColor("Color copied", bare,
                            System.Windows.Media.Color.FromRgb(r, g, b));

                        if (_settingsService!.Settings.SaveHistory)
                            EnsureHistoryService().SaveColorEntry(bare);
                    });
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.SettingsRequested += () =>
                {
                    Dispatcher.BeginInvoke(ShowSettings);
                };

                overlay.SelectionCancelled += () =>
                {
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                };

                overlay.FormClosed += (_, _) =>
                {
                    var mode = overlay.CurrentMode;
                    if (mode is CaptureMode.Rectangle or CaptureMode.Freeform)
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            _settingsService!.Settings.LastCaptureMode = mode;
                            _settingsService.Save();
                        });
                    }

                    Dispatcher.BeginInvoke(() => _isCapturing = false);
                };

                try
                {
                    System.Windows.Forms.Application.Run(overlay);
                }
                finally
                {
                    screenshot.Dispose();
                }
            }
            catch (Exception ex)
            {
                screenshot?.Dispose();
                Dispatcher.BeginInvoke(() =>
                {
                    _isCapturing = false;
                    ToastWindow.ShowError("Capture error", ex.Message);
                });
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void HandleCaptureResult(Bitmap result)
    {
        SoundService.PlayCaptureSound();

        var settings = _settingsService!.Settings;
        var ext = CaptureOutputService.GetExtension(settings.CaptureImageFormat);
        string? requestedPath = null;
        if (settings.SaveToFile)
        {
            var defaultPath = Path.Combine(settings.SaveDirectory, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
            requestedPath = settings.AskForFileNameOnSave
                ? ResolveSavePath(defaultPath, settings.CaptureImageFormat)
                : defaultPath;
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

                    _isCapturing = false;
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
            var defaultStickerPath = Path.Combine(settings.SaveDirectory, $"yoink_sticker_{DateTime.Now:yyyyMMdd_HHmmss}.png");
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

                    _isCapturing = false;
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
                string text = await OcrService.RecognizeAsync(result, _settingsService?.Settings.OcrLanguageTag);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SoundService.PlayTextSound();
                    System.Windows.Clipboard.SetText(text);
                    var prev = text.Length > 100 ? text[..100] + "..." : text;
                    ToastWindow.Show("Text copied", prev);

                    if (_settingsService!.Settings.SaveHistory)
                        EnsureHistoryService().SaveOcrEntry(text);
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
