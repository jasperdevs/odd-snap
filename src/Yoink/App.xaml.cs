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
        _trayIcon.OnSettings += ShowSettings;
        _trayIcon.OnHistory += ShowHistory;
        _trayIcon.OnQuit += () => Shutdown();

        RegisterHotkeys();
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


        var s = _settingsService!.Settings;
        bool ok = _hotkeyService.Register(s.HotkeyModifiers, s.HotkeyKey);
        _hotkeyService.RegisterOcr(s.OcrHotkeyModifiers, s.OcrHotkeyKey);
        _hotkeyService.RegisterPicker(s.PickerHotkeyModifiers, s.PickerHotkeyKey);
        _hotkeyService.RegisterScan(s.ScanHotkeyModifiers, s.ScanHotkeyKey);
        _hotkeyService.RegisterRuler(s.RulerHotkeyModifiers, s.RulerHotkeyKey);


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
            string? filePath = null;

            if (_settingsService!.Settings.SaveHistory)
            {
                var entry = _historyService!.SaveCapture(result);
                filePath = entry.FilePath;
            }

            if (_settingsService.Settings.SaveToFile)
                filePath = SaveToFile(result) ?? filePath;

            var action = _settingsService.Settings.AfterCapture;
            if (action == AfterCaptureAction.ShowPreview)
            {
                var preview = new PreviewWindow(result, filePath);
                preview.Show();
            }
            else
            {
                ClipboardService.CopyToClipboard(result);
                result.Dispose();
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

    private string? SaveToFile(Bitmap bmp)
    {
        var dir = _settingsService!.Settings.SaveDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
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

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        base.OnExit(e);
    }
}
