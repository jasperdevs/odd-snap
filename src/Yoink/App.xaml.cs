using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
using Yoink.Native;
using Yoink.Models;
using Yoink.Services;
using Yoink.Helpers;
using Yoink.UI;
using System.Diagnostics;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Yoink;

public partial class App : Application
{
    private static Mutex? _mutex;
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private HistoryService? _historyService;
    private readonly object _historyGate = new();
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _idleTrimTimer;
    private int _activeUploadCount;
    private volatile bool _isCapturing;
    private bool _historyRecovered;

    private sealed class PersistedCaptureResult
    {
        public required Bitmap Output { get; init; }
        public string? FilePath { get; init; }
        public Services.HistoryEntry? HistoryEntry { get; init; }
    }


    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            base.OnStartup(e);
            try { UninstallService.RemoveInstalledAppEntry(); } catch { }
            try { UninstallService.RemoveStartMenuShortcut(); } catch { }
            try { UninstallService.RemoveStartupEntry(); } catch { }
            try { UninstallService.RemoveAppData(); } catch { }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch { }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "YoinkScreenshotTool_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        base.OnStartup(e);

        try { UninstallService.RegisterInstalledAppEntry(); } catch { }
        try { UninstallService.EnsureStartMenuShortcut(); } catch { }

        _settingsService = new SettingsService();
        _settingsService.Load();
        System.Windows.Forms.Application.EnableVisualStyles();
        SoundService.Muted = _settingsService.Settings.MuteSounds;
        SoundService.SetPack(_settingsService.Settings.SoundPack);
        ToastWindow.SetPosition(_settingsService.Settings.ToastPosition);
        ToastWindow.SetDuration(_settingsService.Settings.ToastDurationSeconds);

        _idleTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _idleTrimTimer.Tick += (_, _) => TrimIdleMemory();
        ScheduleIdleMemoryTrim();

        // Show setup wizard on first run
        if (!_settingsService.Settings.HasCompletedSetup)
        {
            var wizard = new SetupWizard(_settingsService);
            wizard.ShowDialog();
        }

        _trayIcon = new TrayIcon();
        _trayIcon.OnCapture += () => OnHotkeyPressed();
        _trayIcon.OnOcr += () => OnOcrHotkeyPressed();
        _trayIcon.OnColorPicker += () => OnPickerHotkeyPressed();
        _trayIcon.OnGifRecord += () => OnGifHotkeyPressed();
        _trayIcon.OnScrollCapture += () => OnScrollCaptureHotkeyPressed();
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnHistory += ShowHistory;
        _trayIcon.OnQuit += () => Shutdown();

        RegisterHotkeys();

