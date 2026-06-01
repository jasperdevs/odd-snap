using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OddSnap.Capture;
using OddSnap.Models;
using OddSnap.Native;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private void ResetCapturing()
    {
        Volatile.Write(ref _isCapturing, 0);
        RestoreSettingsAfterCapture();
    }

    private void HideSettingsForCapture()
    {
        // Keep app windows capturable. Hiding Settings here made attempts to
        // capture OddSnap's own UI disappear before the screenshot started, and
        // could also change the active window before active-window capture.
    }

    private void RestoreSettingsAfterCapture()
    {
        if (Interlocked.Exchange(ref _settingsHiddenForCapture, 0) == 0)
            return;

        _ = TryPostToAppDispatcher(() =>
        {
            if (_settingsWindow is not null)
                _settingsWindow.Show();
        }, DispatcherPriority.Background, "capture.restore-settings-post");
    }

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
            Bitmap? selectionScreenshot = null;
            RecordingForm? form = null;
            try
            {
                Theme.Refresh();
                var settings = _settingsService!.Settings;
                Helpers.UiChrome.SetUiScale(settings.UiScale);
                bool showCursor = settings.ShowCursor;
                var capture = ScreenCapture.CaptureAllScreens(showCursor);
                selectionScreenshot = capture.Bitmap;
                var bounds = capture.Bounds;
                var s = settings;
                var fmt = s.RecordingFormat;

                string baseDir = s.SaveDirectory;
                string ext = fmt switch { RecordingFormat.MP4 => ".mp4", RecordingFormat.WebM => ".webm", RecordingFormat.MKV => ".mkv", _ => ".gif" };
                string saveRoot = fmt == RecordingFormat.GIF ? baseDir : Path.Combine(baseDir, "Videos");
                string saveDir = s.SaveInMonthlyFolders
                    ? Helpers.CaptureSavePath.GetMonthDirectory(saveRoot)
                    : saveRoot;
                Directory.CreateDirectory(saveDir);
                string fileName = $"{Helpers.FileNameTemplate.Format(s.FileNameTemplate, 0, 0)}{ext}";
                string savePath = Helpers.CaptureSavePath.GetAvailablePath(Path.Combine(saveDir, fileName));
                int maxH = s.RecordingQuality switch { RecordingQuality.P1080 => 1080, RecordingQuality.P720 => 720, RecordingQuality.P480 => 480, _ => 0 };
                int fps = fmt == RecordingFormat.GIF ? s.GifFps : s.RecordingFps;

                bool recMic = fmt != RecordingFormat.GIF && s.RecordMicrophone;
                bool recDesktop = fmt != RecordingFormat.GIF && s.RecordDesktopAudio;
                form = new RecordingForm(selectionScreenshot, bounds, fps, savePath, fmt, maxH,
                    showCursor, recMic, s.MicrophoneDeviceId, recDesktop, s.DesktopAudioDeviceId,
                    _settingsService!.Settings.ShowCaptureMagnifier);
                selectionScreenshot = null;

                form.Shown += (_, _) =>
                {
                    _ = TryPostToAppDispatcher(
                        () => _trayIcon?.UpdateRecordingState(true),
                        DispatcherPriority.Background,
                        "capture.recording-shown-post");
                };

                form.RecordingCompleted += (path, firstFrame) =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);

                        Services.HistoryEntry? historyEntry = null;
                        try
                        {
                            if (s.SaveHistory)
                                historyEntry = EnsureHistoryService().SaveMediaEntry(path);
                        }
                        catch (Exception ex)
                        {
                            AppDiagnostics.LogError("capture.recording-history", ex, $"Failed to save recording history for {Path.GetFileName(path)}.");
                        }

                        var copiedToClipboard = TryCopyRecordingFileToClipboard(path);

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
                            ToastWindow.ShowImagePreview(
                                firstFrame,
                                isGif ? "GIF recorded" : "Video recorded",
                                copiedToClipboard ? "File copied to clipboard" : "Saved; clipboard copy failed",
                                path,
                                false);
                        }
                        else
                        {
                            var fi = new FileInfo(path);
                            string label = fi.Extension.TrimStart('.').ToUpper();
                            string size = fi.Length > 1024 * 1024
                                ? $"{fi.Length / 1024.0 / 1024.0:F1} MB"
                                : $"{fi.Length / 1024:N0} KB";
                            var copyStatus = copiedToClipboard ? "File copied to clipboard" : "Saved; clipboard copy failed";
                            ToastWindow.Show($"{label} recorded", $"{fi.Name} · {size} · {copyStatus}", path);
                        }

                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.recording-completed-post"))
                    {
                        firstFrame?.Dispose();
                        ResetCapturingWithoutUiRestore();
                    }
                };

                form.RecordingFailed += ex =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Recording error",
                            "OddSnap could not finish the recording. Try again, or check Settings -> Recording.",
                            ex.Message);
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.recording-failed-post"))
                    {
                        AppDiagnostics.LogError("capture.recording-failed", ex);
                        ResetCapturingWithoutUiRestore();
                    }
                };

                form.RecordingCancelled += () =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        ResetCapturing();
                    }, DispatcherPriority.Background, "capture.recording-cancelled-post"))
                    {
                        ResetCapturingWithoutUiRestore();
                    }
                };

                form.FormClosed += (_, _) =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        _trayIcon?.UpdateRecordingState(false);
                        ResetCapturing();
                    }, DispatcherPriority.Background, "capture.recording-closed-post"))
                    {
                        ResetCapturingWithoutUiRestore();
                    }
                };

                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex)
            {
                selectionScreenshot?.Dispose();
                form?.Dispose();
                if (!TryPostToAppDispatcher(() =>
                {
                    ResetCapturing();
                    ShowCaptureProcessingFailed(
                        "Recording error",
                        "OddSnap could not start recording. Try again, or check Settings -> Recording.",
                        ex.Message);
                }, DispatcherPriority.Normal, "capture.recording-start-failed-post"))
                {
                    AppDiagnostics.LogError("capture.recording-start", ex);
                    ResetCapturingWithoutUiRestore();
                }
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
            Bitmap? selectionScreenshot = null;
            ScrollingCaptureForm? form = null;
            try
            {
                Theme.Refresh();
                bool showCursor = _settingsService!.Settings.ShowCursor;
                var capture = ScreenCapture.CaptureAllScreens(showCursor);
                selectionScreenshot = capture.Bitmap;
                var bounds = capture.Bounds;
                form = new ScrollingCaptureForm(selectionScreenshot, bounds, showCursor,
                    _settingsService!.Settings.ShowCaptureMagnifier,
                    _settingsService!.Settings.ScrollingCaptureMode);
                selectionScreenshot = null;

                form.CaptureCompleted += result =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        HandleCaptureResult(result);
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.scrolling-completed-post"))
                    {
                        result.Dispose();
                        ResetCapturingWithoutUiRestore();
                    }
                };

                form.CaptureFailed += message =>
                {
                    if (!TryPostToAppDispatcher(() =>
                    {
                        ResetCapturing();
                        ShowCaptureProcessingFailed(
                            "Scroll capture error",
                            "OddSnap could not finish the scrolling capture. Try a smaller scroll area or a visible scrollable window.",
                            message);
                        ScheduleIdleMemoryTrim();
                    }, DispatcherPriority.Normal, "capture.scrolling-failed-post"))
                    {
                        AppDiagnostics.LogWarning("capture.scrolling-failed", message);
                        ResetCapturingWithoutUiRestore();
                    }
                };

                form.CaptureCancelled += () =>
                {
                    if (!TryPostToAppDispatcher(ResetCapturing, DispatcherPriority.Background, "capture.scrolling-cancelled-post"))
                        ResetCapturingWithoutUiRestore();
                };

                form.FormClosed += (_, _) =>
                {
                    if (!TryPostToAppDispatcher(ResetCapturing, DispatcherPriority.Background, "capture.scrolling-closed-post"))
                        ResetCapturingWithoutUiRestore();
                };

                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex)
            {
                selectionScreenshot?.Dispose();
                form?.Dispose();
                if (!TryPostToAppDispatcher(() =>
                {
                    ResetCapturing();
                    ShowCaptureProcessingFailed(
                        "Scroll capture error",
                        "OddSnap could not start scrolling capture. Try again with a visible scrollable window.",
                        ex.Message);
                }, DispatcherPriority.Normal, "capture.scrolling-start-failed-post"))
                {
                    AppDiagnostics.LogError("capture.scrolling-start", ex);
                    ResetCapturingWithoutUiRestore();
                }
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
            ResetCapturing();
            ShowCaptureProcessingFailed(
                "Capture error",
                "OddSnap could not capture the screen. Try again, or choose another capture mode.",
                ex.Message);
        }
    }

    private void CaptureActiveWindowNow()
    {
        Bitmap? bmp = null;
        try
        {
            var bounds = ScreenCapture.GetVirtualScreenBounds();
            var hwnd = Native.User32.GetForegroundWindow();
            if (!WindowDetector.TryGetCapturableWindowBounds(hwnd, bounds, out var windowRect, out var failureMessage))
            {
                ResetCapturing();
                ToastWindow.ShowError("Capture error", failureMessage);
                return;
            }

            var captureRegion = new Rectangle(
                windowRect.Left + bounds.X,
                windowRect.Top + bounds.Y,
                windowRect.Width,
                windowRect.Height);
            captureRegion.Intersect(bounds);

            if (captureRegion.Width <= 1 || captureRegion.Height <= 1)
            {
                ResetCapturing();
                ToastWindow.ShowError("Capture error", "Active window is out of bounds. Use region capture or move the window onscreen.");
                return;
            }

            bmp = ScreenCapture.CaptureRegion(captureRegion, _settingsService!.Settings.ShowCursor);
            HandleCaptureResult(bmp);
            bmp = null;
        }
        catch (Exception ex)
        {
            bmp?.Dispose();
            ResetCapturing();
            ShowCaptureProcessingFailed(
                "Capture error",
                "OddSnap could not capture the active window. Try again, or use region capture.",
                ex.Message);
        }
    }

    private void LaunchOverlay(CaptureMode initialMode, bool useAiRedirect = false)
    {
        Interlocked.Exchange(ref _captureRequestedTimestamp, PerformanceTrace.Timestamp());
        LaunchWithDelay(() => LaunchOverlayNow(initialMode, useAiRedirect));
    }

    private void LaunchOverlayNow(CaptureMode initialMode, bool useAiRedirect = false)
    {
        var requestedAt = Interlocked.Exchange(ref _captureRequestedTimestamp, 0);
        if (requestedAt == 0)
            requestedAt = PerformanceTrace.Timestamp();
        try
        {
            CaptureOverlayThread.Post(() => RunOverlayCaptureSession(initialMode, useAiRedirect, requestedAt));
        }
        catch (Exception ex)
        {
            ResetCapturing();
            ShowCaptureProcessingFailed(
                "Capture error",
                "OddSnap could not start the capture overlay. Try again, or check capture settings.",
                ex.Message);
        }
    }

    private void RunOverlayCaptureSession(CaptureMode initialMode, bool useAiRedirect, long requestedAt)
    {
        Bitmap? screenshot = null;
        try
        {
            var screenshotStarted = PerformanceTrace.Timestamp();
            bool showCursor = _settingsService!.Settings.ShowCursor;
            var (bmp, bounds) = _settingsService.Settings.OverlayCaptureAllMonitors
                ? ScreenCapture.CaptureAllScreensLowLatency(showCursor)
                : ScreenCapture.CaptureCurrentScreenLowLatency(showCursor);
            screenshot = bmp;
            PerformanceTrace.LogIfSlow(
                "perf.capture.overlay-screenshot",
                screenshotStarted,
                TimeSpan.FromMilliseconds(80),
                $"{bounds.Width}x{bounds.Height} mode={initialMode}");

            var overlay = new RegionOverlayForm(
                screenshot,
                bounds,
                initialMode,
                _settingsService!.Settings.WindowDetection,
                _settingsService.Settings.CenterSelectionAspectRatio)
            {
                ShowCrosshairGuides = _settingsService!.Settings.ShowCrosshairGuides,
                DetectWindows = _settingsService.Settings.DetectWindows,
                ShowCaptureMagnifier = _settingsService.Settings.ShowCaptureMagnifier,
                AnnotationStrokeShadow = _settingsService.Settings.AnnotationStrokeShadow,
                CaptureDockSide = _settingsService.Settings.CaptureDockSide,
                UiScale = _settingsService.Settings.UiScale
            };
            overlay.SetEnabledTools(_settingsService.Settings.EnabledTools);
            overlay.SetToolbarLayout(
                _settingsService.Settings.ToolbarToolOrderIds,
                _settingsService.Settings.ToolbarPinnedToolIds);
            overlay.SetShowToolNumberBadges(_settingsService.Settings.ShowToolNumberBadges);
            overlay.Shown += (_, _) =>
                PerformanceTrace.LogElapsed(
                    "perf.capture.overlay-shown",
                    requestedAt,
                    $"{bounds.Width}x{bounds.Height} mode={initialMode}");

            overlay.RegionSelected += sel =>
            {
                overlay.Hide();
                using var annotated = overlay.RenderAnnotatedBitmap();
                var cropped = ScreenCapture.CropRegion(annotated, sel);
                overlay.Close();
                HandleCaptureResult(cropped, useAiRedirect);
            };

            overlay.FreeformSelected += fbmp =>
            {
                overlay.Hide();
                overlay.Close();
                HandleCaptureResult(fbmp, useAiRedirect);
            };

            overlay.OcrRegionSelected += sel =>
            {
                overlay.Hide();
                using var annotated = overlay.RenderAnnotatedBitmap();
                var cropped = ScreenCapture.CropRegion(annotated, sel);
                overlay.Close();
                HandleOcrResult(cropped);
            };

            overlay.ScanRegionSelected += sel =>
            {
                overlay.Hide();
                SoundService.PlayScanSound();
                using var annotated = overlay.RenderAnnotatedBitmap();
                var scanned = ScreenCapture.CropRegion(annotated, sel);
                overlay.Close();
                if (!TryPostToAppDispatcherAsync(async () =>
                {
                    Bitmap? preview = null;
                    try
                    {
                        ToastWindow.Show(ToastSpec.Standard("Scan", "Reading code...") with { SuppressSound = true });
                        var scanResult = await ProcessScanCaptureAsync(scanned);
                        var decoded = scanResult.Decoded;
                        preview = scanResult.Preview;
                        if (decoded is not null)
                        {
                            var copySucceeded = TryCopyCaptureTextToClipboard(decoded.Text);
                            if (_settingsService!.Settings.SaveHistory)
                                EnsureHistoryService().SaveCodeEntry(decoded.Text, decoded.Format.ToString());
                            var prev = decoded.Text.Length > 100 ? decoded.Text[..100] + "..." : decoded.Text;
                            var title = decoded.Format == ZXing.BarcodeFormat.QR_CODE
                                ? copySucceeded ? "QR Code copied" : "QR Code found"
                                : copySucceeded ? "Barcode copied" : "Barcode found";
                            if (preview is not null)
                            {
                                ToastWindow.ShowInlinePreview(preview, title, prev, suppressSound: true);
                                preview = null;
                            }
                            else
                            {
                                ToastWindow.Show(ToastSpec.Standard(title, prev) with { SuppressSound = true });
                            }
                        }
                        else
                        {
                            ToastWindow.Show("Scan", "No QR/barcode found");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowCaptureProcessingFailed(
                            "Scan failed",
                            "OddSnap could not scan this region. Try a clearer QR/barcode region.",
                            ex.Message);
                    }
                    finally
                    {
                        preview?.Dispose();
                        scanned.Dispose();
                        ScheduleIdleMemoryTrim();
                    }
                }, DispatcherPriority.Normal, "capture.scan-post"))
                {
                    scanned.Dispose();
                }
            };

            overlay.StickerRegionSelected += sel =>
            {
                overlay.Hide();
                using var annotated = overlay.RenderAnnotatedBitmap();
                var sticker = ScreenCapture.CropRegion(annotated, sel);
                overlay.Close();

                if (!TryPostToAppDispatcherAsync(async () =>
                {
                    try
                    {
                        ToastWindow.Show("Sticker", "Processing, please wait...");
                        var processed = await StickerService.ProcessAsync(sticker, _settingsService!.Settings.StickerUploadSettings);
                        if (processed.Success && processed.Image is not null)
                        {
                            HandleStickerResult(processed.Image, processed.ProviderName);
                        }
                        else
                        {
                            ShowCaptureProcessingFailed(
                                "Sticker failed",
                                "OddSnap could not create the sticker. Check Settings -> Stickers and try again.",
                                processed.Error ?? "No sticker model configured");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowCaptureProcessingFailed(
                            "Sticker failed",
                            "OddSnap could not create the sticker. Check Settings -> Stickers and try again.",
                            ex.Message);
                    }
                    finally
                    {
                        sticker.Dispose();
                        ScheduleIdleMemoryTrim();
                    }
                }, DispatcherPriority.Normal, "capture.sticker-post"))
                {
                    sticker.Dispose();
                }
            };

            overlay.UpscaleRegionSelected += sel =>
            {
                overlay.Hide();
                using var annotated = overlay.RenderAnnotatedBitmap();
                var upscaled = ScreenCapture.CropRegion(annotated, sel);
                overlay.Close();

                if (!TryPostToAppDispatcherAsync(async () =>
                {
                    try
                    {
                        var upscaleSettings = _settingsService!.Settings.UpscaleUploadSettings ?? new UpscaleSettings();
                        if (upscaleSettings.ShowPreviewWindow)
                        {
                            var previewWindow = new UpscaleResultWindow(upscaled, _settingsService!, (result, providerName) =>
                            {
                                HandleUpscaleResult(result, providerName);
                            });
                            previewWindow.Show();
                        }
                        else
                        {
                            ToastWindow.Show("Upscale", "Processing, please wait...");
                            var processed = await UpscaleService.ProcessAsync(upscaled, upscaleSettings);
                            if (processed.Success && processed.Image is not null)
                            {
                                HandleUpscaleResult(processed.Image, processed.ProviderName);
                            }
                            else
                            {
                                ShowCaptureProcessingFailed(
                                    "Upscale failed",
                                    "OddSnap could not upscale this capture. Check Settings -> Upscale and try again.",
                                    processed.Error ?? "No upscale model configured");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppDiagnostics.LogError("capture.upscale", ex);
                        ShowCaptureProcessingFailed(
                            "Upscale failed",
                            "OddSnap could not upscale this capture. Check Settings -> Upscale and try again.",
                            ex.Message);
                    }
                    finally
                    {
                        upscaled.Dispose();
                        ScheduleIdleMemoryTrim();
                    }
                }, DispatcherPriority.Normal, "capture.upscale-post"))
                {
                    upscaled.Dispose();
                }
            };

            overlay.ColorPicked += hex =>
            {
                _ = TryPostToAppDispatcher(() =>
                {
                    SoundService.PlayColorSound();
                    string bare = hex.TrimStart('#');
                    var copySucceeded = TryCopyCaptureTextToClipboard(bare);
                    byte r = Convert.ToByte(bare[..2], 16);
                    byte g = Convert.ToByte(bare[2..4], 16);
                    byte b = Convert.ToByte(bare[4..6], 16);
                    ToastWindow.ShowWithColor(copySucceeded ? "Color copied" : "Color picked", bare,
                        System.Windows.Media.Color.FromRgb(r, g, b), suppressSound: true);

                    if (_settingsService!.Settings.SaveHistory)
                        EnsureHistoryService().SaveColorEntry(bare);
                }, DispatcherPriority.Normal, "capture.color-picked-post");
                overlay.Close();
            };

            overlay.ToolbarActionRequested += actionId =>
            {
                overlay.Hide();
                overlay.Close();
                if (!TryPostToAppDispatcher(
                        () => LaunchToolbarActionFromOverlay(actionId),
                        DispatcherPriority.Background,
                        "capture.toolbar-action-post"))
                {
                    ResetCapturingWithoutUiRestore();
                }
            };

            overlay.FormClosed += (_, _) =>
            {
                screenshot?.Dispose();
                screenshot = null;

                var mode = overlay.CurrentMode;
                if (mode is CaptureMode.Rectangle or CaptureMode.Center or CaptureMode.Freeform)
                {
                    _ = TryPostToAppDispatcher(() =>
                    {
                        _settingsService!.Settings.LastCaptureMode = mode;
                        _settingsService.Save();
                    }, DispatcherPriority.Background, "capture.save-last-mode-post");
                }

                if (!TryPostToAppDispatcher(ResetCapturing, DispatcherPriority.Background, "capture.overlay-closed-post"))
                    ResetCapturingWithoutUiRestore();
            };

            overlay.PrepareFirstMoveChrome();
            overlay.Show();
        }
        catch (Exception ex)
        {
            screenshot?.Dispose();
            if (!TryPostToAppDispatcher(() =>
            {
                ResetCapturing();
                ShowCaptureProcessingFailed(
                    "Capture error",
                    "OddSnap could not start the capture overlay. Try again, or check capture settings.",
                    ex.Message);
            }, DispatcherPriority.Normal, "capture.overlay-start-failed-post"))
            {
                AppDiagnostics.LogError("capture.overlay-start", ex);
                ResetCapturingWithoutUiRestore();
            }
        }
    }

    private void LaunchToolbarActionFromOverlay(string actionId)
    {
        Volatile.Write(ref _isCapturing, 1);

        switch (actionId)
        {
            case "_fullscreen":
                LaunchWithDelay(CaptureFullscreenNow);
                break;
            case "_activeWindow":
                LaunchWithDelay(CaptureActiveWindowNow);
                break;
            case "_scrollCapture":
                LaunchScrollingCapture();
                break;
            case "_record":
                if (RecordingForm.Current != null)
                {
                    RecordingForm.Current.RequestStop();
                    ResetCapturing();
                }
                else
                {
                    LaunchGifRecording();
                }
                break;
            default:
                ResetCapturing();
                break;
        }
    }

    private static Task<ScanProcessingResult> ProcessScanCaptureAsync(Bitmap scanned)
        => Task.Run(() =>
        {
            var decoded = BarcodeService.DecodeDetailed(scanned);
            if (decoded is null)
                return new ScanProcessingResult(null, null);

            Bitmap? preview = null;
            try
            {
                preview = BarcodeService.RenderPreview(decoded.Text, decoded.Format);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("capture.scan.preview", $"Failed to render scan preview: {ex.Message}", ex);
            }

            return new ScanProcessingResult(decoded, preview);
        });

    private sealed record ScanProcessingResult(BarcodeDecodeResult? Decoded, Bitmap? Preview);

    private static bool TryCopyCaptureTextToClipboard(string text)
    {
        try
        {
            ClipboardService.CopyTextToClipboard(text);
            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError(
                "Copy failed",
                $"OddSnap could not copy this capture result. The result will still be shown and saved when history is enabled.\n{ex.Message}");
            return false;
        }
    }

    private static bool TryCopyRecordingFileToClipboard(string path)
    {
        try
        {
            ClipboardService.CopyFilesToClipboard(path);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("recording.copy-file", $"Failed to copy recording file {Path.GetFileName(path)}: {ex.Message}", ex);
            return false;
        }
    }

}
