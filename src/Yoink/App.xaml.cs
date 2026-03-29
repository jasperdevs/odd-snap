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
        _mutex = new Mutex(true, "YoinkScreenshotTool_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        base.OnStartup(e);

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
        _hotkeyService.RulerHotkeyPressed += () => OnToolHotkeyPressed(CaptureMode.Ruler);
        _hotkeyService.GifHotkeyPressed += OnGifHotkeyPressed;

        var s = _settingsService!.Settings;
        bool ok = _hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey);
        _hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        _hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey);
        _hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey);
        _hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey);
        _hotkeyService.RegisterGif(s.GifHotkeyModifiers, s.GifHotkeyKey);


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

                    var overlay = new RegionOverlayForm(screenshot, bounds, initialMode)
                    {
                        ShowCrosshairGuides = _settingsService!.Settings.ShowCrosshairGuides
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

            if (_settingsService!.Settings.SaveHistory)
            {
                historyEntry = _historyService!.SaveCapture(output);
                filePath = historyEntry.FilePath;
            }

            if (_settingsService.Settings.SaveToFile)
                filePath = SaveToFile(output) ?? filePath;

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
            var dest = _settingsService!.Settings.ImageUploadDestination;
            var settings = _settingsService.Settings.ImageUploadSettings;
            var result = await UploadService.UploadAsync(
                filePath, dest, settings);

            if (result.Success)
            {
                System.Windows.Clipboard.SetText(result.Url);
                PreviewWindow.AttachUploadedLink(filePath, result.Url, UploadService.GetName(dest));

                // Store upload URL in history entry
                var entry = historyEntry ?? _historyService?.Entries.FirstOrDefault(e =>
                    string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.UploadUrl = result.Url;
                    entry.UploadProvider = UploadService.GetName(dest);
                    var currentName = Path.GetFileName(entry.FilePath);
                    var prefix = UploadService.GetName(dest).ToLowerInvariant() + "_";
                    entry.FileName = currentName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? currentName
                        : prefix + currentName;
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

    private string? SaveToFile(Bitmap bmp)
    {
        var dir = _settingsService!.Settings.SaveDirectory;
        Directory.CreateDirectory(dir);
        var ext = CaptureOutputService.GetExtension(_settingsService.Settings.CaptureImageFormat);
        var path = Path.Combine(dir, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
        CaptureOutputService.SaveBitmap(bmp, path, _settingsService.Settings.CaptureImageFormat, _settingsService.Settings.JpegQuality);
        return path;
    }

    // ─── Settings / History ─────────────────────────────────────────

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settingsService!, _historyService!);
        _settingsWindow.HotkeyChanged += () => RegisterHotkeys();
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