        if (_settingsService.Settings.AutoCheckForUpdates)
            _ = CheckForUpdatesOnStartupAsync();

    }

    public void RegisterHotkeys()
    {
        _hotkeyService?.Dispose();
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.OcrHotkeyPressed += OnOcrHotkeyPressed;
        _hotkeyService.PickerHotkeyPressed += OnPickerHotkeyPressed;
        _hotkeyService.ScanHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Scan);
        _hotkeyService.StickerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Sticker);
        _hotkeyService.RulerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Ruler);
        _hotkeyService.GifHotkeyPressed += OnGifHotkeyPressed;
        _hotkeyService.FullscreenHotkeyPressed += OnFullscreenHotkeyPressed;
        _hotkeyService.ActiveWindowHotkeyPressed += OnActiveWindowHotkeyPressed;
        _hotkeyService.ScrollCaptureHotkeyPressed += OnScrollCaptureHotkeyPressed;

        var s = _settingsService!.Settings;
        var failed = new List<string>();

        if (!_hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey))
            failed.Add("Capture");
        if (!_hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey))
            failed.Add("OCR");
        if (!_hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey))
            failed.Add("Color Picker");
        if (!_hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey))
            failed.Add("Scanner");
        if (!_hotkeyService.RegisterSticker(s.StickerHotkeyModifiers, s.StickerHotkeyKey))
            failed.Add("Sticker");
        if (!_hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey))
            failed.Add("Ruler");
        if (!_hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey))
            failed.Add("GIF");
        if (!_hotkeyService.RegisterFullscreen(s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey))
            failed.Add("Fullscreen");
        if (!_hotkeyService.RegisterActiveWindow(s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey))
            failed.Add("Active Window");
        if (!_hotkeyService.RegisterScrollCapture(s.ScrollCaptureHotkeyModifiers, s.ScrollCaptureHotkeyKey))
            failed.Add("Scroll Capture");

        if (failed.Count > 0)
            ToastWindow.ShowError("Hotkey conflicts", $"Could not register: {string.Join(", ", failed)}. Try different combos.");
        else
        {
            var name = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);
            ToastWindow.Show("Yoink ready", $"{name} to capture, Alt+C for colors");
        }
    }

    // ─── Hotkeys (all open unified overlay with different initial tool) ──

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode));
    }

    private void OnToolHotkeyPressed(CaptureMode mode)
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchOverlay(mode));
    }

    private void OnOcrHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchOverlay(CaptureMode.Ocr));
    }

    private void OnPickerHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchOverlay(CaptureMode.ColorPicker));
    }

    private void OnGifHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(LaunchGifRecording);
    }

    private void OnScrollCaptureHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(LaunchScrollingCapture);
    }

    private void OnFullscreenHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchWithDelay(CaptureFullscreenNow));
    }

    private void OnActiveWindowHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        Dispatcher.BeginInvoke(() => LaunchWithDelay(CaptureActiveWindowNow));
    }

    /// <summary>Applies capture delay (if set) before running the action.</summary>
    private void LaunchWithDelay(Action action)
    {
        int delay = _settingsService!.Settings.CaptureDelaySeconds;
        if (delay > 0)
        {
            int remaining = delay;
            ToastWindow.Show($"Capturing in {remaining}...", "");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                remaining--;
                if (remaining > 0)
                    ToastWindow.Show($"Capturing in {remaining}...", "");
                else
                {
                    timer.Stop();
                    ToastWindow.DismissCurrent();
                    action();
                }
            };
            timer.Start();
            return;
        }
        action();
    }

    // ─── GIF recording launch ───────────────────────────────────────

    private void LaunchGifRecording()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var thread = new Thread(() =>
            {
                try
                {
                    var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
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
                        recMic, s.MicrophoneDeviceId, recDesktop, s.DesktopAudioDeviceId);

                    form.RecordingCompleted += (path, firstFrame) =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            try { EnsureHistoryService().SaveGifEntry(path); } catch { }

                            // Always copy file to clipboard
                            try
                            {
                                var files = new System.Collections.Specialized.StringCollection { path };
                                System.Windows.Clipboard.SetFileDropList(files);
                            }
                            catch { }

                            // Show toast with first-frame preview if available
                            if (firstFrame != null)
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
                                ToastWindow.Show($"{label} recorded", $"{fi.Name} · {size}");
                            }

                            ScheduleIdleMemoryTrim();
                        });
                    };

                    form.RecordingCancelled += () =>
                    {
                        Dispatcher.BeginInvoke(() => _isCapturing = false);
                    };

                    form.FormClosed += (_, _) =>
                    {
                        Dispatcher.BeginInvoke(() => _isCapturing = false);
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
        };
        timer.Start();
    }

    // ─── Scrolling capture launch ──────────────────────────────────

    private void LaunchScrollingCapture()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var thread = new Thread(() =>
            {
                try
                {
                    var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
                    var form = new ScrollingCaptureForm(bmp, bounds);

                    form.CaptureCompleted += result =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            HandleCaptureResult(result);
                            ScheduleIdleMemoryTrim();
                        });
                    };

                    form.CaptureCancelled += () =>
                    {
                        Dispatcher.BeginInvoke(() => _isCapturing = false);
                    };

                    form.FormClosed += (_, _) =>
                    {
                        Dispatcher.BeginInvoke(() => _isCapturing = false);
                    };

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
        };
        timer.Start();
    }

    private void CaptureFullscreenNow()
    {
        Bitmap? bmp = null;
        try
        {
            (bmp, _) = ScreenCapture.CaptureAllScreens();
            HandleCaptureResult(new Bitmap(bmp));
            bmp.Dispose();
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
            (bmp, var bounds) = ScreenCapture.CaptureAllScreens();
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

            using var cropped = ScreenCapture.CropRegion(bmp, crop);
            HandleCaptureResult(new Bitmap(cropped));
            bmp.Dispose();
        }
        catch (Exception ex)
        {
            bmp?.Dispose();
            _isCapturing = false;
            ToastWindow.ShowError("Capture error", ex.Message);
        }
    }

    // ─── Unified overlay launch ─────────────────────────────────────

    private void LaunchOverlay(CaptureMode initialMode)
    {
        LaunchWithDelay(() => LaunchOverlayNow(initialMode));
    }

    private void LaunchOverlayNow(CaptureMode initialMode)
    {
        // Use Background priority to yield to input processing, then launch immediately
        Dispatcher.BeginInvoke(() =>
        {
            var thread = new Thread(() =>
            {
                Bitmap? screenshot = null;
                try
                {
                    var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
                    screenshot = bmp;

                    var overlay = new RegionOverlayForm(screenshot, bounds, initialMode, _settingsService!.Settings.WindowDetection)
                    {
                        ShowCrosshairGuides = _settingsService!.Settings.ShowCrosshairGuides,
                        DetectWindows = _settingsService.Settings.DetectWindows,
                        DetectControls = _settingsService.Settings.DetectControls
                    };
                    overlay.SetEnabledTools(_settingsService.Settings.EnabledTools);
                    overlay.SetShowToolNumberBadges(_settingsService.Settings.ShowToolNumberBadges);

                    // Screenshot capture (rect / fullscreen)
                    overlay.RegionSelected += sel =>
                    {
                        overlay.Hide();
                        using var annotated = overlay.RenderAnnotatedBitmap();
                        using var cropped = ScreenCapture.CropRegion(annotated, sel);
                        var clone = new Bitmap(cropped); // clone before thread exits
                        HandleCaptureResult(clone);
                        overlay.Close();
                        System.Windows.Forms.Application.ExitThread();
                    };

                    overlay.FreeformSelected += fbmp =>
                    {
                        overlay.Hide();
                        var clone = new Bitmap(fbmp);
                        HandleCaptureResult(clone);
                        fbmp.Dispose();
                        overlay.Close();
                        System.Windows.Forms.Application.ExitThread();
                    };

                    // OCR capture
                    overlay.OcrRegionSelected += sel =>
                    {
                        overlay.Hide();
                        using var annotated = overlay.RenderAnnotatedBitmap();
                        using var cropped = ScreenCapture.CropRegion(annotated, sel);
                        var clone = new Bitmap(cropped);
                        HandleOcrResult(clone);
                        overlay.Close();
                        System.Windows.Forms.Application.ExitThread();
                    };

                    // Barcode / QR scan
                    overlay.ScanRegionSelected += sel =>
                    {
                        overlay.Hide();
                        SoundService.PlayScanSound();
                        using var annotated = overlay.RenderAnnotatedBitmap();
                        using var cropped = ScreenCapture.CropRegion(annotated, sel);
                        var clone = new Bitmap(cropped);
                        Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var text = BarcodeService.Decode(clone);
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
                                clone.Dispose();
                            }
                        });
                        overlay.Close();
                        System.Windows.Forms.Application.ExitThread();
                    };

                    // Sticker capture (remove background then treat as normal image)
                    overlay.StickerRegionSelected += sel =>
                    {
                        overlay.Hide();
                        using var annotated = overlay.RenderAnnotatedBitmap();
                        using var cropped = ScreenCapture.CropRegion(annotated, sel);
                        var clone = new Bitmap(cropped);

                        Dispatcher.BeginInvoke(async () =>
                        {
                            try
                            {
                                var processed = await StickerService.ProcessAsync(clone, _settingsService!.Settings.StickerUploadSettings);
                                if (processed.Success && processed.Image is not null)
                                {
                                    HandleStickerResult(processed.Image, processed.ProviderName);
                                }
                                else
                                {
                                    ToastWindow.Show("Sticker", processed.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                ToastWindow.ShowError("Sticker error", ex.Message);
                            }
                            finally
                            {
                                clone.Dispose();
                                overlay.Close();
                                System.Windows.Forms.Application.ExitThread();
                            }
                        });
                    };

                    // Color picker – copy hex without # prefix
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
                        // Save last used mode
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
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ─── Result handlers ────────────────────────────────────────────

    private void HandleCaptureResult(Bitmap result)
    {
        SoundService.PlayCaptureSound();

        var settings = _settingsService!.Settings;
        var ext = CaptureOutputService.GetExtension(settings.CaptureImageFormat);
        var defaultPath = Path.Combine(settings.SaveDirectory, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
        string? requestedPath = settings.AskForFileNameOnSave
            ? ResolveSavePath(defaultPath, settings.CaptureImageFormat)
            : defaultPath;
        if (requestedPath is null)
        {
            result.Dispose();
            _isCapturing = false;
            return;
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
                    // Always copy to clipboard
                    ClipboardService.CopyToClipboard(persisted.Output);

                    var action = settings.AfterCapture;
                    if (action == AfterCaptureAction.ShowPreview)
                    {
                        ToastWindow.ShowImagePreview(persisted.Output, persisted.FilePath, settings.AutoPinPreviews);
                    }
                    else
                    {
                        var dims = $"{persisted.Output.Width}x{persisted.Output.Height}";
                        persisted.Output.Dispose();
                        ToastWindow.Show("Copied to clipboard", dims);
                    }

                    if (persisted.FilePath != null && settings.AutoUploadScreenshots
                        && settings.ImageUploadDestination != UploadDestination.None)
                    {
                        _ = UploadFileAsync(persisted.FilePath, "Screenshot", persisted.HistoryEntry);
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
        var defaultStickerPath = Path.Combine(settings.SaveDirectory, $"yoink_sticker_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        string? requestedPath = settings.AskForFileNameOnSave
            ? ResolveSavePath(defaultStickerPath, CaptureImageFormat.Png)
            : defaultStickerPath;
        if (requestedPath is null)
        {
            result.Dispose();
            _isCapturing = false;
            return;
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
                string text = await OcrService.RecognizeAsync(result);
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

    private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)
    {
        Interlocked.Increment(ref _activeUploadCount);
        try
        {
            SoundService.PlayUploadStartSound();
            var dest = _settingsService!.Settings.ImageUploadDestination;
            var settings = _settingsService.Settings.ImageUploadSettings;
            var result = await UploadService.UploadAsync(
                filePath, dest, settings);

            if (result.Success)
            {
                SoundService.PlayUploadDoneSound();
                System.Windows.Clipboard.SetText(result.Url);

                // Store upload URL in history entry
                var entry = historyEntry ?? _historyService?.Entries.FirstOrDefault(e =>
                    string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    lock (_historyGate)
                    {
                        entry.UploadUrl = result.Url;
                        if (string.IsNullOrWhiteSpace(entry.UploadProvider))
                        {
                            entry.UploadProvider = UploadService.GetName(dest);
                            var currentName = Path.GetFileName(entry.FilePath);
                            var prefix = UploadService.GetName(dest).ToLowerInvariant() + "_";
                            entry.FileName = currentName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                ? currentName
                                : prefix + currentName;
                        }
                        EnsureHistoryService().SaveIndex();
                    }
                }
            }
            else
            {
                ToastWindow.ShowError(
                    result.IsRateLimit ? $"{label} upload rate-limited" : $"{label} upload failed",
                    result.Error);
            }
        }
        catch (Exception ex)
        {
            ToastWindow.ShowError($"{label} upload error", ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeUploadCount);
            ScheduleIdleMemoryTrim();
        }
    }

    private string SaveToFile(Bitmap bmp, string path)
    {
        var settings = _settingsService!.Settings;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Save path must include a directory.");

        Directory.CreateDirectory(directory);
        CaptureOutputService.SaveBitmap(bmp, path, settings.CaptureImageFormat, settings.JpegQuality);
        return path;
    }

    private string SaveStickerToFile(Bitmap bmp, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Save path must include a directory.");

        Directory.CreateDirectory(directory);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private string? ResolveSavePath(string defaultPath, CaptureImageFormat format)
    {
        if (!_settingsService!.Settings.AskForFileNameOnSave)
            return defaultPath;

        var dlg = new SaveFileDialog
        {
            InitialDirectory = Path.GetDirectoryName(defaultPath),
            FileName = Path.GetFileName(defaultPath),
            Filter = format switch
            {
                CaptureImageFormat.Jpeg => "JPEG image|*.jpg;*.jpeg|PNG image|*.png|Bitmap image|*.bmp",
                CaptureImageFormat.Bmp => "Bitmap image|*.bmp|PNG image|*.png|JPEG image|*.jpg;*.jpeg",
                _ => "PNG image|*.png|JPEG image|*.jpg;*.jpeg|Bitmap image|*.bmp"
            },
            AddExtension = true,
            OverwritePrompt = true
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // ─── Settings / History ─────────────────────────────────────────

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        var win = new SettingsWindow(_settingsService!, EnsureHistoryService());
        Action hotkeyHandler = () => RegisterHotkeys();
        Action uninstallHandler = BeginUninstall;
        win.HotkeyChanged += hotkeyHandler;
        win.UninstallRequested += uninstallHandler;
        win.Closed += (_, _) =>
        {
            win.HotkeyChanged -= hotkeyHandler;
            win.UninstallRequested -= uninstallHandler;
            _settingsWindow = null;
            ScheduleIdleMemoryTrim();
        };
        _settingsWindow = win;
        win.Show();
    }

    private void ShowHistory()
    {
        ShowSettings();
        Dispatcher.BeginInvoke(() =>
        {
            var tab = _settingsWindow?.FindName("HistoryTab") as System.Windows.Controls.RadioButton;
            if (tab is not null)
            {
                tab.IsChecked = true;
                tab.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        }, DispatcherPriority.Loaded);
    }

    private void BeginUninstall()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var result = MessageBox.Show(
                "Uninstall Yoink? This will remove the app data and try to remove the app folder.",
                "Confirm uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try { UninstallService.RemoveStartupEntry(); } catch { }
            try { UninstallService.RemoveInstalledAppEntry(); } catch { }
            try { UninstallService.RemoveStartMenuShortcut(); } catch { }
            try { UninstallService.RemoveAppData(); } catch { }
            try { UninstallService.ScheduleInstallFolderRemoval(); } catch { }

            ToastWindow.Show("Uninstalling", "Yoink will close and remove its files.");
            Shutdown();
        });
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
                return;

            var detail = string.IsNullOrWhiteSpace(result.AssetName)
                ? $"{result.LatestVersionLabel} is available on GitHub Releases."
                : $"{result.LatestVersionLabel} is ready: {result.AssetName}";

            _ = Dispatcher.BeginInvoke(() => ToastWindow.Show("Update available", detail));
        }
        catch
        {
            // Ignore background update check failures.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleTrimTimer?.Stop();
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        try { Services.LocalStickerEngineService.Shutdown(); } catch { }
        base.OnExit(e);
    }

    private HistoryService EnsureHistoryService()
    {
        lock (_historyGate)
        {
            if (_historyService is null)
            {
                _historyService = new HistoryService();
                _historyService.Load();
                if (!_historyRecovered)
                {
                    _historyService.RecoverFromDirectories(
                        _settingsService!.Settings.SaveDirectory,
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history"));
                    _historyRecovered = true;
                }
                _historyService.PruneByRetention(_settingsService!.Settings.HistoryRetention);
            }

            _historyService.CompressHistory = _settingsService!.Settings.CompressHistory;
            _historyService.JpegQuality = _settingsService.Settings.JpegQuality;
            _historyService.CaptureImageFormat = _settingsService.Settings.CaptureImageFormat;
            return _historyService;
        }
    }

    private void ScheduleIdleMemoryTrim()
    {
        if (_idleTrimTimer is null)
            return;

        _idleTrimTimer.Stop();
        _idleTrimTimer.Start();
    }

    private void TrimIdleMemory()
    {
        _idleTrimTimer?.Stop();

        if (_isCapturing || Volatile.Read(ref _activeUploadCount) > 0)
        {
            ScheduleIdleMemoryTrim();
            return;
        }

        // Release all caches and optional services
        _historyService = null;
        UI.SettingsWindow.ClearThumbCache();

        // Release ONNX inference sessions (can be 100-500MB per model)
        try { Services.LocalStickerEngineService.ReleaseSessions(); } catch { }

        // Aggressive GC + LOH compaction + working set trim
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        ProcessMemory.TrimCurrentProcessWorkingSet();
    }
}
