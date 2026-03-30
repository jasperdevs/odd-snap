using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Yoink.Capture;
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
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private bool _isCapturing;


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
        SoundService.Muted = _settingsService.Settings.MuteSounds;
        ToastWindow.SetPosition(_settingsService.Settings.ToastPosition);
        PreviewWindow.SetPosition(_settingsService.Settings.ToastPosition);

        _historyService = new HistoryService();
        _historyService.Load();
        // Recover captures from save directory + history dir that aren't in the index
        _historyService.RecoverFromDirectories(
            _settingsService.Settings.SaveDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yoink", "history"));
        _historyService.CompressHistory = _settingsService.Settings.CompressHistory;
        _historyService.JpegQuality = _settingsService.Settings.JpegQuality;
        _historyService.CaptureImageFormat = _settingsService.Settings.CaptureImageFormat;
        _historyService.PruneByRetention(_settingsService.Settings.HistoryRetention);

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

        var s = _settingsService!.Settings;
        bool ok = _hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey);
        _hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        _hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey);
        _hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey);
        _hotkeyService.RegisterSticker(s.StickerHotkeyModifiers, s.StickerHotkeyKey);
        _hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey);
        _hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey);
        _hotkeyService.RegisterFullscreen(s.FullscreenHotkeyModifiers, s.FullscreenHotkeyKey);
        _hotkeyService.RegisterActiveWindow(s.ActiveWindowHotkeyModifiers, s.ActiveWindowHotkeyKey);


        var name = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);
        if (!ok)
            ToastWindow.Show("Hotkey failed", $"Could not register {name}. Try a different combo.");
        else
            ToastWindow.Show("Yoink ready", $"{name} to capture, Alt+C for colors");
    }

    // ─── Hotkeys (all open unified overlay with different initial tool) ──

    private void OnHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() => LaunchOverlay(_settingsService!.Settings.DefaultCaptureMode));
    }

    private void OnToolHotkeyPressed(CaptureMode mode)
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() => LaunchOverlay(mode));
    }

    private void OnOcrHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() => LaunchOverlay(CaptureMode.Ocr));
    }

    private void OnPickerHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(() => LaunchOverlay(CaptureMode.ColorPicker));
    }

    private void OnGifHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(LaunchGifRecording);
    }

    private void OnFullscreenHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(CaptureFullscreenNow);
    }

    private void OnActiveWindowHotkeyPressed()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        PreviewWindow.DismissCurrent();
        ToastWindow.DismissCurrent();
        Dispatcher.BeginInvoke(CaptureActiveWindowNow);
    }

    // ─── GIF recording launch ───────────────────────────────────────

    private void LaunchGifRecording()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var thread = new Thread(() =>
            {
                try
                {
                    System.Windows.Forms.Application.EnableVisualStyles();
                    var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
                    var s = _settingsService!.Settings;

                    string saveDir = s.SaveDirectory;
                    Directory.CreateDirectory(saveDir);
                    string fileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.gif";
                    string savePath = Path.Combine(saveDir, fileName);

                    var form = new RecordingForm(bmp, bounds, s.GifFps, s.GifMaxDuration, savePath);

                    form.RecordingCompleted += path =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            _isCapturing = false;
                            Services.HistoryEntry? historyEntry = null;

                            // Save to history
                            try
                            {
                                historyEntry = _historyService?.SaveGifEntry(path);
                            }
                            catch { }

                            // Show preview (copies to clipboard, supports drag & drop)
                            var preview = new PreviewWindow(path);
                            preview.Show();

                        });
                    };

                    form.RecordingCancelled += () =>
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
                        ToastWindow.Show("GIF error", "Recording failed");
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
        try
        {
            var (bmp, _) = ScreenCapture.CaptureAllScreens();
            HandleCaptureResult(new Bitmap(bmp));
            bmp.Dispose();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            ToastWindow.Show("Capture error", ex.Message);
        }
    }

    private void CaptureActiveWindowNow()
    {
        try
        {
            var (bmp, bounds) = ScreenCapture.CaptureAllScreens();
            var hwnd = Native.User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !Native.User32.GetWindowRect(hwnd, out var rect))
            {
                bmp.Dispose();
                _isCapturing = false;
                ToastWindow.Show("Capture error", "Couldn't find the active window.");
                return;
            }

            var crop = new Rectangle(rect.Left - bounds.X, rect.Top - bounds.Y, rect.Width, rect.Height);
            crop.Intersect(new Rectangle(System.Drawing.Point.Empty, bmp.Size));
            if (crop.Width <= 1 || crop.Height <= 1)
            {
                bmp.Dispose();
                _isCapturing = false;
                ToastWindow.Show("Capture error", "Active window is out of bounds.");
                return;
            }

            using var cropped = ScreenCapture.CropRegion(bmp, crop);
            HandleCaptureResult(new Bitmap(cropped));
            bmp.Dispose();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            ToastWindow.Show("Capture error", ex.Message);
        }
    }

    // ─── Unified overlay launch ─────────────────────────────────────

    private void LaunchOverlay(CaptureMode initialMode)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var thread = new Thread(() =>
            {
                Bitmap? screenshot = null;
                try
                {
                    System.Windows.Forms.Application.EnableVisualStyles();
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
                                ToastWindow.Show("Sticker error", ex.Message);
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
                                _historyService!.SaveColorEntry(bare);
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
                        ToastWindow.Show("Capture error", ex.Message);
                    });
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        };
        timer.Start();
    }

    // ─── Result handlers ────────────────────────────────────────────

    private void HandleCaptureResult(Bitmap result)
    {
        SoundService.PlayCaptureSound();

        Dispatcher.BeginInvoke(() =>
        {
            var output = CaptureOutputService.PrepareBitmap(result, _settingsService!.Settings.CaptureMaxLongEdge);
            result.Dispose();
            string? filePath = null;
            Services.HistoryEntry? historyEntry = null;

            if (_settingsService.Settings.SaveToFile)
            {
                var ext = CaptureOutputService.GetExtension(_settingsService.Settings.CaptureImageFormat);
                var requestedPath = ResolveSavePath(
                    Path.Combine(_settingsService.Settings.SaveDirectory, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}"),
                    _settingsService.Settings.CaptureImageFormat);
                if (requestedPath is null)
                {
                    output.Dispose();
                    return;
                }
                filePath = requestedPath;
            }

            if (_settingsService!.Settings.SaveHistory)
            {
                historyEntry = _historyService!.SaveCapture(output);
                filePath ??= historyEntry.FilePath;
            }

            if (_settingsService.Settings.SaveToFile)
                SaveToFile(output, filePath!);

            var action = _settingsService.Settings.AfterCapture;
            if (action == AfterCaptureAction.ShowPreview)
            {
                var preview = new PreviewWindow(output, filePath);
                preview.Show();
            }
            else
            {
                ClipboardService.CopyToClipboard(output);
                output.Dispose();
            }

            // Auto-upload screenshot
            if (filePath != null && _settingsService.Settings.AutoUploadScreenshots
                && _settingsService.Settings.ImageUploadDestination != UploadDestination.None)
            {
                _ = UploadFileAsync(filePath, "Screenshot", historyEntry);
            }

            _isCapturing = false;
        });
    }

    private void HandleStickerResult(Bitmap result, string providerName)
    {
        SoundService.PlayCaptureSound();

        Dispatcher.BeginInvoke(() =>
        {
            var output = CaptureOutputService.PrepareBitmap(result, _settingsService!.Settings.CaptureMaxLongEdge);
            result.Dispose();
            string? filePath = null;
            Services.HistoryEntry? historyEntry = null;

            if (_settingsService.Settings.SaveToFile)
            {
                var requestedPath = ResolveSavePath(
                    Path.Combine(_settingsService.Settings.SaveDirectory, $"yoink_sticker_{DateTime.Now:yyyyMMdd_HHmmss}.png"),
                    CaptureImageFormat.Png);
                if (requestedPath is null)
                {
                    output.Dispose();
                    return;
                }
                filePath = requestedPath;
            }

            if (_settingsService!.Settings.SaveHistory)
            {
                historyEntry = _historyService!.SaveStickerEntry(output, providerName);
                filePath ??= historyEntry.FilePath;
            }

            if (_settingsService.Settings.SaveToFile)
                SaveStickerToFile(output, filePath!);

            var action = _settingsService.Settings.AfterCapture;
            if (action == AfterCaptureAction.ShowPreview)
            {
                var preview = new PreviewWindow(output, filePath);
                preview.Show();
            }
            else
            {
                ClipboardService.CopyToClipboard(output);
                output.Dispose();
            }

            if (filePath != null && _settingsService.Settings.AutoUploadScreenshots
                && _settingsService.Settings.ImageUploadDestination != UploadDestination.None)
            {
                _ = UploadFileAsync(filePath, "Sticker", historyEntry);
            }

            _isCapturing = false;
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
                        _historyService!.SaveOcrEntry(text);
                }
                else
                {
                    ToastWindow.Show("OCR", "No text found");
                }
            }
            catch (Exception ex)
            {
                ToastWindow.Show("OCR error", ex.Message);
            }
            finally { result.Dispose(); }
        });
    }

    private async Task UploadFileAsync(string filePath, string label, Services.HistoryEntry? historyEntry = null)
    {
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
                PreviewWindow.AttachUploadedLink(filePath, result.Url, UploadService.GetName(dest));

                // Store upload URL in history entry
                var entry = historyEntry ?? _historyService?.Entries.FirstOrDefault(e =>
                    string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
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
                    _historyService!.SaveIndex();
                }
            }
            else
            {
                SoundService.PlayErrorSound();
                ToastWindow.Show(
                    result.IsRateLimit ? $"{label} upload rate-limited" : $"{label} upload failed",
                    result.Error);
            }
        }
        catch (Exception ex)
        {
            SoundService.PlayErrorSound();
            ToastWindow.Show($"{label} upload error", ex.Message);
        }
    }

    private string SaveToFile(Bitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CaptureOutputService.SaveBitmap(bmp, path, _settingsService.Settings.CaptureImageFormat, _settingsService.Settings.JpegQuality);
        return path;
    }

    private string SaveStickerToFile(Bitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
        _settingsWindow = new SettingsWindow(_settingsService!, _historyService!);
        _settingsWindow.HotkeyChanged += () => RegisterHotkeys();
        _settingsWindow.UninstallRequested += BeginUninstall;
        _settingsWindow.Show();
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
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        base.OnExit(e);
    }
}
