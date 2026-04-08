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
                string fileName = $"{Helpers.FileNameTemplate.Format(s.FileNameTemplate)}{ext}";
                string savePath = Path.Combine(saveDir, fileName);
                int maxH = s.RecordingQuality switch { RecordingQuality.P1080 => 1080, RecordingQuality.P720 => 720, RecordingQuality.P480 => 480, _ => 0 };
                int fps = fmt == RecordingFormat.GIF ? s.GifFps : s.RecordingFps;

                bool recMic = fmt != RecordingFormat.GIF && s.RecordMicrophone;
                bool recDesktop = fmt != RecordingFormat.GIF && s.RecordDesktopAudio;
                var form = new RecordingForm(bmp, bounds, fps, savePath, fmt, maxH,
                    showCursor, recMic, s.MicrophoneDeviceId, recDesktop, s.DesktopAudioDeviceId,
                    _settingsService!.Settings.ShowCaptureMagnifier);

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

    private void LaunchOverlay(CaptureMode initialMode, bool useAiRedirect = false)
    {
        LaunchWithDelay(() => LaunchOverlayNow(initialMode, useAiRedirect));
    }

    private void LaunchOverlayNow(CaptureMode initialMode, bool useAiRedirect = false)
    {
        var thread = new Thread(() =>
        {
            Bitmap? screenshot = null;
            try
            {
                bool showCursor = _settingsService!.Settings.ShowCursor;
                var (bmp, bounds) = ScreenCapture.CaptureCurrentScreen(showCursor);
                screenshot = bmp;

                var overlay = new RegionOverlayForm(screenshot, bounds, initialMode, _settingsService!.Settings.WindowDetection)
                {
                    ShowCrosshairGuides = _settingsService!.Settings.ShowCrosshairGuides,
                    DetectWindows = _settingsService.Settings.DetectWindows,
                    ShowCaptureMagnifier = _settingsService.Settings.ShowCaptureMagnifier,
                    AnnotationStrokeShadow = _settingsService.Settings.AnnotationStrokeShadow,
                    CaptureDockSide = _settingsService.Settings.CaptureDockSide
                };
                overlay.SetEnabledTools(_settingsService.Settings.EnabledTools);
                overlay.SetShowToolNumberBadges(_settingsService.Settings.ShowToolNumberBadges);

                overlay.RegionSelected += sel =>
                {
                    overlay.Hide();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var cropped = ScreenCapture.CropRegion(annotated, sel);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                    HandleCaptureResult(cropped, useAiRedirect);
                };

                overlay.FreeformSelected += fbmp =>
                {
                    overlay.Hide();
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                    HandleCaptureResult(fbmp, useAiRedirect);
                };

                overlay.OcrRegionSelected += sel =>
                {
                    overlay.Hide();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var cropped = ScreenCapture.CropRegion(annotated, sel);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                    HandleOcrResult(cropped);
                };

                overlay.ScanRegionSelected += sel =>
                {
                    overlay.Hide();
                    SoundService.PlayScanSound();
                    using var annotated = overlay.RenderAnnotatedBitmap();
                    var scanned = ScreenCapture.CropRegion(annotated, sel);
                    overlay.Close();
                    System.Windows.Forms.Application.ExitThread();
                    Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var decoded = BarcodeService.DecodeDetailed(scanned);
                            if (decoded is not null)
                            {
                                ClipboardService.CopyTextToClipboard(decoded.Text);
                                var prev = decoded.Text.Length > 100 ? decoded.Text[..100] + "..." : decoded.Text;
                                var preview = BarcodeService.RenderPreview(decoded.Text, decoded.Format);
                                var title = decoded.Format == ZXing.BarcodeFormat.QR_CODE ? "QR Code copied" : "Barcode copied";
                                ToastWindow.ShowInlinePreview(preview, title, prev);
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
                            ToastWindow.Show("Sticker", "Processing, please wait...");
                            var processed = await StickerService.ProcessAsync(sticker, _settingsService!.Settings.StickerUploadSettings);
                            if (processed.Success && processed.Image is not null)
                            {
                                HandleStickerResult(processed.Image, processed.ProviderName);
                            }
                            else
                            {
                                ToastWindow.ShowError("Sticker failed", processed.Error ?? "No sticker model configured");
                            }
                        }
                        catch (Exception ex)
                        {
                            ToastWindow.ShowError("Sticker failed", ex.Message);
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
                        ClipboardService.CopyTextToClipboard(bare);
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

}
